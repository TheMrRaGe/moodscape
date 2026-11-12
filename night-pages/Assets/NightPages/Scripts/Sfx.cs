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

            // A hinge, stick-slip: little catches in a train, each ringing the wood and the iron. Loops; Door plays
            // it at whatever speed the door moves (slower is lower and longer).
            var catches = new List<float>();
            for (float at = 0f; at < 2f; )
            {
                catches.Add(at);
                at += 1f / (55f + 35f * Mathf.Sin(at * 2.1f) + 25f * Mathf.PerlinNoise(at * 5f, 1.7f)) * (0.8f + (float)rng.NextDouble() * 0.4f);
            }
            int ci = 0;
            Make("doorcreak", 2f, t =>
            {
                while (ci + 1 < catches.Count && catches[ci + 1] <= t) ci++;
                float s = 0f;
                for (int k = Mathf.Max(0, ci - 3); k <= ci; k++)
                {
                    float d = t - catches[k];
                    if (d < 0f || d > 0.04f) continue;
                    s += Mathf.Exp(-d * 140f) * (Sin(720f, d) * 0.6f + Sin(1650f, d) * 0.35f + Sin(380f, d) * 0.4f);
                }
                return s * 0.55f * (0.75f + 0.25f * Mathf.PerlinNoise(t * 3f, 4.2f));
            });
            // A slipper dragged, then the stick: shuffle-and-thud, a dead man's walk (one stride).
            lp = 0f;
            Make("shuffle", 1.1f, t =>
            {
                lp += (Noise() - lp) * 0.08f;
                float drag = t < 0.45f ? Mathf.Sin(Mathf.PI * t / 0.45f) * lp * 1.4f : 0f;
                return drag + Thump(t, 0.62f, 75f, 30f) * 0.7f + Thump(t, 0.64f, 140f, 60f) * 0.25f;
            });
            // Crockery: a stack of plates giving way, shards after.
            var shards = new List<float>();
            for (float at = 0.03f; at < 1.4f; at += 0.015f + (float)rng.NextDouble() * 0.09f * (1f + at * 3f)) shards.Add(at);
            Make("smash", 1.6f, t =>
            {
                float s = Thump(t, 0f, 180f, 18f) * 0.5f;
                foreach (var at in shards)
                {
                    float d = t - at;
                    if (d < 0f || d > 0.05f) continue;
                    float hz = 2200f + (at * 7919f % 1f) * 2600f;
                    s += Mathf.Exp(-d * 90f) * Sin(hz, d) * Mathf.Exp(-at * 2f) * 0.6f;
                }
                return s + Noise() * Mathf.Exp(-t * 22f) * 0.5f;
            });
            // Something small rolling across a worktop, then the soft wet crack of it landing.
            Make("roll", 1.8f, t =>
            {
                float r = t < 1.2f ? (0.2f + 0.2f * Sin(9f, t)) * Mathf.PerlinNoise(t * 60f, 2f) * Noise() : 0f;
                return r * 0.5f + Thump(t, 1.32f, 120f, 40f) * 0.6f + Noise() * (t > 1.32f ? Mathf.Exp(-(t - 1.32f) * 45f) : 0f) * 0.35f;
            });
            // A single knuckle on wood.
            Make("tap", 0.25f, t => Thump(t, 0f, 210f, 45f) * 0.8f + Noise() * Mathf.Exp(-t * 200f) * 0.3f);
            // Something heavy dragged over boards.
            lp = 0f;
            Make("drag", 1.6f, t =>
            {
                lp += (Noise() - lp) * 0.04f;
                float judder = 0.6f + 0.4f * Mathf.Sign(Sin(17f, t));
                return lp * judder * Mathf.Sin(Mathf.PI * t / 1.6f) * 2.2f;
            });

            // ---- the house's daily life (chores, the clock, the jobs)

            // A tap running into a sink: two bands of noise with a slow wobble. Loops.
            float w1 = 0f, w2 = 0f;
            Make("water", 3f, t =>
            {
                float n = Noise();
                w1 += (n - w1) * 0.35f; w2 += (w1 - w2) * 0.5f;
                return (w1 - w2 * 0.7f) * (0.55f + 0.2f * Mathf.PerlinNoise(t * 7f, 0.3f)) * 0.5f;
            });
            // A sponge or a brush going over something: a short scratchy swish.
            lp = 0f;
            Make("scrub", 0.35f, t => { float n = Noise(); lp += (n - lp) * 0.45f; return (n - lp) * Mathf.Sin(Mathf.PI * t / 0.35f) * 0.45f; });
            // Pouring: a glug every so often over a thin stream. Loops.
            lp = 0f;
            Make("pour", 2f, t =>
            {
                lp += (Noise() - lp) * 0.2f;
                float glug = Mathf.Exp(-((t * 5.3f) % 1f) * 9f) * Sin(240f + 60f * Mathf.Sin(t * 3f), t);
                return lp * 0.25f + glug * 0.18f;
            });
            // The hall clock striking: a struck rod, inharmonic partials ringing down.
            Make("chime", 3.5f, t =>
            {
                float e = Mathf.Exp(-t * 1.3f);
                return e * 0.3f * (Sin(196f, t) + 0.7f * Sin(196f * 2.76f, t) * Mathf.Exp(-t * 1.5f) + 0.45f * Sin(196f * 5.4f, t) * Mathf.Exp(-t * 3f) + 0.3f * Sin(392.6f, t)) + Noise() * Mathf.Exp(-t * 80f) * 0.2f;
            });
            // A kettle coming to the boil: a rumble that grows bubbles. Loops (louder as it heats).
            lp = 0f;
            Make("boil", 3f, t =>
            {
                lp += (Noise() - lp) * 0.08f;
                float pop = Mathf.PerlinNoise(t * 40f, 1.1f) > 0.72f ? Noise() * 0.5f : 0f;
                return lp * 0.6f + pop * 0.3f;
            });
            // A match struck: a rasp, then the flare.
            lp = 0f;
            Make("strike", 0.9f, t =>
            {
                lp += (Noise() - lp) * 0.5f;
                float rasp = t < 0.12f ? (Noise() - lp) * 0.8f : 0f;
                float flare = t > 0.1f ? lp * Mathf.Exp(-(t - 0.1f) * 5f) * 0.6f : 0f;
                return rasp + flare;
            });
            // Fire catching: a soft whoomph.
            lp = 0f;
            Make("whoosh", 1.2f, t => { lp += (Noise() - lp) * 0.06f; return lp * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t / 1.2f)) * 2.2f; });
            // A voice on an old tape: words you can almost make out (a buzz shaped like syllables), under hiss. Loops.
            float ph = 0f; lp = 0f;
            Make("murmur", 6f, t =>
            {
                float syll = Mathf.PerlinNoise(t * 4.5f, 2.2f);
                float pitch = 125f + 35f * Mathf.PerlinNoise(t * 0.9f, 5.1f);
                ph += pitch / SR;
                float buzz = (ph % 1f) * 2f - 1f;
                lp += (buzz - lp) * 0.12f;                       // a muffled throat
                float open = syll > 0.45f ? (syll - 0.45f) * 2.4f : 0f;
                float pause = Mathf.PerlinNoise(t * 0.7f, 8.8f) > 0.3f ? 1f : 0.1f;
                return lp * open * pause * 0.7f + Noise() * 0.035f;
            });
            // Tape hiss on its own. Loops.
            Make("hiss", 3f, t => Noise() * 0.06f + Sin(50f, t) * 0.012f);
            // Paper: a sheet taken, a rustle.
            lp = 0f;
            Make("paper", 0.5f, t => { float n = Noise(); lp += (n - lp) * 0.7f; return (n - lp) * Mathf.Exp(-t * 7f) * 0.6f * (0.6f + 0.4f * Mathf.PerlinNoise(t * 90f, 3f)); });
            // A lid or a bin: a hollow tin clunk.
            Make("clunk", 0.6f, t => Thump(t, 0f, 160f, 14f) * 0.5f + Thump(t, 0f, 470f, 25f) * 0.3f + Noise() * Mathf.Exp(-t * 120f) * 0.3f);
            // A ratchet click (winding the clock).
            Make("ratchet", 0.08f, t => Noise() * Mathf.Exp(-t * 160f) * 0.6f + Thump(t, 0f, 1900f, 120f) * 0.4f);

            // ---- things hitting things (pitch is varied per hit, so one clip of each will do)

            // Wood on wood or on the floor: a dull knock with a little body.
            Make("bonk", 0.35f, t => Thump(t, 0f, 140f, 22f) * 0.6f + Thump(t, 0f, 410f, 45f) * 0.35f + Noise() * Mathf.Exp(-t * 160f) * 0.35f);
            // Crockery or glass set down hard: a short bright ring.
            Make("clink", 0.6f, t => Mathf.Exp(-t * 14f) * (Sin(2350f, t) * 0.35f + Sin(3710f, t) * 0.22f * Mathf.Exp(-t * 10f) + Sin(5480f, t) * 0.12f * Mathf.Exp(-t * 20f)) + Noise() * Mathf.Exp(-t * 300f) * 0.3f);
            // Metal: a tin or a pan, inharmonic and ringing on.
            Make("clang", 1.2f, t => Mathf.Exp(-t * 4.5f) * (Sin(520f, t) * 0.3f + Sin(1270f, t) * 0.22f + Sin(2010f, t) * 0.15f * Mathf.Exp(-t * 6f) + Sin(3330f, t) * 0.08f * Mathf.Exp(-t * 9f)) + Noise() * Mathf.Exp(-t * 200f) * 0.3f);
            // Something soft landing: paper, cloth, a loaf, a slipper.
            lp = 0f;
            Make("flop", 0.3f, t => { lp += (Noise() - lp) * 0.15f; return lp * Mathf.Exp(-t * 18f) * 2.2f + Thump(t, 0f, 90f, 30f) * 0.25f; });
            // One thing breaking (a cup, a glass): a crack and a scatter of small pieces.
            var bits = new List<float>();
            for (float at = 0.02f; at < 0.7f; at += 0.012f + (float)rng.NextDouble() * 0.06f * (1f + at * 4f)) bits.Add(at);
            Make("shatter", 0.9f, t =>
            {
                float s = Thump(t, 0f, 260f, 30f) * 0.35f + Noise() * Mathf.Exp(-t * 60f) * 0.5f;
                foreach (var at in bits)
                {
                    float d = t - at;
                    if (d < 0f || d > 0.04f) continue;
                    s += Mathf.Exp(-d * 110f) * Sin(2600f + (at * 6151f % 1f) * 3200f, d) * Mathf.Exp(-at * 3f) * 0.45f;
                }
                return s;
            });
            // Liquid hitting the floor: a slap and a spatter.
            lp = 0f;
            Make("splash", 0.8f, t => { lp += (Noise() - lp) * 0.3f; return lp * Mathf.Exp(-t * 7f) * 1.1f * (0.6f + 0.4f * Mathf.PerlinNoise(t * 70f, 0.4f)) + Thump(t, 0f, 110f, 25f) * 0.2f; });
            // A drop of water from a tap into a sink, or onto a puddle.
            Make("plink", 0.25f, t => Mathf.Exp(-t * 30f) * Sin(900f + 1400f * Mathf.Exp(-t * 40f), t) * 0.4f);
            // A wet foot on boards.
            lp = 0f;
            Make("squelch", 0.25f, t => { lp += (Noise() - lp) * 0.25f; return lp * Mathf.Sin(Mathf.PI * t / 0.25f) * 0.9f; });
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
            a.volume = vol * Settings.S.effects * (Voices.Contains(name) ? Settings.S.voices : 1f);
            a.pitch = pitch;
            a.Play();
            Object.Destroy(g, c.length / Mathf.Max(0.1f, pitch) + 0.3f);
            Captions.Heard(name, pos, vol);
        }

        static readonly HashSet<string> Voices = new HashSet<string> { "whisper", "breath", "gasp", "murmur", "lullaby" };

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
            a.volume = vol * (name == "drone" || name == "heart" ? 1f : Settings.S.ambience * (Voices.Contains(name) ? Settings.S.voices : 1f));
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
