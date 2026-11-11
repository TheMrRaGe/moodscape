using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The night. Builds a dream space from the fear engine's current choice and makes each change real
    /// when the engine asks for it: a different space, a presence, something wrong with the room, the
    /// light, the sound, how things move. New things appear only where the player isn't looking.
    /// A door appears after a while; walking through it wakes you up.
    /// </summary>
    public class Dream : MonoBehaviour
    {
        public static readonly Vector3 Origin = new Vector3(0f, -300f, 0f);
        public bool active;

        class Pending { public Vector3 pos; public System.Func<Vector3, Transform> make; }

        GameDirector g;
        Transform root, space, wallsT, dyn;
        int day;
        float doorAt, hardEnd, hx, hz, ceil, moveTimer, texTimer, stepTimer;
        bool roofed, ending, exitMoved, cutting;
        int stepsOwed;
        Transform exit, window;
        Monster monster;
        float shrink = 1f;
        Transform figure { get { return monster != null ? monster.transform : null; } }
        Vector3 mirrorAnchor;
        Light lamp;
        AudioSource loop;
        bool loopFollows;
        readonly List<Transform> props = new List<Transform>();
        readonly List<Pending> pending = new List<Pending>();
        Material paintMat;
        DreamMaze maze;
        MazeStalker stalker;
        float deadline, nightLength, timerShown;
        int warned;
        Texture2D fallback;
        Color[] fallbackPx;

        int[] Cur { get { return g.fear.current; } }
        Camera Cam { get { return g.player.cam; } }
        Vector3 PlayerPos { get { return g.player.transform.position; } }

        public void Init(GameDirector director)
        {
            g = director;
            fallback = new Texture2D(48, 48, TextureFormat.RGB24, false);
            fallback.filterMode = FilterMode.Bilinear;
            fallbackPx = new Color[48 * 48];
            PaintFallback(0f);
        }

        /// <summary>The texture hung on walls: the live Moodscape painting, or a stand-in when the painter is off.</summary>
        /// <summary>The maze this dream is (see DreamMaze), while one is running.</summary>
        public DreamMaze Maze { get { return maze; } }

        /// <summary>The thing hunting in the maze (see MazeStalker).</summary>
        public MazeStalker Stalker { get { return stalker; } }

        /// <summary>Seconds of night left to get out in.</summary>
        public float TimeLeft { get { return maze != null ? Mathf.Max(0f, deadline - Time.time) : 0f; } }

        /// <summary>More night (a page read), or less.</summary>
        public void AddTime(float seconds) { deadline += seconds; }

        public Texture PaintingTexture { get { return g.link.HasFrame && g.link.Live ? (Texture)g.link.Tex : fallback; } }

        void PaintFallback(float t)
        {
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++)
                {
                    float a = Mathf.PerlinNoise(x * 0.09f + t * 0.21f, y * 0.09f - t * 0.13f);
                    float b = Mathf.PerlinNoise(x * 0.21f - t * 0.1f + 40f, y * 0.21f + 9f);
                    float v = Mathf.Clamp01(a * 1.3f - 0.3f);
                    fallbackPx[y * 48 + x] = new Color(0.05f + 0.5f * v * b, 0.02f + 0.08f * v, 0.03f + 0.12f * a * (1f - b));
                }
            fallback.SetPixels(fallbackPx);
            fallback.Apply();
        }

        public void Begin(int newDay)
        {
            day = newDay;
            active = true;
            ending = false;
            exitMoved = false;
            cutting = false;
            exit = window = null;
            monster = null;
            root = new GameObject("Dream").transform;
            root.position = Origin;
            dyn = new GameObject("Dynamic").transform;
            dyn.SetParent(root, false);
            lamp = Build.Lamp("Dream light", root, new Vector3(0f, 2.4f, 0f), Color.white, 1.8f, 22f);
            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.transform.SetParent(root, false);
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.62f, 0.85f);
            moon.intensity = 0.22f;
            moon.transform.rotation = Quaternion.Euler(52f, 35f, 0f);
            paintMat = Build.TexMat(PaintingTexture);

            // Move the player in first: everything below is placed relative to where they stand.
            g.player.Teleport(Origin + new Vector3(0f, 0.1f, 0f), 0f);
            g.fear.onApply = OnEngineApply;
            g.fear.canChange = s => !FearEngine.IsLook(s) || (monster != null && !monster.Pending);
            g.fear.BeginDream(day, Time.time);
            BuildMaze();
            for (int s = 1; s < FearEngine.Slots.Length; s++) ApplySlot(s, Cur[s]);

            doorAt = Time.time + 40f + 6f * day;
            hardEnd = Time.time + 170f;
        }

        // The dream is a maze of rooms that rearranges itself where you aren't looking, bigger on later nights.
        void BuildMaze()
        {
            if (space != null) Destroy(space.gameObject);
            ClearProps();
            space = new GameObject("Space").transform;
            space.SetParent(root, false);
            wallsT = null;
            g.player.speedMul = 1f;
            g.player.Crouch(false);
            shrink = 1f;
            hx = hz = 400f; ceil = DreamMaze.H; roofed = true;
            int size = day <= 2 ? 4 : day <= 4 ? 5 : 6;
            maze = new DreamMaze(g, root, size, day, Random.Range(1, 1000000), paintMat);
            // Hard, not impossible: enough night to cross it twice over, not enough to see all of it.
            nightLength = (60f + size * size * 4.5f) * 1.3f;       // (the extra is for time spent hiding from it)
            QualitySettings.pixelLightCount = Mathf.Max(QualitySettings.pixelLightCount, 8);      // the room bulbs near you all count
            stalker = new MazeStalker(g, maze, dyn, day, Origin.y);
            stalker.onCatch = Die;
            maze.onHide = stalker.Hide;
            deadline = Time.time + nightLength;
            warned = 0;
            timerShown = -1f;
            var fog = new Color(0.02f, 0.016f, 0.016f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.1f, 0.1f, 0.115f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = 0.03f;
            Cam.backgroundColor = fog;
            g.Log("dream: " + nightLength.ToString("0") + " s of night to find the way out");
        }

        public void Clear()
        {
            Tour.Step("dream clear: start");
            active = false;
            if (stalker != null) stalker.Cleanup();
            stalker = null;
            if (maze != null) maze.Destroy();
            maze = null;
            g.fear.onApply = null;
            g.fear.canChange = null;
            monster = null;
            StopAllCoroutines();
            if (root != null) Destroy(root.gameObject);
            if (loop != null) Destroy(loop.gameObject);
            loop = null;
            props.Clear();
            pending.Clear();
            g.player.speedMul = 1f;
            g.player.eyeHeight = 1.65f;
            g.player.Roll = 0f;
            g.player.fovOffset = 0f;
            g.player.Crouch(false);
        }

        public void ForceEnd() { End(false); }

        void End(bool caught)
        {
            if (ending) return;
            ending = true;
            g.fear.EndDream();
            g.SaveTeller();
            g.OnDreamEnd(caught);
        }

        // Not getting out is dying: run out of night, or let it reach you.
        void Die(string how)
        {
            if (ending) return;
            ending = true;
            g.fear.EndDream();
            g.SaveTeller();
            g.OnDreamDeath(how);
        }

        void OnEngineApply(int slot, int opt)
        {
            if (slot == FearEngine.Space && maze != null) maze.Shift(PlayerPos, Cam);      // a new "space" is the maze rearranging itself
            else if (slot == FearEngine.Space) StartCoroutine(Cut(opt));
            else ApplySlot(slot, opt);
        }

        // A new space is a hard cut: black for a moment, then you are somewhere else.
        IEnumerator Cut(int opt)
        {
            cutting = true;
            g.hud.fade = 1f;
            g.hud.fadeTarget = 1f;
            yield return new WaitForSeconds(0.3f);
            g.player.Teleport(Origin + new Vector3(0f, 0.1f, 0f), g.player.transform.eulerAngles.y);
            BuildSpace(opt);
            for (int s = 1; s < FearEngine.Slots.Length; s++) ApplySlot(s, Cur[s]);
            if (exit != null) PlaceExit();
            g.hud.fadeSpeed = 2.5f;
            g.hud.fadeTarget = 0f;
            cutting = false;
        }

        // ---------------------------------------------------------------- spaces

        /// <summary>For test captures: show one particular space, whatever the engine would have picked.</summary>
        public void ShowSpace(int k)
        {
            g.fear.current[FearEngine.Space] = k;
            if (maze != null) { maze.Destroy(); maze = null; foreach (Transform c in root) if (c.name == "Maze") Destroy(c.gameObject); }
            BuildSpace(k);
            for (int s = 1; s < FearEngine.Slots.Length; s++) ApplySlot(s, Cur[s]);
            g.player.Teleport(Origin + new Vector3(0f, 0.1f, 0f), 0f);
        }

        /// <summary>For test captures: the monster in this dream, if any.</summary>
        public Monster CurrentMonster { get { return monster; } }

        void BuildSpace(int k)
        {
            if (space != null) Destroy(space.gameObject);
            ClearProps();
            space = new GameObject("Space").transform;
            space.SetParent(root, false);
            wallsT = new GameObject("Walls").transform;
            wallsT.SetParent(space, false);
            g.player.speedMul = 1f;
            g.player.Crouch(false);
            shrink = 1f;
            Color fog;
            float density;

            if (k == 0)             // an endless hallway
            {
                hx = 2f; hz = 80f; ceil = 3f; roofed = true;
                var wall = Build.Mat(new Color(0.36f, 0.3f, 0.26f));
                Build.Box("Floor", space, new Vector3(0f, -0.1f, 0f), new Vector3(4.4f, 0.2f, 164f), new Color(0.16f, 0.1f, 0.08f));
                Build.Box("Ceiling", space, new Vector3(0f, ceil + 0.1f, 0f), new Vector3(4.4f, 0.2f, 164f), new Color(0.2f, 0.18f, 0.17f));
                Build.Box("W", wallsT, new Vector3(-2.1f, 1.5f, 0f), new Vector3(0.2f, 3f, 164f), wall);
                Build.Box("E", wallsT, new Vector3(2.1f, 1.5f, 0f), new Vector3(0.2f, 3f, 164f), wall);
                Build.Box("N", wallsT, new Vector3(0f, 1.5f, 81f), new Vector3(4.4f, 3f, 0.2f), wall);
                Build.Box("S", wallsT, new Vector3(0f, 1.5f, -81f), new Vector3(4.4f, 3f, 0.2f), wall);
                var door = Build.Mat(new Color(0.12f, 0.08f, 0.06f));
                for (int i = -13; i <= 13; i++)
                {
                    float side = (i & 1) == 0 ? -1f : 1f;
                    Build.Box("Door", wallsT, new Vector3(side * 1.97f, 1.05f, i * 6f), new Vector3(0.06f, 2.1f, 1f), door, false);
                    if (i % 4 == 0) Build.Box("Painting", wallsT, new Vector3(-side * 1.97f, 1.7f, i * 6f + 3f), new Vector3(0.05f, 1.2f, 1.7f), paintMat, false);
                }
                fog = new Color(0.02f, 0.015f, 0.015f); density = 0.05f;
            }
            else if (k == 1)        // a flooded cellar
            {
                hx = 25f; hz = 25f; ceil = 2.6f; roofed = true;
                Room4(new Color(0.2f, 0.2f, 0.2f), new Color(0.12f, 0.12f, 0.12f), new Color(0.1f, 0.1f, 0.1f), 3f, 1.6f, 1.4f);
                var pillar = Build.Mat(new Color(0.22f, 0.21f, 0.2f));
                for (int x = -3; x <= 3; x++)
                    for (int z = -3; z <= 3; z++)
                        if (x != 0 || z != 0) Build.Box("Pillar", space, new Vector3(x * 7f, ceil / 2f, z * 7f), new Vector3(0.6f, ceil, 0.6f), pillar);
                Build.Box("Water", space, new Vector3(0f, 0.22f, 0f), new Vector3(50f, 0.02f, 50f), Build.Mat(new Color(0.01f, 0.03f, 0.04f), 0f, 0.95f), false);
                g.player.speedMul = 0.7f;
                fog = new Color(0.03f, 0.05f, 0.06f); density = 0.05f;
            }
            else if (k == 2)        // a childhood bedroom, built for someone much larger than you
            {
                hx = 12f; hz = 12f; ceil = 9f; roofed = true;
                Room4(new Color(0.42f, 0.44f, 0.5f), new Color(0.3f, 0.22f, 0.2f), new Color(0.4f, 0.4f, 0.42f), 6f, 4f, 5f);
                Build.Box("Bed", space, new Vector3(7f, 1.1f, 4f), new Vector3(6f, 2.2f, 11f), new Color(0.45f, 0.5f, 0.6f));
                Build.Box("Pillow", space, new Vector3(7f, 2.5f, 8f), new Vector3(4.5f, 0.6f, 2f), new Color(0.8f, 0.8f, 0.78f));
                Build.Box("Wardrobe", space, new Vector3(-6f, 4f, 10.6f), new Vector3(5.5f, 8f, 2f), new Color(0.26f, 0.17f, 0.11f));
                Build.Box("Chair seat", space, new Vector3(-7f, 1.7f, -5f), new Vector3(3f, 0.3f, 3f), new Color(0.3f, 0.2f, 0.13f));
                Build.Box("Chair back", space, new Vector3(-8.4f, 3.4f, -5f), new Vector3(0.3f, 3.6f, 3f), new Color(0.3f, 0.2f, 0.13f));
                for (int i = 0; i < 4; i++)
                    Build.Box("Chair leg", space, new Vector3(-7f + ((i & 1) == 0 ? -1.3f : 1.3f), 0.8f, -5f + (i < 2 ? -1.3f : 1.3f)), new Vector3(0.3f, 1.6f, 0.3f), new Color(0.3f, 0.2f, 0.13f));
                fog = new Color(0.03f, 0.03f, 0.05f); density = 0.035f;
            }
            else if (k == 4)        // a crawlspace: a ceiling you can't stand up under
            {
                hx = 10f; hz = 10f; ceil = 1.05f; roofed = true;
                Room4(new Color(0.24f, 0.2f, 0.16f), new Color(0.13f, 0.1f, 0.08f), new Color(0.18f, 0.15f, 0.12f), 1.2f, 0.5f, 0.55f);
                var joist = Build.Mat(new Color(0.22f, 0.15f, 0.09f));
                for (int i = -9; i <= 9; i += 2)
                    Build.Box("Joist", space, new Vector3(i, ceil - 0.08f, 0f), new Vector3(0.12f, 0.16f, 20f), joist);
                g.player.Crouch(true);
                fog = new Color(0.02f, 0.017f, 0.014f); density = 0.09f;
            }
            else if (k == 5)        // a walkway over nothing: one plank wide, no rail, no bottom
            {
                hx = 0.6f; hz = 60f; ceil = 6f; roofed = false;
                var plank = Build.Mat(new Color(0.3f, 0.22f, 0.15f));
                Build.Box("Walkway", space, new Vector3(0f, -0.05f, 0f), new Vector3(1.2f, 0.1f, 120f), plank);
                for (int i = -58; i <= 58; i += 4)
                    Build.Box("Post", space, new Vector3((i / 4) % 2 == 0 ? -0.66f : 0.66f, -6f, i), new Vector3(0.1f, 12f, 0.1f), new Color(0.12f, 0.1f, 0.08f));
                fog = new Color(0.03f, 0.03f, 0.045f); density = 0.035f;
            }
            else                    // a field with one door
            {
                hx = 90f; hz = 90f; ceil = 6f; roofed = false;
                Build.Box("Ground", space, new Vector3(0f, -0.1f, 0f), new Vector3(200f, 0.2f, 200f), new Color(0.07f, 0.09f, 0.06f));
                var bark = Build.Mat(new Color(0.05f, 0.045f, 0.04f));
                var rng = new System.Random(day * 31 + 5);
                for (int i = 0; i < 70; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 6f + (float)rng.NextDouble() * 80f, h = 5f + (float)rng.NextDouble() * 5f;
                    Build.Box("Tree", wallsT, new Vector3(Mathf.Cos(a) * r, h / 2f, Mathf.Sin(a) * r), new Vector3(0.28f, h, 0.28f), bark);
                }
                for (int i = 0; i < 4; i++)
                {
                    var q = Build.Box("Sky", space, Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(0f, 16f, 85f), new Vector3(34f, 20f, 0.1f), paintMat, false);
                    q.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                }
                fog = new Color(0.05f, 0.06f, 0.06f); density = 0.03f;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.07f, 0.085f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = density;
            Cam.backgroundColor = fog;
        }

        // A closed square room with a painting on each wall.
        void Room4(Color wallC, Color floorC, Color ceilC, float paintW, float paintH, float paintY)
        {
            var wall = Build.Mat(wallC);
            float w = hx * 2f, d = hz * 2f;
            Build.Box("Floor", space, new Vector3(0f, -0.1f, 0f), new Vector3(w + 0.4f, 0.2f, d + 0.4f), floorC);
            Build.Box("Ceiling", space, new Vector3(0f, ceil + 0.1f, 0f), new Vector3(w + 0.4f, 0.2f, d + 0.4f), ceilC);
            Build.Box("W", wallsT, new Vector3(-hx - 0.1f, ceil / 2f, 0f), new Vector3(0.2f, ceil, d), wall);
            Build.Box("E", wallsT, new Vector3(hx + 0.1f, ceil / 2f, 0f), new Vector3(0.2f, ceil, d), wall);
            Build.Box("N", wallsT, new Vector3(0f, ceil / 2f, hz + 0.1f), new Vector3(w, ceil, 0.2f), wall);
            Build.Box("S", wallsT, new Vector3(0f, ceil / 2f, -hz - 0.1f), new Vector3(w, ceil, 0.2f), wall);
            Build.Box("Painting", wallsT, new Vector3(-hx + 0.03f, paintY, 0f), new Vector3(0.05f, paintH, paintW), paintMat, false);
            Build.Box("Painting", wallsT, new Vector3(hx - 0.03f, paintY, 0f), new Vector3(0.05f, paintH, paintW), paintMat, false);
            Build.Box("Painting", wallsT, new Vector3(0f, paintY, hz - 0.03f), new Vector3(paintW, paintH, 0.05f), paintMat, false);
            Build.Box("Painting", wallsT, new Vector3(0f, paintY, -hz + 0.03f), new Vector3(paintW, paintH, 0.05f), paintMat, false);
        }

        /// <summary>A point on the floor between min and max metres from the player, inside the space.</summary>
        Vector3 RandomPoint(float min, float max, bool preferUnseen)
        {
            if (maze != null) return maze.RandomPoint(PlayerPos, Mathf.Min(min, 30f), Mathf.Min(max, 36f), preferUnseen, Cam);
            Vector3 best = PlayerPos;
            for (int i = 0; i < 40; i++)
            {
                float a = Random.value * Mathf.PI * 2f, r = Random.Range(min, max);
                Vector3 p = PlayerPos + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Vector3 local = p - Origin;
                float mx = Mathf.Min(1.2f, hx * 0.5f), mz = Mathf.Min(1.2f, hz * 0.5f);
                local.x = Mathf.Clamp(local.x, -hx + mx, hx - mx);
                local.z = Mathf.Clamp(local.z, -hz + mz, hz - mz);
                local.y = 0f;
                p = Origin + local;
                if ((p - PlayerPos).magnitude < min * 0.7f) continue;
                best = p;
                if (!preferUnseen || !Vis.Sees(Cam, p + Vector3.up * 1.5f)) break;
            }
            return best;
        }

        // ---------------------------------------------------------------- slots

        void ClearProps()
        {
            foreach (var p in props) if (p != null) Destroy(p.gameObject);
            props.Clear();
            pending.Clear();
        }

        void Queue(Vector3 pos, System.Func<Vector3, Transform> make)
        {
            pending.Add(new Pending { pos = pos, make = make });
        }

        void ApplySlot(int slot, int opt)
        {
            switch (slot)
            {
                case FearEngine.Behavior: ApplyPresence(opt); break;
                case FearEngine.Wrongness: ApplyWrongness(opt); break;
                case FearEngine.Light: ApplyLight(opt); break;
                case FearEngine.Sound: ApplySound(opt); break;
                case FearEngine.Body:
                case FearEngine.Head:
                case FearEngine.Arms:
                case FearEngine.Face:
                case FearEngine.Gait:
                case FearEngine.Kind:
                case FearEngine.Shift:
                    // Its shape is only ever swapped while it is out of sight (see Update).
                    if (monster != null) monster.Want(g.fear.Look());
                    break;
                case FearEngine.Motion:
                    break;
                default:
                    // The rest of its genome: what it stands on, its skin, its head, what else is wrong with it.
                    if (FearEngine.IsLook(slot) && monster != null) monster.Want(g.fear.Look());
                    break;
            }
        }

        void ApplyPresence(int opt)
        {
            if (stalker != null) return;                // in the maze it comes and goes in waves of its own
            if (monster != null) Destroy(monster.gameObject);
            monster = null;
            stepsOwed = 0;
            if (opt < 2) return;
            monster = Monster.Create(dyn, g.fear.Look());
            if (opt == 2) figure.position = RandomPoint(14f, 20f, true);
            else if (opt == 3)
            {
                mirrorAnchor = PlayerPos + g.player.Forward * 7f;
                figure.position = mirrorAnchor * 2f - PlayerPos;
            }
            else if (opt == 5) figure.position = RandomPoint(9f, 13f, false);
            else figure.position = RandomPoint(18f, 28f, true);
        }

        void ApplyWrongness(int opt)
        {
            ClearProps();
            if (opt != 5) shrink = 1f;
            g.player.eyeHeight = opt == 3 ? 1.0f : 1.65f;
            Vector3 c = PlayerPos;
            if (opt == 1)           // too many doors
            {
                var m = Build.Mat(new Color(0.13f, 0.09f, 0.07f));
                for (int i = 0; i < 14; i++)
                {
                    Vector3 pos = RandomPoint(4f, 11f, false);
                    Queue(pos, at =>
                    {
                        var d = Build.Box("Extra door", dyn, Vector3.zero, new Vector3(1f, 2.1f, 0.07f), m, false).transform;
                        d.position = at + Vector3.up * 1.05f;
                        d.rotation = Quaternion.LookRotation(Vis.Flat(c - at));
                        return d;
                    });
                }
            }
            else if (opt == 2)      // furniture on the ceiling
            {
                var m = Build.Mat(new Color(0.28f, 0.19f, 0.12f));
                for (int i = 0; i < 9; i++)
                {
                    Vector3 pos = RandomPoint(2f, 12f, false) + Vector3.up * (ceil - 0.06f);
                    Queue(pos, at =>
                    {
                        var table = new GameObject("Ceiling table").transform;
                        table.SetParent(dyn, false);
                        table.position = at;
                        Build.Box("Top", table, new Vector3(0f, 0f, 0f), new Vector3(1.6f, 0.08f, 1f), m, false);
                        Build.Box("Leg", table, new Vector3(0.7f, 0.4f, 0.4f), new Vector3(0.08f, 0.8f, 0.08f), m, false);
                        Build.Box("Leg", table, new Vector3(-0.7f, 0.4f, 0.4f), new Vector3(0.08f, 0.8f, 0.08f), m, false);
                        Build.Box("Leg", table, new Vector3(0.7f, 0.4f, -0.4f), new Vector3(0.08f, 0.8f, 0.08f), m, false);
                        Build.Box("Leg", table, new Vector3(-0.7f, 0.4f, -0.4f), new Vector3(0.08f, 0.8f, 0.08f), m, false);
                        table.rotation = Quaternion.Euler(180f, Random.value * 360f, 0f);
                        table.position = at - Vector3.up * 0.8f;
                        return table;
                    });
                }
            }
            else if (opt == 4)      // faces turned to the wall
            {
                for (int i = 0; i < 9; i++)
                {
                    Vector3 pos = RandomPoint(6f, 12f, false);
                    Queue(pos, at =>
                    {
                        var f = Build.Figure("Turned away", dyn);
                        f.position = at;
                        f.rotation = Quaternion.LookRotation(Vis.Flat(at - c));
                        return f;
                    });
                }
            }
        }

        void ApplyLight(int opt)
        {
            if (window != null) Destroy(window.gameObject);
            window = null;
            if (opt == 4)
            {
                Vector3 pos = RandomPoint(30f, 45f, false) + Vector3.up * 3f;
                window = Build.Box("Distant window", dyn, Vector3.zero, new Vector3(4f, 5f, 0.1f), new Color(0.9f, 0.85f, 0.6f), false, 2.5f).transform;
                window.position = pos;
                window.rotation = Quaternion.LookRotation(Vis.Flat(PlayerPos - pos));
                Build.Lamp("Window glow", window, new Vector3(0f, 0f, 1.5f), new Color(1f, 0.9f, 0.6f), 1.2f, 10f);
            }
        }

        void ApplySound(int opt)
        {
            if (loop != null) Destroy(loop.gameObject);
            loop = null;
            if (opt == 0) return;
            string clip = FearEngine.SoundClips[opt];
            loopFollows = opt == 1 || opt == 4;
            loop = Sfx.Loop(clip, null, opt == 2 ? 0.7f : 0.55f, true, opt == 2 ? 0.8f : 1f);
            loop.transform.position = loopFollows ? PlayerPos - g.player.Forward * 2f + Vector3.up * 1.5f : RandomPoint(14f, 20f, false) + Vector3.up * 1.5f;
        }

        // ---------------------------------------------------------------- the exit

        void PlaceExit()
        {
            Vector3 pos = RandomPoint(18f, 40f, true);
            if (exit == null)
            {
                exit = new GameObject("Exit door").transform;
                exit.SetParent(root, false);
                var warm = new Color(1f, 0.8f, 0.5f);
                Build.Box("Post", exit, new Vector3(-0.6f, 1.1f, 0f), new Vector3(0.12f, 2.2f, 0.12f), warm, false, 2f);
                Build.Box("Post", exit, new Vector3(0.6f, 1.1f, 0f), new Vector3(0.12f, 2.2f, 0.12f), warm, false, 2f);
                Build.Box("Lintel", exit, new Vector3(0f, 2.26f, 0f), new Vector3(1.32f, 0.12f, 0.12f), warm, false, 2f);
                Build.Box("Light inside", exit, new Vector3(0f, 1.1f, 0f), new Vector3(1.08f, 2.2f, 0.03f), new Color(1f, 0.93f, 0.8f), false, 1.2f);
                Build.Lamp("Door glow", exit, new Vector3(0f, 1.4f, 0f), warm, 1.6f, 9f);
            }
            exit.position = pos;
            exit.rotation = Quaternion.LookRotation(Vis.Flat(PlayerPos - pos));
        }

        // ---------------------------------------------------------------- every frame

        void Update()
        {
            if (!active || g == null || g.hud.modal != Modal.None) return;
            float now = Time.time, dt = Time.deltaTime;
            Vector3 pp = PlayerPos;

            if (stalker != null) monster = stalker.It;
            bool monsterSeen = monster != null && (Vis.Sees(Cam, monster.HeadPos, monster.transform) || Vis.Sees(Cam, monster.transform.position + Vector3.up * 0.6f, monster.transform));
            if (monster != null && monster.ApplyIfUnseen(Cam)) g.Log("dream: it is changing " + (monster.changedInView ? "in front of you" : "while you aren't looking") + ": " + g.fear.DescribeLook(g.fear.Look()));
            if (!ending && !cutting) g.fear.Tick(now, Reactions.Level, Reactions.Watching, monsterSeen);

            UpdateLight(now, pp);
            UpdatePresence(now, dt, pp);

            if (loop != null && loopFollows)
                loop.transform.position = Vector3.Lerp(loop.transform.position, pp - g.player.Forward * 1.8f + Vector3.up * 1.5f, dt * 2f);

            int motion = Cur[FearEngine.Motion];
            if (wallsT != null)
            {
                // The room is shrinking: the walls come in while you aren't facing them, but never onto you.
                if (Cur[FearEngine.Wrongness] == 5 && roofed && hx > 2f)
                {
                    Vector3 local = pp - Origin;
                    float floorS = Mathf.Max(0.35f, (Mathf.Abs(local.x) + 1.4f) / hx, (Mathf.Abs(local.z) + 1.4f) / hz);
                    if (!Vis.Sees(Cam, Origin + new Vector3(Mathf.Sign(local.x) * hx * shrink, 1.2f, local.z)))
                        shrink = Mathf.Max(floorS, shrink - dt * 0.012f);
                }
                float bx = motion == 1 ? 1f + 0.035f * Mathf.Sin(now * 0.9f) : 1f, bz = motion == 1 ? 1f + 0.035f * Mathf.Sin(now * 0.9f + 1f) : 1f;
                var want = new Vector3(shrink * bx, 1f, shrink * bz);
                if (wallsT.localScale != want) wallsT.localScale = want;
            }
            if (!ending && pp.y < Origin.y - 6f)
            {
                Sfx.Ui("sting", 0.9f);
                if (maze != null) { g.Log("dream: fell"); Die("you fell"); return; }
                g.Log("dream: fell from the walkway; waking early");
                End(true);
                return;
            }
            if (motion == 2 && now > moveTimer)
            {
                moveTimer = now + 1.3f;
                foreach (var p in props)
                {
                    if (p == null || Vis.Sees(Cam, p.position, p)) continue;
                    Vector3 to = pp - p.position; to.y = 0f;
                    if (to.magnitude > 2.4f) p.position += to.normalized * 0.8f;
                }
            }

            // Things only come into being where you aren't looking.
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (Vis.Sees(Cam, pending[i].pos)) continue;
                props.Add(pending[i].make(pending[i].pos));
                pending.RemoveAt(i);
            }

            if (now > texTimer)
            {
                texTimer = now + 0.35f;
                var want = PaintingTexture;
                if (paintMat.mainTexture != want) paintMat.mainTexture = want;
                if (want == fallback) PaintFallback(now);
            }

            if (maze != null) { UpdateMaze(now, pp); return; }

            if (exit == null && now >= doorAt)
            {
                PlaceExit();
                g.hud.Say("There is a door now.", 3.5f);
                g.Log("dream: the way out has appeared");
            }
            if (exit != null && !ending)
            {
                float d = (exit.position - pp).magnitude;
                if (day >= 5 && !exitMoved && d < 8f)
                {
                    exitMoved = true;
                    PlaceExit();
                    Sfx.Ui("creak", 0.6f, 0.7f);
                    g.hud.Say("Not that one.", 3f);
                    g.Log("dream: the door moved away once (days 5 and later)");
                }
                else if (d < 1.6f)
                {
                    g.Steady(0.02f, "found the door out of the dream");
                    End(false);
                }
            }
            if (now > hardEnd && !ending)
            {
                g.Log("dream: ran its full length; waking");
                End(false);
            }
        }

        // The maze: it changes, the night runs down, and the way out is a door with a clock ticking behind it.
        void UpdateMaze(float now, Vector3 pp)
        {
            if (ending) return;
            float left = deadline - now;
            maze.Tick(pp, Cam, Mathf.Clamp01(left / nightLength));
            // The dream's own haunts: more of them, sooner, while it is toying with you.
            bool toying = stalker != null && (stalker.state == MazeStalker.State.Toy || stalker.state == MazeStalker.State.Search);
            maze.TickHaunts(pp, Cam, Mathf.Clamp01(left / nightLength), toying, stalker != null && stalker.Hidden != null);
            float d = (maze.ExitPosition - pp).magnitude;
            if (day >= 3 && !exitMoved && d < 5.5f && maze.RoomsToExit(pp) == 0 && left > 35f)
            {
                exitMoved = true;
                if (maze.MoveExit(pp, Cam))
                {
                    Sfx.Ui("creak", 0.6f, 0.7f);
                    g.hud.Say("Not that one.", 3f);
                }
            }
            else if (d < 1.5f)
            {
                g.Steady(0.03f, "got out of the dream with " + Mathf.Max(0f, left).ToString("0") + " s of night left");
                g.Log("dream: out of the maze (" + maze.Size + " by " + maze.Size + ", it changed " + maze.shifts + " times, " + maze.pagesRead + " of " + maze.pagesPlaced + " pages read)");
                End(false);
                return;
            }
            if (Mathf.Floor(left) != timerShown) { timerShown = Mathf.Floor(left); g.RefreshDreamTimer(); }
            if (warned == 0 && left < nightLength * 0.5f) { warned = 1; g.hud.Say("It's getting light somewhere. Not in here.", 3.5f); }
            else if (warned == 1 && left < 30f) { warned = 2; g.hud.Say("You're running out of night.", 3.5f); Sfx.Ui("sting", 0.35f, 0.6f); }
            else if (warned == 2 && left < 10f) { warned = 3; g.hud.Say("Wake up. Wake up.", 3.5f); }
            if (left <= 0f)
            {
                g.Log("dream: the night ran out in the maze, " + maze.RoomsToExit(pp) + " rooms from the way out");
                Sfx.Ui("sting", 0.9f);
                Die("the night ran out");
            }
        }

        void UpdateLight(float now, Vector3 pp)
        {
            int mode = Cur[FearEngine.Light];
            Vector3 offset = new Vector3(0f, Mathf.Min(2.4f, ceil - 0.3f), 0f);
            Color c = new Color(0.72f, 0.78f, 0.9f);
            float intensity = 1.8f, range = 22f;
            if (mode == 1) intensity = Mathf.PerlinNoise(now * 9f, 0.3f) > 0.42f ? 1.1f : 0.06f;
            else if (mode == 2) { c = new Color(1f, 0.07f, 0.04f); intensity = 1.35f; }
            else if (mode == 3)
            {
                offset += new Vector3(Mathf.Sin(now * 1.7f) * 3.2f, 0f, Mathf.Cos(now * 1.7f) * 0.6f);
                c = new Color(1f, 0.85f, 0.6f); intensity = 1.5f; range = 9f;
            }
            else if (mode == 4) intensity = 0.1f;
            if (maze != null)
            {
                // Indoors and close: you see the room you're in and a little of the next. In the last half-minute the light goes bad.
                // You carry very little light: the lit rooms are lit by their own bulbs, the dark ones are dark.
                range = 7f; intensity = Mathf.Clamp(intensity, 0.45f, 1.0f);
                if (maze.Blackout) intensity = 0.12f + 0.25f * Mathf.PerlinNoise(now * 11f, 3f);
                float left = deadline - now;
                if (left < 30f) { c = Color.Lerp(new Color(1f, 0.12f, 0.06f), c, Mathf.Clamp01(left / 30f)); intensity *= Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(left / 30f)); }
            }
            lamp.transform.position = pp + offset;
            lamp.color = Color.Lerp(lamp.color, c, Time.deltaTime * 3f);
            lamp.intensity = mode == 1 ? intensity : Mathf.Lerp(lamp.intensity, intensity, Time.deltaTime * 3f);
            lamp.range = range;
        }

        void UpdatePresence(float now, float dt, Vector3 pp)
        {
            if (stalker != null)
            {
                if (!ending) stalker.Tick(now, dt, pp, Cam, Mathf.Clamp01((deadline - now) / Mathf.Max(1f, nightLength)));
                return;
            }
            int mode = Cur[FearEngine.Behavior];
            if (mode == 1)
            {
                // Footsteps that keep pace behind you, and take two more after you stop.
                Vector3 v = g.player.velocity; v.y = 0f;
                bool moving = v.magnitude > 1f;
                if (moving) stepsOwed = 2;
                if (now > stepTimer && (moving || stepsOwed > 0))
                {
                    stepTimer = now + 0.55f;
                    if (!moving) stepsOwed--;
                    Sfx.At("step", pp - g.player.Forward * 3f, 0.8f, 0.75f);
                }
                return;
            }
            if (figure == null) return;
            float dist = (figure.position - pp).magnitude;
            if (mode == 5)
            {
                // It leaves. Every time you find it, it is already walking away, and it never turns around.
                if (Vis.Sees(Cam, monster.HeadPoint, figure))
                {
                    Vector3 away = figure.position - pp; away.y = 0f;
                    figure.position += away.normalized * 1.3f * dt;
                }
                if (dist > 24f && !Vis.Sees(Cam, monster.HeadPoint, figure)) figure.position = RandomPoint(9f, 13f, true);
                figure.rotation = Quaternion.LookRotation(Vis.Flat(figure.position - pp));
                return;
            }
            if (mode == 2)
            {
                // It only stands. If you walk up to it, it is somewhere else when you next look.
                if (dist < 6f && !Vis.Sees(Cam, monster.HeadPoint, figure)) figure.position = RandomPoint(14f, 20f, true);
                else if (dist < 3f) figure.position = RandomPoint(14f, 20f, true);
            }
            else if (mode == 3)
            {
                // A reflection with no mirror: wherever you go, it goes the opposite way about one fixed point.
                Vector3 target = mirrorAnchor * 2f - pp;
                target.y = Origin.y;
                figure.position = target;
                if (dist < 1.6f)
                {
                    mirrorAnchor = pp + g.player.Forward * 7f;
                    g.Shake(0.02f, "walked into your reflection");
                }
            }
            else if (mode == 4)
            {
                // It only moves while you aren't looking at it.
                if (!Vis.Sees(Cam, monster.HeadPoint, figure))
                {
                    Vector3 to = pp - figure.position; to.y = 0f;
                    figure.position += to.normalized * Mathf.Min((maze != null ? 1.5f : 2.4f) * dt, to.magnitude);
                }
                if (dist < 1.2f && !ending)
                {
                    Sfx.Ui("sting", 0.9f);
                    if (maze != null) { g.Log("dream: it reached you in the maze"); Die("it reached you"); return; }
                    g.Log("dream: it reached you; waking early");
                    End(true);
                    return;
                }
            }
            figure.rotation = Quaternion.LookRotation(Vis.Flat(pp - figure.position));
        }
    }
}
