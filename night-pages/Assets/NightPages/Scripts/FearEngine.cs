using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NightPages
{
    public class FearOption
    {
        public string label;
        public int intensity;    // 0 calm, 1 unsettling, 2 frightening. Later days unlock higher ones.
        public string line;      // how the manuscript describes it the next morning
        public string[] families; // which kinds of fear it belongs to (FearTypes ids)
    }

    public class FearSlot
    {
        public string name;
        public FearOption[] options;
        public FearSlot(string name, params FearOption[] options) { this.name = name; this.options = options; }
    }

    [System.Serializable]
    public class OptStat
    {
        public string key;
        public float mean;
        public float n;
    }

    [System.Serializable]
    public class FearSave
    {
        public int dreams;
        public List<OptStat> stats = new List<OptStat>();
    }

    /// <summary>
    /// The learner behind the dreams, a small port of Moodscape's Unease mirror.
    /// It changes one part of the dream, compares the player's reaction over the next few seconds with
    /// just before, and keeps the change if they reacted or undoes it if they didn't. What it learns
    /// about each option is saved, so later dreams (and later runs) start from what worked.
    /// Every decision is logged to the Mind overlay (F1) and the Unity console.
    /// </summary>
    public class FearEngine
    {
        public const int Space = 0, Behavior = 1, Body = 2, Head = 3, Arms = 4, Face = 5, Gait = 6, Kind = 7, Shift = 8, Wrongness = 9, Light = 10, Sound = 11, Motion = 12,
            Legs = 13, Skin = 14, HeadKind = 15, Extra = 16, Hallucination = 17;

        /// <summary>The slots that are what the monster looks like, in the order Monster.cs expects them (its genome).</summary>
        public static readonly int[] LookSlots = { Body, Head, Arms, Face, Gait, Kind, Shift, Legs, Skin, HeadKind, Extra };
        public const int FirstLook = Body, LastLook = Shift;     // the original seven genes, still in one run

        /// <summary>
        /// Option labels the player has asked never to see (for example "A spider"). The game is meant to
        /// unsettle, not to ambush someone with a real phobia they didn't agree to. Filled from settings.
        /// </summary>
        public static readonly HashSet<string> Banned = new HashSet<string>();

        public static bool IsLook(int slot) { return System.Array.IndexOf(LookSlots, slot) >= 0; }

        /// <summary>Slots that belong to the dream (the hallucinations are chosen while awake).</summary>
        public static bool InDream(int slot) { return slot != Hallucination; }

        /// <summary>Above this reaction level the player counts as very frightened (see the sweet spot in Tick).</summary>
        public const float HotLevel = 0.8f;
        /// <summary>How long they may stay very frightened before the engine backs off, and for how long.</summary>
        public const float HotSeconds = 10f, EaseSeconds = 20f;

        public static readonly string[] SoundClips = { null, "breath", "lullaby", "typing", "whisper", "scratch", "beep" };

        const string U = FearTypes.Unknown, Hu = FearTypes.Hunted, Bo = FearTypes.Body, An = FearTypes.Animal, Di = FearTypes.Disgust,
            Un = FearTypes.Uncanny, Tr = FearTypes.Trapped, Na = FearTypes.Nature, Me = FearTypes.Medical, Lo = FearTypes.Loss, Se = FearTypes.Self;

        static FearOption O(string label, int intensity, string line, params string[] families)
        {
            return new FearOption { label = label, intensity = intensity, line = line, families = families };
        }

        // Option order matters: Dream.cs and Monster.cs act on the index. Add new options at the end of a slot.
        public static readonly FearSlot[] Slots =
        {
            new FearSlot("space",
                O("An endless hallway", 0, "There was a hallway that did not end.", U, Tr),
                O("A flooded cellar", 1, "There was a door at the bottom of the water.", Na, Tr),
                O("A childhood bedroom", 0, "It was the right bedroom, only he was the wrong size.", Lo, Un),
                O("A field with one door", 1, "There was a door standing by itself in a field.", U, Na),
                O("A crawlspace", 1, "The ceiling came down to meet him, and he had to go on his knees.", Tr),
                O("A walkway over nothing", 1, "The floor was one plank wide, and under it there was no bottom.", Na, Hu)),
            // What the monster does.
            new FearSlot("it",
                O("Not here", 0, null),
                O("Heard, never seen", 1, "Something kept pace behind him and stopped when he stopped.", U, Hu),
                O("It only stands", 1, "It stood at the far end and did nothing at all.", U, Un),
                O("It mirrors you", 2, "It moved when he moved, exactly, like a bad reflection.", Se, Un),
                O("It moves when you don't look", 2, "It only came closer when he looked away.", Hu),
                O("It walks away from you", 1, "Every time he found it, it was already leaving.", Lo)),
            // What the monster is. These change while it is out of sight, or in front of you (see "how it changes").
            new FearSlot("its body",
                O("Tall and thin", 0, null),
                O("Hunched over", 1, "It was bent over, as if listening at a keyhole.", Bo),
                O("Too tall for the room", 1, "It was too tall for the room and had to bend its neck.", Bo, U),
                O("On all fours", 2, "It went on all fours, and it was fast.", An, Hu),
                O("The size of a child", 1, "It was no bigger than he had been, that year.", Un, Lo)),
            new FearSlot("its head",
                O("An ordinary head", 0, null),
                O("Head tilted too far", 1, "Its head lay over on its shoulder like a question.", Bo, Un),
                O("A long neck", 1, "Its neck went on for much too long.", Bo),
                O("Too many heads", 2, "It had more than one head, and they did not agree.", Bo),
                O("No head", 1, "Above the collar there was nothing at all.", Bo, U)),
            new FearSlot("its arms",
                O("Arms at its sides", 0, null),
                O("Arms that reach the floor", 1, "Its hands rested on the floor without it bending.", Bo),
                O("Too many arms", 2, "He stopped counting its arms.", Bo, An),
                O("Reaching for you", 1, "It held its arms out to him, the way you do for a child.", Hu, Lo),
                O("No arms", 1, "It had no arms, and did not seem to miss them.", Bo),
                O("Bending the wrong way", 2, "Its elbows bent the wrong way, and it didn't mind.", Bo),
                O("A mantis's forelegs", 2, "It held its arms folded up in front of it, like praying, and they were full of hooks.", An, Bo),
                O("Spider legs where its arms should be", 2, "Where its arms should have been, two long jointed legs came up over its head and down to the floor.", An, Bo)),
            new FearSlot("its face",
                O("No face", 0, null, U),
                O("Pale eyes", 1, "It had eyes, and they were the only bright thing.", U),
                O("A mouth and nothing else", 2, "It had a mouth and nothing else.", Bo, Di),
                O("A smile", 2, "It was smiling. It had been smiling the whole time.", Un),
                O("Too many eyes", 2, "He tried to meet its eyes and couldn't decide which.", Bo, Di)),
            new FearSlot("how it holds itself",
                O("Perfectly still", 0, null),
                O("Swaying", 1, "It swayed, very slightly, like something hung up.", Un),
                O("Twitching", 1, "Every few seconds something in it jerked.", Bo, Un),
                O("Shaking its head, slowly", 1, "It shook its head at him, slowly: no.", Se),
                O("Limping, dragging one leg", 1, "It dragged one leg, and it was still faster than him.", Bo, Hu),
                O("Gliding, as if carried", 1, "Its legs hardly moved. It came toward him as if something were carrying it.", Un, U),
                O("Jerky, like a film with frames missing", 2, "It moved like an old film with frames missing: here, then there, then closer.", Un)),
            // What it resembles: the common phobias and the five basic fears, as anatomy it can take on.
            new FearSlot("what it is like",
                O("A person, almost", 0, null, Un),
                O("A spider", 2, "It had too many legs, and its knees were higher than its back.", An, Bo),
                O("A doll", 1, "It was a doll, with a doll's glass eyes, and it had been put there to watch him.", Un),
                O("A clown", 1, "It was painted to look happy.", Un),
                O("A snake", 1, "It had no arms or legs, and it stood up anyway.", An),
                O("A hound", 2, "It was a dog, nearly, and it had his scent.", An, Hu),
                O("A swarm", 2, "It was not one thing. It was hundreds of small things agreeing to be one.", An, Di),
                O("Covered in holes", 2, "Its skin was full of small holes, close together, and he could not stop looking at them.", Di, Bo),
                O("Drowned", 1, "It was wet through, and its hair hung over its face, and it dripped.", Na, Di),
                O("The doctor", 2, "It wore a mask over its mouth, and its fingers were needles.", Me, Bo),
                O("Her, from behind", 1, "It had Grandma's cardigan and Grandma's hair, and it would not turn around.", Lo, Un),
                O("You", 2, "It was wearing his coat. It was exactly his height.", Se),
                O("A shadow", 1, "He could only see it out of the corner of his eye.", U, Na)),
            new FearSlot("how it changes",
                O("Only when you look away", 0, null, U),
                O("While you watch", 1, "It changed while he watched, as if it were still deciding what to be.", Bo)),
            new FearSlot("wrongness",
                O("Nothing", 0, null),
                O("Too many doors", 1, "There were more doors than there were walls to hold them.", U, Tr),
                O("Furniture on the ceiling", 1, "The table was set for dinner, on the ceiling.", Un),
                O("Everything slightly too large", 1, "Everything was a little too large, or he was six again.", Un, Lo),
                O("Faces turned to the wall", 2, "They were all standing with their faces to the wall.", Un, Lo),
                O("The room is shrinking", 2, "Every time he looked away, the walls had come in a little.", Tr)),
            new FearSlot("light",
                O("Dim and steady", 0, null),
                O("Flicker", 1, "The light kept almost going out.", U),
                O("Deep red", 1, "The light was the red of a closed eyelid.", Bo),
                O("One swinging bulb", 1, "One bulb swung, and the shadows swung the other way.", U),
                O("Dark, with a distant window", 2, "It was dark except for one window, a long way off.", Na, Lo)),
            new FearSlot("sound",
                O("Silence", 0, null),
                O("Breathing", 1, "Something was breathing just behind his ear.", Hu),
                O("A lullaby, played slow", 1, "Someone was playing her lullaby, too slowly.", Lo, Un),
                O("Typing from another room", 1, "He could hear himself typing in another room.", Se),
                O("Whispering", 2, "The whispering knew his name.", U),
                O("Scratching inside the walls", 1, "Something small was busy inside the walls.", An, Di),
                O("A heart monitor", 1, "Somewhere a heart monitor was keeping time, and it was his.", Me)),
            new FearSlot("motion",
                O("Still", 0, null),
                O("The walls breathe", 1, "The walls were breathing.", Bo),
                O("Things move when unseen", 2, "Nothing moved until he turned his back.", U, Hu)),
            // ---- The rest of its genome. Option 0 of each means "whatever its kind would have"; anything else
            // overrides it, so the engine can put any head on any body on anything at all to stand on.
            new FearSlot("what it stands on",
                O("As its kind", 0, null),
                O("Two legs", 0, null, Un),
                O("A spider's legs", 2, "Below the waist it was a spider.", An, Bo),
                O("Four legs, like a dog", 2, "It ran on four legs, and none of them were a dog's.", An, Hu),
                O("A tail instead of legs", 1, "Below the waist it was a tail, and the tail was still coming round the corner.", An),
                O("It drags itself on its hands", 2, "It had no legs. It came on its hands, and it was fast.", Bo, Hu),
                O("A carpet of crawling things", 2, "Where its legs should have been there were insects, hundreds, carrying it.", An, Di),
                O("Dozens of legs", 2, "It had too many legs to be one animal.", An, Di)),
            new FearSlot("its skin",
                O("As its kind", 0, null),
                O("Dark, wet, veined", 1, "Its skin was dark and wet and he could see the veins working.", Bo, Di),
                O("Pale, full of holes", 2, "Its skin was full of small holes, close together.", Di, Bo),
                O("Drowned", 1, "It was the colour of something that had been under water a long time.", Na, Di),
                O("Cracked porcelain", 1, "It was porcelain, and cracked, and something showed through the cracks.", Un),
                O("Greasepaint", 1, "It was painted white, thickly, the way you paint over something.", Un),
                O("A hospital gown", 1, "It wore a hospital gown, tied at the back.", Me),
                O("Grandma's cardigan", 1, "It was wearing Grandma's cardigan.", Lo, Un),
                O("Your coat", 2, "It was wearing his coat.", Se),
                O("A shell", 2, "It was hard all over, plated, and it clicked.", An),
                O("Scales", 1, "It had scales, and they lay down when it moved toward him.", An),
                O("Mangy fur", 1, "It had fur, in patches, and where there wasn't fur there was skin.", An, Di),
                O("Shadow", 1, "It was the colour of the dark behind it.", U)),
            new FearSlot("its head is",
                O("As its kind", 0, null),
                O("Smooth and bare", 0, null, U),
                O("A doll's head", 1, "It had a doll's head, too big for it.", Un),
                O("A clown's head", 1, "Its face was painted to look happy.", Un),
                O("A spider's eyes", 2, "Where its face should be there was a cluster of eyes.", An, Bo),
                O("A snake's head", 1, "It had a snake's head, and it tasted the air.", An),
                O("A dog's jaws", 2, "Its head was all jaw.", An, Hu),
                O("A surgical mask", 2, "It wore a surgical mask, and the mask moved when it breathed.", Me),
                O("Grandma's hair, from behind", 1, "All he ever saw was the back of Grandma's head.", Lo, Un),
                O("Your face", 2, "It had his face, and it was wearing it badly.", Se, Un),
                O("Wet hair over its face", 1, "Its hair hung wet over its face.", Na, Un)),
            new FearSlot("and",
                O("Nothing more", 0, null),
                O("Needles for fingers", 2, "Its fingers were needles.", Me, Bo),
                O("Holes along its arms", 2, "Its arms were full of small holes.", Di),
                O("Things crawling on it", 2, "Small things were crawling all over it, and it let them.", An, Di),
                O("It drips", 1, "It dripped, steadily, wherever it stood.", Na),
                O("A second face in its chest", 2, "There was another face in its chest, and that one was awake.", Bo, Un),
                O("Strings, like a puppet", 1, "There were strings going up from its wrists into the dark.", Un, Tr),
                O("Ribs that open", 2, "Its ribs opened, a little, and closed again.", Bo)),
            // ---- Awake: what the house shows you that isn't there. Chosen and learned the same way.
            new FearSlot("hallucination",
                O("Nothing", 0, null),
                O("In the corner of your eye", 1, "He kept seeing it at the edge of things.", U, Hu),
                O("The walls breathe", 1, "The walls had been breathing all day.", Bo),
                O("A painting looks back", 1, "One of the paintings had turned to look at him.", Un),
                O("The manuscript wrote itself", 2, "There was a line in the manuscript he had not written.", Se),
                O("A reflection in the window", 2, "In the window, behind his reflection, something stood.", Hu, Se),
                O("A whisper close by", 1, "Someone said something just behind his ear.", U),
                O("The hall stretches", 1, "The hall was longer than it had been.", Tr, U),
                O("It in a doorway, then not", 2, "It stood in a doorway, and then it didn't.", Hu)),
        };

        class Change { public int slot, from, to; public float t, before, made; public bool unwatched, gated, seen; }

        public int[] current = new int[Slots.Length];
        public System.Action<int, int> onApply;      // (slot, option): the dream makes it real
        public System.Func<int, bool> canChange;     // may this slot change right now? (the monster's look only changes unseen)
        public System.Action<string> log;
        public System.Action<string> onEase;         // the dream had to back off: the storyteller's adaptation hears about it
        /// <summary>
        /// The storyteller's learner (FearCore). Its instinct gives every option a first impression from what the option
        /// is made of (its kinds of fear, its slot, the words in it), so a combination nobody has tried still has an
        /// informed guess; each option's own record takes over as it builds. One compartment per domain: the dream,
        /// the monster's look, the hallucinations.
        /// </summary>
        public FearCore.Learner brain;
        public string lastLine;                      // the manuscript's first line the next morning

        readonly FearSave save = new FearSave();
        readonly Dictionary<string, OptStat> stats = new Dictionary<string, OptStat>();
        readonly List<KeyValuePair<float, float>> buf = new List<KeyValuePair<float, float>>();
        readonly System.Random rng = new System.Random();
        Change change;
        float nextAt, lastCredit, beat = 4.5f, lastTick = -1f, hotFor, easeUntil = -1f;
        int eases;
        string leader;
        int cap = 1, day = 1;

        static string SavePath { get { return Path.Combine(Application.persistentDataPath, "nightpages-fear.json"); } }

        public int Dreams { get { return save.dreams; } }

        public FearEngine()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(SavePath), save);
                    foreach (var s in save.stats) stats[s.key] = s;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] Couldn't read the fear profile (" + e.Message + "); starting fresh."); }
        }

        void Say(string s) { if (log != null) log(s); }

        OptStat Stat(int slot, int opt)
        {
            string key = Slots[slot].name + ":" + Slots[slot].options[opt].label;
            OptStat s;
            if (!stats.TryGetValue(key, out s))
            {
                s = new OptStat { key = key };
                stats[key] = s;
                save.stats.Add(s);
            }
            return s;
        }

        public float Mean(int slot, int opt) { return Stat(slot, opt).mean; }

        OptStat Fam(string id)
        {
            string key = "kind of fear:" + id;
            OptStat s;
            if (!stats.TryGetValue(key, out s))
            {
                s = new OptStat { key = key };
                stats[key] = s;
                save.stats.Add(s);
            }
            return s;
        }

        /// <summary>How much an option's kinds of fear have worked on this player so far (0 if it has none).</summary>
        float FamilyPrior(FearOption o)
        {
            if (o.families == null || o.families.Length == 0) return 0f;
            float sum = 0f;
            foreach (var f in o.families) sum += Fam(f).mean;
            return sum / o.families.Length;
        }

        /// <summary>Every kind of fear with its learned score and evidence, best first.</summary>
        public List<KeyValuePair<FearTypes.Family, OptStat>> Families()
        {
            var list = new List<KeyValuePair<FearTypes.Family, OptStat>>();
            foreach (var f in FearTypes.All) list.Add(new KeyValuePair<FearTypes.Family, OptStat>(f, Fam(f.id)));
            list.Sort((a, b) => b.Value.mean.CompareTo(a.Value.mean));
            return list;
        }

        /// <summary>The kind of fear that has worked best on this player, once there's some evidence for it; null before.</summary>
        public string TopFamily()
        {
            string best = null;
            float bestMean = 0.05f;
            foreach (var f in FearTypes.All)
            {
                var st = Fam(f.id);
                if (st.n >= 2f && st.mean > bestMean) { bestMean = st.mean; best = f.id; }
            }
            return best;
        }

        public bool Easing(float now) { return now < easeUntil; }

        float Gauss()
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }

        // Thompson sampling: draw a plausible score for each allowed option and take the best draw.
        // Options it knows little about get wide draws, so they still get tried.
        int Sample(int slot, int exclude)
        {
            int best = -1;
            float bestDraw = float.NegativeInfinity;
            var opts = Slots[slot].options;
            for (int i = 0; i < opts.Length; i++)
            {
                if (i == exclude || !Allowed(slot, i)) continue;
                var s = Stat(slot, i);
                // Until an option has evidence of its own, it borrows a first impression: what its kinds of fear have
                // scored, and what the instinct makes of everything it is made of.
                const float k = 1.5f;
                float prior = FamilyPrior(opts[i]);
                if (brain != null) prior = 0.5f * (prior + brain.instinct.Value(Domain(slot), Tags(slot, i)));
                float guess = (s.n * s.mean + k * prior) / (s.n + k);
                float draw = guess + Gauss() * 0.45f / Mathf.Sqrt(1f + s.n);
                if (draw > bestDraw) { bestDraw = draw; best = i; }
            }
            return best;
        }

        static readonly Dictionary<int, string[]> tagCache = new Dictionary<int, string[]>();
        static readonly HashSet<string> Stop = new HashSet<string> { "with", "that", "your", "from", "like", "into", "over", "then", "than", "when", "only", "once", "there", "nothing", "more", "kind" };

        public static string Domain(int slot) { return slot == Hallucination ? "hallucination" : IsLook(slot) ? "look" : "dream"; }

        /// <summary>What an option is made of, for the instinct: its kinds of fear, its slot, the words in its name.</summary>
        public static string[] Tags(int slot, int opt)
        {
            int key = slot * 64 + opt;
            string[] t;
            if (tagCache.TryGetValue(key, out t)) return t;
            var o = Slots[slot].options[opt];
            var list = new List<string> { "slot:" + Slots[slot].name };
            if (o.families != null) list.AddRange(o.families);
            foreach (var w in o.label.ToLowerInvariant().Split(' ', ',', '\'', '.'))
                if (w.Length >= 4 && !Stop.Contains(w)) list.Add(w.TrimEnd('s'));
            t = list.ToArray();
            tagCache[key] = t;
            return t;
        }

        bool Allowed(int slot, int opt)
        {
            if (Slots[slot].options[opt].intensity > cap) return false;
            if (Banned.Contains(Slots[slot].options[opt].label)) return false;
            if (day < 2 && slot == Behavior && opt != 0) return false;      // the first night, it isn't here yet
            return true;
        }

        public void BeginDream(int newDay, float now)
        {
            day = newDay;
            cap = day <= 4 ? 1 : 2;
            hotFor = 0f; easeUntil = -1f; eases = 0; lastTick = -1f;
            beat = day <= 1 ? 7f : 4.5f;
            buf.Clear();
            change = null;
            nextAt = now + 7f;
            lastCredit = now;
            for (int s = 0; s < Slots.Length; s++) current[s] = 0;
            current[Space] = Mathf.Max(0, Sample(Space, -1));
            if (day > 1)
            {
                // Start from what has worked on this player before, for about half the dream's parts.
                for (int s = 1; s < Slots.Length; s++)
                {
                    if (!InDream(s)) continue;
                    if (!IsLook(s) && rng.NextDouble() < 0.5) continue;
                    int k = Sample(s, -1);
                    if (k > 0 && Stat(s, k).n >= 1f && Stat(s, k).mean > 0.05f) current[s] = k;
                }
                // From the third night it is always here in some form.
                if (day >= 3 && current[Behavior] == 0) current[Behavior] = Mathf.Max(1, Sample(Behavior, 0));
            }
            Say("dream " + (save.dreams + 1) + " (day " + day + ", intensity up to " + cap + "): starts as " + Describe());
        }

        /// <summary>The monster's five traits as the engine currently has them.</summary>
        public int[] Look()
        {
            var look = new int[LookSlots.Length];
            for (int i = 0; i < LookSlots.Length; i++) look[i] = current[LookSlots[i]];
            return look;
        }

        /// <summary>The version of the monster that has unsettled this player most: the one that walks the house.</summary>
        public int[] BestLook()
        {
            var look = new int[LookSlots.Length];
            for (int k = 0; k < LookSlots.Length; k++)
            {
                int s = LookSlots[k];
                float best = 0.05f;
                for (int i = 1; i < Slots[s].options.Length; i++)
                {
                    var st = Stat(s, i);
                    if (st.n >= 1f && st.mean > best) { best = st.mean; look[k] = i; }
                }
            }
            return look;
        }

        int[] lastComposed;

        /// <summary>
        /// Compose a monster for this player: each gene drawn by Thompson sampling from what has frightened them
        /// (borrowing from the kinds of fear it belongs to when a gene has no evidence yet), so it leans hard on
        /// what works and still tries combinations nobody has seen. Never the same thing twice in a row.
        /// </summary>
        public int[] Compose(int maxIntensity)
        {
            int keepCap = cap;
            cap = maxIntensity;
            int[] look = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                look = new int[LookSlots.Length];
                for (int k = 0; k < LookSlots.Length; k++)
                {
                    int s = LookSlots[k];
                    int o = Sample(s, -1);
                    look[k] = o < 0 ? 0 : o;
                }
                if (lastComposed == null || !System.Linq.Enumerable.SequenceEqual(look, lastComposed)) break;
            }
            cap = keepCap;
            lastComposed = look;
            return look;
        }

        /// <summary>Thompson-sample one option of a slot (for things chosen while awake, like the hallucinations).</summary>
        public int Choose(int slot, int maxIntensity, int exclude = -1)
        {
            int keepCap = cap;
            cap = maxIntensity;
            int o = Sample(slot, exclude);
            cap = keepCap;
            return o;
        }

        /// <summary>Learn from something tried while awake: y is the change in the player's reaction it caused.</summary>
        public void Credit(int slot, int opt, float y)
        {
            y = Mathf.Clamp(y, -1f, 1f);
            var st = Stat(slot, opt);
            st.n += 1f;
            st.mean += (y - st.mean) / Mathf.Clamp(st.n, 1f, 8f);
            var o = Slots[slot].options[opt];
            if (o.families != null)
                foreach (var f in o.families)
                {
                    var fs = Fam(f);
                    fs.n += 0.5f;
                    fs.mean += (y - fs.mean) / Mathf.Clamp(fs.n, 1f, 15f);
                }
        }

        /// <summary>Save now (the waking hours learn too).</summary>
        public void SaveNow()
        {
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(save, true)); }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] Couldn't save the fear profile: " + e.Message); }
        }

        public string DescribeLook(int[] look)
        {
            var parts = new List<string>();
            for (int i = 0; i < look.Length && i < LookSlots.Length; i++)
                if (look[i] != 0 || i < 7) parts.Add(Slots[LookSlots[i]].options[look[i]].label);
            return string.Join(", ", parts.ToArray());
        }

        public string Describe()
        {
            var parts = new List<string>();
            for (int s = 0; s < Slots.Length; s++)
                if (s == Space || (current[s] != 0 && (!IsLook(s) || current[Behavior] >= 2))) parts.Add(Slots[s].options[current[s]].label);
            return string.Join(" / ", parts.ToArray());
        }

        float Avg(float t0, float t1)
        {
            float sum = 0f;
            int n = 0;
            foreach (var p in buf)
                if (p.Key >= t0 && p.Key <= t1) { sum += p.Value; n++; }
            return n == 0 ? 0f : sum / n;
        }

        /// <summary>
        /// Call every frame of the dream. reaction is -1 (amused, bored) to +1 (unsettled). monsterSeen says
        /// whether the player can see the monster right now: a change to its look is only judged from the
        /// moment they next lay eyes on it.
        /// </summary>
        public void Tick(float now, float reaction, bool watching, bool monsterSeen)
        {
            buf.Add(new KeyValuePair<float, float>(now, reaction));
            while (buf.Count > 0 && buf[0].Key < now - 8f) buf.RemoveAt(0);
            float dt = lastTick < 0f ? 0f : Mathf.Clamp(now - lastTick, 0f, 0.5f);
            lastTick = now;

            // The sweet spot. In a haunted-house study, enjoyment rose with fear up to a point and then fell,
            // and long, large swings in heart rate tended to be followed by feeling bad. So the engine finds
            // what frightens you, but doesn't hold you at the top: after HotSeconds of being very frightened
            // it takes away the most intense thing on screen and changes nothing for EaseSeconds.
            if (watching && reaction > HotLevel) hotFor += dt;
            else hotFor = Mathf.Max(0f, hotFor - dt * 0.5f);
            if (hotFor > HotSeconds && now >= easeUntil) EaseOff(now);
            if (now < easeUntil) return;

            // Slow credit: whatever is on screen while the player is unsettled gets a little of it.
            if (now - lastCredit >= 1f)
            {
                lastCredit = now;
                if (watching)
                    for (int s = 0; s < Slots.Length; s++)
                    {
                        if (!InDream(s) || (IsLook(s) && !monsterSeen)) continue;
                        var st = Stat(s, current[s]);
                        st.mean += 0.01f * (reaction - st.mean);
                        st.n += 0.01f;
                    }
            }

            if (change != null)
            {
                if (change.gated && !change.seen)
                {
                    if (monsterSeen)
                    {
                        change.seen = true;
                        change.t = now;
                        change.before = Avg(now - 1.5f, now);
                    }
                    else if (now > change.made + 18f)
                    {
                        Say("the player never looked at it after '" + Slots[change.slot].options[change.to].label + "'; keeping it, nothing learned");
                        change = null;
                        nextAt = now + beat * 0.5f;
                    }
                    return;
                }
                if (!watching) change.unwatched = true;
                if (now >= change.t + 3f) Judge(now);
            }
            else if (now >= nextAt) Mutate(now);
        }

        void EaseOff(float now)
        {
            hotFor = 0f;
            eases++;
            easeUntil = now + EaseSeconds;
            nextAt = easeUntil + beat * 0.5f;
            if (change != null) { Say("dropped the test of '" + Slots[change.slot].options[change.to].label + "' to ease off"); change = null; }
            int worst = -1, worstLevel = 0;
            for (int s = 1; s < Slots.Length; s++)
            {
                int lvl = Slots[s].options[current[s]].intensity;
                if (lvl > worstLevel && (canChange == null || canChange(s))) { worstLevel = lvl; worst = s; }
            }
            string what = "";
            if (worst >= 0)
            {
                what = "; took away '" + Slots[worst].options[current[worst]].label + "'";
                current[worst] = 0;
                if (onApply != null) onApply(worst, 0);
            }
            if (eases >= 3 && cap > 0)
            {
                cap--;
                what += "; that's the third time tonight, so the most intense options are off for the rest of this dream";
            }
            Say("EASING OFF: the player has been very frightened for " + HotSeconds + " s. Enjoyment of horror peaks at moderate fear, so nothing changes for " + EaseSeconds + " s" + what);
            if (onEase != null) onEase("very frightened in the dream for " + HotSeconds + " s");
        }

        void Mutate(float now)
        {
            var slots = new List<int>();
            for (int s = 0; s < Slots.Length; s++)
            {
                if (!InDream(s) || Sample(s, current[s]) < 0) continue;
                if (IsLook(s) && current[Behavior] < 2) continue;     // nothing to look at yet
                if (canChange != null && !canChange(s)) continue;
                slots.Add(s);
                if (s != Space) { slots.Add(s); slots.Add(s); }     // a whole new space is a big cut: try it less often
            }
            if (slots.Count == 0) { nextAt = now + beat; return; }
            int slot = slots[rng.Next(slots.Count)];
            int to = Sample(slot, current[slot]);
            change = new Change { slot = slot, from = current[slot], to = to, t = now, made = now, before = Avg(now - 1.5f, now), gated = IsLook(slot) };
            current[slot] = to;
            Say("trying " + Slots[slot].name + ": " + Slots[slot].options[change.from].label + " -> " + Slots[slot].options[to].label);
            if (onApply != null) onApply(slot, to);
        }

        void Judge(float now)
        {
            var c = change;
            change = null;
            var opt = Slots[c.slot].options[c.to];
            if (c.unwatched)
            {
                Say("couldn't read the player during '" + opt.label + "'; nothing learned");
                nextAt = now + beat * 0.6f;
                return;
            }
            float after = Avg(c.t + 0.6f, c.t + 3f);
            float y = Mathf.Clamp(after - c.before, -1f, 1f);     // only the change counts, so nothing rides on what was already working
            if (brain != null) brain.instinct.Learn(Domain(c.slot), Tags(c.slot, c.to), y);
            var st = Stat(c.slot, c.to);
            st.n += 1f;
            st.mean += (y - st.mean) / Mathf.Clamp(st.n, 1f, 8f);
            if (opt.families != null)
                foreach (var f in opt.families)
                {
                    var fs = Fam(f);
                    fs.n += 1f;
                    fs.mean += (y - fs.mean) / Mathf.Clamp(fs.n, 1f, 15f);
                }
            string top = TopFamily();
            if (top != null && top != leader)
            {
                leader = top;
                Say("LEARNING: the kind of fear that works best on this player so far is " + FearTypes.Name(top) +
                    " (score " + Fam(top).mean.ToString("+0.00;-0.00") + " over " + Fam(top).n.ToString("0") + " tests)");
            }
            bool keep = y > 0.06f || c.slot == Space;
            if (keep)
            {
                Say("KEPT '" + opt.label + "': reaction " + y.ToString("+0.00;-0.00") + " (learned score " + st.mean.ToString("+0.00;-0.00") + ")");
                nextAt = now + beat * 1.3f;
            }
            else
            {
                Say("undid '" + opt.label + "': reaction " + y.ToString("+0.00;-0.00") + " (learned score " + st.mean.ToString("+0.00;-0.00") + ")");
                current[c.slot] = c.from;
                if (onApply != null) onApply(c.slot, c.from);
                nextAt = now + beat * 0.6f;
            }
        }

        /// <summary>The option that has unsettled this player most so far, or slot -1 if nothing has yet.</summary>
        public void Best(out int slot, out int opt)
        {
            slot = -1; opt = -1;
            float best = 0.05f;
            for (int s = 0; s < Slots.Length; s++)
                for (int i = 0; i < Slots[s].options.Length; i++)
                {
                    var o = Slots[s].options[i];
                    var st = Stat(s, i);
                    if (o.line == null || st.n < 1f || st.mean <= best) continue;
                    best = st.mean; slot = s; opt = i;
                }
        }

        /// <summary>The option in one slot that has worked best on this player (option 0 if nothing has yet).</summary>
        public int BestIn(int slot, int maxIntensity)
        {
            int pick = 0;
            float best = 0.05f;
            for (int i = 0; i < Slots[slot].options.Length; i++)
            {
                var st = Stat(slot, i);
                if (Slots[slot].options[i].intensity > maxIntensity || st.n < 1f || st.mean <= best) continue;
                best = st.mean; pick = i;
            }
            return pick;
        }

        public void EndDream()
        {
            change = null;
            save.dreams++;
            // Tomorrow's chapter opens with the part of tonight's dream that landed hardest.
            lastLine = null;
            float best = float.NegativeInfinity;
            for (int s = 0; s < Slots.Length; s++)
            {
                var o = Slots[s].options[current[s]];
                if (o.line == null) continue;
                float m = Stat(s, current[s]).mean;
                if (m > best) { best = m; lastLine = o.line; }
            }
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(save, true)); }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] Couldn't save the fear profile: " + e.Message); }
            Say("dream over; fear profile saved (" + save.stats.Count + " options scored, " + save.dreams + " dreams)");
        }
    }
}
