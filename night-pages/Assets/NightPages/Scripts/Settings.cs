using System.IO;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Every setting, in one file (settings.json, next to the storyteller's memory), applied as soon as it changes.
    /// The first time it runs it takes over the old scattered keys (hardcore, the job, the stay, banned forms).
    /// </summary>
    [System.Serializable]
    public class Settings
    {
        // ---- display
        public int quality = -1;               // -1: leave Unity's default
        public int windowMode = 0;             // 0 fullscreen, 1 borderless, 2 windowed
        public int frameCap = 0;               // 0 monitor rate, else the cap (30, 60, 120, 999 = none)
        public bool vsync = true;
        public float fov = 68f;
        public float brightness = 0f;          // -1..1
        public bool grain = true, vignette = true;
        public float vrScale = 1f;
        // ---- audio
        public float master = 1f, effects = 1f, ambience = 1f, drone = 1f, voices = 1f;
        public bool mono = false;
        public bool captions = true;
        public int captionSize = 1;            // 0 small, 1 medium, 2 large
        // ---- controls
        public float mouse = 1f;
        public bool invertY = false;
        public bool toggleSprint = false;
        public float holdStep = 0.12f;
        // ---- VR comfort
        public int vrTurn = 0;                 // 0 snap 30, 1 snap 45, 2 smooth
        public bool vrVignette = true, vrSeated = false;
        public bool watchRight = false;
        // ---- content
        public int gore = 0;                   // 0 full, 1 implied, 2 off
        public int scares = 0;                 // 0 full, 1 softer, 2 gentle
        public bool reduceFlashing = false;
        public float shake = 1f;
        public bool ghostsOnly = false;
        public string banned = "";
        // ---- senses and privacy
        public bool remember = true;
        public bool voice = true;              // the AI voice writes some of the house's words (needs a key; see StoryVoice)
        // ---- the game
        public int dayLength = 1;              // 0: 24 min, 1: 32 min, 2: 48 min
        public bool choreHints = true;
        public int difficulty = 1;             // 0 easy, 1 medium, 2 hard, 3 hardcore (see Difficulty.cs)
        public bool thinking = false;
        // ---- the next run (character creation remembers the last one)
        public string job = "random";
        public int days = 7;

        public static Settings S = new Settings();
        static string PathOf { get { return System.IO.Path.Combine(Application.persistentDataPath, "settings.json"); } }

        public static void Load()
        {
            try
            {
                if (File.Exists(PathOf)) { S = JsonUtility.FromJson<Settings>(File.ReadAllText(PathOf)) ?? new Settings(); return; }
            }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] settings unreadable, starting fresh: " + e.Message); }
            S = new Settings();
            // Take over the old keys.
            S.difficulty = PlayerPrefs.GetInt("np_hardcore", 0) == 1 ? 3 : 1;
            S.job = PlayerPrefs.GetString("np_job", "random");
            S.days = PlayerPrefs.GetInt("np_days", 7);
            S.banned = PlayerPrefs.GetString("np_banned", "");
            S.ghostsOnly = PlayerPrefs.GetInt("np_ghostsonly", 0) == 1;
            Save();
        }

        public static void Save()
        {
            try { File.WriteAllText(PathOf, JsonUtility.ToJson(S, true)); }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't save settings: " + e.Message); }
        }

        /// <summary>How strong the sharpest scares may be (2 full, 1 softer, 0 gentle).</summary>
        public static int ScareCap { get { return S.scares == 2 ? 0 : S.scares == 1 ? 1 : 2; } }

        /// <summary>The day's speed on the clock (game minutes per real second).</summary>
        public static float ClockRate { get { return S.dayLength == 0 ? 0.6667f : S.dayLength == 2 ? 0.3333f : 0.5f; } }
        public static string[] DayLengthNames = { "24 minutes", "32 minutes", "48 minutes" };

        /// <summary>Apply what can change on the fly (screen, sound, the forms never to show).</summary>
        public static void Apply()
        {
            if (!Application.isBatchMode)
            {
                if (S.quality >= 0 && S.quality < QualitySettings.names.Length) QualitySettings.SetQualityLevel(S.quality, true);
                Screen.fullScreenMode = S.windowMode == 0 ? FullScreenMode.ExclusiveFullScreen : S.windowMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                QualitySettings.vSyncCount = S.vsync && S.frameCap == 0 ? 1 : 0;
                Application.targetFrameRate = S.frameCap == 0 ? -1 : S.frameCap >= 999 ? -1 : S.frameCap;
                AudioListener.volume = Mathf.Clamp01(S.master);
                var cfg = AudioSettings.GetConfiguration();
                var want = S.mono ? AudioSpeakerMode.Mono : AudioSpeakerMode.Stereo;
                if (cfg.speakerMode != want) { cfg.speakerMode = want; AudioSettings.Reset(cfg); }
            }
            FearEngine.Banned.Clear();
            foreach (var label in (S.banned ?? "").Split(','))
                if (label.Trim().Length > 0) FearEngine.Banned.Add(label.Trim());
            if (UnityEngine.XR.XRSettings.enabled) UnityEngine.XR.XRSettings.eyeTextureResolutionScale = Mathf.Clamp(S.vrScale, 0.5f, 1.6f);
        }
    }
}
