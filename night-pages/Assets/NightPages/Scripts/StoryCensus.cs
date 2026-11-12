using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The story census: the studio tells thousands of stories at every stay length, without playing them, and
    /// reports how many different ones there were, how often each part was used or could find nothing that fit,
    /// what is really going on and how often, and a handful of whole stories to read by eye (census.txt).
    /// </summary>
    public partial class GameDirector
    {
        public static bool StudioCensus = true;
        const int CensusStories = 10000;

        IEnumerator StoryCensus(StringBuilder summary)
        {
            var report = new StringBuilder();
            report.Append("NIGHT PAGES STORY CENSUS\n").Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("\n\n");
            var usage = new Dictionary<string, int>();
            var slots = new[] { "who", "what", "why", "explain", "act", "keepsake" };
            var samples = new List<string>();
            int keep = RunDays;
            var t0 = Time.realtimeSinceStartup;
            foreach (int len in RunLengths)
            {
                RunDays = len;
                var cores = new HashSet<string>();
                var stories = new HashSet<string>();
                var empty = new Dictionary<string, int>();
                var explains = new Dictionary<string, int>();
                int fewSigns = 0, noRules = 0, layersShort = 0;
                var r = new System.Random(len * 7919);
                for (int n = 0; n < CensusStories; n++)
                {
                    double d = r.NextDouble();
                    var truthHere = d < 0.6 ? Truth.Killer : d < 0.8 ? Truth.Keepers : Truth.House;
                    var t = Tale.Tell(truthHere, r, null, null, null, 1952, 30, null);
                    var key = new StringBuilder(t.CoreKey);
                    int layers = len >= 30 ? 2 : len >= 14 ? 1 : 0;
                    var above = t;
                    var told = new List<Tale> { t };
                    for (int i = 0; i < layers; i++)
                    {
                        var o = Tale.Tell(truthHere, r, null, null, above, 1930, 1, null);
                        if (o.who == null || o.what == null) layersShort++;
                        key.Append(" | ").Append(o.CoreKey);
                        told.Add(o);
                        above = o;
                    }
                    cores.Add(t.CoreKey);
                    stories.Add(key.ToString());
                    foreach (var s in slots)
                    {
                        var p = Slot(t, s);
                        if (p == null) { int c; empty.TryGetValue(s, out c); empty[s] = c + 1; }
                        else { int c; usage.TryGetValue(s + ":" + p.id, out c); usage[s + ":" + p.id] = c + 1; }
                    }
                    foreach (var sg in t.signs) { int c; usage.TryGetValue("sign:" + sg.id, out c); usage["sign:" + sg.id] = c + 1; }
                    if (t.signs.Count < 4) fewSigns++;
                    if (t.rules.Count == 0) noRules++;
                    string ex = t.explain != null ? t.explain.id : "none";
                    { int c; explains.TryGetValue(ex, out c); explains[ex] = c + 1; }
                    if (n < 5)
                    {
                        var sb = new StringBuilder("--- " + LengthName(len) + ", story " + (n + 1) + " (truth " + truthHere + ")\n");
                        for (int i = 0; i < told.Count; i++) sb.Append("  ").Append(told[i].Bible(i == 0 ? "the story" : i == 1 ? "under it" : "under that")).Append("\n");
                        sb.Append("  last word: ").Append(t.Epilogue ?? "(none)").Append("\n");
                        var pages = t.Pages();
                        if (pages.Count > 0) sb.Append("  a page: ").Append(pages[r.Next(pages.Count)].Replace("\n", " ")).Append("\n");
                        var clues = t.Clues();
                        if (clues.Count > 0) sb.Append("  a note: ").Append(clues[r.Next(clues.Count)].Replace("\n", " ")).Append("\n");
                        samples.Add(sb.ToString());
                    }
                    if (n % 2000 == 1999) yield return null;
                }
                report.Append("== ").Append(LengthName(len)).Append(": ").Append(CensusStories).Append(" stories told\n");
                // The calendar: real days onto story days, and how a day's two pieces of work are shared out.
                var cal = new StringBuilder("  days (real>story, work pieces of 2): ");
                int prev = 0, quiet = 0;
                for (int c = 1; c <= len; c++)
                {
                    int sd = StoryDay(c), idx, cnt;
                    Share(c, out idx, out cnt);
                    int from = 2 * idx / cnt, to = 2 * (idx + 1) / cnt;
                    if (to <= from) quiet++;
                    cal.Append(c).Append(">").Append(sd).Append("(").Append(to - from).Append(") ");
                    if (sd < prev) Problem("story census: " + len + " days: day " + c + " goes back a story day");
                    prev = sd;
                }
                if (StoryDay(1) != 1 || StoryDay(len) != Days) Problem("story census: " + len + " days don't start on story day 1 and end on " + Days);
                report.Append(cal).Append("\n  days with no work (quiet days): ").Append(quiet).Append("\n");
                report.Append("  different cores (who/what/why/really/act/proof/truth): ").Append(cores.Count).Append("\n");
                report.Append("  different whole stories (with the older ones under them): ").Append(stories.Count).Append("\n");
                foreach (var s in slots)
                {
                    int c; empty.TryGetValue(s, out c);
                    report.Append("  ").Append(s).Append(" found nothing that fit: ").Append(c).Append(" (").Append((100f * c / CensusStories).ToString("0.0")).Append("%)\n");
                    if (c > CensusStories / 20 && s != "act") Problem("story census, " + len + " days: '" + s + "' found nothing that fit in " + (100f * c / CensusStories).ToString("0") + "% of stories");
                }
                report.Append("  fewer than 4 signs: ").Append(fewSigns).Append("; no rules: ").Append(noRules).Append("; older story missing parts: ").Append(layersShort).Append("\n");
                report.Append("  what was really going on: ");
                foreach (var kv in explains) report.Append(kv.Key).Append(" ").Append((100f * kv.Value / CensusStories).ToString("0")).Append("%  ");
                report.Append("\n\n");
                Log("story census, " + LengthName(len) + ": " + cores.Count + " different cores, " + stories.Count + " different whole stories in " + CensusStories);
            }
            RunDays = keep;

            report.Append("== how often each part was used (all lengths together)\n");
            var keys = new List<string>(usage.Keys);
            keys.Sort();
            string lastSlot = null;
            foreach (var k in keys)
            {
                string slot = k.Substring(0, k.IndexOf(':'));
                if (slot != lastSlot) { report.Append("\n").Append(slot).Append(":\n"); lastSlot = slot; }
                report.Append("  ").Append(k.Substring(k.IndexOf(':') + 1).PadRight(18)).Append(usage[k]).Append("\n");
            }
            report.Append("\n== whole stories to read\n\n");
            foreach (var s in samples) report.Append(s).Append("\n");
            try { File.WriteAllText(Path.Combine(Tour.OutDir, "census.txt"), report.ToString()); }
            catch (System.Exception e) { Problem("story census: couldn't write census.txt: " + e.Message); }
            Log("story census: done in " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s (census.txt)");
            summary.Append("  \"census\": \"done\",\n");
        }

        static Tale.Piece Slot(Tale t, string s)
        {
            switch (s)
            {
                case "who": return t.who;
                case "what": return t.what;
                case "why": return t.why;
                case "explain": return t.explain;
                case "act": return t.act;
                default: return t.keepsake;
            }
        }
    }
}
