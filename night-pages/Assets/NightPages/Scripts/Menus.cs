using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The pages: the title (the house at dusk from the end of the drive, with the typed page beside it), character
    /// creation, the options, the pause menu, the box of pages, and the morning diary that Continue reads.
    /// </summary>
    public partial class GameDirector
    {
        Menu menu;
        /// <summary>Who came to the house this run.</summary>
        public Character hero = new Character();
        int pauseClosedFrame = -10;

        static readonly string[] EndingNames = { "Confession", "The gentler story", "Reunion", "Inheritance", "Taken", "Unfinished" };

        void EnsureMenu()
        {
            if (menu == null) menu = gameObject.AddComponent<Menu>();
        }

        // ---------------------------------------------------------------- the title

        void ShowTitle()
        {
            EnsureMenu();
            phase = Phase.Title;
            Time.timeScale = 1f;
            player.frozen = true;
            haunt.ClearTransient();
            TitleScene();
            hud.header = ""; hud.body = ""; hud.footer = "";
            hud.fadeSpeed = 0.6f;
            hud.fadeTarget = 0f;
            hero = LoadHero();
            menu.Show(TitlePage());
            Log("title: the house at dusk from the end of the drive" + (Diary.Exists ? "; a diary is waiting (Continue)" : ""));
        }

        // The house at half past seven, from where the drive meets the clearing, with one window lit: which one
        // depends on how the last run ended.
        void TitleScene()
        {
            house.SetHour(19.5f, 1);
            foreach (var r in house.rooms) r.lit = false;
            int last = PlayerPrefs.GetInt("np_lastending", -1);
            if (house.rooms.Count > 0)
            {
                var lit = house.rooms[((last + 1) % house.rooms.Count + house.rooms.Count) % house.rooms.Count];
                lit.lit = true;
                Log("title: the " + lit.name + " window is lit" + (last >= 0 ? " (the last run ended: " + EndingNames[Mathf.Clamp(last, 0, 5)] + ")" : ""));
            }
            house.ShowSwitches();
            Vector3 at = new Vector3(2.6f, Outdoors.Ground + 0.05f, 26.5f);
            Vector3 to = new Vector3(0f, 0f, 8f) - at;
            player.Teleport(at, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - 13f);     // the house sits right of centre, clear of the page
        }

        MenuPage TitlePage()
        {
            var p = new MenuPage { title = "NIGHT PAGES", compact = true, intro = "A prototype. The house is a long way from anywhere. Your grandparents left it to you." };
            var d = Diary.Exists ? Diary.Read() : null;
            p.Add(new MenuEntry { label = "Continue", activate = () => { var dd = Diary.Read(); if (dd != null) ContinueRun(dd); }, enabled = () => Diary.Exists,
                                  hint = d != null ? "Your diary: " + d.Summary + " Written " + d.saved + "." : "Nothing written yet. The diary is kept each morning." });
            p.Add(MenuEntry.Act("New run", () => menu.Push(CreatePage()), "Who you are, what you do, how long you stay, and how hard it pushes."));
            p.Add(MenuEntry.Act("Options", () => menu.Push(OptionsPage()), "Display, sound, controls, VR comfort, content, senses and privacy."));
            p.Add(MenuEntry.Act("The box of pages", () => menu.Push(BoxPage()), "What you've found so far, across every run."));
            p.Add(MenuEntry.Act("How to play", () => menu.Push(HowPage())));
            p.Add(MenuEntry.Act("Quit", Application.Quit));
            p.footer = "Up/Down: choose     Enter: select";
            return p;
        }

        MenuPage HowPage()
        {
            var p = new MenuPage
            {
                title = "How to play",
                intro = "Days: do the chores on your list, then the afternoon's work. Evenings, the house is less quiet. At night you sleep, and dream; " +
                        "each dream is a maze that changes. Get out before the night runs out.\n\n" +
                        "WASD to move, mouse to look, E to use, Shift to hurry. Hold the left mouse button to work with your hands (scrub, sweep, turn, pour). " +
                        "Holding something: the mouse wheel moves it nearer or further, and the middle button with the mouse turns it. Right button throws. Tab: your watch.\n\n" +
                        "F1: see what the game is thinking. V: let it listen for gasps (microphone). Esc: pause.\n\n" +
                        "VR: left stick to walk, right stick to turn, point and pull the trigger to use, grip to pick up. Your watch is on your left wrist.\n\n" +
                        "The dreams learn what unsettles you, and they remember.",
            };
            p.Add(MenuEntry.Act("Back", () => menu.Back()));
            return p;
        }

        MenuPage BoxPage()
        {
            int found = PlayerPrefs.GetInt("np_endings", 0);
            int n = 0;
            for (int i = 0; i < EndingNames.Length; i++) if ((found & (1 << i)) != 0) n++;
            var p = new MenuPage { title = "The box of pages", intro = n == 0 ? "Empty, so far. Endings you reach are kept here." : n + " of " + EndingNames.Length + " endings found." };
            p.Add(MenuEntry.Head("Endings"));
            for (int i = 0; i < EndingNames.Length; i++)
            {
                bool got = (found & (1 << i)) != 0;
                p.Add(new MenuEntry { label = got ? EndingNames[i] : "· · ·", enabled = () => false });
            }
            p.Add(MenuEntry.Head("Dreams"));
            p.Add(new MenuEntry { label = "Dreams remembered", value = () => fear.Dreams.ToString(), enabled = () => false });
            p.Add(MenuEntry.Act("Back", () => menu.Back()));
            return p;
        }

        // ---------------------------------------------------------------- character creation

        static Character LoadHero()
        {
            try
            {
                var s = PlayerPrefs.GetString("np_hero", "");
                if (s.Length > 0) { var c = JsonUtility.FromJson<Character>(s); if (c != null) return c; }
            }
            catch { }
            return new Character();
        }

        static void SaveHero(Character c)
        {
            PlayerPrefs.SetString("np_hero", JsonUtility.ToJson(c));
            PlayerPrefs.Save();
        }

        static string[] JobChoices
        {
            get
            {
                var l = new List<string> { "any (drawn at random)" };
                foreach (var id in Job.Ids) l.Add(Job.NameOf(id));
                return l.ToArray();
            }
        }

        static int JobIndex { get { return Settings.S.job == "random" ? 0 : System.Array.IndexOf(Job.Ids, Settings.S.job) + 1; } }
        static void SetJobIndex(int i) { Settings.S.job = i <= 0 ? "random" : Job.Ids[Mathf.Clamp(i - 1, 0, Job.Ids.Length - 1)]; }

        static string[] LengthChoices
        {
            get { var l = new string[RunLengths.Length]; for (int i = 0; i < l.Length; i++) l[i] = LengthName(RunLengths[i]); return l; }
        }

        static readonly string[] DifficultyHints =
        {
            "Fewer frights and longer rests. Lose a dream and you simply wake.",
            "The house as it's meant to be. Lose a dream and you dream it again.",
            "It pushes harder and sooner, and the figure comes a day early. A lost dream comes back with less night.",
            "It pushes hardest. Lose a dream and you start again from day one; the diary burns.",
        };

        MenuPage CreatePage()
        {
            var p = new MenuPage { title = "Who comes to the house", intro = "Everything here is remembered for next time." };
            p.Add(MenuEntry.Head("You"));
            p.Add(MenuEntry.Text("Name", () => hero.name, s => hero.name = s, 18, "Type a name, or leave it empty. The house will use it."));
            p.Add(MenuEntry.Act("Suggest a name", () => hero.name = Character.SuggestNames[Random.Range(0, Character.SuggestNames.Length)]));
            p.Add(MenuEntry.Choice("How you're addressed", Character.AddressNames, () => hero.address, i => hero.address = i));
            p.Add(MenuEntry.Choice("Skin", Character.SkinNames, () => hero.skin, i => hero.skin = i, "Your hands, in the house and on the watch."));
            p.Add(MenuEntry.Choice("Clothes", Character.ClothesNames, () => hero.clothes, i => hero.clothes = i, "Your sleeves."));
            p.Add(MenuEntry.Choice("Coat", Character.CoatNames, () => hero.coat, i => hero.coat = i, "It hangs by the front door all week."));
            p.Add(MenuEntry.Head("The stay"));
            p.Add(MenuEntry.Choice("You work as", JobChoices, () => JobIndex, SetJobIndex, "The afternoon's work, and how the story reaches you."));
            p.Add(MenuEntry.Choice("You stay for", LengthChoices, () => Mathf.Max(0, System.Array.IndexOf(RunLengths, Settings.S.days)), i => Settings.S.days = RunLengths[i],
                                   "Longer stays tell more than one story, one under another."));
            var diff = MenuEntry.Choice("Difficulty", DifficultyNames, () => Level, i => Settings.S.difficulty = i);
            diff.hint = null;
            p.Add(diff);
            p.Add(MenuEntry.Choice("A day lasts", Settings.DayLengthNames, () => Settings.S.dayLength, i => Settings.S.dayLength = i, "Real time, from waking to dark."));
            p.Add(MenuEntry.Act("Surprise me", () =>
            {
                hero.name = Character.SuggestNames[Random.Range(0, Character.SuggestNames.Length)];
                hero.address = Random.Range(0, 3);
                hero.skin = Random.Range(0, Character.SkinNames.Length);
                hero.clothes = Random.Range(0, Character.ClothesNames.Length);
                hero.coat = Random.Range(0, Character.CoatNames.Length);
                Settings.S.job = "random";
                Settings.Save();
            }, "A stranger, with a job drawn at random. Length and difficulty stay as they are."));
            p.Add(MenuEntry.Act("Begin", () =>
            {
                if (Diary.Exists) menu.Push(ConfirmPage("Start a new run?", "Your diary (" + (Diary.Read() != null ? Diary.Read().Summary : "a run in progress") + ") will be burnt.", "Burn it and begin", BeginNew));
                else BeginNew();
            }));
            // The difficulty's explanation follows the choice.
            p.onShow = () => { };
            diff.value = () => DifficultyNames[Level];
            var baseStep = diff.step;
            diff.step = d => { baseStep(d); diff.hint = DifficultyHints[Level]; };
            diff.hint = DifficultyHints[Level];
            return p;
        }

        MenuPage ConfirmPage(string title, string intro, string yes, System.Action act)
        {
            var p = new MenuPage { title = title, intro = intro };
            p.Add(MenuEntry.Act("No, go back", () => menu.Back()));
            p.Add(MenuEntry.Act(yes, act));
            return p;
        }

        void BeginNew()
        {
            SaveHero(hero);
            Settings.Save();
            Diary.Burn();
            RunDays = System.Array.IndexOf(RunLengths, Settings.S.days) >= 0 ? Settings.S.days : 7;
            menu.Close();
            NewRun();
        }

        /// <summary>Your look where it can be seen: your sleeves and hands (the watch arm), and the coat on its hook.</summary>
        void ApplyLook()
        {
            Color skin = Character.SkinTones[Mathf.Clamp(hero.skin, 0, Character.SkinTones.Length - 1)];
            Color cloth = Character.ClothesColours[Mathf.Clamp(hero.clothes, 0, Character.ClothesColours.Length - 1)];
            Color coat = Character.CoatColours[Mathf.Clamp(hero.coat, 0, Character.CoatColours.Length - 1)];
            foreach (var r in player.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                if (n == "Sleeve") r.material.color = cloth;
                else if (n == "Cuff") r.material.color = cloth * 0.8f;
                else if (n == "Hand" || n == "Wrist") r.material.color = skin;
            }
            if (house.leave != null) { var rr = house.leave.GetComponent<Renderer>(); if (rr != null) rr.material.color = coat; }
            Log("you: " + hero.Name + " (" + Character.AddressNames[hero.address] + "), " + Character.SkinNames[hero.skin] + " skin, " + Character.ClothesNames[hero.clothes] +
                ", a " + Character.CoatNames[hero.coat] + " coat");
        }

        // ---------------------------------------------------------------- options

        MenuPage OptionsPage()
        {
            var p = new MenuPage { title = "Options" };
            p.Add(MenuEntry.Act("Display", () => menu.Push(DisplayPage())));
            p.Add(MenuEntry.Act("Sound", () => menu.Push(SoundPage())));
            p.Add(MenuEntry.Act("Controls", () => menu.Push(ControlsPage())));
            p.Add(MenuEntry.Act("VR comfort", () => menu.Push(VRPage())));
            p.Add(MenuEntry.Act("Content", () => menu.Push(ContentPage()), "What can appear, how strong, and what never."));
            p.Add(MenuEntry.Act("Senses and privacy", () => menu.Push(SensesPage()), "The camera, the microphone, and what the game remembers about you."));
            p.Add(MenuEntry.Act("The game", () => menu.Push(GamePage())));
            p.Add(MenuEntry.Act("Back", () => menu.Back()));
            return p;
        }

        static string Pct(float v) { return Mathf.RoundToInt(v * 100f) + "%"; }

        MenuPage DisplayPage()
        {
            var q = new List<string> { "default" };
            q.AddRange(QualitySettings.names);
            var p = new MenuPage { title = "Display" };
            p.Add(MenuEntry.Choice("Quality", q.ToArray(), () => Settings.S.quality + 1, i => Settings.S.quality = i - 1));
            p.Add(MenuEntry.Choice("Window", new[] { "fullscreen", "borderless", "windowed" }, () => Settings.S.windowMode, i => Settings.S.windowMode = i));
            int[] caps = { 0, 30, 60, 120, 999 };
            p.Add(MenuEntry.Choice("Frame rate", new[] { "match the monitor", "30", "60", "120", "unlimited" }, () => Mathf.Max(0, System.Array.IndexOf(caps, Settings.S.frameCap)), i => Settings.S.frameCap = caps[i]));
            p.Add(MenuEntry.Toggle("Vsync", () => Settings.S.vsync, b => Settings.S.vsync = b, "Only when the frame rate matches the monitor."));
            p.Add(MenuEntry.Slider("Field of view", 55f, 95f, 1f, () => Settings.S.fov, v => Settings.S.fov = v, v => v.ToString("0") + "°"));
            p.Add(MenuEntry.Slider("Brightness", -1f, 1f, 0.1f, () => Settings.S.brightness, v => Settings.S.brightness = v, v => v.ToString("+0.0;-0.0;0"),
                                   "Set it so the dark corner of a room is dark but not black."));
            p.Add(MenuEntry.Toggle("Film grain", () => Settings.S.grain, b => Settings.S.grain = b));
            p.Add(MenuEntry.Toggle("Dark edges", () => Settings.S.vignette, b => Settings.S.vignette = b, "The vignette. Fear still closes in at the edges."));
            p.Add(MenuEntry.Slider("VR resolution", 0.5f, 1.6f, 0.1f, () => Settings.S.vrScale, v => Settings.S.vrScale = v, Pct));
            p.Add(MenuEntry.Act("Back", () => menu.Back()));
            return p;
        }

        MenuPage SoundPage()
        {
            var p = new MenuPage { title = "Sound" };
            p.Add(MenuEntry.Slider("Everything", 0f, 1f, 0.05f, () => Settings.S.master, v => Settings.S.master = v, Pct));
            p.Add(MenuEntry.Slider("Sounds", 0f, 1f, 0.05f, () => Settings.S.effects, v => Settings.S.effects = v, Pct, "Doors, steps, things breaking."));
            p.Add(MenuEntry.Slider("The house around you", 0f, 1f, 0.05f, () => Settings.S.ambience, v => Settings.S.ambience = v, Pct, "Wind, rain, the fridge, the clock."));
            p.Add(MenuEntry.Slider("The drone", 0f, 1f, 0.05f, () => Settings.S.drone, v => Settings.S.drone = v, Pct, "The low tone that rises with dread."));
            p.Add(MenuEntry.Slider("Voices", 0f, 1f, 0.05f, () => Settings.S.voices, v => Settings.S.voices = v, Pct, "Whispers, breathing, the phone."));
            p.Add(MenuEntry.Toggle("Mono", () => Settings.S.mono, b => Settings.S.mono = b, "One channel: you lose where sounds come from."));
            p.Add(MenuEntry.Toggle("Captions", () => Settings.S.captions, b => Settings.S.captions = b, "What you hear, in words, with where it is."));
            p.Add(MenuEntry.Choice("Caption size", new[] { "small", "medium", "large" }, () => Settings.S.captionSize, i => Settings.S.captionSize = i));
            p.Add(MenuEntry.Act("Back", () => menu.Back()));
            return p;
        }

        MenuPage ControlsPage()
        {
            var p = new MenuPage { title = "Controls" };
            p.Add(MenuEntry.Slider("Mouse speed", 0.2f, 3f, 0.1f, () => Settings.S.mouse, v => Settings.S.mouse = v, v => "×" + v.ToString("0.0")));
            p.Add(MenuEntry.Toggle("Invert look", () => Settings.S.invertY, b => Settings.S.invertY = b));
            p.Add(MenuEntry.Toggle("Tap to hurry", () => Settings.S.toggleSprint, b => Settings.S.toggleSprint = b, "Shift turns hurrying on and off, instead of holding it. Stopping ends it."));
            p.Add(MenuEntry.Slider("Wheel step (held things)", 0.04f, 0.3f, 0.02f, () => Settings.S.holdStep, v => Settings.S.holdStep = v, v => v.ToString("0.00") + " m"));
            p.Add(MenuEntry.Act("Back", () => menu.Back()));
            return p;
        }

        MenuPage VRPage()
        {
            var p = new MenuPage { title = "VR comfort" };
            p.Add(MenuEntry.Choice("Turning", new[] { "snap 30°", "snap 45°", "smooth" }, () => Settings.S.vrTurn, i => Settings.S.vrTurn = i, "Snap turning is kinder to most stomachs."));
            p.Add(MenuEntry.Slider("Resolution", 0.5f, 1.6f, 0.1f, () => Settings.S.vrScale, v => Settings.S.vrScale = v, Pct));
            p.Add(MenuEntry.Act("Back", () => menu.Back()));
            return p;
        }

        MenuPage ContentPage()
        {
            var p = new MenuPage { title = "Content", intro = "Difficulty sets how often and how soon. These set what, and how far. They always win." };
            p.Add(MenuEntry.Choice("Scares", new[] { "full", "softer", "gentle" }, () => Settings.S.scares, i => Settings.S.scares = i,
                                   "Softer and gentle cap how strong the worst moments and the figure can get."));
            p.Add(MenuEntry.Choice("Gore", new[] { "shown", "implied", "off" }, () => Settings.S.gore, i => Settings.S.gore = i,
                                   "Implied: you hear it, you see what's left. Off: none of it."));
            p.Add(MenuEntry.Toggle("Fewer flashes", () => Settings.S.reduceFlashing, b => Settings.S.reduceFlashing = b, "Flickering lights flicker once, not on and on."));
            p.Add(MenuEntry.Slider("Screen shake", 0f, 1f, 0.1f, () => Settings.S.shake, v => Settings.S.shake = v, Pct));
            p.Add(MenuEntry.Toggle("Only ghosts", () => Settings.S.ghostsOnly, b => Settings.S.ghostsOnly = b,
                                   "What's really going on is always supernatural: no illness, no grief, no one real. From the next run."));
            p.Add(MenuEntry.Act("Things never to show", () => menu.Push(BannedPage()), "Any form you never want to see, the dreams leave out."));
            p.Add(MenuEntry.Act("Back", () => menu.Back()));
            return p;
        }

        static HashSet<string> BannedSet()
        {
            var set = new HashSet<string>();
            foreach (var s in (Settings.S.banned ?? "").Split(',')) if (s.Trim().Length > 0) set.Add(s.Trim());
            return set;
        }

        MenuPage BannedPage()
        {
            var p = new MenuPage { title = "Things never to show" };
            for (int s = 0; s < FearEngine.Slots.Length; s++)
            {
                var slot = FearEngine.Slots[s];
                if (slot.options == null || slot.options.Length < 2) continue;
                p.Add(MenuEntry.Head(slot.name));
                for (int o = 1; o < slot.options.Length; o++)
                {
                    string label = slot.options[o].label;
                    p.Add(MenuEntry.Choice(label, new[] { "allowed", "never" }, () => BannedSet().Contains(label) ? 1 : 0, i =>
                    {
                        var set = BannedSet();
                        if (i == 1) set.Add(label); else set.Remove(label);
                        Settings.S.banned = string.Join(",", new List<string>(set).ToArray());
                        Log("content: '" + label + "' is now " + (i == 1 ? "never shown" : "allowed"));
                    }));
                }
            }
            p.Add(MenuEntry.Act("Back", () => menu.Back()));
            return p;
        }

        MenuPage SensesPage()
        {
            var p = new MenuPage { title = "Senses and privacy", intro = "Everything stays on this computer. Nothing is recorded or sent anywhere." };
            p.Add(MenuEntry.Toggle("Camera (your face)", () => FaceReader.On, b => { if (b != FaceReader.On) FaceReader.Enable(b); },
                                   "Reads your expression to see what lands. The picture never leaves the game and isn't saved."));
            p.Add(MenuEntry.Toggle("Microphone (gasps)", () => Reactions.voice != null && Reactions.voice.Listening, b => { if (Reactions.voice != null) Reactions.voice.Toggle(); },
                                   "Loudness only. Nothing is recorded."));
            p.Add(MenuEntry.Toggle("Remember me", () => Settings.S.remember, b => Settings.S.remember = b,
                                   "Off: what it learns about you is kept only until you quit."));
            p.Add(MenuEntry.Act("Forget me", () => menu.Push(ConfirmPage("Forget you?", "What the house has learned about what frightens you, and your habits, will be deleted. " +
                                                                    "The game starts fresh with you next time it starts.", "Forget me", ForgetMe))));
            p.Add(MenuEntry.Act("Back", () => menu.Back()));
            return p;
        }

        void ForgetMe()
        {
            foreach (var path in new[] { TellerPath, HabitsPath, System.IO.Path.Combine(Application.persistentDataPath, "nightpages-fear.json"),
                                         System.IO.Path.Combine(Application.persistentDataPath, "np_paint_wishes.json") })
                try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }
            forgotten = true;
            Log("privacy: forgot you (the storyteller's memory, your habits, the fear profile). Nothing more is saved this session");
            menu.Back();
            hud.Say("Forgotten.", 2.5f);
        }

        /// <summary>After Forget me, nothing more is written about you until the game restarts.</summary>
        bool forgotten;
        bool MayRemember { get { return Settings.S.remember && !forgotten; } }

        MenuPage GamePage()
        {
            var p = new MenuPage { title = "The game" };
            p.Add(MenuEntry.Choice("A day lasts", Settings.DayLengthNames, () => Settings.S.dayLength, i => Settings.S.dayLength = i, "Real time, from waking to dark."));
            p.Add(MenuEntry.Choice("Difficulty (next run)", DifficultyNames, () => Settings.S.difficulty, i => Settings.S.difficulty = i));
            p.Add(MenuEntry.Toggle("Show what it's thinking", () => hud.mind, b => hud.mind = b, "The log of every decision the game makes (F1)."));
            p.Add(MenuEntry.Act("Back", () => menu.Back()));
            return p;
        }

        // ---------------------------------------------------------------- pause

        bool CanPause
        {
            get
            {
                return !busy && hud.modal == Modal.None && Time.frameCount - pauseClosedFrame > 2 &&
                       (phase == Phase.Morning || phase == Phase.Afternoon || phase == Phase.Evening || phase == Phase.Dream);
            }
        }

        void OpenPause()
        {
            EnsureMenu();
            Time.timeScale = 0f;
            var p = new MenuPage { title = "Paused", back = Resume };
            p.Add(MenuEntry.Act("Back to it", Resume));
            p.Add(MenuEntry.Act("Options", () => menu.Push(OptionsPage())));
            p.Add(MenuEntry.Act("How to play", () => menu.Push(HowPage())));
            p.Add(MenuEntry.Act("Leave for the title", () => menu.Push(ConfirmPage("Leave for the title?", "Your diary was written this morning (day " + calendar + "). Anything since then is lost.",
                                                                                    "Leave", () => { menu.Close(); Time.timeScale = 1f; pauseClosedFrame = Time.frameCount; LeaveToTitle(); }))));
            p.Add(MenuEntry.Act("Quit the game", () => menu.Push(ConfirmPage("Quit?", "Your diary was written this morning. Anything since then is lost.", "Quit", Application.Quit))));
            menu.Show(p);
            Log("paused");
        }

        void Resume()
        {
            menu.Close();
            Time.timeScale = 1f;
            pauseClosedFrame = Time.frameCount;
            if (!VRRig.Active) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        void LeaveToTitle()
        {
            StopAllCoroutines();
            busy = false;
            if (dream.active) dream.Clear();
            haunt.ClearTransient();
            hud.ClearSubtitles();
            ShowTitle();
        }

        // ---------------------------------------------------------------- the diary

        void WriteDiary()
        {
            if (Tour.Requested) return;
            var d = new Diary
            {
                calendar = calendar, runDays = RunDays, truth = (int)truth, houseSeed = House.BuiltSeed, layout = house.Layout, wearSeed = house.wearSeed,
                rewriteSeed = rewriteSeed, job = job.Id, difficulty = Level, grip = grip, clues = clues, cluesSpawned = cluesSpawned,
                tagFace = tagFace, tagAway = tagAway, tagBlame = tagBlame, dreamDeaths = dreamDeaths, cellarOpened = cellarOpened,
                rungOpen = rungOpen, rungShown = rungShown, rungDay = (int[])rungDay.Clone(),
                who = hero.Copy(), tale = Diary.Of(tale), written = script.Export(),
                found = new List<string>(found), deduced = new List<string>(deduced), deducedWrong = new List<string>(deducedWrong),
            };
            foreach (var o in older) d.older.Add(Diary.Of(o));
            d.Write();
            Log("diary: written for the morning of day " + calendar + " of " + RunDays);
        }

        void ContinueRun(Diary d)
        {
            menu.Close();
            RunDays = d.runDays > 0 ? d.runDays : 7;
            if (d.difficulty != Level) { Settings.S.difficulty = d.difficulty; Settings.Save(); Log("diary: this run is on " + DifficultyNames[Level] + ", as it was begun"); }
            House.Seed = d.houseSeed;
            RebuildHouse(d.layout);
            runsStarted++;
            house.wearSeed = d.wearSeed;
            nights.NewRun();
            rng = new System.Random();
            truth = (Truth)d.truth;
            grip = d.grip;
            tagFace = d.tagFace; tagAway = d.tagAway; tagBlame = d.tagBlame;
            clues = d.clues;
            cluesSpawned = Mathf.Max(0, d.clues - (d.found != null ? d.found.Count : 0));     // the story's notes left but never read are left again
            dreamDeaths = d.dreamDeaths;
            cellarOpened = d.cellarOpened;
            sawCellarFloor = false;
            rewriteSeed = d.rewriteSeed;
            foreach (var n in notes) if (n.go != null) Destroy(n.go);
            notes.Clear();
            job = Job.Make(d.job);
            job.Init(this);
            WireHouse();
            ApplyDifficulty();
            ResetRungs();
            rungOpen = d.rungOpen; rungShown = d.rungShown;
            if (d.rungDay != null && d.rungDay.Length == 6) rungDay = (int[])d.rungDay.Clone();
            script.Reset();
            script.Import(d.written);
            hud.mindLines.Clear();
            hero = d.who != null ? d.who.Copy() : new Character();
            tale = Tale.Restore(truth, d.tale, rng);
            tale.Meet(hero);
            older.Clear();
            foreach (var o in d.older) { var t = Tale.Restore(truth, o, rng); t.Meet(hero); older.Add(t); }
            Log("diary: continuing day " + d.calendar + " of " + RunDays + " (" + d.Summary + "), truth " + truth + ", house layout " + house.Layout);
            LogBible();
            haunt.SetTale(tale);
            ApplyLook();
            found.Clear(); deduced.Clear(); deducedWrong.Clear();
            if (d.found != null) found.AddRange(d.found);
            if (d.deduced != null) deduced.AddRange(d.deduced);
            if (d.deducedWrong != null) deducedWrong.AddRange(d.deducedWrong);
            HideClues();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            StartCoroutine(BeginDay(d.calendar));
        }
            // ---------------------------------------------------------------- the studio's check of all this

        /// <summary>
        /// Studio: build every page (nothing may throw, nothing may be empty), photograph the title scene, and send
        /// a diary through the file and back (the story must come back the same).
        /// </summary>
        System.Collections.IEnumerator MenuChecks(System.Text.StringBuilder summary)
        {
            EnsureMenu();
            int pages = 0, rows = 0;
            var all = new List<System.Func<MenuPage>> { TitlePage, HowPage, BoxPage, CreatePage, OptionsPage, DisplayPage, SoundPage, ControlsPage, VRPage, ContentPage, BannedPage, SensesPage, GamePage };
            foreach (var f in all)
            {
                MenuPage pg = null;
                try { pg = f(); }
                catch (System.Exception e) { Problem("menus: a page threw: " + e.Message); continue; }
                pages++;
                int sel = 0;
                foreach (var it in pg.items)
                {
                    rows++;
                    if (it.Selectable) sel++;
                    try { if (it.value != null) it.value(); if (it.getText != null) it.getText(); }
                    catch (System.Exception e) { Problem("menus: '" + pg.title + "' / '" + it.label + "' threw showing its value: " + e.Message); }
                }
                if (sel == 0) Problem("menus: '" + pg.title + "' has nothing to choose");
            }
            Log("menus: " + pages + " pages built, " + rows + " rows");
            // Every setting steps forward and back without throwing (then all are put back as they were).
            var keep = JsonUtility.ToJson(Settings.S);
            foreach (var f in new List<System.Func<MenuPage>> { DisplayPage, SoundPage, ControlsPage, VRPage, ContentPage, GamePage })
                foreach (var it in f().items)
                {
                    if (it.step == null || it.label.StartsWith("Window") || it.label.StartsWith("Quality")) continue;
                    try { it.step(1); it.value(); it.step(-1); it.value(); }
                    catch (System.Exception e) { Problem("menus: stepping '" + it.label + "' threw: " + e.Message); }
                }
            Settings.S = JsonUtility.FromJson<Settings>(keep);

            // The diary, through the file and back.
            hero = new Character { name = "Ellis", address = 1, skin = 3, clothes = 2, coat = 4 };
            tale.Meet(hero);
            var d = new Diary { calendar = calendar, runDays = RunDays, truth = (int)truth, houseSeed = House.BuiltSeed, layout = house.Layout, job = job.Id, who = hero.Copy(),
                                tale = Diary.Of(tale), written = script.Export(), rungDay = (int[])rungDay.Clone() };
            foreach (var o in older) d.older.Add(Diary.Of(o));
            var back = JsonUtility.FromJson<Diary>(JsonUtility.ToJson(d));
            var again = Tale.Restore(truth, back.tale, new System.Random(1));
            if (again.CoreKey != tale.CoreKey) Problem("diary: the story didn't come back the same (" + tale.CoreKey + " -> " + again.CoreKey + ")");
            else Log("diary: the story came back the same through the file (" + again.CoreKey + "); " + back.Summary);
            if (back.who == null || back.who.name != "Ellis") Problem("diary: who you are didn't come back");
            ApplyLook();

            // The title scene, as the title shows it.
            TitleScene();
            yield return Settle(2.5f);
            Shot("title: the house at dusk from the end of the drive");
            summary.Append("  \"menus\": ").Append(pages).Append(",\n");
        }
    }
}
