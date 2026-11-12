using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The haunt grammar and the director: the hunter's way of doing things nobody wrote.
    ///
    /// A grammar haunt is a gene: a kind of thing (the cups, the books, the shoes...), something done to them (turned
    /// to face you, upside down, lined up toward the cellar, laid on your bed, doubled, gone, left wet, knocked
    /// over), where (the next room, the room you hide in, the kitchen, the room you just left...), when (while you
    /// look away, the moment you leave the room, overnight) and how it reaches you (silently, with a sound, a smell,
    /// a cold patch). Every gene is something the house can really do with the objects it has.
    ///
    /// Breeding: a pool of about a dozen genes lives in the storyteller as ordinary incidents, so the learner scores
    /// them by your reactions like everything else. Each morning the ones that worked are kept and mutated (one piece
    /// changed), the ones you shrugged off are dropped, and a couple of new ones arrive at random. The pool is kept
    /// between runs (unless you've asked it not to remember you).
    ///
    /// The director: each morning it reads the learner's instinct for what moves you (by kind of fear and by sense)
    /// and sets the day's goal (the kind that works best) and what to avoid (the sense you shrug off), and biases the
    /// storyteller's choices that way. Every choice is logged.
    /// </summary>
    public partial class Hauntings
    {
        class Gene
        {
            public int thing, action, place, timing, sense;
            public string Key { get { return thing + "." + action + "." + place + "." + timing + "." + sense; } }
            public Gene Copy() { return (Gene)MemberwiseClone(); }
        }

        static readonly string[] ThingNames = { "cups", "glasses", "plates and bowls", "books", "bottles and jars", "shoes", "small things" };
        static readonly string[][] ThingWords =
        {
            new[] { "cup", "mug" }, new[] { "glass" }, new[] { "plate", "saucer", "bowl", "dish" }, new[] { "book" },
            new[] { "bottle", "jar", "tin" }, new[] { "shoe", "slipper", "boot" }, new string[0],
        };
        static readonly string[] ActionNames = { "turned to face you", "turned upside down", "lined up pointing at the cellar door", "laid out on your bed", "doubled", "gone", "left standing in water", "knocked over" };
        static readonly string[] ActionTags = { FearTypes.Hunted, FearTypes.Uncanny, FearTypes.Uncanny, FearTypes.Self, FearTypes.Uncanny, FearTypes.Loss, FearTypes.Nature, FearTypes.Unknown };
        static readonly string[] PlaceNames = { "the next room", "the room you hide in", "the kitchen", "the room you just left", "the room you're in" };
        static readonly string[] TimingNames = { "while you looked away", "the moment you left the room", "overnight" };
        static readonly string[] SenseNames = { "silently", "with a sound", "with a smell", "with a cold patch where it happened" };
        static readonly string[] SenseTags = { "sight", "sound", "smell", "touch" };
        static readonly string[] Smells = { "lavender talc", "pipe tobacco", "wet earth", "something sweet gone over", "his aftershave", "candle smoke" };

        readonly List<Gene> pool = new List<Gene>();
        readonly Dictionary<string, FearCore.Incident> geneIncidents = new Dictionary<string, FearCore.Incident>();
        readonly List<Gene> overnight = new List<Gene>();
        Gene waitingToLeave;
        Room waitingRoom;
        readonly List<GameObject> vanished = new List<GameObject>();
        readonly Dictionary<GameObject, Vector3> vanishedFrom = new Dictionary<GameObject, Vector3>();
        System.Random geneRng = new System.Random();
        string goalFamily, avoidSense;

        static string GenesPath { get { return Path.Combine(Application.persistentDataPath, "nightpages-genes.txt"); } }

        void InitGrammar()
        {
            LoadGenes();
            while (pool.Count < 10) pool.Add(RandomGene());
            foreach (var gn in pool) Register(gn);
            g.Log("haunt grammar: " + pool.Count + " invented haunts in the pool (" + (File.Exists(GenesPath) ? "carried over from before" : "new") + "): " + Describe(pool[0]) + "; " + Describe(pool[1]) + "; ...");
            var before = teller.Bias;
            teller.Bias = i => (before != null ? before(i) : 0f) + DirectorBias(i);
            var outcome = teller.Outcome;
            teller.Outcome = (inc, y) => { if (outcome != null) outcome(inc, y); if (inc.id.StartsWith("gram:")) g.Log("haunt grammar: '" + inc.label + "' got a reaction of " + y.ToString("+0.00;-0.00")); };
        }

        Gene RandomGene()
        {
            var gn = new Gene
            {
                thing = geneRng.Next(ThingNames.Length), action = geneRng.Next(ActionNames.Length), place = geneRng.Next(PlaceNames.Length),
                timing = geneRng.Next(TimingNames.Length), sense = geneRng.Next(SenseNames.Length),
            };
            Fix(gn);
            return gn;
        }

        // Some pieces don't go together: nothing is "left" by the room you're in while you're in it, overnight.
        static void Fix(Gene gn)
        {
            if (gn.place == 4 && gn.timing == 1) gn.timing = 0;
            if (gn.place == 3) gn.timing = 1;
            if (gn.timing == 1 && gn.place != 3 && gn.place != 4) gn.place = 3;
            if (gn.action == 3 && gn.place == 4) gn.place = 0;
        }

        string Describe(Gene gn)
        {
            return (gn.thing == 6 ? "the small things" : "every one of the " + ThingNames[gn.thing]) + " in " + PlaceNames[gn.place] + ", " + ActionNames[gn.action] + ", " +
                   TimingNames[gn.timing] + ", " + SenseNames[gn.sense];
        }

        string[] TagsOf(Gene gn)
        {
            return new[] { ActionTags[gn.action], SenseTags[gn.sense], "moved", gn.place == 4 ? "close" : "far", gn.timing == 2 ? "home" : gn.place == 1 ? "behind" : "someone", "gram:" + ThingNames[gn.thing] };
        }

        void Register(Gene gn)
        {
            string id = "gram:" + gn.Key;
            if (geneIncidents.ContainsKey(id)) return;
            int tier = gn.action == 2 || gn.action == 3 || gn.action == 4 ? 1 : 0;
            var inc = new FearCore.Incident { id = id, label = Describe(gn), tier = tier, cost = tier == 1 ? 1.5f : 1f, minEpisode = 2, tags = TagsOf(gn), cooldown = 120f };
            var gene = gn;
            inc.fire = () => Fire(inc, () => FireGene(gene));
            inc.can = () => CanGene(gene);
            teller.Add(inc);
            geneIncidents[id] = inc;
        }

        void Unregister(Gene gn)
        {
            string id = "gram:" + gn.Key;
            FearCore.Incident inc;
            if (!geneIncidents.TryGetValue(id, out inc)) return;
            teller.incidents.Remove(inc);
            geneIncidents.Remove(id);
        }

        // ---------------------------------------------------------------- doing it

        Room PlaceRoom(Gene gn)
        {
            Room here = Here();
            switch (gn.place)
            {
                case 0:
                    foreach (var r in g.house.rooms) if (r != here && r != g.house.hall && !Vis.Sees(Cam, r.center + Vector3.up)) return r;
                    return null;
                case 1: return g.dread != null && g.dread.Refuge != null && g.dread.Refuge != here ? g.dread.Refuge : null;
                case 2: { var k = RoomNamed("kitchen"); return k != here ? k : null; }
                case 3: return waitingRoom;
                default: return here;
            }
        }

        bool CanGene(Gene gn)
        {
            if (waitingToLeave != null) return false;
            if (gn.timing == 1) return Here() != null && Here() != g.house.hall;
            if (gn.timing == 2) return !overnight.Exists(o => o.Key == gn.Key);
            return PlaceRoom(gn) != null;
        }

        bool FireGene(Gene gn)
        {
            if (gn.timing == 2)
            {
                overnight.Add(gn);
                g.Log("haunt grammar: tonight, " + Describe(gn) + " (you'll find it in the morning)");
                return true;
            }
            if (gn.timing == 1)
            {
                waitingToLeave = gn;
                waitingRoom = Here();
                g.Log("haunt grammar: waiting for you to leave the " + waitingRoom.name + " (" + Describe(gn) + ")");
                return true;
            }
            return Apply(gn, PlaceRoom(gn), true);
        }

        List<Physical> ThingsIn(Gene gn, Room room, bool unseen)
        {
            var list = new List<Physical>();
            foreach (var p in Physical.All)
            {
                if (p == null || p.mov == null || p.mov.held || !p.gameObject.activeInHierarchy) continue;
                if (g.house.RoomAt(p.transform.position) != room) continue;
                if (unseen && Vis.Sees(Cam, p.transform.position)) continue;
                string n = Physical.Label(p.name);
                var words = ThingWords[gn.thing];
                bool match = words.Length == 0 ? p.mov.mass < 1.5f : false;
                foreach (var w in words) if (n.Contains(w)) match = true;
                if (match) list.Add(p);
            }
            return list;
        }

        /// <summary>Do it to the matching things in that room. False if there was nothing there to do it to.</summary>
        bool Apply(Gene gn, Room room, bool unseen)
        {
            if (room == null) return false;
            var things = ThingsIn(gn, room, unseen);
            if (things.Count == 0 && gn.thing != 6) { gn = gn.Copy(); gn.thing = 6; things = ThingsIn(gn, room, unseen); }
            if (things.Count == 0) return false;
            if (things.Count > 8) things = things.GetRange(0, 8);
            Vector3 face = PlayerPos;
            Vector3 cellar = g.house.cellar != null ? g.house.cellar.transform.position : room.center;
            int done = 0;
            for (int i = 0; i < things.Count; i++)
            {
                var p = things[i];
                var t = p.transform;
                Vector3 pos = t.position;
                switch (gn.action)
                {
                    case 0:
                        { Vector3 d = Vis.Flat(face - pos); if (d.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(d.normalized, Vector3.up); }
                        break;
                    case 1:
                        t.rotation = Quaternion.Euler(180f, t.eulerAngles.y, 0f) ;
                        t.position = pos + Vector3.up * 0.002f;
                        var c = p.GetComponent<Contents>();
                        if (c != null && c.litres > 0.01f) { Liquid.Spill(pos + Vector3.up * 0.05f, c.litres, c.kind, t); c.litres = 0f; if (c.surface != null) c.surface.SetActive(false); }
                        break;
                    case 2:
                        {
                            Vector3 dir = Vis.Flat(cellar - room.center);
                            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
                            var b = BoundsOf(t);
                            t.position = new Vector3(room.center.x, b.extents.y + 0.01f, room.center.z) + dir * (i * 0.28f - 0.5f);
                            t.rotation = Quaternion.LookRotation(dir, Vector3.up);
                        }
                        break;
                    case 3:
                        if (g.house.bedTop == null) return false;
                        {
                            var b = BoundsOf(g.house.bedTop);
                            var ob = BoundsOf(t);
                            t.position = new Vector3(b.center.x + (i % 3 - 1) * 0.3f, b.max.y + ob.extents.y + 0.01f, b.center.z + (i / 3 - 0.5f) * 0.35f);
                        }
                        break;
                    case 4:
                        if (i >= 3) break;
                        var copy = Object.Instantiate(p.gameObject, g.house.transform);
                        copy.name = p.name;
                        copy.transform.position = pos + Vis.Flat(Random.onUnitSphere).normalized * 0.14f;
                        break;
                    case 5:
                        vanishedFrom[p.gameObject] = pos;
                        vanished.Add(p.gameObject);
                        p.gameObject.SetActive(false);
                        break;
                    case 6:
                        var r = Stains.Mark(g.house.transform, new Vector3(pos.x, BoundsOf(t).min.y + 0.001f, pos.z), Vector3.up, 0.16f, 0.16f, "blot", Liquid.MarkOf("water"), 0.8f);
                        if (r != null) Stains.Cleanable(r, "Wipe", "the water", 60f, "rag", "sponge");
                        break;
                    case 7:
                        p.mov.Wake();
                        if (p.mov.Body != null) p.mov.Body.AddForce(Vis.Flat(Random.onUnitSphere).normalized * 1.2f + Vector3.up * 0.4f, ForceMode.Impulse);
                        break;
                }
                done++;
                lastTrace = t.position;
            }
            if (done == 0) return false;
            // How it reaches you.
            if (gn.sense == 1) Sfx.At(gn.action == 7 ? "smash" : gn.action == 2 || gn.action == 3 ? "drag" : "clink", room.center + Vector3.up, 0.5f, 0.9f + Random.value * 0.2f);
            else if (gn.sense == 2) g.hud.Say("A smell of " + Smells[Random.Range(0, Smells.Length)] + ", from the " + room.name + ".", 3.5f);
            else if (gn.sense == 3) g.hud.Say("The air in the " + room.name + " is colder than it should be.", 3.5f);
            g.Log("haunt grammar: " + done + " " + ThingNames[gn.thing] + " in the " + room.name + " " + ActionNames[gn.action] + " (" + TimingNames[gn.timing] + ", " + SenseNames[gn.sense] + ")");
            return true;
        }

        void TickGrammar()
        {
            if (waitingToLeave == null) return;
            if (Here() == waitingRoom) return;
            var gn = waitingToLeave;
            waitingToLeave = null;
            if (!Apply(gn, waitingRoom, false)) g.Log("haunt grammar: you left the " + waitingRoom.name + ", but there was nothing there to do it to");
            waitingRoom = null;
        }

        /// <summary>Morning: what was done overnight is waiting; what vanished yesterday is back; then the pool breeds and the director sets the day.</summary>
        void BeginDayGrammar(int day)
        {
            foreach (var go in vanished) if (go != null) { go.SetActive(true); Vector3 p; if (vanishedFrom.TryGetValue(go, out p)) go.transform.position = p; }
            if (vanished.Count > 0) g.Log("haunt grammar: what was gone yesterday is back where it was");
            vanished.Clear(); vanishedFrom.Clear();
            waitingToLeave = null; waitingRoom = null;
            foreach (var gn in overnight)
            {
                Room r = gn.place == 4 || gn.place == 3 ? RoomNamed("bedroom") : PlaceRoom(gn) ?? RoomNamed("kitchen");
                if (!Apply(gn, r, false)) g.Log("haunt grammar: overnight, nothing to do it to (" + Describe(gn) + ")");
            }
            overnight.Clear();
            if (day >= 2) Breed();
            Direct();
        }

        // ---------------------------------------------------------------- breeding

        void Breed()
        {
            var learner = teller.learner;
            var scored = new List<KeyValuePair<float, Gene>>();
            foreach (var gn in pool)
            {
                var tags = TagsOf(gn);
                float e = learner.Expect("incident", "gram:" + gn.Key, tags);
                int tries = learner.Tries("incident", "gram:" + gn.Key);
                scored.Add(new KeyValuePair<float, Gene>(tries > 0 ? e : e - 0.01f, gn));
            }
            scored.Sort((a, b) => b.Key.CompareTo(a.Key));
            var keep = new List<Gene>();
            var dropped = new List<string>();
            for (int i = 0; i < scored.Count; i++)
            {
                var gn = scored[i].Value;
                int tries = learner.Tries("incident", "gram:" + gn.Key);
                if (i >= 6 || (tries >= 2 && scored[i].Key < -0.02f)) { dropped.Add(Describe(gn) + " (" + scored[i].Key.ToString("+0.00;-0.00") + ")"); Unregister(gn); continue; }
                keep.Add(gn);
            }
            var born = new List<string>();
            // Children of the best: one piece changed.
            for (int i = 0; i < Mathf.Min(4, keep.Count); i++)
            {
                var child = keep[i].Copy();
                switch (geneRng.Next(5))
                {
                    case 0: child.thing = geneRng.Next(ThingNames.Length); break;
                    case 1: child.action = geneRng.Next(ActionNames.Length); break;
                    case 2: child.place = geneRng.Next(PlaceNames.Length); break;
                    case 3: child.timing = geneRng.Next(TimingNames.Length); break;
                    default: child.sense = geneRng.Next(SenseNames.Length); break;
                }
                Fix(child);
                if (keep.Exists(k => k.Key == child.Key)) continue;
                keep.Add(child);
                Register(child);
                born.Add(Describe(child));
            }
            // And a couple of strangers.
            for (int i = 0; i < 2 && keep.Count < 12; i++)
            {
                var gn = RandomGene();
                if (keep.Exists(k => k.Key == gn.Key)) continue;
                keep.Add(gn);
                Register(gn);
                born.Add(Describe(gn) + " (new)");
            }
            pool.Clear();
            pool.AddRange(keep);
            if (dropped.Count > 0) g.Log("haunt grammar: dropped what you shrugged off or what came last: " + string.Join("; ", dropped.ToArray()));
            if (born.Count > 0) g.Log("haunt grammar: invented overnight: " + string.Join("; ", born.ToArray()));
            SaveGenes();
        }

        void SaveGenes()
        {
            if (g == null || !g.MayRemember) return;
            var sb = new StringBuilder();
            foreach (var gn in pool) sb.Append(gn.thing).Append(' ').Append(gn.action).Append(' ').Append(gn.place).Append(' ').Append(gn.timing).Append(' ').Append(gn.sense).Append('\n');
            try { File.WriteAllText(GenesPath, sb.ToString()); } catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't save the invented haunts: " + e.Message); }
        }

        void LoadGenes()
        {
            pool.Clear();
            if (!Settings.S.remember) return;
            try
            {
                if (!File.Exists(GenesPath)) return;
                foreach (var line in File.ReadAllLines(GenesPath))
                {
                    var p = line.Split(' ');
                    if (p.Length != 5) continue;
                    var gn = new Gene();
                    int.TryParse(p[0], out gn.thing); int.TryParse(p[1], out gn.action); int.TryParse(p[2], out gn.place); int.TryParse(p[3], out gn.timing); int.TryParse(p[4], out gn.sense);
                    if (gn.thing >= ThingNames.Length || gn.action >= ActionNames.Length || gn.place >= PlaceNames.Length || gn.timing >= TimingNames.Length || gn.sense >= SenseNames.Length) continue;
                    Fix(gn);
                    if (!pool.Exists(x => x.Key == gn.Key)) pool.Add(gn);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't read the invented haunts: " + e.Message); }
        }

        // ---------------------------------------------------------------- the director

        static readonly string[] Families = { FearTypes.Unknown, FearTypes.Uncanny, FearTypes.Hunted, FearTypes.Loss, FearTypes.Self, FearTypes.Animal, FearTypes.Nature, FearTypes.Trapped, FearTypes.Medical, FearTypes.Body, FearTypes.Disgust };

        /// <summary>Morning: read what moves this player and set the day's goal and what to avoid.</summary>
        void Direct()
        {
            var inst = teller.learner.instinct;
            float best = float.MinValue, worst = float.MaxValue;
            string bestF = null, worstS = null;
            var sb = new StringBuilder();
            foreach (var f in Families)
            {
                if (f == FearTypes.Disgust && Settings.S.gore == 2) continue;
                float v = inst.Value("incident", new[] { f });
                sb.Append(f).Append(' ').Append(v.ToString("+0.00;-0.00")).Append("  ");
                if (v > best) { best = v; bestF = f; }
            }
            foreach (var s in SenseTags)
            {
                float v = inst.Value("incident", new[] { s });
                if (v < worst) { worst = v; worstS = s; }
            }
            goalFamily = bestF;
            avoidSense = worst < -0.01f ? worstS : null;
            g.Log("director: today's goal is " + goalFamily + " (what moves you most so far: " + sb.ToString().Trim() + ")" +
                  (avoidSense != null ? "; it avoids " + avoidSense + ", which you shrug off (" + worst.ToString("+0.00;-0.00") + ")" : "") +
                  (g.dread != null && g.dread.Refuge != null ? "; it knows you hide in the " + g.dread.Refuge.name : ""));
        }

        float DirectorBias(FearCore.Incident i)
        {
            float b = 0f;
            if (i.tags == null) return 0f;
            foreach (var t in i.tags)
            {
                if (goalFamily != null && t == goalFamily) b += 0.35f;
                if (avoidSense != null && t == avoidSense) b -= 0.25f;
            }
            if (i.id.StartsWith("gram:") && teller.learner.Tries("incident", i.id) == 0) b += 0.15f;      // try what it invented
            return b;
        }

        // ---------------------------------------------------------------- studio

        public int GrammarCount { get { return pool.Count; } }

        /// <summary>Studio: do pool gene `i` now in the room it names (or the kitchen), seen or not.</summary>
        public bool ForceGene(int i, out string what)
        {
            what = null;
            if (i < 0 || i >= pool.Count) return false;
            var gn = pool[i].Copy();
            gn.timing = 0;
            Room r = PlaceRoom(gn);
            if (r == null || gn.place == 4 || gn.place == 3) r = RoomNamed("kitchen");
            what = Describe(gn).Replace(PlaceNames[pool[i].place], "the " + r.name);
            return Apply(gn, r, false);
        }
    }
}
