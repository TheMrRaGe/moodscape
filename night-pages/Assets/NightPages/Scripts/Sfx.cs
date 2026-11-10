using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Every sound in the prototype is generated here in code, so the project needs no audio files.
    /// Replace these with real recordings later: keep the names and nothing else has to change.
    /// </summary>
    public static class Sfx
    {
        const int SR = 22050;
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static System.Random rng = new System.Random(7);
        static AudioSource ui;

        static float Noise() { return (float)(rng.NextDouble() * 2.0 - 1.0); }
        static float Sin(float hz, float t) { return Mathf.Sin(2f * Mathf.PI * hz * t); }

        static void Make(string name, float secs, System.Func<float, float> f)
        {
            int n = Mathf.Max(1, (int)(SR * secs));
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)SR), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, SR, false);
            clip.SetData(data, 0);
            clips[name] = clip;
        }

        static float Thump(float t, float t0, float hz, float decay)
        {
            float d = t - t0;
            return d < 0f ? 0f : Mathf.Exp(-d * decay) * Sin(hz, d);
        }

        /// <summary>Forget cached clips (they don't survive a restart of Play mode when domain reload is off).</summary>
        public static void ResetStatics() { clips.Clear(); recorded = null; ui = null; }

        // Real recordings in Resources/NPSounds (see THIRD_PARTY.md). "step_0".."step_9" are takes of "step";
        // a name ending in "_loop" is one clip. A recording always wins over the generated sound of the same name.
        static Dictionary<string, List<AudioClip>> recorded;

        static void LoadRecorded()
        {
            recorded = new Dictionary<string, List<AudioClip>>();
            foreach (var c in Resources.LoadAll<AudioClip>("NPSounds"))
            {
                string n = c.name;
                int us = n.LastIndexOf('_');
                string key = n;
                int dummy;
                if (us > 0 && int.TryParse(n.Substring(us + 1), out dummy)) key = n.Substring(0, us);
                else if (n.EndsWith("_loop")) key = n.Substring(0, n.Length - 5);
                List<AudioClip> list;
                if (!recorded.TryGetValue(key, out list)) recorded[key] = list = new List<AudioClip>();
                list.Add(c);
            }
        }

        public static bool HasRecording(string name)
        {
            if (recorded == null) LoadRecorded();
            return recorded.ContainsKey(name);
        }

        public static void Init()
        {
            if (clips.Count > 0) return;
            rng = new System.Random(7);
            float lp = 0f;

            Make("knock", 1.0f, t => 0.8f * (Thump(t, 0f, 95f, 30f) + Thump(t, 0.27f, 90f, 30f) + Thump(t, 0.54f, 85f, 26f)));
            Make("slam", 0.9f, t => { lp += (Noise() - lp) * 0.2f; return Thump(t, 0f, 52f, 9f) + lp * Mathf.Exp(-t * 16f) * 0.9f; });
            Make("step", 0.14f, t => { lp += (Noise() - lp) * 0.12f; return lp * Mathf.Exp(-t * 34f) * 1.6f + Thump(t, 0f, 70f, 40f) * 0.5f; });
            // Steps on grass and leaf litter: a soft crunch, two layers of filtered noise.
            float g1 = 0f, g2 = 0f;
            Make("stepgrass", 0.3f, t =>
            {
                float n = Noise();
                g1 += (n - g1) * 0.35f;
                g2 += (n - g2) * 0.05f;
                float crunch = (n - g1) * (Mathf.PerlinNoise(t * 180f, 3f) > 0.55f ? 1f : 0.25f);
                return (crunch * 0.6f + g2 * 1.6f) * Mathf.Exp(-t * 11f) * Mathf.Min(1f, t * 60f);
            });
            Make("click", 0.05f, t => Noise() * Mathf.Exp(-t * 130f) * 0.7f);
            Make("creak", 0.9f, t => Sin(Mathf.Lerp(190f, 140f, t / 0.9f), t) * (0.5f + 0.5f * Sin(23f, t)) * Mathf.Sin(Mathf.PI * t / 0.9f) * 0.35f);
            Make("sting", 1.8f, t =>
            {
                float f = Mathf.Lerp(280f, 620f, t / 1.8f);
                float env = Mathf.Min(1f, t * 14f) * Mathf.Exp(-t * 1.4f);
                return env * 0.33f * (Sin(f, t) + Sin(f * 1.059f, t) + Sin(f * 1.498f, t) * 0.7f);
            });
            Make("drone", 8f, t => 0.3f * Sin(49f, t) + 0.25f * Sin(49.75f, t) + 0.1f * Sin(98.25f, t));
            Make("heart", 1.0f, t => Thump(t, 0.02f, 58f, 22f) + 0.8f * Thump(t, 0.3f, 50f, 24f));
            lp = 0f;
            Make("breath", 4f, t =>
            {
                lp += (Noise() - lp) * 0.07f;
                return lp * Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * t / 2f)), 1.6f) * 1.5f;
            });
            float gate = 0f, gateTarget = 0f;
            int lastStep = -1;
            lp = 0f;
            Make("whisper", 3f, t =>
            {
                int step = (int)(t * 7f);
                if (step != lastStep) { lastStep = step; gateTarget = rng.NextDouble() < 0.6 ? (float)rng.NextDouble() : 0f; }
                gate += (gateTarget - gate) * 0.002f;
                float n = Noise();
                float hp = n - lp;
                lp += (n - lp) * 0.3f;
                float fade = Mathf.Min(1f, Mathf.Min(t, 3f - t) * 6f);
                return hp * gate * fade * 0.6f;
            });
            var hits = new List<float>();
            for (float at = 0.1f; at < 2.9f; at += 0.09f + (float)rng.NextDouble() * 0.3f) hits.Add(at);
            Make("typing", 3f, t =>
            {
                float s = 0f;
                for (int i = 0; i < hits.Count; i++)
                {
                    float d = t - hits[i];
                    if (d >= 0f && d < 0.05f) s += Noise() * Mathf.Exp(-d * 120f);
                }
                return s * 0.6f;
            });
            // Small claws in a wall cavity: bursts of filtered noise, irregular.
            var scr = new List<float>();
            for (float at = 0.05f; at < 2.9f; at += 0.12f + (float)rng.NextDouble() * 0.5f) scr.Add(at);
            lp = 0f;
            Make("scratch", 3f, t =>
            {
                float s = 0f;
                for (int i = 0; i < scr.Count; i++)
                {
                    float d = t - scr[i];
                    if (d >= 0f && d < 0.11f) s += Mathf.Sin(Mathf.PI * d / 0.11f);
                }
                float n = Noise();
                lp += (n - lp) * 0.45f;
                return (n - lp) * s * 0.55f;
            });
            // A heart monitor: one clean beep, then quiet. Loops at about 70 a minute.
            Make("beep", 0.86f, t => t < 0.11f ? Sin(980f, t) * 0.35f * Mathf.Min(1f, (0.11f - t) * 40f) : 0f);
            // A single drop of water landing in water.
            Make("drip", 0.35f, t => Sin(Mathf.Lerp(1400f, 650f, Mathf.Min(1f, t * 9f)), t) * Mathf.Exp(-t * 22f) * 0.5f);
            // The cue: a music box, five notes falling, a little out of tune and slowing as the spring runs down.
            float[] mb = { 1047f, 988f, 831f, 784f, 659f };
            Make("musicbox", 2.6f, t =>
            {
                float s = 0f, at = 0f;
                for (int i = 0; i < mb.Length; i++)
                {
                    float d = t - at;
                    if (d >= 0f)
                    {
                        float hz = mb[i] * (1f - 0.012f * i) * (1f + 0.003f * Sin(5f, t));
                        s += Mathf.Exp(-d * 4.5f) * (Sin(hz, d) + 0.45f * Sin(hz * 2.76f, d) * Mathf.Exp(-d * 9f) + 0.2f * Sin(hz * 5.4f, d) * Mathf.Exp(-d * 14f));
                    }
                    at += 0.3f + 0.05f * i;
                }
                return s * 0.28f;
            });
            // An old telephone bell: two bursts of a struck bell, the clapper at 20 Hz.
            Make("ring", 3f, t =>
            {
                bool on = t < 0.4f || (t > 0.6f && t < 1.0f);
                if (!on) return 0f;
                float clap = 0.5f + 0.5f * Mathf.Sign(Sin(20f, t));
                return clap * 0.35f * (Sin(1250f, t) + 0.6f * Sin(1810f, t) + 0.3f * Sin(2470f, t)) * Mathf.Exp(-((t % 0.05f) * 30f));
            });
            // A television tuned to nothing: hiss with the 60 Hz buzz in it.
            lp = 0f;
            Make("static", 3f, t => { float n = Noise(); lp += (n - lp) * 0.5f; return (n - lp * 0.6f) * 0.25f + Sin(60f, t) * 0.03f + Sin(15734f, t) * 0.01f; });
            // An 8 mm projector: the motor, and the claw pulling each frame through at 18 per second.
            Make("projector", 2f, t => 0.12f * Sin(110f, t) + 0.05f * Sin(220f, t) + Noise() * Mathf.Exp(-((t * 18f) % 1f) * 30f) * 0.35f);
            // The fridge: a low hum that runs and stops.
            Make("fridge", 6f, t => 0.06f * Sin(60f, t) + 0.035f * Sin(120f, t) + 0.01f * Noise());
            // Something giving inside a body: a dry crack with a knuckle-pop tail.
            lp = 0f;
            Make("crack", 0.35f, t => { lp += (Noise() - lp) * 0.6f; return lp * Mathf.Exp(-t * 60f) * 1.4f + Thump(t, 0.01f, 140f, 50f) * 0.5f + (t > 0.08f ? Noise() * Mathf.Exp(-(t - 0.08f) * 90f) * 0.5f : 0f); });
            // A sharp breath in, the kind you take waking up.
            lp = 0f;
            Make("gasp", 0.8f, t => { lp += (Noise() - lp) * 0.25f; return lp * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t / 0.55f)) * (t < 0.55f ? 1.6f : 0f); });
            // Something heavy settling: the mattress taking weight beside you.
            Make("sink", 1.4f, t => Sin(Mathf.Lerp(90f, 60f, t / 1.4f), t) * (0.5f + 0.5f * Sin(13f, t)) * Mathf.Sin(Mathf.PI * t / 1.4f) * 0.4f);
            float[] notes = { 392f, 330f, 392f, 330f, 349f, 294f, 330f, 262f };
            Make("lullaby", 12f, t =>
            {
                int i = Mathf.Min(notes.Length - 1, (int)(t / 1.5f));
                float d = t - i * 1.5f;
                float env = Mathf.Min(1f, d * 8f) * Mathf.Exp(-d * 1.3f);
                float hz = notes[i] * 0.982f * (1f + 0.004f * Sin(4.5f, t));
                return env * 0.4f * (Sin(hz, t) + 0.3f * Sin(hz * 2f, t));
            });
        }

        public static AudioClip Clip(string name)
        {
            if (recorded == null) LoadRecorded();
            List<AudioClip> takes;
            if (recorded.TryGetValue(name, out takes) && takes.Count > 0) return takes[Random.Range(0, takes.Count)];
            Init();
            AudioClip c;
            return clips.TryGetValue(name, out c) ? c : null;
        }

        /// <summary>Play a sound once at a place in the world.</summary>
        public static void At(string name, Vector3 pos, float vol = 1f, float pitch = 1f)
        {
            var c = Clip(name);
            if (c == null) return;
            var g = new GameObject("Sfx " + name);
            g.transform.position = pos;
            var a = g.AddComponent<AudioSource>();
            Spatial(a);
            a.clip = c;
            a.volume = vol;
            a.pitch = pitch;
            a.Play();
            Object.Destroy(g, c.length / Mathf.Max(0.1f, pitch) + 0.3f);
        }

        /// <summary>Play a sound once with no position (interface clicks, stings).</summary>
        public static void Ui(string name, float vol = 1f, float pitch = 1f)
        {
            var c = Clip(name);
            if (c == null) return;
            if (ui == null)
            {
                var g = new GameObject("Sfx UI");
                Object.DontDestroyOnLoad(g);
                ui = g.AddComponent<AudioSource>();
                ui.spatialBlend = 0f;
            }
            ui.pitch = pitch;
            ui.PlayOneShot(c, vol);
        }

        /// <summary>A looping sound. Positioned in the world when spatial, otherwise in the player's head.</summary>
        public static AudioSource Loop(string name, Transform parent, float vol, bool spatial, float pitch = 1f)
        {
            var g = new GameObject("Loop " + name);
            if (parent != null) g.transform.SetParent(parent, false);
            var a = g.AddComponent<AudioSource>();
            if (spatial) Spatial(a); else a.spatialBlend = 0f;
            a.clip = Clip(name);
            a.loop = true;
            a.volume = vol;
            a.pitch = pitch;
            a.Play();
            return a;
        }

        static void Spatial(AudioSource a)
        {
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 1.5f;
            a.maxDistance = 32f;
            a.dopplerLevel = 0f;
        }
    }
}
