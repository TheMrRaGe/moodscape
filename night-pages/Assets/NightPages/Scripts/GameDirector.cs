using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    public enum Phase { Title, Morning, Afternoon, Evening, Dream, Ending }
    public enum Truth { Killer, Keepers, House }

    /// <summary>
    /// Runs a playthrough: seven days of chores, writing, evening, dream. Owns the run's hidden state
    /// (which truth is real, Grip, clues, what kind of lines you've been writing) and picks the ending.
    /// </summary>
    public partial class GameDirector : MonoBehaviour
    {
        public static GameDirector I;
        public const int Days = 7;

        public Phase phase = Phase.Title;
        public int day;
        public float grip = 0.8f;
        public Truth truth;
        /// <summary>Hardcore: not getting out of a dream sends you back to day one. (Otherwise you dream it again, in a new maze.)</summary>
        public bool hardcore;
        /// <summary>How many times this run you didn't get out of a dream.</summary>
        public int dreamDeaths;
        public int tagFace, tagAway, tagBlame, clues, rewriteSeed;
        public bool cellarOpened;
        public System.Random rng = new System.Random();

        public Hud hud;
        public MoodscapeLink link;
        public FearEngine fear;
        /// <summary>The FEAR Engine's storyteller (FearCore): when, how big and which fright; its learner is shared with the dreams.</summary>
        public FearCore.Storyteller teller;
        public Hallucinations halluc;
        public Uncanny uncanny;
        public Nights nights;
        public House house;
        public Outdoors outdoors;
        public Dread dread;
        public Player player;
        public Chores chores;
        public Manuscript script;
        public Hauntings haunt;
        public Dream dream;
        public VRHud vrHud;

        class Note { public GameObject go; public string text; public bool read; }
        readonly List<Note> notes = new List<Note>();
        int cluesSpawned;
        bool busy;
        Material housePaint;
        AudioSource drone, heart;
        float paintTimer;

        static readonly string[][] ClueTexts =
        {
            new[]   // Killer
            {
                "A pharmacy receipt. Sleeping tablets, two boxes.\n\nThe signature on the slip is yours.",
                "A child's drawing: a house, two tall figures lying down, one small figure standing.\n\nOn the back, in Grandma's hand: \"We will never speak of this.\"",
                "A condolence card, never sent.\n\n\"So sorry about your grandparents. So sudden, and both in one night.\"",
                "A newspaper clipping, folded small.\n\n\"COUPLE MISSING. SON, 6, FOUND UNHARMED AT HOME.\"",
            },
            new[]   // Keepers
            {
                "A letter from a court.\n\n\"We find no grounds to remove the child from his parents.\"\n\nIt is dated a week before they disappeared.",
                "Train tickets, never used. Three passengers.\n\nThe date is the day they vanished.",
                "Grandpa's ledger. One line is underlined twice.\n\n\"Concrete, 6 bags. Cellar.\"",
                "A note in handwriting you don't know.\n\n\"We never left you. Come downstairs.\"",
            },
            new[]   // House
            {
                "The deed to the house. The list of owners runs to four pages.\n\nNone of them ever sold it.",
                "A floor plan, dated 1911.\n\nThe house has two fewer rooms than it does now.",
                "A photograph of your parents in the hall.\n\nBehind them is a door that isn't there.",
                "Grandma's diary, the last entry.\n\n\"It has been so patient with the boy. It will not be patient forever.\"",
            },
        };

        void Awake() { I = this; }

        void Start()
        {
            Sfx.Init();
            hud = gameObject.AddComponent<Hud>();
            link = gameObject.AddComponent<MoodscapeLink>();
            fear = new FearEngine();
            fear.log = s => Log("fear engine: " + s);
            // The storyteller and its learner (instinct + record), saved beside the fear profile.
            var brain = new FearCore.Learner(FlyWiring.Load());
            try { if (System.IO.File.Exists(TellerPath)) brain.Load(System.IO.File.ReadAllText(TellerPath)); }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't read the storyteller's memory (" + e.Message + "); starting fresh"); }
            teller = new FearCore.Storyteller(brain) { Log = s => Log(s) };
            fear.brain = brain;
            fear.onEase = why => teller.Overwhelmed(why, 0.2f, Time.time);
            // A hallucination's measured effect goes back into the fear engine's slot 17, so the dreams know it too.
            teller.Outcome = (inc, y) => { int o = Hallucinations.OptionOf(inc); if (o > 0) fear.Credit(FearEngine.Hallucination, o, y); };
            Log("fear engine: the instinct is wired from the " + brain.instinct.Source);

            dream = gameObject.AddComponent<Dream>();
            dream.Init(this);
            housePaint = Build.TexMat(dream.PaintingTexture);
            Monster.faceMaterial = housePaint;
            Reactions.heart = gameObject.AddComponent<HeartLink>();
            Reactions.voice = gameObject.AddComponent<VoiceSense>();

            if (FaceReader.WantedOn && !Application.isBatchMode) FaceReader.Enable(true);
            // -nplayout N puts the rooms in layout N (0-23) instead of a random one (for tests).
            var cl = System.Environment.GetCommandLineArgs();
            int li = System.Array.IndexOf(cl, "-nplayout");
            if (li >= 0 && li + 1 < cl.Length) int.TryParse(cl[li + 1], out House.ForceLayout);
            house = new GameObject("House").AddComponent<House>();
            house.BuildAll(housePaint);
            outdoors = new GameObject("Outdoors").AddComponent<Outdoors>();
            outdoors.BuildAll(house);
            for (int k = 0; k <= Monster.Shadow; k++) { Material mb, mh; MonsterSkin.For(k, out mb, out mh); }   // paint every skin now, not mid-scare
            player = Player.Create(house.bedSpawn);
            Mirror.HideFrom(player.cam);          // what is only in the mirror stays only in the mirror
            VRRig.Attach(player);
            if (!VRRig.Active) Look.Attach(player.cam);
            vrHud = VRHud.Create(hud, player);
            house.SetTime(0, 1);

            chores = new Chores(this);
            script = new Manuscript(this);
            script.Reset();
            dread = gameObject.AddComponent<Dread>();
            dread.Init(this);
            haunt = gameObject.AddComponent<Hauntings>();
            haunt.Init(this);
            halluc = gameObject.AddComponent<Hallucinations>();
            halluc.Init(this);
            halluc.Register(teller);
            uncanny = gameObject.AddComponent<Uncanny>();
            uncanny.Init(this);
            nights = gameObject.AddComponent<Nights>();
            nights.Init(this);

            WireHouse();
            // Forms the player has asked never to see, e.g. PlayerPrefs "np_banned" = "A spider,A clown".
            FearEngine.Banned.Clear();
            foreach (var label in PlayerPrefs.GetString("np_banned", "").Split(','))
                if (label.Trim().Length > 0) FearEngine.Banned.Add(label.Trim());

            drone = Sfx.Loop("drone", transform, 0f, false);
            heart = Sfx.Loop("heart", transform, 0f, false);

            if (Tour.Requested) { StartCoroutine(RunTour()); return; }       // headless test runs (see Tour.cs)
            ShowTitle();
        }

        // ---------------------------------------------------------------- logging and Grip

        /// <summary>Every decision the game makes goes through here: the Mind overlay (F1) and the Unity console.</summary>
        public void Log(string line)
        {
            Debug.Log("[NightPages] " + line);
            Tour.Note(line);
            if (hud != null) hud.Log(line);
        }

        static string TellerPath { get { return System.IO.Path.Combine(Application.persistentDataPath, "nightpages-teller.txt"); } }

        /// <summary>Keep what the storyteller has learned (every morning, after every dream, on quitting).</summary>
        public void SaveTeller()
        {
            if (teller == null) return;
            try { System.IO.File.WriteAllText(TellerPath, teller.learner.Save()); }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't save the storyteller's memory: " + e.Message); }
        }

        void OnApplicationQuit() { SaveTeller(); }

        public void Shake(float amount, string why)
        {
            if (teller != null) teller.Stimulus(amount * 5f, Time.time);
            grip = Mathf.Clamp01(grip - amount);
            Look.Kick(amount * 3f);
            Log("grip -" + amount.ToString("0.00") + " -> " + grip.ToString("0.00") + ": " + why);
        }

        public void Steady(float amount, string why)
        {
            grip = Mathf.Clamp01(grip + amount);
            Log("grip +" + amount.ToString("0.00") + " -> " + grip.ToString("0.00") + ": " + why);
        }

        // ---------------------------------------------------------------- flow

        void ShowTitle()
        {
            phase = Phase.Title;
            player.frozen = true;
            hud.fade = hud.fadeTarget = 1f;
            hud.header = "NIGHT PAGES";
            hud.body = "A prototype.\n\nWASD to move, mouse to look, E to use, Shift to hurry.\nThe dreams learn what unsettles you, and they remember.\n\n" +
                       "<size=22>F1: see what the game is thinking.  F2: skip ahead.  V: let it listen for gasps (microphone).\n" +
                       "Hold F / G in a dream to fake a scared / amused reaction.  A VR headset is picked up by itself.</size>";
            hud.vrBody = "A prototype.\n\nLeft stick to walk, right stick to turn. Point your right hand and pull the trigger to use things.\n" +
                         "The dreams learn what unsettles you, and they remember.\n\n" +
                         "<size=50>Left Y: see what the game is thinking.  Keyboard V: let it listen for gasps.</size>";
            hardcore = PlayerPrefs.GetInt("np_hardcore", 0) == 1;
            titleBody = hud.body;
            ShowHardcore();
            hud.Open(Modal.Title);
        }

        string titleBody;

        void ShowHardcore()
        {
            hud.body = titleBody + "\n\n<size=22>Each night's dream is a maze that changes. Get out before the night runs out.\n" +
                       (hardcore ? "HARDCORE: if you don't get out, you start again from day one." : "If you don't get out, you dream it again, in a different maze.") + "</size>";
            hud.footer = "[E]  Begin        [1]  Hardcore: " + (hardcore ? "ON" : "off");
        }

        /// <summary>A page found in the dream: this run's story, and (the first time) how far the way out is.</summary>
        public void ReadDreamPage(string text, string hint)
        {
            hud.header = "A page";
            hud.body = text + (hint != null ? "\n\n<i>" + hint + "</i>" : "");
            hud.footer = "[E] or [Esc] to put it down. The night doesn't wait.";
            player.frozen = true;
            hud.Open(Modal.Reading);
            Sfx.Ui("pageturn", 0.6f);
            Log("dream: read a page" + (hint != null ? " (" + hint + ")" : " again"));
        }

        public void RefreshDreamTimer() { if (phase == Phase.Dream) RefreshObjectives(); }

        public void OnDreamDeath(string how) { StartCoroutine(DreamDeath(how)); }

        // You didn't get out. Hardcore: the run is over, back to day one. Otherwise: asleep again, at the start of
        // the same night's dream, in a maze you haven't seen.
        IEnumerator DreamDeath(string how)
        {
            busy = true;
            player.frozen = true;
            dreamDeaths++;
            yield return FadeOut(2.5f);
            dream.Clear();
            Shake(0.06f, "didn't get out of the dream (" + how + ")");
            hud.ClearSubtitles();
            if (hardcore)
            {
                Log("dream: died in the dream on day " + day + " in hardcore (" + how + "): back to day one");
                hud.Say("You didn't wake up.", 4f);
                yield return new WaitForSeconds(4f);
                busy = false;
                NewRun();
                yield break;
            }
            Log("dream: died in the dream on day " + day + " (" + how + "): dreaming it again in a new maze (" + dreamDeaths + " so far this run)");
            hud.Say(how == "it reached you" ? "It had you. Then you were asleep again, somewhere else." : "You didn't get out. You're asleep again, and it isn't the same place.", 4.5f);
            yield return new WaitForSeconds(1.6f);
            phase = Phase.Dream;
            dream.Begin(day);
            RefreshObjectives();
            yield return new WaitForSeconds(0.4f);
            hud.fadeSpeed = 0.5f;
            hud.fadeTarget = 0f;
            player.frozen = false;
            busy = false;
        }

        // What the house's own things do when used.
        void WireHouse()
        {
            house.typewriter.use = UseTypewriter;
            house.bed.use = UseBed;
            house.shelf.use = ReadManuscript;
            house.cellar.use = UseCellar;
            house.cellar.promptFn = () => cellarOpened ? "Go down to the cellar" : "The cellar door";
            house.cellarStairs.use = () => { if (!busy) StartCoroutine(Stairs(false)); };
            house.cellarFloor.use = LookAtCellarFloor;
            house.leave.use = () => StartCoroutine(EndRun("leave"));
            house.leave.can = () => day >= Days && phase != Phase.Dream && phase != Phase.Ending;
        }

        int runsStarted;

        /// <summary>
        /// A new house for a new playthrough: the rooms in a different order (layout -1 = random, or 0-23). Everything that
        /// pointed into the old house is let go first.
        /// </summary>
        public void RebuildHouse(int layout = -1)
        {
            int keep = House.ForceLayout;
            if (layout >= 0) House.ForceLayout = layout;
            haunt.ClearTransient();
            if (halluc != null) halluc.Clear();
            if (player != null) player.Drop();
            DestroyImmediate(house.gameObject);
            DestroyImmediate(outdoors.gameObject);
            house = new GameObject("House").AddComponent<House>();
            house.BuildAll(housePaint);
            outdoors = new GameObject("Outdoors").AddComponent<Outdoors>();
            outdoors.BuildAll(house);
            WireHouse();
            chores.Wire();
            uncanny.Wire();
            house.SetTime(phase == Phase.Evening ? 1 : 0, day);
            House.ForceLayout = keep;
        }

        void NewRun()
        {
            if (runsStarted++ > 0) RebuildHouse();        // every playthrough after the first gets the rooms in a new order
            nights.NewRun();
            rng = new System.Random();
            double r = rng.NextDouble();
            truth = r < 0.6 ? Truth.Killer : r < 0.8 ? Truth.Keepers : Truth.House;
            grip = 0.8f;
            tagFace = tagAway = tagBlame = clues = cluesSpawned = 0;
            dreamDeaths = 0;
            cellarOpened = false;
            sawCellarFloor = false;
            rewriteSeed = rng.Next(3);
            foreach (var n in notes) if (n.go != null) Destroy(n.go);
            notes.Clear();
            script.Reset();
            hud.mindLines.Clear();
            Log("new run. truth drawn: " + truth + " (60/20/20 odds; the player isn't told)");
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            StartCoroutine(BeginDay(1));
        }

        IEnumerator FadeOut(float speed)
        {
            hud.fadeSpeed = speed;
            hud.fadeTarget = 1f;
            while (hud.fade < 0.995f) yield return null;
        }

        IEnumerator BeginDay(int d)
        {
            Tour.Step("begin day: start");
            busy = true;
            player.frozen = true;
            yield return FadeOut(1.5f);
            hud.modal = Modal.None;
            hud.ClearSubtitles();
            day = d;
            phase = Phase.Morning;
            Tour.Step("begin day " + d + ": lighting the house");
            house.SetTime(0, day);
            player.speedMul = 1f;
            player.eyeHeight = 1.65f;
            player.Teleport(house.bedSpawn, 200f);
            Tour.Step("begin day " + d + ": picking chores");
            chores.Pick(day);
            script.foreign = null;
            dread.BeginDay(day);
            uncanny.BeginDay(day);
            haunt.BeginDay(day);
            SaveTeller();
            RefreshObjectives();
            hud.Say(day >= Days ? "Day " + day + ". The last one." : "Day " + day, 3f);
            if (day > 1 && !string.IsNullOrEmpty(fear.lastLine)) hud.Say("You wake with the dream still in the room.", 3.5f);
            if (day >= Days) hud.Say("Your coat is on the hook by the front door. You could just go.", 5f);
            yield return new WaitForSeconds(0.4f);
            hud.fadeSpeed = 0.8f;
            hud.fadeTarget = 0f;
            player.frozen = false;
            busy = false;
        }

        void RefreshObjectives()
        {
            hud.objectives.Clear();
            if (phase == Phase.Morning)
                foreach (var c in chores.today) hud.objectives.Add(new KeyValuePair<string, bool>(c.text, c.done));
            else if (phase == Phase.Afternoon)
                hud.objectives.Add(new KeyValuePair<string, bool>("Write today's chapter (typewriter, study)", false));
            else if (phase == Phase.Evening)
            {
                foreach (var n in notes)
                    if (!n.read) { hud.objectives.Add(new KeyValuePair<string, bool>("Something has been left out for you", false)); break; }
                hud.objectives.Add(new KeyValuePair<string, bool>("Go to bed (bedroom)", false));
            }
            else if (phase == Phase.Dream)
            {
                int left = Mathf.CeilToInt(dream.TimeLeft);
                hud.objectives.Add(new KeyValuePair<string, bool>(dream.Maze != null ? "Find the way out before the night ends  (" + (left / 60) + ":" + (left % 60).ToString("00") + ")" : "Find the way out", false));
                if (dream.Maze != null) hud.objectives.Add(new KeyValuePair<string, bool>("Follow the ticking. Pages buy time.", false));
            }
        }

        public void OnChoreDone()
        {
            if (chores.AllDone && phase == Phase.Morning)
            {
                phase = Phase.Afternoon;
                hud.Say("The chores are done. The page is waiting.", 3.5f);
            }
            RefreshObjectives();
        }

        void UseTypewriter()
        {
            if (phase == Phase.Afternoon) script.Begin(day, day > 1 ? fear.lastLine : null);
            else if (phase == Phase.Morning) hud.Say("Chores first. That was always the rule in this house.", 3f);
            else ReadManuscript();
        }

        public void OnChapterDone()
        {
            hud.modal = Modal.None;
            player.frozen = false;
            phase = Phase.Evening;
            house.SetTime(1, day);
            haunt.Evening();
            SpawnClue();
            RefreshObjectives();
            hud.Say("Evening. The house settles around you.", 3.5f);
        }

        void ReadManuscript()
        {
            hud.header = "The manuscript";
            hud.body = script.FullText();
            hud.footer = "[E] or [Esc] to put it down";
            player.frozen = true;
            hud.Open(Modal.Reading);
            Sfx.Ui(Sfx.HasRecording("bookopen") && hud.header == "The manuscript" ? "bookopen" : "pageturn", 0.6f);
        }

        void UseBed()
        {
            if (busy) return;
            if (phase == Phase.Evening) StartCoroutine(Sleep());
            else hud.Say("Not yet. There's still the day to get through.", 3f);
        }

        IEnumerator Sleep()
        {
            busy = true;
            player.frozen = true;
            yield return FadeOut(0.7f);
            int unread = 0;
            foreach (var n in notes) if (!n.read) unread++;
            if (unread > 0) Shake(0.05f * unread, "went to bed without reading what was left out (" + unread + ")");
            haunt.ClearTransient();
            hud.ClearSubtitles();
            // Some nights you wake before the dream, and can't move.
            if (nights.ParalysisTonight(day))
            {
                haunt.paused = true;
                yield return nights.Paralysis();
                haunt.paused = false;
            }
            phase = Phase.Dream;
            dream.Begin(day);
            RefreshObjectives();
            yield return new WaitForSeconds(0.6f);
            hud.fadeSpeed = 0.5f;
            hud.fadeTarget = 0f;
            player.frozen = false;
            busy = false;
        }

        public void OnDreamEnd(bool caught)
        {
            StartCoroutine(Wake(caught));
        }

        IEnumerator Wake(bool caught)
        {
            Tour.Step("wake: start");
            busy = true;
            player.frozen = true;
            yield return FadeOut(caught ? 6f : 1.2f);
            Tour.Step("wake: clearing the dream");
            dream.Clear();
            Tour.Step("wake: dream cleared");
            if (caught) Shake(0.08f, "it reached you in the dream");
            if (day >= Days) yield return EndRun("night");
            else
            {
                // Some mornings you only think you've woken.
                if (nights.FalseAwakening(day + 1))
                {
                    phase = Phase.Morning;
                    yield return nights.FalseMorning(day + 1);
                }
                yield return BeginDay(day + 1);
            }
        }

        void UseCellar()
        {
            if (day >= 6 && clues >= 3 && !cellarOpened)
            {
                cellarOpened = true;
                Sfx.At("unlock", house.cellar.transform.position, 0.8f);
                Sfx.At("creak", house.cellar.transform.position, 0.9f, 0.6f);
                Shake(0.08f, "opened the cellar door");
                hud.Say(truth == Truth.Killer ? "The cold comes up the stairs. You know this cold. You carried it down here."
                    : truth == Truth.Keepers ? "The cold comes up the stairs, and with it, very quietly, your name."
                    : "The stairs go down further than the house is tall.", 6f);
            }
            else if (cellarOpened && !busy) StartCoroutine(Stairs(true));
            else { Sfx.At("unlock", house.cellar.transform.position, 0.35f, 1.25f); hud.Say(day < 3 ? "Locked. It has always been locked." : "Locked. Something on the other side is leaning on it.", 3.5f); }
        }

        // The cellar is its own space under the house; the stairs are a short fade.
        IEnumerator Stairs(bool down)
        {
            busy = true;
            player.frozen = true;
            player.Drop();
            yield return FadeOut(1.6f);
            if (down)
            {
                player.Teleport(house.cellarSpawn, 180f);
                RenderSettings.ambientLight = new Color(0.06f, 0.055f, 0.05f);
                RenderSettings.fogDensity = 0.06f;
                Log("went down into the cellar");
            }
            else
            {
                player.Teleport(house.hallSouth + new Vector3(0f, 0.05f, 0.6f), 0f);
                house.SetTime(phase == Phase.Evening ? 1 : 0, day);
            }
            yield return new WaitForSeconds(0.5f);
            hud.fadeSpeed = 0.8f;
            hud.fadeTarget = 0f;
            player.frozen = false;
            busy = false;
        }

        bool sawCellarFloor;

        void LookAtCellarFloor()
        {
            if (!sawCellarFloor)
            {
                sawCellarFloor = true;
                clues++;
                tagFace++;
                Shake(0.06f, "looked at the cellar floor (counts as a clue)");
            }
            hud.header = "The cellar floor";
            hud.body = truth == Truth.Killer
                ? "One patch of concrete is newer than the rest. Smoother. The spade is leaning where you left it.\n\nYou remember how heavy it was. You remember being proud of how level you got it."
                : truth == Truth.Keepers
                ? "One patch of concrete is newer than the rest, though not new. Twenty years, about.\n\nSomeone has scratched two sets of initials into it from underneath. That isn't possible. You read them anyway. They are your parents'."
                : "One patch of concrete is newer than the rest.\n\nWhile you watch, it gets a little larger.";
            hud.footer = "[E] or [Esc] to look away";
            player.frozen = true;
            hud.Open(Modal.Reading);
        }

        // ---------------------------------------------------------------- clues

        void SpawnClue()
        {
            if (day < 2 || cluesSpawned >= ClueTexts[(int)truth].Length) return;
            var free = new List<Room>(house.rooms);
            var room = free[rng.Next(free.Count)];
            var note = new Note { text = ClueTexts[(int)truth][cluesSpawned++] };
            note.go = Build.Box("Note", house.transform, room.notePoint, new Vector3(0.22f, 0.02f, 0.3f), new Color(0.95f, 0.92f, 0.8f), true, 0.6f);
            var it = Build.Usable(note.go, "Read what's been left here");
            it.use = () => ReadNote(note);
            notes.Add(note);
            Log("clue " + cluesSpawned + " for the " + truth + " truth left in the " + room.name);
        }

        void ReadNote(Note n)
        {
            if (!n.read)
            {
                n.read = true;
                clues++;
                tagFace++;
                Shake(0.02f, "read a clue (" + clues + " found). Facing it costs a little now and counts later");
            }
            hud.header = "Left for you";
            hud.body = n.text;
            hud.footer = "[E] or [Esc] to put it down";
            player.frozen = true;
            hud.Open(Modal.Reading);
            Sfx.Ui(Sfx.HasRecording("bookopen") && hud.header == "The manuscript" ? "bookopen" : "pageturn", 0.6f);
            if (n.go != null) n.go.GetComponent<Renderer>().sharedMaterial = Build.Mat(new Color(0.5f, 0.48f, 0.42f));
            RefreshObjectives();
        }

        // ---------------------------------------------------------------- endings

        IEnumerator EndRun(string how)
        {
            busy = true;
            player.frozen = true;
            yield return FadeOut(1f);
            if (dream.active) dream.Clear();
            haunt.ClearTransient();
            hud.ClearSubtitles();
            phase = Phase.Ending;
            string title, text;
            int id;
            bool faced = tagFace >= tagAway;
            if (how == "leave")
            {
                id = 5; title = "UNFINISHED";
                text = "You take your coat and leave the pages on the desk.\n\nThe house watches you to the end of the road. It will keep.";
            }
            else if (truth == Truth.Killer && grip >= 0.45f && clues >= 3 && faced)
            {
                id = 0; title = "CONFESSION";
                text = "You finish the last chapter as what it always was.\n\nThen you pick up the telephone, and tell them where to dig.";
            }
            else if (truth == Truth.Killer)
            {
                id = 1; title = "THE GENTLER STORY";
                text = "You feed the fire a page at a time until the story is simple again.\n\nIt was a good house. Anyone would have said so.\n\nIn the morning you make coffee. Two cups.";
            }
            else if (truth == Truth.Keepers && clues >= 3 && faced)
            {
                id = 2; title = "REUNION";
                text = "You go down the cellar stairs with a hammer.\n\nUnder the concrete, they have been waiting twenty years to be called by name. You say both names.";
            }
            else if (truth == Truth.Keepers)
            {
                id = 3; title = "INHERITANCE";
                text = "They did what they did to keep you. You decide that is a kind of love.\n\nYou stay. On the anniversary, you set two extra places.";
            }
            else if (grip < 0.5f || cellarOpened)
            {
                id = 4; title = "TAKEN";
                text = "There is one more door than there was, and it is open.\n\nThe house has been so patient with you.";
            }
            else
            {
                id = 5; title = "UNFINISHED";
                text = "Nobody did anything in this house. That is the worst of it.\n\nYou leave before it can make you stay.";
            }
            PlayerPrefs.SetInt("np_endings", PlayerPrefs.GetInt("np_endings", 0) | (1 << id));
            PlayerPrefs.Save();
            Log("ending: " + title + " (truth " + truth + ", grip " + grip.ToString("0.00") + ", clues " + clues +
                ", faced " + tagFace + " / looked away " + tagAway + " / blamed " + tagBlame + ")");
            hud.header = title;
            hud.body = text + "\n\n<size=22>This run's truth: " + truth + ".  Grip " + Mathf.RoundToInt(grip * 100f) + "%.  Clues " + clues +
                       ".  Dreams remembered: " + fear.Dreams + ".</size>";
            hud.footer = "[R]  Begin again. The dreams will remember you.";
            hud.Open(Modal.Ending);
            busy = false;
        }

        // ---------------------------------------------------------------- every frame

        void Update()
        {
            Tour.Frame++;
            if (hud == null || player == null) return;
            if (In.Down(Btn.Mind)) hud.mind = !hud.mind;
            if (In.Down(Btn.Face))
            {
                FaceReader.Enable(!FaceReader.On);
                Log("face reading: " + (FaceReader.On ? "turning on" : "off") + " (the camera image stays on this PC; nothing is saved)");
                hud.Say(FaceReader.On ? "Camera off. The house can't see your face." : "Camera on. The house is learning your face.", 3.5f);
            }
            if (In.Down(Btn.Voice))
            {
                Reactions.voice.Toggle();
                Log("voice sensing: " + Reactions.voice.Status + " (loudness only; nothing is recorded)");
                hud.Say("Microphone: " + Reactions.voice.Status, 3f);
            }

            if (In.Down(Btn.Pause) && (hud.modal == Modal.None || hud.modal == Modal.Paused))
            {
                bool pausing = hud.modal == Modal.None;
                hud.modal = pausing ? Modal.Paused : Modal.None;
                Time.timeScale = pausing ? 0f : 1f;
                Cursor.lockState = pausing ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = pausing;
            }

            switch (hud.modal)
            {
                case Modal.Title:
                    if (!busy && In.Down(Btn.One))
                    {
                        hardcore = !hardcore;
                        PlayerPrefs.SetInt("np_hardcore", hardcore ? 1 : 0);
                        PlayerPrefs.Save();
                        ShowHardcore();
                        Sfx.Ui("pageturn", 0.5f);
                    }
                    if (!busy && In.Down(Btn.Interact)) NewRun();
                    break;
                case Modal.Ending:
                    if (!busy && In.Down(Btn.Restart)) NewRun();
                    break;
                case Modal.Writing:
                    script.HandleInput();
                    break;
                case Modal.Reading:
                    if (!hud.Fresh && (In.Down(Btn.Interact) || In.Down(Btn.Pause)))
                    {
                        Sfx.Ui(hud.header == "The manuscript" ? "bookclose" : "pageturn", 0.55f);
                        hud.Close();
                        player.frozen = false;
                    }
                    break;
            }

            if (In.Down(Btn.SkipAhead) && !busy) SkipAhead();

            Reactions.Tick(link, player);
            hud.grip = phase == Phase.Title || phase == Phase.Ending ? 1f : grip;
            // What the storyteller is told: how well you're holding up, and how late in the day it is.
            teller.Standing = grip;
            teller.Arc = phase == Phase.Evening ? 0.8f : phase == Phase.Afternoon ? 0.35f : 0.15f;

            // The house's paintings (and the bedroom mirror) show whatever Moodscape is painting.
            if (Time.time > paintTimer)
            {
                paintTimer = Time.time + 0.5f;
                var want = dream.PaintingTexture;
                if (housePaint.mainTexture != want) housePaint.mainTexture = want;
            }

            float dread = phase == Phase.Dream ? 0.5f : phase == Phase.Evening ? 0.3f : 0.12f;
            bool quiet = phase == Phase.Title || phase == Phase.Ending;
            drone.volume = Mathf.MoveTowards(drone.volume, quiet ? 0.08f : dread + (1f - grip) * 0.3f, Time.unscaledDeltaTime * 0.3f);
            heart.volume = Mathf.MoveTowards(heart.volume, quiet ? 0f : Mathf.Clamp01((0.55f - grip) * 2f) * 0.8f + Mathf.Max(0f, Reactions.Level) * (phase == Phase.Dream ? 0.4f : 0f), Time.unscaledDeltaTime * 0.5f);
            heart.pitch = 1f + (1f - grip) * 0.4f;

            if (hud.mind)
                hud.mindStatus = "day " + day + " · " + phase + " · truth " + truth + " · grip " + grip.ToString("0.00") + " · clues " + clues +
                                 "\nlines: faced " + tagFace + " / away " + tagAway + " / blamed " + tagBlame +
                                 "\nreaction " + Reactions.Level.ToString("+0.00;-0.00") + ", led by " + Reactions.Source + Reactions.Describe() +
                                 "\n" + link.Status + (VRRig.Active ? "\nVR: on" : "") +
                                 "\nstoryteller: " + teller.Status() + "\nlearner: " + teller.learner.Describe() +
                                 (phase == Phase.Dream ? "\ndream: " + fear.Describe() : "");
        }

        void SkipAhead()
        {
            Log("debug: skipping ahead from " + phase);
            if (hud.modal == Modal.Reading) { hud.modal = Modal.None; player.frozen = false; }
            switch (phase)
            {
                case Phase.Morning: chores.CompleteAll(); OnChoreDone(); break;
                case Phase.Afternoon: script.AutoWrite(day); break;
                case Phase.Evening: StartCoroutine(Sleep()); break;
                case Phase.Dream: dream.ForceEnd(); break;
            }
        }
    }
}
