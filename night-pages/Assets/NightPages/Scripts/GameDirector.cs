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
        /// <summary>
        /// The story's own calendar: everything that escalates (hauntings, the dream, chores, the house's wear, the
        /// job's passages) is tuned to seven story days. A run can be longer or shorter than that (RunDays): each
        /// real day maps onto a story day, so a 3-day run tells days 1, 4 and 7, and a 30-day run lingers.
        /// </summary>
        public const int Days = 7;
        /// <summary>How many days this run lasts (chosen on the title screen: 3, 7, 14 or 30).</summary>
        public static int RunDays = 7;
        public static readonly int[] RunLengths = { 3, 7, 14, 30 };

        public Phase phase = Phase.Title;
        /// <summary>The story day (1 to 7) that everything is tuned to. See calendar for the real day.</summary>
        public int day;
        /// <summary>The real day of the run, 1 to RunDays.</summary>
        public int calendar;

        /// <summary>Which story day (1 to 7) a real day of the run is.</summary>
        public static int StoryDay(int cal)
        {
            if (RunDays <= 1) return Days;
            if (cal >= RunDays) return Days;
            float k = (cal - 1) * (Days - 1) / (float)(RunDays - 1);
            return Mathf.Clamp(1 + Mathf.RoundToInt(k), 1, Days);
        }

        /// <summary>Of the real days that share this one's story day, which this is (0-based) and how many share it.</summary>
        public static void Share(int cal, out int index, out int count)
        {
            int sd = StoryDay(cal);
            index = 0; count = 0;
            for (int c = 1; c <= RunDays; c++)
            {
                if (StoryDay(c) != sd) continue;
                if (c < cal) index++;
                count++;
            }
            if (count == 0) count = 1;
        }

        public bool LastDay { get { return calendar >= RunDays; } }
        public float grip = 0.8f;
        public Truth truth;
        /// <summary>This run's ghost story (who, what happened, why they stay, how they show it): see Tale.</summary>
        public Tale tale;
        /// <summary>Long stays: the older stories under this one (one for a fortnight, two for a month), oldest last.</summary>
        public readonly List<Tale> older = new List<Tale>();
        /// <summary>The "ghosts only" option: limit what can really be going on to the supernatural.</summary>
        public static bool GhostsOnly { get { return Settings.S.ghostsOnly; } }
        /// <summary>Hardcore: not getting out of a dream sends you back to day one. (See Difficulty.cs for the four levels.)</summary>
        public bool hardcore { get { return Level == 3; } }
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
        /// <summary>The fly-brain body that moves the dream's monster (learns which ways of moving frighten you).</summary>
        public FlyBody flyBody;
        /// <summary>The AI voice (StoryVoice): some notes, dream pages and waking lines written by a language model.</summary>
        public StoryVoice voice;
        public Hallucinations halluc;
        public Uncanny uncanny;
        public Nights nights;
        public House house;
        public Outdoors outdoors;
        public Dread dread;
        public Player player;
        public Chores chores;
        public Manuscript script;
        /// <summary>What you do for a living this run (the afternoon's work, and how the story is told).</summary>
        public Job job;
        public bool workDone;
        public DayClock clock;
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
            Settings.Load();
            Settings.Apply();
            Sfx.Init();
            hud = gameObject.AddComponent<Hud>();
            voice = gameObject.AddComponent<StoryVoice>();
            voice.Init(this);
            link = gameObject.AddComponent<MoodscapeLink>();
            fear = new FearEngine();
            fear.log = s => Log("fear engine: " + s);
            // The storyteller and its learner (instinct + record), saved beside the fear profile.
            var brain = new FearCore.Learner(FlyWiring.Load());
            try { if (Settings.S.remember && System.IO.File.Exists(TellerPath)) brain.Load(System.IO.File.ReadAllText(TellerPath)); }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't read the storyteller's memory (" + e.Message + "); starting fresh"); }
            teller = new FearCore.Storyteller(brain) { Log = s => Log(s) };
            // The dream monster's body: the same fly wiring, as movement (see FlyBody).
            flyBody = new FlyBody(brain.instinct);
            if (Settings.S.remember) flyBody.Load();
            ApplyDifficulty();
            // What it learned about your habits in earlier runs (weaker now, but it remembers).
            try { if (Settings.S.remember && System.IO.File.Exists(HabitsPath)) { teller.tactics.Load(System.IO.File.ReadAllText(HabitsPath)); Log("habits: remembered from before: " + Habits.Describe(this)); } }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't read habits: " + e.Message); }
            // Walking into doors used to count as slamming them, so every player was remembered as a slammer. Once: forget it.
            try { if (PlayerPrefs.GetInt("np_doorfix", 0) == 0) { teller.tactics.Forget("slams doors"); PlayerPrefs.SetInt("np_doorfix", 1); PlayerPrefs.Save(); Log("habits: forgot 'slams doors' (it was counting walking into doors)"); } }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't read your habits (" + e.Message + ")"); }
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
            Hands.Attach(player);
            Watch.Attach(player);
            if (!VRRig.Active) Look.Attach(player.cam);
            vrHud = VRHud.Create(hud, player);
            house.SetTime(0, 1);
            clock = DayClock.Attach(this);
            Playtest.Attach(this);

            chores = new Chores(this);
            job = Job.Make(Settings.S.job == "random" ? "writer" : Settings.S.job);
            job.Init(this);
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
            // Forms the player has asked never to see come from the settings file (Settings.Apply, above).

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
            if (Playtest.I != null) Playtest.I.Note(line);
            if (voice != null) voice.Heard(line);
            if (hud != null) hud.Log(line);
        }

        static string HabitsPath { get { return System.IO.Path.Combine(Application.persistentDataPath, "nightpages-habits.txt"); } }
        static string TellerPath { get { return System.IO.Path.Combine(Application.persistentDataPath, "nightpages-teller.txt"); } }

        /// <summary>Keep what the storyteller has learned (every morning, after every dream, on quitting).</summary>
        public void SaveTeller()
        {
            if (teller == null || !MayRemember) return;
            if (flyBody != null) flyBody.Save();
            try { System.IO.File.WriteAllText(TellerPath, teller.learner.Save()); }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't save the storyteller's memory: " + e.Message); }
            try { System.IO.File.WriteAllText(HabitsPath, teller.tactics.Save()); }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't save your habits: " + e.Message); }
            Painted.WriteWishes(teller.learner);
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

        public static string LengthName(int days)
        {
            return days == 3 ? "a weekend (3 days)" : days == 7 ? "a week (7 days)" : days == 14 ? "a fortnight (14 days)" : days == 30 ? "a month (30 days)" : days + " days";
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

        /// <summary>How the last dream death happened ("the night ran out", "it reached you", ...).</summary>
        public string lastDreamDeath;

        public void OnDreamDeath(string how) { lastDreamDeath = how; StartCoroutine(DreamDeath(how)); }

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
                Log("dream: died in the dream on day " + day + " in hardcore (" + how + "): back to day one, and the diary burns");
                Diary.Burn();
                hud.Say("You didn't wake up.", 4f);
                yield return new WaitForSeconds(4f);
                busy = false;
                NewRun();
                yield break;
            }
            if (Level == 0)
            {
                Log("dream: didn't get out on day " + day + " (" + how + "), on easy: you wake, and that night is gone");
                hud.Say("You wake with a start. Whatever it was, it's over.", 4f);
                yield return new WaitForSeconds(2f);
                busy = false;
                yield return Wake(false);
                yield break;
            }
            dreamRetry = true;
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
            // Whatever isn't this run's job is just his things.
            house.typewriter.promptFn = () => "Grandpa's typewriter";
            house.typewriter.use = () => hud.Say("His typewriter. You've never managed more than a line on it.", 3f);
            if (house.deck != null) { house.deck.promptFn = () => "The tape deck"; house.deck.use = () => hud.Say("There's a tape threaded on it. You leave it be.", 3f); }
            if (house.enlarger != null) { house.enlarger.promptFn = () => "Grandpa's enlarger"; house.enlarger.use = () => hud.Say("He used to print his own photographs in here. The chemicals have gone brown.", 3.5f); }
            if (job != null) job.Wire(house);
            house.bed.use = UseBed;
            house.shelf.promptFn = () => "Look over your work so far";
            house.shelf.use = ReadManuscript;
            house.cellar.use = UseCellar;
            house.cellar.promptFn = () => cellarOpened ? "Go down to the cellar" : "The cellar door";
            house.cellarStairs.use = () => { if (!busy) StartCoroutine(Stairs(false)); };
            house.cellarFloor.use = LookAtCellarFloor;
            house.leave.use = () => StartCoroutine(EndRun("leave"));
            house.leave.can = () => LastDay && phase != Phase.Dream && phase != Phase.Ending;
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
            house.SetHour(clock != null ? clock.Hour : 10f, Mathf.Max(1, day));
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
            string pick = Settings.S.job == "random" ? Job.Ids[rng.Next(Job.Ids.Length)] : Settings.S.job;
            job = Job.Make(pick);
            job.Init(this);
            WireHouse();
            ApplyDifficulty();
            ResetRungs();
            Log("this run you work as " + job.Name + " (" + (Settings.S.job == "random" ? "drawn at random" : "chosen when you began") + ")");
            script.Reset();
            hud.mindLines.Clear();
            Log("new run. truth drawn: " + truth + " (60/20/20 odds; the player isn't told)");
            tale = Tale.Tell(truth, rng, fear.brain, s => Log(s), null, 1952, 30, GhostsOnly ? "x-ghost" : null);
            tale.Meet(hero);
            voice.NewRun();
            older.Clear();
            int layers = RunDays >= 30 ? 2 : RunDays >= 14 ? 1 : 0;
            var above = tale;
            for (int i = 0; i < layers; i++)
            {
                int y = int.Parse(above.Var("year", "1960")) - 25 - rng.Next(30);
                above = Tale.Tell(truth, rng, fear.brain, s => Log(s), above, y, 1, null);
                above.Meet(hero);
                older.Add(above);
            }
            LogBible();
            haunt.SetTale(tale);
            ApplyLook();
            found.Clear(); deduced.Clear(); deducedWrong.Clear();
            HideClues();
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

        IEnumerator BeginDay(int cal)
        {
            int d = StoryDay(cal);
            Tour.Step("begin day: start");
            busy = true;
            player.frozen = true;
            yield return FadeOut(1.5f);
            hud.modal = Modal.None;
            hud.ClearSubtitles();
            calendar = cal;
            day = d;
            phase = Phase.Morning;
            if (RunDays != Days) Log("day " + cal + " of " + RunDays + ": story day " + d + (StoryDay(cal - 1) == d && cal > 1 ? " again (a slower day)" : ""));
            Tour.Step("begin day " + d + ": lighting the house");
            foreach (var r in house.rooms) r.lit = false;
            house.ShowSwitches();
            clock.StartDay();
            workDone = false;
            int share, shares;
            Share(cal, out share, out shares);
            script.NewDay(day, share, shares);
            job.BeginDay(day);
            if (script.DayDone) { workDone = true; Log("work: none today (a slower day in a long stay)"); }
            player.speedMul = 1f;
            player.eyeHeight = 1.65f;
            player.Teleport(house.bedSpawn, 200f);
            Tour.Step("begin day " + d + ": picking chores");
            chores.Pick(day);
            script.foreign = null;
            dread.BeginDay(day);
            uncanny.BeginDay(day);
            dreamRetry = false;
            ConsiderRung();
            voice.AskForDay(cal, d);
            haunt.BeginDay(day);
            house.Wear(day);
            SaveTeller();
            RefreshObjectives();
            hud.Say(LastDay ? "Day " + calendar + ". The last one." : "Day " + calendar + (RunDays != Days ? " of " + RunDays : ""), 3f);
            if (calendar == 1) hud.Say(job.Intro, 5f);
            string woke = voice.Waking(calendar);
            if (woke != null) hud.Say(woke, 4f);
            else if (calendar > 1 && !string.IsNullOrEmpty(fear.lastLine)) hud.Say("You wake with the dream still in the room.", 3.5f);
            if (LastDay) hud.Say("Your coat is on the hook by the front door. You could just go.", 5f);
            UpdateBoard();
            WriteDiary();
            yield return new WaitForSeconds(0.4f);
            hud.fadeSpeed = 0.8f;
            hud.fadeTarget = 0f;
            player.frozen = false;
            busy = false;
        }

        /// <summary>Redraw the day's list (chores call this as they change).</summary>
        public void RefreshList() { RefreshObjectives(); }

        void RefreshObjectives()
        {
            hud.objectives.Clear();
            if (phase == Phase.Morning || phase == Phase.Afternoon || phase == Phase.Evening)
            {
                if (In.Held(Btn.Watch) && clock != null) hud.objectives.Add(new KeyValuePair<string, bool>("It's " + clock.Now + ".", false));
                chores.List(hud.objectives, phase != Phase.Morning);
                hud.objectives.Add(new KeyValuePair<string, bool>(DayClock.Say(job.From).PadRight(9) + job.ListLine, workDone));
                if (phase == Phase.Evening)
                {
                    foreach (var n in notes)
                        if (!n.read) { hud.objectives.Add(new KeyValuePair<string, bool>("now      Something has been left out for you", false)); break; }
                    hud.objectives.Add(new KeyValuePair<string, bool>(DayClock.Say(DayClock.BedFrom).PadRight(9) + "Bed (bedroom)", false));
                }
            }
            else if (phase == Phase.Dream)
            {
                int left = Mathf.CeilToInt(dream.TimeLeft);
                hud.objectives.Add(new KeyValuePair<string, bool>(dream.Maze != null ? "Find the way out before the night ends  (" + (left / 60) + ":" + (left % 60).ToString("00") + ")" : "Find the way out", false));
                if (dream.Maze != null) hud.objectives.Add(new KeyValuePair<string, bool>("Follow the ticking. Pages buy time.", false));
                if (dream.Maze != null) hud.objectives.Add(new KeyValuePair<string, bool>("[Q] Flashlight " + Mathf.RoundToInt(dream.Battery * 100f) + "%" + (dream.TorchOn ? " (on: it can see the light)" : ""), false));
            }
        }

        public void OnChoreDone()
        {
            if (phase == Phase.Morning && chores.MorningDone) hud.Say("That's the morning's jobs. " + (workDone ? "" : "The work is waiting."), 3f);
            RefreshObjectives();
        }

        /// <summary>The day's work is done (the last piece of it chosen, or skipped).</summary>
        public void OnChapterDone()
        {
            hud.Close();
            player.frozen = false;
            workDone = true;
            job.DayDone();
            Log("work: done for day " + day + " at " + clock.Now);
            hud.Say(job.DoneLine, 3.5f);
            RefreshObjectives();
        }

        /// <summary>The clock moved the day on: noon, dusk, and (if you're still up) one in the morning.</summary>
        void TimeOfDay()
        {
            if (busy || clock == null || !clock.running) return;
            if (phase != Phase.Morning && phase != Phase.Afternoon && phase != Phase.Evening) return;
            float m = clock.minutes;
            if (phase == Phase.Morning && m >= DayClock.Noon)
            {
                phase = Phase.Afternoon;
                Log("clock: noon. Afternoon");
                hud.Say(workDone ? "Midday." : "Midday. The work is waiting: " + job.ListLine.ToLower() + ".", 3.5f);
                RefreshObjectives();
            }
            if (phase == Phase.Afternoon && m >= DayClock.Dusk)
            {
                phase = Phase.Evening;
                Log("clock: six o'clock. Evening");
                haunt.Evening();
                SpawnClue();
                if (RunDays <= 3) SpawnClue();        // a short stay: two a night
                RefreshObjectives();
                hud.Say("Evening. The light's going. The switches are by the doors.", 3.5f);
                if (!workDone) hud.Say("Today's work isn't done.", 3f);
            }
            if (m >= DayClock.PassOut && hud.modal == Modal.None)
            {
                Log("clock: one in the morning; you fall asleep where you are");
                hud.Say("You don't remember lying down.", 3f);
                StartCoroutine(Sleep());
            }
        }

        public void ReadWork() { ReadManuscript(); }

        void ReadManuscript()
        {
            hud.header = job != null ? job.RecordTitle : "The manuscript";
            hud.body = script.FullText();
            hud.footer = "[E] or [Esc] to put it down";
            player.frozen = true;
            hud.Open(Modal.Reading);
            Sfx.Ui(Sfx.HasRecording("bookopen") && hud.header == "The manuscript" ? "bookopen" : "pageturn", 0.6f);
        }

        void UseBed()
        {
            if (busy) return;
            if (phase == Phase.Evening && (workDone || clock.minutes >= DayClock.BedFrom)) StartCoroutine(Sleep());
            else if (phase == Phase.Evening) hud.Say("Not yet. The work's not done. (Or wait until nine, and let it go.)", 3f);
            else hud.Say("Not yet. There's still the day to get through. It's " + clock.Now + ".", 3f);
        }

        IEnumerator Sleep()
        {
            busy = true;
            player.frozen = true;
            yield return FadeOut(0.7f);
            int unread = 0;
            foreach (var n in notes) if (!n.read) unread++;
            if (unread > 0) Shake(0.05f * unread, "went to bed without reading what was left out (" + unread + ")");
            if (!workDone) { script.SkipRest(); workDone = true; Log("work: went to bed without finishing it (counts as looking away)"); }
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
            if (LastDay) yield return EndRun("night");
            else
            {
                // Some mornings you only think you've woken.
                if (nights.FalseAwakening(StoryDay(calendar + 1)))
                {
                    phase = Phase.Morning;
                    yield return nights.FalseMorning(StoryDay(calendar + 1));
                }
                yield return BeginDay(calendar + 1);
            }
        }

        void UseCellar()
        {
            if (day >= 6 && deduced.Count >= 1 && !cellarOpened)
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
                house.SetHour(clock.Hour, day);
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

        /// <summary>The whole story, as the engine built it, written to the log before day 1 (every decision visible).</summary>
        void LogBible()
        {
            Log("story bible: " + RunDays + " days (" + LengthName(RunDays) + "), truth " + truth + ", " + (1 + older.Count) + " " + (older.Count > 0 ? "stories, one under another" : "story"));
            Log("story bible: " + tale.Bible("the story"));
            for (int i = 0; i < older.Count; i++) Log("story bible: " + older[i].Bible(i == 0 ? "under it" : "under that"));
            var sb = new System.Text.StringBuilder("story bible: days ");
            for (int c = 1; c <= RunDays; c++) sb.Append(c > 1 ? ", " : "").Append(c).Append(">").Append(StoryDay(c));
            Log(sb.ToString() + " (real day > story day)");
            if (older.Count > 0) Log("story bible: the older stor" + (older.Count > 1 ? "ies surface" : "y surfaces") + " from story day 3, in the clues and the dream's pages");
            Log("story bible: endings need " + DeductionsNeeded + " deduction" + (DeductionsNeeded == 1 ? "" : "s") + " from the hidden papers; the last word will be: " + (tale.Epilogue ?? "(none)"));
        }

        void SpawnClue()
        {
            // Today's note: the story's own (the truth's papers are hidden in the house instead: see Clues.cs).
            var all = tale != null ? tale.Clues() : new List<string>();
            // The voice's note for this evening, if it wrote one: left as well as the story's own.
            string said = voice.Note(calendar);
            if (said != null && day >= 2)
            {
                var vr = house.rooms[rng.Next(house.rooms.Count)];
                var vn = new Note { text = said };
                vn.go = Build.Box("Note", house.transform, vr.notePoint + new Vector3(0.12f, 0f, 0.1f), new Vector3(0.22f, 0.02f, 0.3f), new Color(0.93f, 0.9f, 0.82f), true, 0.6f);
                Build.Usable(vn.go, "Read what's been left here").use = () => ReadNote(vn);
                notes.Add(vn);
                Log("voice: its note for this evening is in the " + vr.name);
            }
            // The older stories' notes come after this one's (they surface once this story is under way).
            foreach (var o in older) all.AddRange(o.Clues());
            if (day < 2 || cluesSpawned >= all.Count) return;
            var free = new List<Room>(house.rooms);
            var room = free[rng.Next(free.Count)];
            var note = new Note { text = all[cluesSpawned++] };
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
                text = job.Leave + (truth == Truth.House && Knows("before")
                    ? "\n\nYou know what it is now, and that it was here before the house was. You don't look back. It doesn't need you to."
                    : "\n\nThe house watches you to the end of the road. It will keep.");
            }
            else if (truth == Truth.Killer && grip >= 0.45f && deduced.Count >= DeductionsNeeded && faced)
            {
                id = 0; title = "CONFESSION";
                text = job.Confession;
            }
            else if (truth == Truth.Killer)
            {
                id = 1; title = "THE GENTLER STORY";
                text = job.Gentler + "\n\nIt was a good house. Anyone would have said so.\n\nIn the morning you make coffee. Two cups.";
            }
            else if (truth == Truth.Keepers && deduced.Count >= DeductionsNeeded && faced)
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
            PlayerPrefs.SetInt("np_lastending", id);
            Diary.Burn();
            PlayerPrefs.Save();
            Log("ending: " + title + " (truth " + truth + ", grip " + grip.ToString("0.00") + ", clues " + clues + ", deductions " + deduced.Count + " (" + string.Join(", ", deduced.ToArray()) + ")" + (deducedWrong.Count > 0 ? ", written wrong " + deducedWrong.Count : "") +
                ", faced " + tagFace + " / looked away " + tagAway + " / blamed " + tagBlame + ")");
            hud.header = title;
            string last = tale != null ? tale.Epilogue : null;
            if (!string.IsNullOrEmpty(last)) text += "\n\n<i>" + last + "</i>";
            hud.body = text + "\n\n<size=22>This run's truth: " + truth + ".  Grip " + Mathf.RoundToInt(grip * 100f) + "%.  Clues " + clues +
                       ".  Dreams remembered: " + fear.Dreams + ".</size>";
            hud.footer = "[E]  Back to the title. The dreams will remember you.";
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

            if (In.Down(Btn.Pause) && CanPause) OpenPause();
            if (In.Down(Btn.Board) && CanPause && phase != Phase.Dream) OpenBoard();

            switch (hud.modal)
            {
                case Modal.Ending:
                    if (!busy && (In.Down(Btn.Restart) || In.Down(Btn.Interact) || In.Down(Btn.Pause))) ShowTitle();
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

            if (phase == Phase.Morning || phase == Phase.Afternoon || phase == Phase.Evening)
            {
                chores.Tick();
                job.Tick();
                TimeOfDay();
                if (In.Down(Btn.Watch) || (Time.frameCount % 20 == 0)) RefreshObjectives();
            }
            Reactions.Tick(link, player);
            WallHands.Tick(this);
            Habits.Tick(this);
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
            drone.volume = Mathf.MoveTowards(drone.volume, (quiet ? 0.08f : dread + (1f - grip) * 0.3f) * Settings.S.drone, Time.unscaledDeltaTime * 0.3f);
            heart.volume = Mathf.MoveTowards(heart.volume, quiet ? 0f : Mathf.Clamp01((0.55f - grip) * 2f) * 0.8f + Mathf.Max(0f, Reactions.Level) * (phase == Phase.Dream ? 0.4f : 0f), Time.unscaledDeltaTime * 0.5f);
            heart.pitch = 1f + (1f - grip) * 0.4f;

            if (hud.mind)
                hud.mindStatus = "day " + day + " · " + phase + " · truth " + truth + " · grip " + grip.ToString("0.00") + " · clues " + clues +
                                 "\nlines: faced " + tagFace + " / away " + tagAway + " / blamed " + tagBlame +
                                 "\nreaction " + Reactions.Level.ToString("+0.00;-0.00") + ", led by " + Reactions.Source + Reactions.Describe() +
                                 "\n" + link.Status + (VRRig.Active ? "\nVR: on" : "") + "\nclock " + clock.Now + " · work: " + job.Name + (workDone ? " (done)" : "") +
                                 "\nstoryteller: " + teller.Status() + "\nlearner: " + teller.learner.Describe() + (flyBody != null ? "\nfly body: " + flyBody.Describe() : "") +
                                 (phase == Phase.Dream ? "\ndream: " + fear.Describe() : "");
        }

        void SkipAhead()
        {
            Log("debug: skipping ahead from " + phase);
            if (hud.modal == Modal.Reading) { hud.modal = Modal.None; player.frozen = false; }
            switch (phase)
            {
                case Phase.Morning: chores.CompleteAll(); OnChoreDone(); clock.JumpTo(DayClock.Noon); break;
                case Phase.Afternoon: if (!workDone) script.AutoWrite(day); clock.JumpTo(DayClock.Dusk); break;
                case Phase.Evening: clock.JumpTo(DayClock.BedFrom); StartCoroutine(Sleep()); break;
                case Phase.Dream: dream.ForceEnd(); break;
            }
        }
    }
}
