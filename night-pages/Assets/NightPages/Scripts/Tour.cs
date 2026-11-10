using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Headless test runs, in the style of Verge's capture tools: the built game is started from the command
    /// line, does its job with no window and nobody at the keyboard, writes what it found to a folder, and quits.
    ///
    ///   NightPages.exe -batchmode -nptour &lt;folder&gt;     screenshots of every room, day and evening, the cellar,
    ///                                                   every monster form and every dream space, plus a
    ///                                                   check that the furniture you can move stays on the floor
    ///   NightPages.exe -batchmode -npbot &lt;folder&gt; [-npruns 3]
    ///                                                   plays whole runs at speed (chores, writing, clues, dreams
    ///                                                   with a stand-in reaction), and reports each ending
    ///
    /// Both write log.txt (every decision the game made), errors.txt (every error and exception), and
    /// summary.json. tools/unity-agent runs these for you.
    /// </summary>
    public static class Tour
    {
        public static string Mode, OutDir;
        public static int Runs = 3;
        /// <summary>Make tonight's sleep paralysis / tomorrow's false awakening happen (for the photographs).</summary>
        public static bool ForceParalysis, ForceFalseAwakening;
        public static readonly List<string> Lines = new List<string>();
        public static readonly List<string> Errors = new List<string>();
        static bool parsed;

        // ---- hang watchdog: if frames stop, say where the game was
        public static volatile int Frame;
        public static volatile string Crumb = "starting";
        static readonly object recentGate = new object();
        static readonly Queue<string> recent = new Queue<string>();

        /// <summary>Mark the step the game is on, so a hang can say where it happened.</summary>
        public static void Step(string where) { if (Mode != null) Crumb = where; }

        public static void StartWatchdog()
        {
            var t = new System.Threading.Thread(() =>
            {
                int last = -1; var since = System.DateTime.UtcNow; bool reported = false;
                while (true)
                {
                    System.Threading.Thread.Sleep(1000);
                    int f = Frame;
                    if (f != last) { last = f; since = System.DateTime.UtcNow; reported = false; continue; }
                    if (!reported && (System.DateTime.UtcNow - since).TotalSeconds > 20)
                    {
                        reported = true;
                        string[] tail;
                        lock (recentGate) tail = recent.ToArray();
                        try
                        {
                            File.WriteAllText(Path.Combine(OutDir, "hang.txt"),
                                "HANG: no frame for 20 s.\nLast step: " + Crumb + "\n\nLast log lines:\n" + string.Join("\n", tail) + "\n");
                        }
                        catch (System.Exception) { }
                    }
                }
            }) { IsBackground = true, Name = "NightPages watchdog" };
            t.Start();
        }

        public static bool Requested
        {
            get
            {
                if (!parsed)
                {
                    parsed = true;
                    var a = System.Environment.GetCommandLineArgs();
                    for (int i = 0; i + 1 < a.Length; i++)
                    {
                        if (a[i] == "-nptour") { Mode = "tour"; OutDir = a[i + 1]; }
                        if (a[i] == "-npbot") { Mode = "bot"; OutDir = a[i + 1]; }
                        if (a[i] == "-npvideo") { Mode = "video"; OutDir = a[i + 1]; }
                        if (a[i] == "-npruns") int.TryParse(a[i + 1], out Runs);
                    }
                }
                return Mode != null;
            }
        }

        public static void ResetStatics() { parsed = false; Mode = OutDir = null; Lines.Clear(); Errors.Clear(); }

        public static void Note(string line)
        {
            if (Mode == null) return;
            string l = Time.realtimeSinceStartup.ToString("0.0").PadLeft(6) + "  " + line;
            Lines.Add(l);
            lock (recentGate) { recent.Enqueue(l); while (recent.Count > 30) recent.Dequeue(); }
        }

        public static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            return sb.ToString().Trim('-');
        }
    }

    public partial class GameDirector
    {
        readonly List<string> shots = new List<string>();
        RenderTexture shotRT;
        Texture2D shotTex;

        IEnumerator RunTour()
        {
            Directory.CreateDirectory(Tour.OutDir);
            // Test runs happen while you're working: silent, unless it's the "watch" job (a window, with sound).
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-npsound") < 0) AudioListener.volume = 0f;
            Tour.StartWatchdog();
            Application.logMessageReceived += (msg, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    Tour.Errors.Add(type + ": " + msg + "\n" + stack);
            };
            Log("test run: " + Tour.Mode + ", writing to " + Tour.OutDir + (Application.isBatchMode ? " (no window)" : ""));
            var summary = new StringBuilder("{\n");
            if (Tour.Mode == "tour") yield return CaptureTour(summary);
            else if (Tour.Mode == "video") yield return MonsterVideo(summary);
            else yield return BotRuns(summary);
            summary.Append("  \"errors\": ").Append(Tour.Errors.Count).Append(",\n");
            summary.Append("  \"models_installed\": ").Append(Models.Installed).Append(",\n  \"models_missing\": ").Append(Models.Missing).Append("\n}\n");
            File.WriteAllText(Path.Combine(Tour.OutDir, "summary.json"), summary.ToString());
            File.WriteAllLines(Path.Combine(Tour.OutDir, "log.txt"), Tour.Lines.ToArray());
            File.WriteAllText(Path.Combine(Tour.OutDir, "errors.txt"), Tour.Errors.Count == 0 ? "no errors\n" : string.Join("\n\n", Tour.Errors.ToArray()));
            Debug.Log("[NightPages] test run finished: " + Tour.Errors.Count + " errors");
            yield return null;
            Application.Quit(Tour.Errors.Count == 0 ? 0 : 3);
        }

        // ---------------------------------------------------------------- screenshots

        static float YawTo(Vector3 from, Vector3 to) { return Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg; }

        IEnumerator Settle(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        // Render the player's view straight to a PNG. Works with no window (cameras are rendered by hand).
        void Shot(string name)
        {
            const int W = 1280, H = 720;
            if (shotRT == null)
            {
                shotRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                shotRT.antiAliasing = Look.I != null ? 1 : 4;     // the post stack anti-aliases
                shotTex = new Texture2D(W, H, TextureFormat.RGB24, false);
            }
            var cam = player.cam;
            var old = cam.targetTexture;
            cam.targetTexture = shotRT;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = shotRT;
            shotTex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            shotTex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = old;
            string file = (shots.Count + 1).ToString("00") + "-" + Tour.Safe(name) + ".png";
            File.WriteAllBytes(Path.Combine(Tour.OutDir, file), shotTex.EncodeToPNG());
            shots.Add(file);
            Log("shot: " + file);
        }

        // ---------------------------------------------------------------- the monster film

        RenderTexture filmRT;
        Texture2D filmTex;
        int filmFrames;

        void Frame()
        {
            const int W = 1280, H = 720;
            if (filmRT == null)
            {
                filmRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                filmTex = new Texture2D(W, H, TextureFormat.RGB24, false);
                Directory.CreateDirectory(Path.Combine(Tour.OutDir, "frames"));
            }
            var cam = player.cam;
            var old = cam.targetTexture;
            cam.targetTexture = filmRT;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = filmRT;
            filmTex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            filmTex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = old;
            filmFrames++;
            if (filmFrames % 150 == 0) Log("film: " + filmFrames + " frames");
            File.WriteAllBytes(Path.Combine(Tour.OutDir, "frames", "f" + filmFrames.ToString("00000") + ".jpg"), filmTex.EncodeToJPG(88));
        }

        /// <summary>
        /// A short film of it in the dark hall, by one failing bulb: standing, then becoming three other things in
        /// turn, gradually, coming closer between each. 30 frames a second, written as numbered JPEGs for ffmpeg.
        /// </summary>
        IEnumerator MonsterVideo(StringBuilder summary)
        {
            yield return StartRunForTest();
            haunt.paused = true;
            day = 7;
            phase = Phase.Evening;
            house.SetTime(1, 7);
            // Every light in the house off, every door shut: only the one failing bulb over the hall.
            foreach (var r in house.rooms) r.darkUntil = Time.time + 1e6f;
            foreach (var l in house.GetComponentsInChildren<Light>(true)) l.enabled = false;
            if (Outdoors.I != null) foreach (var l in Outdoors.I.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var d in house.doors) d.Snap(false);
            // The grandfather clock would fill the right of the frame: everything standing where it stands goes for the film.
            foreach (var r in house.GetComponentsInChildren<Renderer>())
            {
                Vector3 c = r.bounds.center, s = r.bounds.size;
                if (Mathf.Abs(c.x - 1.15f) < 0.45f && Mathf.Abs(c.z) < 0.45f && s.x < 1f && s.z < 1f) r.enabled = false;
            }
            RenderSettings.ambientLight = new Color(0.004f, 0.004f, 0.006f);
            RenderSettings.fogColor = Color.black;
            var bulb = Build.Lamp("Failing bulb", house.transform, new Vector3(0f, 2.35f, 1.7f), new Color(1f, 0.82f, 0.6f), 2.2f, 6.5f, true);
            Build.Prim(PrimitiveType.Sphere, "Bulb", house.transform, new Vector3(0f, 2.42f, 1.7f), new Vector3(0.07f, 0.09f, 0.07f), Build.Mat(new Color(1f, 0.85f, 0.6f), 1.5f), false);
            player.frozen = true;
            player.Teleport(new Vector3(0f, 0.05f, -0.7f), 0f);
            player.LookPitch(-3f);
            if (Look.I != null) Look.I.Cut();
            hud.fade = hud.fadeTarget = 0f;
            hud.ClearSubtitles();
            hud.objectives.Clear();
            Time.captureFramerate = 30;

            int[] Pick(int legs)
            {
                var l = fear.Compose(2);
                l[Monster.Shift] = 0;
                l[Monster.Legs] = legs;
                return l;
            }
            var looks = new[] { Pick(Monster.TwoLegs), Pick(Monster.SpiderLegs), Pick(Monster.FourLegs), Pick(Monster.OnHands) };
            var it = Monster.Create(null, looks[0]);
            it.transform.position = new Vector3(0f, 0f, 3.6f);
            it.FaceToward(player.transform.position);
            for (int i = 0; i < looks.Length; i++) Log("film: form " + (i + 1) + ": " + fear.DescribeLook(looks[i]));

            float clock = 0f;
            System.Func<float> flicker = () =>
            {
                // A bulb on its way out: mostly on, dips, stutters, and now and then a long dark.
                float n = Mathf.PerlinNoise(clock * 3.1f, 0.3f), m = Mathf.PerlinNoise(clock * 17f, 4.2f);
                if (n < 0.14f) return 0.03f;
                return m < 0.22f ? 0.4f : 1f;
            };
            System.Func<float, IEnumerator> film = null;
            film = secs => FilmFor(secs, () => { clock += Time.deltaTime; bulb.intensity = 2.2f * flicker(); });

            yield return film(4f);
            for (int k = 1; k < looks.Length; k++)
            {
                // It comes closer: not walking, arriving, a little at a time.
                Vector3 from = it.transform.position, to = from + Vector3.back * 0.9f;
                for (int s = 0; s < 4; s++)
                {
                    it.transform.position = Vector3.Lerp(from, to, (s + 1) / 4f);
                    it.FaceToward(player.transform.position);
                    Sfx.At("step", it.transform.position, 0.4f, 0.8f);
                    yield return film(0.45f + Random.value * 0.4f);
                }
                yield return film(0.8f);
                // Then it changes, in front of you.
                StartCoroutine(it.TransformInto(looks[k], 1.15f));
                float guard = 0f;
                while ((it.Transforming || guard < 0.2f) && guard < 30f) { guard += Time.deltaTime; clock += Time.deltaTime; bulb.intensity = 2.2f * Mathf.Max(0.4f, flicker()); Frame(); yield return null; }
                yield return film(1.5f);
            }
            // The bulb gives out. Its eyes stay.
            bulb.intensity = 0f;
            yield return film(2.5f);
            Time.captureFramerate = 0;
            Destroy(bulb.gameObject);
            summary.Append("  \"mode\": \"video\",\n  \"frames\": ").Append(filmFrames).Append(",\n  \"fps\": 30,\n");
        }

        IEnumerator FilmFor(float seconds, System.Action each)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                each();
                Frame();
                yield return null;
            }
        }

        // A point given as it is in a room's drawing, wherever that room is in this layout.
        Vector3 R(string room, float x, float z) { return house.InRoom(room, new Vector3(x, 0f, z)); }

        IEnumerator Pose(Vector3 from, Vector3 lookAt, float pitch, string name, float wait = 0.4f)
        {
            player.Teleport(from + Vector3.up * 0.05f, YawTo(from, lookAt));
            player.LookPitch(pitch);
            if (Look.I != null) Look.I.Cut();
            yield return Settle(wait + 0.5f);
            if (!name.StartsWith("watch")) Shot(name);
        }

        IEnumerator StartRunForTest()
        {
            NewRun();
            while (busy || phase != Phase.Morning) yield return null;
            hud.fade = hud.fadeTarget = 0f;
        }

        IEnumerator CaptureTour(StringBuilder summary)
        {
            yield return StartRunForTest();
            haunt.paused = true;             // no frights while photographing (the storyteller gets its own test below)
            yield return Settle(3.5f);       // movable things drop into place and remember where they belong

            // Does anything you can move end up under the floor or knocked over?
            int fell = 0, toppled = 0;
            foreach (var m in house.movables)
            {
                if (m == null) continue;
                if (m.transform.position.y < -0.3f) { fell++; Log("PROBLEM: '" + m.name + "' fell through the floor"); }
                else if (Vector3.Angle(m.transform.up, Vector3.up) > 35f) { toppled++; Log("note: '" + m.name + "' settled on its side"); }
            }
            Log("movable things: " + house.movables.Count + ", fell through the floor: " + fell + ", on their side: " + toppled);

            // The face reader, on a photo (test runs have no camera).
            yield return FaceReader.SelfTest(Log);

            // The house, by day.
            yield return Pose(new Vector3(0f, 0f, -6.6f), new Vector3(0f, 0f, 6f), 0f, "day hall looking to the front door");
            yield return Pose(new Vector3(0f, 0f, 6.6f), new Vector3(0f, 0f, -6f), 0f, "day hall looking to the cellar door");
            yield return Pose(R("study", -2.3f, 2.3f), R("study", -6.3f, 4.2f), 8f, "day study and typewriter");
            yield return Pose(R("kitchen", -2.4f, -1.4f), R("kitchen", -6.4f, -5.2f), 8f, "day kitchen");
            yield return Pose(R("living room", 2.3f, -1.3f), R("living room", 6.6f, -5f), 6f, "day living room");
            yield return Pose(R("bedroom", 2.3f, 1.2f), R("bedroom", 6.3f, 6.2f), 8f, "day bedroom");

            // Outside: the house from the drive, the porch, the yard, and the woods all around.
            float G = Outdoors.Ground;
            yield return Pose(new Vector3(3.5f, G, 26f), new Vector3(0f, 2f, 0f), 4f, "day outside from the drive", 1.8f);
            yield return Pose(new Vector3(0f, 0f, 10f), new Vector3(1.5f, 0f, 30f), 2f, "day porch looking down the drive", 1.6f);
            yield return Pose(new Vector3(14f, G, 8f), new Vector3(0f, 1f, -4f), 2f, "day yard east side woodpile", 1.6f);
            yield return Pose(new Vector3(-16f, G, -16f), new Vector3(-4f, 1f, -4f), 3f, "day back yard clothesline and fire pit", 1.6f);
            yield return Pose(new Vector3(12f, G, -6f), new Vector3(10f, 1f, -14f), 3f, "day shed", 1.6f);
            yield return Pose(new Vector3(-6f, G, 14f), new Vector3(-30f, 3f, 30f), -6f, "day edge of the woods", 1.6f);
            yield return Pose(new Vector3(18f, G, 40f), new Vector3(30f, 4f, 60f), -4f, "day drive into the trees", 1.6f);
            yield return Pose(new Vector3(0f, 0f, 6.6f), new Vector3(0f, 0f, -6f), 0f, "day back inside after the yard", 1.6f);

            // Pick something up, carry it, put it down: is it still in the house and on its feet?
            Movable vase = null;
            foreach (var m in house.movables) if (m != null && m.name == "Vase") vase = m;
            if (vase != null)
            {
                Vector3 before = vase.transform.position;
                player.Teleport(new Vector3(-0.3f, 0.05f, -2.1f), YawTo(new Vector3(-0.3f, 0f, -2.1f), before));
                yield return Settle(0.2f);
                player.PickUp(vase);
                player.Teleport(new Vector3(0f, 0.05f, 2.5f), 0f);
                yield return Settle(0.6f);
                player.Drop();
                yield return Settle(2f);
                Vector3 after = vase.transform.position;
                Log("carry test: vase moved " + (after - before).magnitude.ToString("0.0") + " m and rests at height " + after.y.ToString("0.00") +
                    (after.y < -0.3f ? "  PROBLEM: it fell through the floor" : ""));
            }

            // Evening.
            chores.CompleteAll();
            OnChoreDone();
            script.AutoWrite(day);
            yield return Settle(1f);
            yield return Pose(R("living room", 2.3f, -1.3f), R("living room", 6.6f, -5f), 6f, "evening living room");
            yield return Pose(new Vector3(0f, 0f, 6.6f), new Vector3(0f, 0f, -6f), 0f, "evening hall");
            yield return Pose(new Vector3(3.5f, Outdoors.Ground, 26f), new Vector3(0f, 2f, 0f), 4f, "evening outside from the drive", 1.8f);
            yield return Pose(new Vector3(0f, 0f, 6.6f), new Vector3(0f, 0f, -6f), 0f, "evening back inside", 1.6f);

            // The cellar.
            player.Teleport(house.cellarSpawn, 180f);
            RenderSettings.ambientLight = new Color(0.06f, 0.055f, 0.05f);
            RenderSettings.fogDensity = 0.06f;
            yield return Settle(0.5f);
            player.LookPitch(14f);
            yield return Settle(0.2f);
            Shot("cellar");

            // Every monster form, in the hall by day so the shape reads.
            house.SetTime(0, 1);
            Vector3 eye = new Vector3(0f, 0f, 1.2f), spot = new Vector3(0f, 0f, -1.4f);
            var forms = FearEngine.Slots[FearEngine.Kind].options;
            for (int k = 0; k < forms.Length; k++)
            {
                var look = new int[Monster.Traits];
                look[Monster.Kind] = k;
                yield return MonsterShot(look, eye, spot, "monster " + forms[k].label);
            }
            // A few of the anatomy traits, on the plain form.
            int[][] variants =
            {
                new[] { 0, 0, 5, 1, 0, 0, 0 },      // arms bending the wrong way, pale eyes
                new[] { 0, 3, 0, 4, 0, 0, 0 },      // too many heads, too many eyes
                new[] { 3, 0, 1, 3, 0, 0, 0 },      // on all fours, arms to the floor, a smile
                new[] { 2, 2, 2, 2, 0, 0, 0 },      // too tall, long neck, too many arms, a mouth
                new[] { 0, 0, 0, 2, 0, 9, 0 },      // the doctor
                new[] { 4, 1, 3, 0, 0, 2, 0 },      // a child-sized doll with its head over, reaching for you
            };
            foreach (var v in variants)
                yield return MonsterShot(v, eye, spot, "monster " + DescribeVariant(v));
            // Close enough to see the skin.
            Vector3 near = new Vector3(0f, 0f, -0.1f);
            foreach (int k in new[] { Monster.Person, Monster.Doll, Monster.Clown, Monster.Spider, Monster.Snake, Monster.Hound, Monster.Drowned, Monster.Her })
            {
                var look = new int[Monster.Traits];
                look[Monster.Kind] = k;
                look[Monster.Face] = k == Monster.Person ? 1 : 0;
                yield return MonsterShot(look, near, spot, "monster close " + forms[k].label);
            }

            // It was right behind you: stand in the hall, let it arrive, turn round.
            player.Teleport(new Vector3(0f, 0.05f, 1.5f), 0f);
            player.LookPitch(2f);
            yield return Settle(0.4f);
            if (haunt.ForceBehind())
            {
                yield return Settle(1.2f);
                player.Turn(180f);
                player.LookPitch(-4f);
                yield return Settle(0.12f);
                Shot("it was right behind you");
                yield return Settle(1.5f);
            }
            else Log("note: no room behind the player for the 'right behind you' test");

            // The hallucinations (each forced, then photographed in the moment it is there).
            yield return Pose(R("living room", 2.3f, -1.3f), R("living room", 6.6f, -5f), 4f, "before corner of the eye", 0.3f);
            if (halluc.Force(Hallucinations.CornerOfEye)) { yield return Settle(0.15f); Shot("hallucination in the corner of your eye"); yield return Settle(0.2f); halluc.Clear(); }
            else Log("note: no clear spot for 'in the corner of your eye' from the living room");
            yield return Pose(new Vector3(-0.9f, 0f, 0.6f), new Vector3(1.5f, 0f, 4f), 0f, "before doorway", 0.3f);
            if (halluc.Force(Hallucinations.Doorway)) { yield return Settle(0.15f); Shot("hallucination it in a doorway"); yield return Settle(0.2f); halluc.Clear(); }
            else Log("note: no doorway in view for 'it in a doorway'");
            yield return Pose(new Vector3(0f, 0f, -6.6f), new Vector3(0f, 0f, 6f), 0f, "before the hall stretches", 0.3f);
            if (halluc.Force(Hallucinations.HallStretches)) { yield return Settle(2.7f); Shot("hallucination the hall stretches"); yield return Settle(1.6f); halluc.Clear(); }
            else Log("note: 'the hall stretches' needs the player in the hall facing along it");
            if (house.paintings.Count > 0)
            {
                var pic = house.paintings[0].transform;
                Vector3 into = Vis.Flat(house.RoomAt(pic.position).center - pic.position);
                Vector3 stand = pic.position + into * 2.4f; stand.y = 0f;
                yield return Pose(stand, pic.position, -2f, "before painting", 0.3f);
                if (halluc.Force(Hallucinations.PaintingLooks)) { yield return Settle(0.3f); Shot("hallucination a painting looks back"); halluc.Clear(); }
                else Log("note: the painting wasn't in view for 'a painting looks back'");
            }
            house.SetTime(1, 5);
            yield return Pose(R("living room", 4.5f, -4.4f), house.InRoom("living room", new Vector3(4.5f, 1.3f, -7.88f)), 0f, "before window", 0.6f);
            if (halluc.Force(Hallucinations.Reflection)) { yield return Settle(0.4f); Shot("hallucination something outside the window"); halluc.Clear(); }
            else Log("note: no window in view for 'a reflection in the window'");
            house.SetTime(0, 1);

            // The storyteller, left to run an evening on day 5 with a stand-in player who is frightened by each fright
            // (sounds frighten this one more than sights), to check the pacing and that it learns.
            yield return StorytellerSoak();
            yield return NewFrights();

            // Every dream space.
            phase = Phase.Dream;
            dream.Begin(5);
            var spaces = FearEngine.Slots[FearEngine.Space].options;
            for (int k = 0; k < spaces.Length; k++)
            {
                dream.ShowSpace(k);
                hud.fade = hud.fadeTarget = 0f;
                yield return Settle(0.8f);
                player.LookPitch(4f);
                yield return Settle(0.2f);
                Shot("dream " + spaces[k].label);
            }
            dream.Clear();

            // The rooms in other orders: layouts 0, 7, 16 and 23 put every room in every corner once. Each is checked
            // (doors, doorways, windows, somewhere to stand, nothing inside anything) and every room photographed from its doorway.
            phase = Phase.Morning;
            int layoutProblems = house.problems.Count;
            Log("layout " + house.Layout + " (the tour's own: " + house.LayoutDescription + "): " + (house.problems.Count == 0 ? "makes sense" : house.problems.Count + " problems"));
            foreach (int L in new[] { 0, 7, 16, 23 })
            {
                RebuildHouse(L);
                haunt.paused = true;
                yield return Settle(2.5f);
                layoutProblems += house.problems.Count;
                Log("layout " + L + " (" + house.LayoutDescription + "): " + (house.problems.Count == 0 ? "makes sense" : house.problems.Count + " problems"));
                foreach (var name in House.RoomNames)
                {
                    var r = house.Find(name);
                    float sx = Mathf.Sign(r.center.x);
                    Vector3 at = new Vector3(sx * 2.0f, 0f, r.center.z > 0f ? 4f : -4f);       // just inside its door
                    yield return Pose(at, r.center + new Vector3(sx * 1.5f, 0f, 0f), 6f, "layout " + L + " " + name, 0.3f);
                }
            }
            summary.Append("  \"layout_problems\": ").Append(layoutProblems).Append(",\n");

            summary.Append("  \"mode\": \"tour\",\n  \"shots\": ").Append(shots.Count).Append(",\n");
            summary.Append("  \"movables\": ").Append(house.movables.Count).Append(",\n  \"fell_through_floor\": ").Append(fell).Append(",\n");
        }

        IEnumerator StorytellerSoak()
        {
            day = 5;
            phase = Phase.Evening;
            house.SetTime(1, day);
            haunt.BeginDay(day);
            hud.modal = Modal.None;
            player.frozen = false;
            player.Teleport(new Vector3(0f, 0.05f, 0f), 0f);
            haunt.paused = false;
            Time.timeScale = 3f;
            float until = Time.realtimeSinceStartup + 110f;
            int fired = 0;
            FearCore.Incident seen = null;
            var beats = new List<string>();
            var lastBeat = teller.beat;
            while (Time.realtimeSinceStartup < until)
            {
                var inc = teller.LastFired;
                if (inc != null && inc != seen) { seen = inc; fired++; }
                float since = Time.time - teller.LastStimulus;
                bool sound = seen != null && System.Array.IndexOf(seen.tags, "sound") >= 0;
                Reactions.Override = Mathf.Clamp01(0.05f + (sound ? 0.75f : 0.3f) * Mathf.Exp(-since / 2.5f) + Random.Range(-0.04f, 0.04f));
                if (teller.beat != lastBeat) { lastBeat = teller.beat; beats.Add(lastBeat.ToString()); }
                player.Turn(15f * Time.deltaTime);
                yield return null;
            }
            Reactions.Override = null;
            Time.timeScale = 1f;
            Log("storyteller soak: " + fired + " frights in " + (110f * 3f).ToString("0") + " game seconds; beats " + string.Join(" > ", beats.ToArray()) + "; " + teller.Status());
            Log("storyteller soak: " + teller.learner.Describe());
            haunt.ClearTransient();
            uncanny.BeginDay(1);
            haunt.paused = true;
            phase = Phase.Morning;
            day = 1;
            house.SetTime(0, 1);
        }

        // The quieter frights and the nights, each forced and photographed in its moment.
        IEnumerator NewFrights()
        {
            day = 5;
            Vector3 mirror = house.hallMirror != null ? house.hallMirror.transform.position : new Vector3(-1.35f, 1.68f, -2.1f);
            yield return Pose(new Vector3(-0.15f, 0f, -2.1f), mirror - Vector3.up * 0.12f, 0f, "the hall mirror", 0.5f);
            if (uncanny.Force("mirrorbehind")) { yield return Settle(0.7f); Shot("in the mirror something behind you"); yield return Settle(0.2f); }
            uncanny.BeginDay(5);
            house.SetTime(1, 5);
            yield return Pose(R("living room", 4.6f, -3.2f), house.tvScreen != null ? house.tvScreen.transform.position : R("living room", 4.6f, -0.6f), 6f, "before the television", 0.3f);
            if (uncanny.Force("tv")) { yield return Settle(2.5f); Shot("the television on by itself"); }
            yield return Pose(R("study", -4.0f, 4.2f), house.projection != null ? house.projection.transform.position : R("study", -2f, 0.2f), 0f, "before the projector", 0.3f);
            if (uncanny.Force("projector")) { yield return Settle(5f); Shot("the home movie"); }
            uncanny.BeginDay(5);
            house.SetTime(0, 5);
            uncanny.Force("stranger");
            yield return Pose(new Vector3(0.3f, 0f, 2.2f), new Vector3(1.38f, 1.55f, 2.2f), 0f, "a stranger in the family photograph", 0.3f);
            uncanny.Force("facedown");
            yield return Pose(new Vector3(0.2f, 0f, -1.7f), new Vector3(-1.15f, 0.95f, -1.7f), 25f, "photographs face down", 0.6f);
            uncanny.Force("bed");
            yield return Pose(R("bedroom", 4.2f, 3.6f), R("bedroom", 6.3f, 5.6f), 25f, "the bed slept in", 0.3f);
            uncanny.BeginDay(5);

            // Sleep paralysis, from the pillow: the shadow in the doorway, then its face over yours.
            phase = Phase.Evening;
            house.SetTime(1, 5);
            var sp = StartCoroutine(nights.Paralysis());
            yield return Settle(11f);
            Shot("sleep paralysis a shadow in the doorway");
            yield return Settle(10f);
            Shot("sleep paralysis it comes closer");
            yield return Settle(10f);
            Shot("sleep paralysis on your chest");
            yield return sp;
            hud.fade = hud.fadeTarget = 0f;

            // The false awakening: a morning that isn't.
            phase = Phase.Morning;
            var fa = StartCoroutine(nights.FalseMorning(5));
            yield return Settle(2.5f);
            Shot("false awakening it looks like morning");
            yield return Pose(new Vector3(0f, 0f, -3.5f), new Vector3(0f, 1.3f, 3.6f), 0f, "false awakening something in the hall", 0.1f);
            yield return Settle(1.15f);
            Shot("false awakening then it is in front of you");
            yield return fa;
            hud.fade = hud.fadeTarget = 0f;
            player.frozen = false;
            phase = Phase.Morning;
            day = 1;
            house.SetTime(0, 1);
            uncanny.BeginDay(1);
            haunt.paused = true;
        }

        string DescribeVariant(int[] v)
        {
            var parts = new List<string>();
            for (int i = 0; i < v.Length; i++)
                if (v[i] != 0) parts.Add(FearEngine.Slots[FearEngine.LookSlots[i]].options[v[i]].label);
            return string.Join(", ", parts.ToArray());
        }

        IEnumerator MonsterShot(int[] look, Vector3 eye, Vector3 spot, string name)
        {
            var m = Monster.Create(null, look);
            m.transform.position = spot;
            m.FaceToward(eye);
            player.Teleport(eye + Vector3.up * 0.05f, YawTo(eye, spot));
            player.LookPitch(look[Monster.Body] == 2 ? -8f : 4f);
            yield return Settle(0.5f);
            Shot(name);
            Destroy(m.gameObject);
            yield return null;
        }

        // ---------------------------------------------------------------- bot playthroughs

        IEnumerator WaitFor(System.Func<bool> done, float limit, string what)
        {
            float until = Time.realtimeSinceStartup + limit;
            while (!done())
            {
                if (Time.realtimeSinceStartup > until) { Debug.LogError("[NightPages] test run STUCK waiting for " + what + " (phase " + phase + ", day " + day + ")"); yield break; }
                yield return null;
            }
        }

        // ---------------------------------------------------------------- playing it for real

        int playProblems;

        void Problem(string what)
        {
            playProblems++;
            Log("PLAYTHROUGH PROBLEM: " + what);
        }

        static Vector3 CenterOf(Interactable it)
        {
            var c = it.GetComponentInChildren<Collider>();
            return c != null ? c.bounds.center : it.transform.position;
        }

        // Somewhere a person could stand to use something at `c`: clear floor, within reach, same room (or both outside).
        Vector3? StandNear(Vector3 c)
        {
            player.Solid(false);
            try { return StandSpot(c); }
            finally { player.Solid(true); }
        }

        Vector3? StandSpot(Vector3 c)
        {
            bool outside = house.Outside(c);
            Room room = outside ? null : house.RoomAt(c);
            Vector3? best = null;
            float bestD = float.MaxValue;
            float floorY = outside ? Outdoors.Ground : 0f;
            for (float dist = 0.65f; dist <= 1.45f; dist += 0.2f)
                for (int k = 0; k < 16; k++)
                {
                    Vector3 p = new Vector3(c.x, floorY, c.z) + Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward * dist;
                    if (house.Outside(p) != outside || (!outside && house.RoomAt(p) != room)) continue;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.6f, 0.34f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float d = (p - player.transform.position).sqrMagnitude + dist * 4f;
                    if (d < bestD) { bestD = d; best = p; }
                }
            return best;
        }

        // The way there: through the rooms by the doorways, out the front door and down the drive for the yard.
        List<Vector3> Route(Vector3 to)
        {
            Vector3 from = player.transform.position;
            bool fromOut = house.Outside(from), toOut = house.Outside(to);
            var outsideLeg = new List<Vector3> { new Vector3(0f, 0f, 9.3f), new Vector3(0f, Outdoors.Ground, 12.6f), new Vector3(-1.4f, Outdoors.Ground, 17.2f), new Vector3(1.6f, Outdoors.Ground, 23.2f) };
            var pts = new List<Vector3>();
            if (!fromOut && !toOut) pts.AddRange(house.Path(from, to));
            else if (!fromOut)
            {
                pts.AddRange(house.Path(from, house.hallNorth));
                foreach (var p in outsideLeg) if (p.z < to.z + 2f) pts.Add(p);
                pts.Add(to);
            }
            else if (!toOut)
            {
                for (int i = outsideLeg.Count - 1; i >= 0; i--) if (outsideLeg[i].z < from.z - 1f) pts.Add(outsideLeg[i]);
                pts.Add(house.hallNorth);
                pts.AddRange(house.Path(house.hallNorth, to));
            }
            else pts.Add(to);
            return pts;
        }

        // A* over a 0.25 m grid of places a person fits (a capsule check at each), within the box around both ends.
        List<Vector3> GridPath(Vector3 from, Vector3 to)
        {
            // The player's own body would otherwise block every cell round the start.
            player.Solid(false);
            try { return GridSearch(from, to); }
            finally { player.Solid(true); }
        }

        List<Vector3> GridSearch(Vector3 from, Vector3 to)
        {
            const float C = 0.25f;
            float y = from.y;
            Vector3 lo = Vector3.Min(from, to) - new Vector3(2.5f, 0f, 2.5f), hi = Vector3.Max(from, to) + new Vector3(2.5f, 0f, 2.5f);
            int nx = Mathf.CeilToInt((hi.x - lo.x) / C) + 1, nz = Mathf.CeilToInt((hi.z - lo.z) / C) + 1;
            if (nx * nz > 40000) return null;
            var free = new sbyte[nx * nz];                   // 0 unknown, 1 free, -1 blocked
            System.Func<int, int, bool> ok = (x, z) =>
            {
                if (x < 0 || z < 0 || x >= nx || z >= nz) return false;
                int i = z * nx + x;
                if (free[i] == 0)
                {
                    var p = new Vector3(lo.x + x * C, y, lo.z + z * C);
                    // As wide as the player really is (capsule 0.3 m plus the controller's skin), so the path never squeezes
                    // past an open door leaf or a chair with room only for something thinner.
                    bool blocked = Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.5f, 0.36f, ~0, QueryTriggerInteraction.Ignore);
                    free[i] = (sbyte)(blocked ? -1 : 1);
                }
                return free[i] > 0;
            };
            int sx = Mathf.RoundToInt((from.x - lo.x) / C), sz = Mathf.RoundToInt((from.z - lo.z) / C);
            int tx = Mathf.RoundToInt((to.x - lo.x) / C), tz = Mathf.RoundToInt((to.z - lo.z) / C);
            free[sz * nx + sx] = 1;
            free[tz * nx + tx] = 1;
            var g = new float[nx * nz];
            var came = new int[nx * nz];
            for (int i = 0; i < g.Length; i++) { g[i] = float.MaxValue; came[i] = -1; }
            var open = new List<int>();
            int start = sz * nx + sx, goal = tz * nx + tx;
            g[start] = 0f;
            open.Add(start);
            int guard = 0;
            while (open.Count > 0 && guard++ < 20000)
            {
                int bi = 0;
                float bf = float.MaxValue;
                for (int k = 0; k < open.Count; k++)
                {
                    int c = open[k];
                    float f = g[c] + Vector2.Distance(new Vector2(c % nx, c / nx), new Vector2(tx, tz));
                    if (f < bf) { bf = f; bi = k; }
                }
                int cur = open[bi];
                open.RemoveAt(bi);
                if (cur == goal) break;
                int cx = cur % nx, cz = cur / nx;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int ax = cx + dx, az = cz + dz;
                        if (!ok(ax, az)) continue;
                        if (dx != 0 && dz != 0 && (!ok(cx + dx, cz) || !ok(cx, cz + dz))) continue;      // no cutting corners
                        int ni = az * nx + ax;
                        float ng = g[cur] + (dx != 0 && dz != 0 ? 1.414f : 1f);
                        if (ng < g[ni]) { g[ni] = ng; came[ni] = cur; if (!open.Contains(ni)) open.Add(ni); }
                    }
            }
            if (came[goal] < 0 && goal != start) return null;
            var path = new List<Vector3>();
            for (int c = goal; c != start && c >= 0; c = came[c]) path.Insert(0, new Vector3(lo.x + (c % nx) * C, y, lo.z + (c / nx) * C));
            return path;
        }

        // A door in the way, if there is one: whatever the rays toward `p` hit first, at a few heights.
        Door DoorAhead(Vector3 p)
        {
            Vector3 dir = Vis.Flat(p - player.transform.position).normalized;
            foreach (float y in new[] { 0.6f, 1.0f, 1.5f })
            {
                RaycastHit hit;
                if (Physics.Raycast(player.transform.position + Vector3.up * y, dir, out hit, 1.4f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var d = hit.collider.GetComponentInParent<Door>();
                    if (d != null) return d;
                }
            }
            return null;
        }

        // Open a shut door the way a player does (E), standing back while it swings.
        IEnumerator OpenDoor(Door d)
        {
            d.Set(true, false);
            Vector3 back = player.transform.position - Vis.Flat(d.transform.position - player.transform.position).normalized * 0.7f;
            for (float t = 0f; t < 0.9f; t += Time.deltaTime) { player.WalkToward(back, 1.5f); yield return null; }
        }

        IEnumerator WalkTo(Vector3 dest, string what)
        {
            if (house.InCellar(player.transform.position))
            {
                // Up the cellar stairs first, the way a player has to.
                house.cellarStairs.use();
                yield return WaitFor(() => !busy && !house.InCellar(player.transform.position), 15f, "the cellar stairs");
            }
            // Open the doors of the room you're in and the room you're going to, as a player would on the way.
            foreach (var r in new[] { house.RoomAt(player.transform.position), house.RoomAt(dest) })
                if (r != null && r.door != null && !r.door.open && !house.Outside(dest)) r.door.Set(true, true);
            var legs = Route(dest);
            // Cross the destination room by its middle, not straight from the doorway.
            Room destRoom = house.Outside(dest) ? null : house.RoomAt(dest);
            if (destRoom != null && destRoom != house.hall && legs.Count >= 2)
            {
                var mid = house.Middle(destRoom);
                if (mid.HasValue && house.RoomAt(legs[legs.Count - 2]) == destRoom && (legs[legs.Count - 2] - mid.Value).sqrMagnitude > 0.01f) legs.Insert(legs.Count - 1, mid.Value);
            }
            Vector3 at = player.transform.position;
            foreach (var leg in legs)
            {
                // Indoors, each leg is walked by a path round the furniture; outdoors, straight.
                List<Vector3> steps = null;
                if (!house.Outside(leg) && !house.Outside(at)) steps = GridPath(new Vector3(at.x, 0f, at.z), new Vector3(leg.x, 0f, leg.z));
                if (steps == null || steps.Count == 0) steps = new List<Vector3> { leg };
                foreach (var p in steps)
                {
                    float stuck = 0f, best = float.MaxValue;
                    int doors = 0, sidesteps = 0;
                    while (true)
                    {
                        float d = player.WalkToward(p, 2.8f);
                        if (d < 0.2f) break;
                        if (d < best - 0.03f) { best = d; stuck = 0f; } else stuck += Time.deltaTime;
                        if (stuck > 0.6f)
                        {
                            var door = DoorAhead(p);
                            if (door != null && !door.open && doors < 2) { doors++; yield return OpenDoor(door); stuck = 0f; best = float.MaxValue; }
                            else if (sidesteps < 3)
                            {
                                sidesteps++;
                                Vector3 fwd = Vis.Flat(p - player.transform.position).normalized;
                                Vector3 aside = player.transform.position + Vector3.Cross(Vector3.up, fwd) * (sidesteps % 2 == 1 ? -0.6f : 0.6f);
                                for (float s = 0f; s < 0.5f; s += Time.deltaTime) { if (player.WalkToward(aside, 2f) < 0.1f) break; yield return null; }
                                stuck = 0f; best = float.MaxValue;
                            }
                            else
                            {
                                Problem("stuck on the way to " + what + " at " + player.transform.position.ToString("0.0") + " heading for " + p.ToString("0.0") + " (leg to " + leg.ToString("0.0") + ")");
                                player.Teleport(p + Vector3.up * 0.05f, 0f);
                                break;
                            }
                        }
                        yield return null;
                    }
                }
                at = player.transform.position;
            }
        }

        // Walk to it, look at it, and use it with the same ray a player's E uses.
        IEnumerator GoUse(Interactable target, string what, bool use = true)
        {
            if (target == null) { Problem(what + ": isn't in the house"); yield break; }
            Vector3 c = CenterOf(target);
            var stand = StandNear(c);
            if (!stand.HasValue) { Problem(what + ": nowhere to stand within reach of it"); if (use && target.use != null) target.use(); yield break; }
            yield return WalkTo(stand.Value, what);
            player.Aim(c);
            yield return null;
            var seen = player.LookingAt();
            if (seen != target)
                for (int k = 0; k < 4 && seen != target; k++) { player.Aim(c + new Vector3(0f, (k - 1.5f) * 0.12f, 0f)); yield return null; seen = player.LookingAt(); }
            if (seen != target)
                Problem(what + ": can't be used from " + player.transform.position.ToString("0.0") + " (" + (seen != null ? "your aim lands on " + seen.name : player.Probe()) + ")");
            if (use && target.use != null) target.use();
            yield return null;
        }

        IEnumerator DoChores()
        {
            foreach (var c in new List<Chore>(chores.today))
            {
                if (c.done || phase != Phase.Morning) continue;
                Interactable target;
                if (!house.choreTargets.TryGetValue(c.id, out target)) { Problem("chore '" + c.text + "' has nothing to use"); continue; }
                yield return GoUse(target, c.text);
                if (!c.done) Problem("chore '" + c.text + "': using it didn't do it");
                hud.modal = Modal.None;
            }
        }

        IEnumerator BotRuns(StringBuilder summary)
        {
            var endings = new List<string>();
            bool watching = !Application.isBatchMode;       // the "watch" job: show the run at a pace you can follow
            Time.timeScale = watching ? 1.5f : 3f;
            for (int run = 1; run <= Tour.Runs; run++)
            {
                Log("bot: run " + run + " of " + Tour.Runs);
                yield return StartRunForTest();
                // Some runs face everything, some look away from everything: the endings should differ.
                bool brave = run % 2 == 1;
                float started = Time.realtimeSinceStartup;
                while (phase != Phase.Ending)
                {
                    if (Time.realtimeSinceStartup - started > 480f) { Debug.LogError("[NightPages] bot run " + run + " took too long; stopped on day " + day); break; }
                    if (busy) { Tour.Step("bot: waiting while busy, phase " + phase + ", day " + day); yield return null; continue; }
                    Tour.Step("bot: acting in phase " + phase + ", day " + day);
                    switch (phase)
                    {
                        case Phase.Morning:
                            if (watching)
                            {
                                // Walk the house: a few seconds in each room before the chores are done.
                                yield return Pose(new Vector3(0f, 0f, 6.6f), new Vector3(0f, 0f, -6f), 0f, "watch hall", 2.5f);
                                yield return Pose(R("kitchen", -2.4f, -1.4f), R("kitchen", -6.4f, -5.2f), 6f, "watch kitchen", 2.5f);
                                yield return Pose(R("living room", 2.3f, -1.3f), R("living room", 6.6f, -5f), 6f, "watch living room", 2.5f);
                                yield return Pose(R("study", -2.3f, 2.3f), R("study", -6.3f, 4.2f), 8f, "watch study", 2.5f);
                                if (day == 1)
                                {
                                    // Out to the porch and the yard, to hear the woods and see the house from outside.
                                    yield return Pose(new Vector3(0f, 0f, 10f), new Vector3(1.5f, 0f, 30f), 2f, "watch porch", 3f);
                                    yield return Pose(new Vector3(3.5f, Outdoors.Ground, 26f), new Vector3(0f, 2f, 0f), 4f, "watch drive", 3f);
                                    yield return Pose(new Vector3(-16f, Outdoors.Ground, -16f), new Vector3(-4f, 1f, -4f), 3f, "watch back yard", 3f);
                                }
                            }
                            // Do the chores the way a player would: walk there, look at the thing, use it.
                            yield return DoChores();
                            if (phase == Phase.Morning && !chores.AllDone)
                            {
                                Problem("day " + day + ": chores still undone after walking to every one");
                                chores.CompleteAll();
                                OnChoreDone();
                            }
                            break;
                        case Phase.Afternoon:
                            yield return GoUse(house.typewriter, "the typewriter", false);
                            script.AutoWrite(day);
                            break;
                        case Phase.Evening:
                            if (brave)
                            {
                                foreach (var n in notes) if (!n.read) ReadNote(n);
                                hud.modal = Modal.None;
                                player.frozen = false;
                                if (day >= 6) UseCellar();
                            }
                            yield return GoUse(house.bed, "the bed", false);
                            int sleptOn = day;
                            StartCoroutine(Sleep());
                            // (a dream can end almost at once if it reaches you, so a new day also counts as "it happened")
                            yield return WaitFor(() => (phase == Phase.Dream && !busy) || day != sleptOn || phase == Phase.Ending, 30f, "the dream to start");
                            // A stand-in reaction that rises and falls, so the fear engine has something to learn from.
                            float dreamUntil = Time.time + 25f;
                            while (Time.time < dreamUntil && dream.active && phase == Phase.Dream)
                            {
                                Reactions.Override = Mathf.Clamp(0.25f + 0.6f * Mathf.Sin(Time.time * 0.7f) + Random.Range(-0.2f, 0.2f), -1f, 1f);
                                if (watching) player.Turn(20f * Time.deltaTime);         // look slowly around the dream
                                yield return null;
                            }
                            Reactions.Override = null;
                            if (dream.active && phase == Phase.Dream) dream.ForceEnd();
                            yield return WaitFor(() => !busy && phase != Phase.Dream, 30f, "waking up");
                            break;
                    }
                    yield return null;
                }
                string ending = hud.header;
                endings.Add(ending);
                Log("bot: run " + run + " played through: " + playProblems + " playthrough problems so far");
                Log("bot: run " + run + " (" + (brave ? "faced everything" : "looked away") + ") ended: " + ending + ", truth " + truth + ", grip " + grip.ToString("0.00") + ", clues " + clues);
                yield return Settle(0.5f);
            }
            Time.timeScale = 1f;
            summary.Append("  \"playthrough_problems\": ").Append(playProblems).Append(",\n");
            summary.Append("  \"mode\": \"bot\",\n  \"runs\": ").Append(Tour.Runs).Append(",\n  \"endings\": [");
            for (int i = 0; i < endings.Count; i++) summary.Append(i > 0 ? ", " : "").Append('"').Append(endings[i]).Append('"');
            summary.Append("],\n");
            var top = fear.TopFamily();
            summary.Append("  \"top_kind_of_fear\": \"").Append(top == null ? "none yet" : FearTypes.Name(top)).Append("\",\n");
        }
    }
}
