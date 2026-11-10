using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FearCore
{
    /// <summary>
    /// Which glomerulus feeds which Kenyon cell, and how strongly. Night Pages loads the real wiring from the
    /// male fruit fly connectome (MaleCNS v1.0, Janelia FlyEM + Google, CC-BY 4.0): 53 glomeruli, 4,064 Kenyon
    /// cells, about 7 glomeruli per cell. Any game without that file gets <see cref="Random"/>, the FlyHash
    /// control: the same fan-in, random glomeruli. On the bench they learn equally well, which says the gain
    /// comes from the fly's architecture (a wide, sparse expansion read out by prediction error), not from the
    /// particular wiring.
    /// </summary>
    public class Wiring
    {
        public int gloms, kcs;
        public int[] glom, kc;      // one entry per synapse
        public float[] weight;
        public string source = "random (FlyHash control)";

        public static Wiring Random(int gloms = 53, int kcs = 4064, int fanIn = 7, int seed = 1)
        {
            var rng = new System.Random(seed);
            var w = new Wiring { gloms = gloms, kcs = kcs };
            var g = new List<int>(); var k = new List<int>(); var s = new List<float>();
            for (int c = 0; c < kcs; c++)
            {
                var used = new HashSet<int>();
                for (int i = 0; i < fanIn; i++)
                {
                    int gi;
                    do gi = rng.Next(gloms); while (!used.Add(gi));
                    g.Add(gi); k.Add(c); s.Add((float)(1.0 + rng.NextDouble() * 30.0));
                }
            }
            w.glom = g.ToArray(); w.kc = k.ToArray(); w.weight = s.ToArray();
            return w;
        }
    }

    /// <summary>
    /// The instinct: a mushroom body. A fright is described by tags ("uncanny", "sound", "behind"); each tag
    /// lights a few glomeruli, the glomeruli drive the Kenyon cells, and only the top 5% of cells fire, so similar
    /// frights share cells and different ones don't. A readout per domain (one compartment each, so learning
    /// what works in dreams never overwrites what works in the house) is moved by the prediction error: what
    /// happened minus what it expected (Bennett, Philippides and Nowotny 2021). It never sees a fright's name,
    /// only what it is made of, which is why it has an opinion about frights it has never tried, and about a
    /// different game's frights built from the same kinds of thing.
    /// </summary>
    public class Instinct
    {
        public const float Sparse = 0.05f;      // share of Kenyon cells that fire
        public const int GlomsPerTag = 4;
        public float Eta = 0.6f;                // learning rate per unit prediction error (tuned on the bench)

        readonly Wiring wiring;
        readonly int[][] outKc;                 // per glomerulus: its Kenyon cells
        readonly float[][] outW;
        readonly int k;
        readonly Dictionary<string, float[]> comps = new Dictionary<string, float[]>();
        readonly Dictionary<string, int[]> codes = new Dictionary<string, int[]>();
        readonly float[] drive;
        readonly int[] order;

        public string Source { get { return wiring.source; } }
        public IEnumerable<string> Domains { get { return comps.Keys; } }

        public Instinct(Wiring w)
        {
            wiring = w ?? Wiring.Random();
            k = Math.Max(1, (int)(Sparse * wiring.kcs));
            var lk = new List<int>[wiring.gloms]; var lw = new List<float>[wiring.gloms];
            for (int g = 0; g < wiring.gloms; g++) { lk[g] = new List<int>(); lw[g] = new List<float>(); }
            for (int i = 0; i < wiring.glom.Length; i++) { lk[wiring.glom[i]].Add(wiring.kc[i]); lw[wiring.glom[i]].Add(wiring.weight[i]); }
            outKc = new int[wiring.gloms][]; outW = new float[wiring.gloms][];
            for (int g = 0; g < wiring.gloms; g++) { outKc[g] = lk[g].ToArray(); outW[g] = lw[g].ToArray(); }
            drive = new float[wiring.kcs];
            order = new int[wiring.kcs];
        }

        static uint Fnv(string s)
        {
            uint h = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(s)) h = (h ^ b) * 16777619;
            return h;
        }

        /// <summary>The Kenyon cells a set of tags makes fire.</summary>
        public int[] Code(string[] tags)
        {
            string key = string.Join("|", tags);
            int[] c;
            if (codes.TryGetValue(key, out c)) return c;
            var rate = new float[wiring.gloms];
            foreach (var t in tags)
            {
                uint h = Fnv(t);
                for (uint i = 0; i < GlomsPerTag; i++)
                {
                    h = (h ^ (i + 1)) * 16777619;
                    rate[h % (uint)wiring.gloms] += 1f;
                }
            }
            Array.Clear(drive, 0, drive.Length);
            for (int g = 0; g < wiring.gloms; g++)
            {
                if (rate[g] == 0f) continue;
                var ks = outKc[g]; var ws = outW[g];
                for (int i = 0; i < ks.Length; i++) drive[ks[i]] += rate[g] * ws[i];
            }
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => drive[b].CompareTo(drive[a]));
            var list = new List<int>(k);
            for (int i = 0; i < k && drive[order[i]] > 0f; i++) list.Add(order[i]);
            c = list.ToArray();
            codes[key] = c;
            return c;
        }

        float[] Comp(string domain)
        {
            float[] w;
            if (!comps.TryGetValue(domain, out w)) { w = new float[wiring.kcs]; comps[domain] = w; }
            return w;
        }

        /// <summary>What the instinct expects this fright to do to this player, about -1..1.</summary>
        public float Value(string domain, string[] tags)
        {
            var c = Code(tags);
            if (c.Length == 0) return 0f;
            var w = Comp(domain);
            float s = 0f;
            foreach (int i in c) s += w[i];
            return s / c.Length;
        }

        /// <summary>Learn from what happened; returns the prediction error.</summary>
        public float Learn(string domain, string[] tags, float y)
        {
            float v = Value(domain, tags);
            float d = Math.Max(-1f, Math.Min(1f, y - v));
            var w = Comp(domain);
            foreach (int i in Code(tags)) w[i] += Eta * d;
            return d;
        }

        internal void Save(StringBuilder sb)
        {
            foreach (var kv in comps)
            {
                var bytes = new byte[kv.Value.Length * 4];
                Buffer.BlockCopy(kv.Value, 0, bytes, 0, bytes.Length);
                sb.Append("comp\t").Append(kv.Key).Append('\t').Append(Convert.ToBase64String(bytes)).Append('\n');
            }
        }

        internal void LoadComp(string domain, string b64)
        {
            var bytes = Convert.FromBase64String(b64);
            if (bytes.Length != wiring.kcs * 4) return;      // different wiring: start this compartment fresh
            var w = Comp(domain);
            Buffer.BlockCopy(bytes, 0, w, 0, bytes.Length);
        }
    }

    /// <summary>
    /// What the storyteller knows about this player. Two memories, the way an animal has them: the instinct
    /// (above) gives a first impression of anything from what it is made of; the deliberate memory keeps a
    /// score per fright from what it actually did, and takes over as evidence comes in. Choices are Thompson
    /// sampled, so it keeps trying things it's unsure about. It also tracks what it has used recently: frights
    /// wear out with repetition and come back with rest, so it learns what each would do fresh and applies
    /// the wear when choosing.
    ///
    /// On the bench (tools/fear/teller_sim.py, 60 simulated players): inside one game it matches plain Thompson
    /// sampling and beats a hand-made tag prior; in a new game with all-new frights it starts about 2.4 times
    /// stronger over the first twenty, because the instinct carries what kinds of thing frighten this person.
    /// </summary>
    public class Learner
    {
        public const float SampleSd = 0.45f, PriorWeight = 1.5f;
        public const float WearK = 0.3f, WearRest = 0.9f, WearOvernight = 0.45f;

        class Item { public float n, mean; }

        public readonly Instinct instinct;
        readonly Dictionary<string, Item> items = new Dictionary<string, Item>();
        readonly Dictionary<string, float> wear = new Dictionary<string, float>();
        readonly System.Random rng;
        float instErr = 0.25f, memErr = 0.25f;
        int learned;

        public Learner(Wiring wiring = null, int seed = 0)
        {
            instinct = new Instinct(wiring);
            rng = seed == 0 ? new System.Random() : new System.Random(seed);
        }

        static string Key(string domain, string id) { return domain + ":" + id; }

        Item Get(string domain, string id)
        {
            Item it;
            if (!items.TryGetValue(Key(domain, id), out it)) { it = new Item(); items[Key(domain, id)] = it; }
            return it;
        }

        public int Tries(string domain, string id) { return (int)Get(domain, id).n; }

        /// <summary>How much force these kinds of fright still have (1 fresh, lower with recent use).</summary>
        public float Novelty(string[] tags)
        {
            if (tags.Length == 0) return 1f;
            float s = 0f, h;
            foreach (var t in tags) { wear.TryGetValue(t, out h); s += h; }
            return (float)Math.Exp(-WearK * s / tags.Length);
        }

        /// <summary>The best guess of what this fright would do fresh: instinct first, its own record as it builds.</summary>
        public float Expect(string domain, string id, string[] tags)
        {
            var it = Get(domain, id);
            float first = instinct.Value(domain, tags);
            return (it.n * it.mean + PriorWeight * first) / (it.n + PriorWeight);
        }

        float Gauss()
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        /// <summary>A Thompson draw: a plausible score for this fright right now (wear included).</summary>
        public float Draw(string domain, string id, string[] tags)
        {
            var it = Get(domain, id);
            float v = Expect(domain, id, tags) + Gauss() * SampleSd / (float)Math.Sqrt(1f + it.n);
            return v > 0f ? v * Novelty(tags) : v;
        }

        /// <summary>
        /// Learn from a fright: y is the change in the player's reaction it caused (-1..1). Returns a line for
        /// the log saying what was learned.
        /// </summary>
        public string Learn(string domain, string id, string[] tags, float y)
        {
            y = Math.Max(-1f, Math.Min(1f, y));
            float nov = Novelty(tags);
            float fresh = y > 0f ? Math.Min(1f, y / Math.Max(nov, 0.35f)) : y;   // what it would have done fresh
            var it = Get(domain, id);
            float pInst = instinct.Value(domain, tags), pMem = it.n > 0 ? it.mean : pInst;
            instErr = 0.9f * instErr + 0.1f * (fresh - pInst) * (fresh - pInst);
            memErr = 0.9f * memErr + 0.1f * (fresh - pMem) * (fresh - pMem);
            float pe = instinct.Learn(domain, tags, fresh);
            it.n += 1f;
            it.mean += (fresh - it.mean) / Math.Min(it.n, 8f);
            foreach (var t in tags) { float h; wear.TryGetValue(t, out h); wear[t] = h + 1f; }
            learned++;
            return "reaction " + y.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) +
                   (nov < 0.95f ? " (worn to " + (nov * 100f).ToString("0", CultureInfo.InvariantCulture) + "%, so fresh it would be " + fresh.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + ")" : "") +
                   "; instinct expected " + pInst.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " (surprise " + pe.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + ")" +
                   ", its record is now " + it.mean.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " over " + it.n.ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>A step of rest between frights: wear recovers a little.</summary>
        public void Rest()
        {
            var keys = new List<string>(wear.Keys);
            foreach (var t in keys) wear[t] *= WearRest;
        }

        /// <summary>A night's sleep (or a new session): most of the wear is gone.</summary>
        public void Overnight()
        {
            var keys = new List<string>(wear.Keys);
            foreach (var t in keys) wear[t] *= WearOvernight;
        }

        public string Describe()
        {
            return "instinct (" + instinct.Source + ") error " + instErr.ToString("0.00", CultureInfo.InvariantCulture) +
                   ", record error " + memErr.ToString("0.00", CultureInfo.InvariantCulture) + ", " + learned + " frights learned from";
        }

        // ---------------------------------------------------------------- saving (plain text, no engine needed)

        public string Save()
        {
            var sb = new StringBuilder("fear-learner\t1\n");
            foreach (var kv in items)
                if (kv.Value.n > 0f)
                    sb.Append("item\t").Append(kv.Key).Append('\t').Append(kv.Value.n.ToString("R", CultureInfo.InvariantCulture))
                      .Append('\t').Append(kv.Value.mean.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            instinct.Save(sb);
            return sb.ToString();
        }

        public void Load(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var line in text.Split('\n'))
            {
                var p = line.Split('\t');
                try
                {
                    if (p[0] == "item" && p.Length >= 4)
                        items[p[1]] = new Item { n = float.Parse(p[2], CultureInfo.InvariantCulture), mean = float.Parse(p[3], CultureInfo.InvariantCulture) };
                    else if (p[0] == "comp" && p.Length >= 3) instinct.LoadComp(p[1], p[2]);
                }
                catch (FormatException) { }
            }
        }
    }
}
