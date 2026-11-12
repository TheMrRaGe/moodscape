using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The instinct layer: the fruit fly's mushroom body (the same glomerulus-to-Kenyon-cell wiring the fear engine
    /// uses, from the MaleCNS connectome) as the dream monster's body. What it senses becomes a smell-like code
    /// (you in view, you looking at it, how far, dark or lit, you running, you hidden, its hunger and its nerve),
    /// that code lights a sparse set of Kenyon cells, and five output neurons read them, as the fly's own output
    /// neurons do, each voting for a way to move: press in, freeze where it stands, creep, pull back out of sight,
    /// or twitch. The vote is soft (sampled), so it never moves the same way twice.
    ///
    /// It learns like a fly too: dopamine. A few seconds after each choice, how much your reaction rose is the
    /// reward, and the Kenyon cells that were firing strengthen their pull toward what it just did. So it learns,
    /// in each kind of moment, which way of moving frightens you. Drives tilt it: hunger climbs while you're calm
    /// and it hasn't had a reaction out of you; nerve drops when you stare it down. Kept between runs unless you've
    /// asked not to be remembered.
    /// </summary>
    public class FlyBody
    {
        public enum Act { Press, Freeze, Creep, Withdraw, Twitch }
        public static readonly string[] ActNames = { "presses in", "freezes", "creeps", "pulls back out of sight", "twitches" };
        const int Acts = 5;
        static readonly float[] Prior = { 0.25f, 0.05f, 0.1f, -0.15f, -0.1f };
        const float Eta = 0.25f, Temp = 0.25f;

        readonly FearCore.Instinct instinct;
        float[][] w;
        int kcs;
        public float hunger = 0.3f, nerve = 0.7f;
        float baseline;
        // The choice being judged: what it did, the cells that were firing, the reaction before.
        int lastAct = -1;
        int[] lastCode;
        float judgeAt, before;
        float stareFor;
        public int choices;
        readonly int[] counts = new int[Acts];

        static string PathOf { get { return Path.Combine(Application.persistentDataPath, "nightpages-flybody.txt"); } }

        public FlyBody(FearCore.Instinct inst)
        {
            instinct = inst;
            kcs = 0;
            w = new float[Acts][];
        }

        void Ensure(int[] code)
        {
            int max = 0;
            foreach (var c in code) if (c + 1 > max) max = c + 1;
            if (max <= kcs) return;
            int n = Mathf.Max(max, kcs * 2, 2048);
            for (int a = 0; a < Acts; a++)
            {
                var nw = new float[n];
                if (w[a] != null) System.Array.Copy(w[a], nw, w[a].Length);
                w[a] = nw;
            }
            kcs = n;
        }

        /// <summary>What it senses, as tags the wiring turns into a Kenyon-cell code.</summary>
        static string[] Sense(bool sees, bool watched, float dist, bool dark, bool running, bool hidden, float hunger, float nerve)
        {
            return new[]
            {
                sees ? "prey seen" : "prey unseen",
                watched ? "watched" : "unwatched",
                dist < 3f ? "near" : dist < 8f ? "mid" : "far",
                dark ? "dark" : "lit",
                running ? "prey runs" : "prey still",
                hidden ? "prey hidden" : "prey open",
                hunger > 0.6f ? "hungry" : "fed",
                nerve < 0.4f ? "cowed" : "bold",
            };
        }

        /// <summary>Choose how to move now. Logged by the caller when it changes.</summary>
        public Act Choose(bool sees, bool watched, float dist, bool dark, bool running, bool hidden, float reaction, float now, System.Action<string> log)
        {
            Judge(reaction, now, log);
            var code = instinct.Code(Sense(sees, watched, dist, dark, running, hidden, hunger, nerve));
            Ensure(code);
            var v = new float[Acts];
            float max = float.MinValue;
            for (int a = 0; a < Acts; a++)
            {
                float s = Prior[a];
                foreach (var c in code) s += w[a][c];
                if (code.Length > 0) s = Prior[a] + (s - Prior[a]) / code.Length;      // the mean pull of the firing cells
                if (a == (int)Act.Press) s += (hunger - 0.5f) * 0.6f + (nerve - 0.5f) * 0.4f;
                if (a == (int)Act.Withdraw) s += (0.5f - nerve) * 0.8f;
                if (a == (int)Act.Freeze && watched) s += 0.15f;
                v[a] = s;
                if (s > max) max = s;
            }
            float sum = 0f;
            for (int a = 0; a < Acts; a++) { v[a] = Mathf.Exp((v[a] - max) / Temp); sum += v[a]; }
            float r = Random.value * sum;
            int pick = 0;
            for (; pick < Acts - 1; pick++) { r -= v[pick]; if (r <= 0f) break; }
            lastAct = pick;
            lastCode = code;
            judgeAt = now + 3f;
            before = reaction;
            choices++;
            counts[pick]++;
            return (Act)pick;
        }

        /// <summary>Judge the last choice now, from this reaction (tests).</summary>
        public void Observe(float reaction, float now) { Judge(reaction, now, null); }

        // Dopamine: was it worth it? The rise in your reaction, against what it usually gets.
        void Judge(float reaction, float now, System.Action<string> log)
        {
            if (lastAct < 0 || now < judgeAt || lastCode == null) return;
            float rise = reaction - before;
            float rpe = rise - baseline;
            baseline = Mathf.Lerp(baseline, rise, 0.1f);
            foreach (var c in lastCode) w[lastAct][c] += Eta * rpe;
            if (rise > 0.15f) hunger = Mathf.Max(0f, hunger - 0.35f);
            if (log != null && Mathf.Abs(rpe) > 0.12f)
                log("fly body: when it " + ActNames[lastAct] + ", your reaction " + (rise >= 0 ? "rose " : "fell ") + Mathf.Abs(rise).ToString("0.00") +
                    (rpe > 0 ? "; it will do that more in moments like this" : "; it will do that less in moments like this"));
            lastAct = -1;
        }

        /// <summary>Drives, every frame: hunger climbs while you're calm; nerve falls while you stare it down close.</summary>
        public void Drives(float dt, float reaction, bool watched, float dist)
        {
            if (reaction < 0.2f) hunger = Mathf.Min(1f, hunger + dt * 0.012f);
            if (watched && dist < 6f) { stareFor += dt; nerve = Mathf.Max(0f, nerve - dt * 0.05f); }
            else { stareFor = 0f; nerve = Mathf.Min(1f, nerve + dt * 0.02f); }
        }

        public string Describe()
        {
            return "hunger " + hunger.ToString("0.00") + ", nerve " + nerve.ToString("0.00") + ", " + choices + " choices (" +
                   ActNames[0] + " " + counts[0] + ", " + ActNames[1] + " " + counts[1] + ", " + ActNames[2] + " " + counts[2] + ", " + ActNames[3] + " " + counts[3] + ", " + ActNames[4] + " " + counts[4] + ")";
        }

        public void Save()
        {
            if (kcs == 0) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append(kcs).Append('\n');
                for (int a = 0; a < Acts; a++)
                    for (int i = 0; i < kcs; i++)
                        if (Mathf.Abs(w[a][i]) > 1e-4f) sb.Append(a).Append(' ').Append(i).Append(' ').Append(w[a][i].ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                File.WriteAllText(PathOf, sb.ToString());
            }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't save the fly body: " + e.Message); }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(PathOf)) return;
                var lines = File.ReadAllLines(PathOf);
                if (lines.Length == 0) return;
                int n;
                if (!int.TryParse(lines[0], out n) || n <= 0 || n > 200000) return;
                kcs = n;
                for (int a = 0; a < Acts; a++) w[a] = new float[n];
                for (int i = 1; i < lines.Length; i++)
                {
                    var p = lines[i].Split(' ');
                    int a, k; float v;
                    if (p.Length == 3 && int.TryParse(p[0], out a) && int.TryParse(p[1], out k) && float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && a < Acts && k < n)
                        w[a][k] = v;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't read the fly body: " + e.Message); }
        }
    }
}
