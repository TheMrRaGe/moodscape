using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Captions for sounds, with where they came from ("A door slams, behind you"). The house's sounds carry much of
    /// the story, so players who can't hear them (or play quiet) still get them. Off in Options.
    /// </summary>
    public static class Captions
    {
        static readonly Dictionary<string, string> Words = new Dictionary<string, string>
        {
            { "slam", "A door slams" }, { "knock", "Knocking" }, { "creak", "A creak" }, { "doorcreak", "A door creaks" },
            { "scratch", "Scratching" }, { "whisper", "Whispering" }, { "breath", "Breathing" }, { "gasp", "A gasp" },
            { "shuffle", "Shuffling steps" }, { "smash", "Something smashes" }, { "shatter", "Something breaks" },
            { "crack", "A crack" }, { "ring", "The phone rings" }, { "chime", "The clock strikes" }, { "musicbox", "A music box" },
            { "lullaby", "Someone humming" }, { "splash", "A splash" }, { "roll", "Something rolling" }, { "drag", "Something dragged" },
            { "bonk", "A thud" }, { "clang", "A clang" }, { "static", "Static" }, { "sting", "" }, { "unlock", "A lock turns" },
            { "dooropen", "A door opens" }, { "doorclose", "A door closes" }, { "drip", "Dripping" }, { "murmur", "A voice, muffled" },
        };

        public static readonly List<KeyValuePair<string, float>> Showing = new List<KeyValuePair<string, float>>();
        static readonly Dictionary<string, float> last = new Dictionary<string, float>();

        public static void ResetStatics() { Showing.Clear(); last.Clear(); }

        public static void Heard(string name, Vector3 at, float vol)
        {
            if (!Settings.S.captions || vol < 0.12f) return;
            string w;
            if (!Words.TryGetValue(name, out w) || string.IsNullOrEmpty(w)) return;
            var cam = Camera.main;
            if (cam == null) return;
            float lt;
            if (last.TryGetValue(name, out lt) && Time.unscaledTime - lt < 2.5f) return;     // not every drip
            last[name] = Time.unscaledTime;
            Vector3 d = at - cam.transform.position;
            float dist = d.magnitude;
            if (dist < 0.8f && (name == "bonk" || name == "creak")) return;                 // your own doing, right where you are
            d.y = 0f;
            string where;
            if (d.magnitude < 0.5f) where = "";
            else
            {
                float a = Vector3.SignedAngle(Vis.Flat(cam.transform.forward), d, Vector3.up);
                where = Mathf.Abs(a) < 40f ? "ahead" : Mathf.Abs(a) > 135f ? "behind you" : a > 0 ? "to your right" : "to your left";
            }
            string far = dist > 12f ? ", far off" : "";
            Showing.Add(new KeyValuePair<string, float>("[" + w + (where.Length > 0 ? ", " + where : "") + far + "]", Time.unscaledTime + 3.5f));
            while (Showing.Count > 3) Showing.RemoveAt(0);
        }

        public static void Tidy() { Showing.RemoveAll(kv => kv.Value < Time.unscaledTime); }
    }
}
