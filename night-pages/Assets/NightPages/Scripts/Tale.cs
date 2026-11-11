using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The storyteller's story: a different ghost story every run, put together from parts that fit.
    ///
    /// The run's Truth (Killer, Keepers, House) decides how it ends. The Tale decides everything the player lives
    /// through on the way: WHO the house remembers, WHAT happened to them, WHY they're still here, HOW they show
    /// themselves (the signs: the house's grounded, deniable hauntings), what of theirs is still lying about (the
    /// keepsake), the RULES the grandparents wrote down for living with it, and the odd scraps that don't explain
    /// anything (the best ghost stories always have a few).
    ///
    /// Parts carry tags; a part can need tags (it only makes sense if those are already in the story) and ban them
    /// (it contradicts something already there). Picking goes in order (who, what, why, signs, keepsake, rules,
    /// scraps), each from the parts that fit what's been chosen, weighted, and nudged by the fear learner toward
    /// the kinds of thing that have frightened this player before. So a child's ghost gets small quick footsteps and
    /// a shoe under the bed; a lodger who lived in the walls gets food going missing and a bag of rubbish that isn't
    /// yours, and never gets a "doesn't know it's dead" (he isn't).
    ///
    /// Where the parts come from (see the research notes in the repository): what people most often report about
    /// their own homes (a presence felt, 42%; footsteps and voices, 37%; figures, 19%), poltergeist case studies
    /// (small objects moved, 64%; raps, 48%; raps that answer, 16%), sleep paralysis (the intruder in the corner, the
    /// weight on the chest), first-hand accounts on forums (a dead father's walk heard downstairs; a grandmother's
    /// plates broken on her birthday; eggs heard rolling and breaking that never moved; someone peeking round the
    /// door who wasn't home; the bulb on the landing that's always out; "scarier explanations", like a drifter
    /// living in the attic), and internet horror's own grammar (start mundane, one small wrongness, a pattern,
    /// confirmation; rules lists; never explain it all). The rest is made up to sit between them.
    /// </summary>
    public class Tale
    {
        public class Piece
        {
            public string slot, id;
            public string[] tags = new string[0], needs = new string[0], bans = new string[0];
            public Truth[] truths;                   // null: any truth
            public float w = 1f;
            public readonly Dictionary<string, string> v = new Dictionary<string, string>();
            public string[] lines = new string[0];   // what it contributes in words (clues, pages, signs' descriptions)
            public string sign;                      // signs: the haunting it is (Phenomena id)
            public string[] props = new string[0];   // things of theirs for rooms to hold (model ids)

            public Piece T(params string[] t) { tags = t; return this; }
            public Piece Needs(params string[] t) { needs = t; return this; }
            public Piece Bans(params string[] t) { bans = t; return this; }
            public Piece Only(params Truth[] t) { truths = t; return this; }
            public Piece W(float x) { w = x; return this; }
            public Piece V(params string[] kv) { for (int i = 0; i + 1 < kv.Length; i += 2) v[kv[i]] = kv[i + 1]; return this; }
            public Piece L(params string[] l) { lines = l; return this; }
            public Piece S(string s) { sign = s; return this; }
            public Piece Props(params string[] p) { props = p; return this; }
        }

        public Truth truth;
        public Piece who, what, why, keepsake;
        public readonly List<Piece> signs = new List<Piece>(), rules = new List<Piece>(), scraps = new List<Piece>();
        public readonly HashSet<string> tags = new HashSet<string>();
        readonly Dictionary<string, string> vars = new Dictionary<string, string>();
        System.Random rng;

        // ---------------------------------------------------------------- telling

        public static Tale Tell(Truth truth, System.Random rng, FearCore.Learner brain, System.Action<string> log)
        {
            var t = new Tale { truth = truth, rng = rng };
            t.who = t.Pick("who", brain);
            t.what = t.Pick("what", brain);
            t.why = t.Pick("why", brain);
            int nSigns = 4 + rng.Next(2);
            for (int i = 0; i < nSigns; i++) { var s = t.Pick("sign", brain); if (s != null) t.signs.Add(s); }
            t.keepsake = t.Pick("keepsake", brain);
            for (int i = 0; i < 3; i++) { var r = t.Pick("rule", brain); if (r != null) t.rules.Add(r); }
            for (int i = 0; i < 2; i++) { var r = t.Pick("scrap", brain); if (r != null) t.scraps.Add(r); }
            t.vars["year"] = (1952 + rng.Next(30)).ToString();
            t.vars["season"] = new[] { "winter", "the autumn", "that summer", "the spring" }[rng.Next(4)];
            if (log != null) log("tale: " + t.Summary);
            return t;
        }

        Piece Pick(string slot, FearCore.Learner brain)
        {
            var options = new List<Piece>();
            var weights = new List<float>();
            float total = 0f;
            foreach (var p in Library)
            {
                if (p.slot != slot || Chosen(p) || !Fits(p)) continue;
                // The learner's nudge: kinds of thing that have frightened this player before come up more.
                float learned = brain != null ? Mathf.Clamp(brain.Expect("story", p.id, p.tags), -0.5f, 1f) : 0f;
                float w = p.w * (1f + learned);
                if (w <= 0f) continue;
                options.Add(p); weights.Add(w); total += w;
            }
            if (options.Count == 0) return null;
            double r = rng.NextDouble() * total;
            Piece pick = options[options.Count - 1];
            for (int i = 0; i < options.Count; i++) { r -= weights[i]; if (r <= 0) { pick = options[i]; break; } }
            foreach (var tg in pick.tags) tags.Add(tg);
            if (pick.sign != null) tags.Add("sign:" + pick.sign);
            foreach (var kv in pick.v) if (!vars.ContainsKey(kv.Key)) vars[kv.Key] = kv.Value;
            return pick;
        }

        bool Chosen(Piece p) { return p == who || p == what || p == why || p == keepsake || signs.Contains(p) || rules.Contains(p) || scraps.Contains(p); }

        bool Fits(Piece p)
        {
            if (p.truths != null && System.Array.IndexOf(p.truths, truth) < 0) return false;
            foreach (var n in p.needs) if (!Need(n)) return false;
            foreach (var b in p.bans) if (tags.Contains(b)) return false;
            // A sign that is already in the story (by another part) isn't picked twice.
            if (p.sign != null && tags.Contains("sign:" + p.sign)) return false;
            return true;
        }

        // "a|b" means either will do.
        bool Need(string n)
        {
            if (n.IndexOf('|') < 0) return tags.Contains(n);
            foreach (var o in n.Split('|')) if (tags.Contains(o)) return true;
            return false;
        }

        // ---------------------------------------------------------------- words

        /// <summary>Fill a line's blanks ({name}, {he}, {His}, {keepsake}...) from the story.</summary>
        public string Fill(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder();
            int i = 0;
            while (i < s.Length)
            {
                int a = s.IndexOf('{', i);
                if (a < 0) { sb.Append(s, i, s.Length - i); break; }
                int b = s.IndexOf('}', a);
                if (b < 0) { sb.Append(s, i, s.Length - i); break; }
                sb.Append(s, i, a - i);
                string key = s.Substring(a + 1, b - a - 1);
                bool cap = key.Length > 0 && char.IsUpper(key[0]);
                string val;
                if (!vars.TryGetValue(key.ToLowerInvariant(), out val)) val = key;
                if (cap && val.Length > 0) val = char.ToUpperInvariant(val[0]) + val.Substring(1);
                sb.Append(val);
                i = b + 1;
            }
            return sb.ToString();
        }

        public string Var(string key, string fallback = "") { string v; return vars.TryGetValue(key, out v) ? v : fallback; }

        public string Summary
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append(who != null ? who.id : "?").Append(" / ").Append(what != null ? what.id : "?").Append(" / ").Append(why != null ? why.id : "?");
                sb.Append("; signs: ");
                for (int i = 0; i < signs.Count; i++) sb.Append(i > 0 ? ", " : "").Append(signs[i].sign);
                sb.Append("; keepsake: ").Append(keepsake != null ? keepsake.id : "none");
                sb.Append("; rules ").Append(rules.Count).Append(", scraps ").Append(scraps.Count);
                sb.Append(" (truth ").Append(truth).Append("; tags ").Append(string.Join(",", new List<string>(tags).ToArray())).Append(")");
                return sb.ToString();
            }
        }

        /// <summary>Is this haunting one of the story's signs?</summary>
        public bool HasSign(string id) { foreach (var s in signs) if (s.sign == id) return true; return false; }

        /// <summary>The words for one of the story's signs (how the log and the pages describe it), or null.</summary>
        public string SignLine(string id) { foreach (var s in signs) if (s.sign == id && s.lines.Length > 0) return Fill(s.lines[0]); return null; }

        /// <summary>Things that left out in the house or the dream belong to the story (model ids).</summary>
        public List<string> Props()
        {
            var l = new List<string>();
            foreach (var p in new[] { who, what, keepsake }) if (p != null) l.AddRange(p.props);
            foreach (var s in signs) l.AddRange(s.props);
            return l;
        }

        /// <summary>What the dream should make of the rooms: "sickroom", "nursery", "hoard", "burnt", "flooded", "kept", "hurried".</summary>
        public string Mood
        {
            get
            {
                if (tags.Contains("illness")) return "sickroom";
                if (tags.Contains("child")) return "nursery";
                if (tags.Contains("hoard")) return "hoard";
                if (tags.Contains("fire")) return "burnt";
                if (tags.Contains("water")) return "flooded";
                if (tags.Contains("hurried")) return "hurried";
                return "kept";
            }
        }

        /// <summary>The notes left about the house for you to find, in the order you'll find them: the story's, then the truth's own.</summary>
        public List<string> Clues()
        {
            var l = new List<string>();
            if (what != null && what.lines.Length > 0) l.Add(Fill(what.lines[0]));
            if (rules.Count > 0) l.Add(RulesNote());
            if (keepsake != null && keepsake.lines.Length > 0) l.Add(Fill(keepsake.lines[0]));
            if (why != null && why.lines.Length > 0) l.Add(Fill(why.lines[0]));
            foreach (var s in scraps) if (s.lines.Length > 0) l.Add(Fill(s.lines[0]));
            return l;
        }

        /// <summary>The grandparents' list of rules, as a note: "If you hear her slippers in the hall after ten..."</summary>
        public string RulesNote()
        {
            var sb = new StringBuilder();
            sb.Append(Fill(rng.Next(2) == 0 ? "A card in Grandma's handwriting, tucked behind the {room} clock. Numbered, like a recipe.\n\n" : "Typed on Grandpa's machine, folded in four. No title. Numbered.\n\n"));
            for (int i = 0; i < rules.Count; i++) sb.Append(i + 1).Append(". ").Append(Fill(rules[i].lines[0])).Append("\n");
            sb.Append(Fill(rng.Next(2) == 0 ? "\nThe last line has been written over so many times the paper has worn through." : "\nUnderneath, in pencil, in a hand like yours as a boy: \"what happens if we don't\""));
            return sb.ToString();
        }

        /// <summary>Pages for the dream maze: the story told in pieces, in the dreamer's own voice.</summary>
        public List<string> Pages()
        {
            var l = new List<string>();
            foreach (var p in new[] { who, what, why })
                if (p != null) for (int i = 1; i < p.lines.Length; i++) l.Add(Fill(p.lines[i]));
            foreach (var s in signs) if (s.lines.Length > 1) l.Add(Fill(s.lines[1]));
            if (keepsake != null && keepsake.lines.Length > 1) l.Add(Fill(keepsake.lines[1]));
            foreach (var s in scraps) if (s.lines.Length > 1) l.Add(Fill(s.lines[1]));
            return l;
        }

        // ---------------------------------------------------------------- the parts

        static List<Piece> library;
        static List<Piece> Library { get { if (library == null) library = Build(); return library; } }

        static Piece P(string slot, string id) { var p = new Piece { slot = slot, id = id }; return p; }

        static List<Piece> Build()
        {
            const Truth K = Truth.Killer, Ke = Truth.Keepers, H = Truth.House;
            var L = new List<Piece>();

            // ======== WHO the house remembers
            L.Add(P("who", "grandma").T("adult", "old", "woman", "family", "grandparent").W(1.4f)
                .V("name", "Grandma", "rel", "your grandmother", "he", "she", "his", "her", "him", "her", "hers", "hers", "walk", "slippers",
                   "smell", "lavender talc", "voice", "\"...is that you, love?\"", "room", "kitchen", "hands", "her cold dry hands")
                .L("", "Grandma always knew where I was in the house. She said she could hear me think. I never asked her what she heard.")
                .Props("round_spectacles", "wicker_basket_02", "tea_set_01"));
            L.Add(P("who", "grandpa").T("adult", "old", "man", "family", "grandparent").W(1.4f)
                .V("name", "Grandpa", "rel", "your grandfather", "he", "he", "his", "his", "him", "him", "hers", "his", "walk", "stick",
                   "smell", "pipe smoke", "voice", "a cough, the one he did before he said your name", "room", "study", "hands", "his big careful hands")
                .L("", "Grandpa walked with a stick the last ten years. You could hear him coming from anywhere in the house: the scuff, the stick. Scuff, stick.")
                .Props("pocket_watch", "vintage_lighter", "fishermans_hat", "vintage_crutches_01"));
            L.Add(P("who", "mother").T("adult", "woman", "family", "parent").Only(Ke, H)
                .V("name", "Mother", "rel", "your mother", "he", "she", "his", "her", "him", "her", "hers", "hers", "walk", "heels",
                   "smell", "her perfume, the one in the blue bottle", "voice", "humming, the song with no words", "room", "bedroom", "hands", "her thin hands")
                .L("", "I don't remember her face. I remember the sound of her shoes on the hall boards, going away, very fast, then stopping.")
                .Props("ceramic_vase_01", "standing_picture_frame_02"));
            L.Add(P("who", "father").T("adult", "man", "family", "parent").Only(Ke, H)
                .V("name", "Father", "rel", "your father", "he", "he", "his", "his", "him", "him", "hers", "his", "walk", "boots",
                   "smell", "engine oil and cold air", "voice", "your name, the short way he said it", "room", "hall", "hands", "his cracked hands")
                .L("", "Father had a way of walking: heavy on the heel, a pause at the top of every step as if listening. Grandma said I walk the same way now.")
                .Props("rubber_boots", "metal_tool_chest", "wooden_axe"));
            L.Add(P("who", "lodger").T("adult", "man", "stranger", "lodger").W(0.9f)
                .V("name", "Mr Aldous", "rel", "the lodger", "he", "he", "his", "his", "him", "him", "hers", "his", "walk", "socks",
                   "smell", "old sweat and mints", "voice", "a dry little laugh through a wall", "room", "cellar", "hands", "his long yellow fingers")
                .L("", "They took a lodger one winter for the money. Grandma never said his name after. Grandpa took his door off its hinges and burned it.")
                .Props("vintage_suitcase", "trashbag", "russian_food_cans_01"));
            L.Add(P("who", "girl").T("adult", "young", "woman", "stranger", "town").Only(K, H)
                .V("name", "June Harrow", "rel", "the girl from town", "he", "she", "his", "her", "him", "her", "hers", "hers", "walk", "bare",
                   "smell", "rain on a coat", "voice", "a girl laughing, then not", "room", "hall", "hands", "her wet hands")
                .L("", "They still ask in town about June Harrow. Her mother leaves the porch light on. I drive the long way so I don't have to see it.")
                .Props("vintage_flashlight", "postcard_set_01"));
            L.Add(P("who", "brother").T("child", "family", "boy").W(0.9f)
                .V("name", "Tom", "rel", "your little brother", "he", "he", "his", "his", "him", "him", "hers", "his", "walk", "small",
                   "smell", "warm milk and crayons", "voice", "a small boy counting to ten", "room", "bedroom", "hands", "his sticky little hands")
                .L("", "Grandma kept two of everything in the toy box. I asked once who the second one was for. She shut the box on my fingers, not hard, and didn't answer.")
                .Props("rubber_duck_toy", "Ukulele_01"));
            L.Add(P("who", "before").T("child", "stranger", "girl", "old").Only(Ke, H)
                .V("name", "the girl in the photographs", "rel", "the child before you", "he", "she", "his", "her", "him", "her", "hers", "hers", "walk", "small",
                   "smell", "chalk dust and cold", "voice", "a little girl asking to come in", "room", "bedroom", "hands", "her small cold hands")
                .L("", "There's a girl in the old photographs, always at the edge, always looking at the camera. No one in the family can say who she is. She's in the ones from 1931 and the ones from 1964, the same age.")
                .Props("rubber_duck_toy", "standing_picture_frame_01"));
            L.Add(P("who", "nurse").T("adult", "woman", "stranger", "carer").Only(K, Ke)
                .V("name", "Nurse Pell", "rel", "the nurse who came for Grandma", "he", "she", "his", "her", "him", "her", "hers", "hers", "walk", "shoes",
                   "smell", "carbolic soap", "voice", "\"time for your tablets\"", "room", "bedroom", "hands", "her scrubbed red hands")
                .L("", "The last year, a nurse came every day at four. Nurse Pell. Grandma hid from her in the pantry once, and I helped.")
                .Props("bleach_bottle", "multi_cleaner_bottle", "wheelchair_01"));

            // ======== WHAT happened to them
            L.Add(P("what", "longdeath").T("illness", "slow", "bed").Needs("adult").Bans("child")
                .L("A notebook of times, in a careful hand.\n\n\"2.10 water. 3.40 turned. 4.00 tablets. 5.15 asked for {name}.\"\n\nThe last page just says the time. Nothing after it.",
                   "{Name} died slowly in the back bedroom, all one winter. At the end {he} talked to someone in the corner of the room who wasn't there. I'm in that room now. I don't look at the corner.")
                .Props("wheelchair_01", "bleach_bottle"));
            L.Add(P("what", "stairsfall").T("fall", "sudden", "stairs").Bans("lodger")
                .L("A torn envelope. On the back, in pencil, a little drawing of the cellar stairs, and an X on the fourth step from the top.\n\nSomeone has gone over the X again and again until the pencil went through.",
                   "{Name} fell on the cellar stairs. That's what they said. I was in the house. I remember the sound, a long one, and then the house being very quiet for a very long time.")
                .Props("wooden_ladder"));
            L.Add(P("what", "walkedout").T("gone", "woods", "cold", "hurried").Bans("illness")
                .L("A police form, carbon copy, the blue gone grey.\n\n\"Last seen walking toward the tree line, {season}, {year}. Not dressed for the weather. Did not take a light.\"",
                   "{Name} walked out into the trees one night in {season} and nobody saw {him} again. The search went on for six days. On the seventh, Grandpa told them to stop, and they asked him how he knew.")
                .Props("rubber_boots", "vintage_flashlight"));
            L.Add(P("what", "underfloor").T("buried", "cellar", "secret").Only(K).Bans("child")
                .L("A builder's receipt.\n\n\"Concrete, 6 bags. Delivered. Paid cash.\"\n\nThe date is two days after {name} stopped coming to the door.",
                   "I helped mix the concrete. I was proud of how level it came out. I don't know why that's the thing I remember, and not why.")
                .Props("rusted_spade_01", "cardboard_box_01"));
            L.Add(P("what", "kept").T("kept", "secret", "cellar").Only(Ke)
                .L("A list in Grandpa's hand, headed only with the day of the week.\n\n\"Bread. Milk. The good blanket. Do not go down after dark.\"",
                   "They kept {name} in the house after. Not the body. Whatever comes after the body. They kept it fed. They called it looking after.")
                .Props("wooden_bowl_02", "jug_01"));
            L.Add(P("what", "inwalls").T("hoard", "hidden", "alive", "walls").Needs("lodger|stranger").Bans("child", "kept")
                .L("A shopping list that isn't Grandma's, in pencil, on the back of one of her recipe cards:\n\n\"bread. tins. batteries. the boy's sweets.\"",
                   "For three months food went missing. Grandma blamed me. Then she found the hatch behind the airing cupboard, and the nest of blankets, and the jar he'd been using, and the little row of my school photographs, cut out of the album.")
                .Props("trashbag", "russian_food_cans_01", "can_rusted"));
            L.Add(P("what", "fire").T("fire", "sudden", "kitchen").Bans("water")
                .L("A newspaper photograph: the back of this house, the kitchen window black, a fireman carrying a chair out like a person.\n\nThe caption gives the house the wrong number.",
                   "There was a fire in the kitchen the year I was born. Grandma painted over the black, three coats. It comes through every summer, just a shadow, the shape of someone leaning on the stove.")
                .Props("pot_enamel_01"));
            L.Add(P("what", "drowned").T("water", "sudden", "outside").Bans("fire")
                .L("A swimming certificate, \"25 YARDS\", with {name}'s name on it.\n\nIt's been dried flat after being very wet. The ink has run down the page like rain on a window.",
                   "{Name} went into the stock pond after the dog. The dog came back. In the dream the floors are wet and I know whose water it is.")
                .Props("wooden_bucket_01", "watering_can_metal_01"));
            L.Add(P("what", "nothing").T("nothing", "house").Only(H).W(1.2f)
                .L("A list of everyone who has died in the house, in Grandma's hand. It's blank.\n\nUnderneath: \"That's what's wrong with it.\"",
                   "Nothing happened to {name}. That's what I can't get past. Nothing happened, and {he} still hasn't left.")
                .Props());

            // ======== WHY they stay
            L.Add(P("why", "notknow").T("confused").Bans("alive", "nothing")
                .L("A note on the fridge, in {name}'s hand, years old and still being moved about: \"Back soon. Don't lock the door.\"",
                   "{He} doesn't know. {He} still sets two places. {He} still waits up. I think {he}'s waiting up for me, and I think I'm late."));
            L.Add(P("why", "waiting").T("waiting", "longing")
                .L("A birthday card, unopened, addressed to you, in {name}'s handwriting. The stamp is from the year you left.",
                   "{Name} is waiting for someone to come home. I used to think it was me. Then I came home, and {he} kept waiting."));
            L.Add(P("why", "befound").T("found", "secret").Needs("buried|gone|kept|hidden")
                .L("A rough map of the house, the cellar shaded in. Someone has written HERE and then crossed it out, then written it again.",
                   "{He} wants to be found. That's all. Every sign is a finger pointing. I keep looking at the finger."));
            L.Add(P("why", "confess").T("guilt").Only(K)
                .L("A sheet torn from a notepad. Just one word, over and over, in your handwriting, getting smaller: \"sorry sorry sorry sorry\"",
                   "It doesn't want revenge. Revenge would be easier. It wants me to say it, out loud, in the room where it happened."));
            L.Add(P("why", "hungry").T("hunger", "kept").Only(Ke, H)
                .L("The pantry inventory, kept every week for thirty years. The last entries are all for things nobody in the house would eat.",
                   "It's hungry. They fed it. Now they're gone and there's only me, and I can hear it in the walls, being patient."));
            L.Add(P("why", "mine").T("possessive", "jealous").Bans("nothing")
                .L("Grandma's handwriting on the back of a photograph of you: \"{Name} doesn't like it when he has visitors.\"",
                   "{Name} won't share the house. Not with the nurse, not with the lodger, not with me. Every guest left early. I'm not a guest. I don't get to leave."));
            L.Add(P("why", "never").T("notperson").Only(H)
                .L("Grandpa's ledger, the last page. \"Whatever it is, it learned to be {name} by watching. It's still learning. Don't let it watch you.\"",
                   "It isn't {name}. It never was. It's what the house learned from watching {him}, and it does {him} wrong, a little, every time."));

            // ======== HOW it shows: the signs (each is a grounded haunting in Phenomena)
            L.Add(P("sign", "s-walk").S("walk").T("sound").W(1.4f)
                .L("{his} walk in the hall, the {walk} way {he} walked",
                   "Last night, {his} walk in the hall. Not like footsteps, like {him}: the {walk}, the stop at the door, the waiting. I lay there and counted the seconds. I got to four hundred and something before it went away."));
            L.Add(P("sign", "s-smell").S("smell").T("smell").W(1.1f)
                .L("{smell}, out of nowhere",
                   "{Smell}. Just for a breath, at the turn of the stairs. Nobody in this house has smelt of that in years."));
            L.Add(P("sign", "s-plates").S("plates").T("sound", "objects").Needs("family|carer")
                .L("plates breaking in the kitchen",
                   "The good plates. A whole stack, in the cupboard, all at once, like a dropped tray. When I opened the cupboard they were all there, unbroken. Except the one on top. That one was in pieces, and it was {hers}."));
            L.Add(P("sign", "s-eggs").S("eggs").T("sound", "objects").W(0.8f)
                .L("something rolling off the worktop and breaking",
                   "The rolling sound again, across the worktop, and the soft crack of it landing. Twice now. Nothing is ever on the floor."));
            L.Add(P("sign", "s-peek").S("peek").T("sight", "peripheral").W(1.2f)
                .L("someone looking round the door while your back was turned",
                   "Someone looked in at me while I was writing. I saw {him} in the edge of my eye, at the door, the way you'd look in on a child asleep. When I turned round I heard the stairs. We don't have stairs."));
            L.Add(P("sign", "s-presence").S("presence").T("presence").W(1.5f)
                .L("the feeling of someone in the room with you",
                   "You know the feeling when someone's in the room and you haven't turned round yet. I've had it since Tuesday. I haven't turned round yet."));
            L.Add(P("sign", "s-answer").S("answer").T("sound", "communication").W(1.1f)
                .L("knocking that answers",
                   "I knocked on the wall. Once, to see. Something knocked back once. I knocked twice. It knocked twice. I stopped, and it kept going, three, four, five, as if it was me that had to catch up."));
            L.Add(P("sign", "s-rap").S("rap").T("sound").W(1.1f)
                .L("raps from inside the wardrobe",
                   "The wardrobe knocks at night. Not loudly. The way you'd knock if you didn't want to wake the house but you had to be let out."));
            L.Add(P("sign", "s-moved").S("moved").T("objects").W(1.4f)
                .L("{his} things moved while you weren't looking",
                   "I put the {keepsake} in the drawer. In the morning it was out on the table again, where {he} always kept it. I've stopped putting it away."));
            L.Add(P("sign", "s-facing").S("facing").T("objects", "uncanny")
                .L("photographs turned to face you",
                   "The photographs turn. Not all at once. One a day. By the end of the week every face in the house was looking at the chair I sit in."));
            L.Add(P("sign", "s-name").S("name").T("voice")
                .L("{voice}, from another room",
                   "{Voice}. From the kitchen, clear as a bell. I said \"yes?\" before I could stop myself. I think that was a mistake. I think now it knows I'll answer."));
            L.Add(P("sign", "s-bulb").S("bulb").T("light")
                .L("the bulb that always goes when you come near",
                   "The bulb at the end of the hall is out again. I changed it on Monday. I changed it on Wednesday. Grandpa had a box of forty in the cellar. I understand the box now."));
            L.Add(P("sign", "s-food").S("food").T("objects", "alive").Needs("hoard|lodger|alive|hunger")
                .L("food going missing, and rubbish that isn't yours",
                   "Bread gone. Half a tin of peaches, the lid pressed back on. A bag of rubbish by the back door, tied with a knot I don't tie."));
            L.Add(P("sign", "s-clock").S("clock").T("time", "uncanny").W(0.9f)
                .L("time you can't account for",
                   "Twenty minutes, gone. I was in the hall, and then I was in the hall, and the clock had moved on, and my hands were cold, as if I'd been outside."));
            L.Add(P("sign", "s-radio").S("radio").T("sound", "voice").W(0.8f)
                .L("the radio in the study tuning itself",
                   "The radio finds the same station every night, between two others, where there's nothing but a voice reading numbers. The dial is taped down. I taped it."));
            L.Add(P("sign", "s-gap").S("gap").T("sight", "doors").W(1.3f)
                .L("someone standing behind a door as it opens",
                   "I've started opening doors slowly. Through the gap, for just a moment, there's always someone standing in the middle of the room, facing the door. By the time it's open, there isn't."));
            L.Add(P("sign", "s-bottom").S("bottom").T("sight", "figure").W(0.8f)
                .L("a figure at the far end of the hall",
                   "There's a shape at the end of the hall at night, where the light doesn't reach. It's the right height. It's {his} height."));
            L.Add(P("sign", "s-cold").S("cold").T("presence", "cold").W(0.9f)
                .L("a patch of cold that moves",
                   "There's a cold place in the house. It isn't always in the same room. It's where {he} would be, if {he} were still doing the things {he} used to do. Right now it's on the landing. It's coming down."));
            L.Add(P("sign", "s-slam").S("slamback").T("sound", "doors").W(0.9f)
                .L("doors slammed back at you",
                   "I lost my temper and slammed the kitchen door. A moment later, upstairs, someone slammed one back. We have no upstairs."));

            // ======== the keepsake (what of theirs is still lying about)
            L.Add(P("keepsake", "k-slippers").Needs("old|woman").V("keepsake", "slippers")
                .L("A pair of worn slippers under the hall table, toes pointing at the stairs. The insides are warm.",
                   "{His} slippers are under the bed, toes out, the way {he} stepped out of them. Every morning they're nearer the door."));
            L.Add(P("keepsake", "k-pipe").Needs("man").V("keepsake", "pipe")
                .L("A pipe, cold, on the mantel. Packed, as if for later. There's a tooth mark on the stem that isn't from teeth.",
                   "{His} pipe is on the mantel and the house smells of it, fresh, every evening at nine."));
            L.Add(P("keepsake", "k-shoe").Needs("child").V("keepsake", "shoe")
                .L("One small shoe, a child's, buckled, under the kitchen table. No one has worn it in a very long time. The buckle is done up.",
                   "A shoe. Just the one. I've looked for the other everywhere. I think it's still on someone."));
            L.Add(P("keepsake", "k-ring").Needs("adult").Bans("young").V("keepsake", "ring")
                .L("A wedding ring in a saucer of milk in the pantry. The milk is fresh. The ring is {hers}.",
                   "{His} ring turns up wherever I'm going to be. On the pillow. In the cup. Waiting."));
            L.Add(P("keepsake", "k-brush").Needs("woman").Bans("young", "child").V("keepsake", "hairbrush")
                .L("A hairbrush on the dresser, full of long grey hair. Someone has been using it. The hair is still coming.",
                   "{His} hairbrush is full again. I cleaned it out on Sunday."));
            L.Add(P("keepsake", "k-key").V("keepsake", "key on a string").W(0.8f)
                .L("A key on a loop of dirty string, the kind you wear round your neck. It doesn't fit any door in the house.\n\nIt's warm.",
                   "The key on the string fits nothing. I've tried every door. Tonight I'll try the ones that aren't there in the day."));
            L.Add(P("keepsake", "k-tins").Needs("hoard|alive|lodger").V("keepsake", "tin of peaches")
                .L("A tin of peaches, opened with a knife, eaten with fingers. Under the sofa. The date stamp is this week.",
                   "There are tins under every bed now. I don't buy tins."));

            // ======== the rules (each only alongside the sign it's about)
            L.Add(P("rule", "r-walk").Needs("sign:walk").L("If you hear {his} {walk} in the hall after ten, stay in bed. It goes away. Don't open the door to see."));
            L.Add(P("rule", "r-name").Needs("sign:name").L("Don't answer when {he} calls. It isn't asking where you are. It's checking."));
            L.Add(P("rule", "r-answer").Needs("sign:answer").L("Never knock back. Never. Not even once, to see."));
            L.Add(P("rule", "r-rap").Needs("sign:rap").L("The wardrobe stays shut at night. If it knocks, say \"not tonight\" and nothing else."));
            L.Add(P("rule", "r-gap").Needs("sign:gap").L("Open doors slowly. If someone is in the room when the gap opens, close it again and count to twenty. Then it's empty."));
            L.Add(P("rule", "r-bulb").Needs("sign:bulb").L("Change the hall bulb every morning whether it's gone or not. Don't stand under it when you do."));
            L.Add(P("rule", "r-food").Needs("sign:food").L("Leave a plate out. Bread and something sweet. If the plate is clean in the morning, that's good. If the plate is gone, lock your door."));
            L.Add(P("rule", "r-moved").Needs("sign:moved").L("If {his} {keepsake} is out, leave it out. Don't put it back. Putting it back is how it knows you noticed."));
            L.Add(P("rule", "r-slam").Needs("sign:slamback|doors").L("Don't slam doors. It thinks it's being called."));
            L.Add(P("rule", "r-presence").Needs("sign:presence|sign:cold").L("If you feel it in the room, don't turn round. Finish what you were doing and leave at your normal speed."));
            L.Add(P("rule", "r-clock").Needs("sign:clock").L("Wind the hall clock every day. When it stops, you lose time. We don't know where it goes."));
            L.Add(P("rule", "r-cellar").Needs("cellar").L("The cellar door stays locked. It is not keeping anyone out."));
            L.Add(P("rule", "r-dream").W(0.7f).L("If you dream of the house with more rooms in it, don't read everything you find. Follow the ticking."));

            // ======== scraps: things that don't explain anything (research and invention, side by side)
            L.Add(P("scrap", "x-dog").L("A dog's lead, still on its hook by the back door. Grandma's note pinned to it: \"Bramble wouldn't go down the hall. Not once in eleven years. We should have listened to the dog.\"",
                "We had a dog when I was small. Bramble. He'd go anywhere in the house but the hall. He'd sit at the end of it and whine at the floor."));
            L.Add(P("scrap", "x-landing").L("A builder's estimate, never accepted: \"Joists under the hall floor rotten through. Not safe to stand on. How have you been walking on this?\"",
                "The builder said the hall floor should have given way years ago. Something has been holding it up."));
            L.Add(P("scrap", "x-photos").L("A school photograph of you, aged six. Someone has carefully cut out the face and glued it back in upside down.",
                "Someone has been in the albums. My face is cut out of every photograph between the ages of six and nine. The holes are neat. Grandma's scissors were neat."));
            L.Add(P("scrap", "x-tape").L("A cassette, labelled \"Bedroom, 3am, Nov\". On the shelf beside it, the player, the tape wound to the end.\n\nThe last eleven minutes are someone breathing very close to the microphone.",
                "Grandpa recorded the bedroom at night for a month. I found the tapes. He'd labelled one \"breathing\" and one \"breathing (not hers)\"."));
            L.Add(P("scrap", "x-chair").L("Grandma's knitting, abandoned mid-row. The pattern is a child's jumper, very small. The sleeves are far too long.",
                "She knitted for someone the whole time I knew her. Jumpers with long, long sleeves. None of them ever fitted anyone."));
            L.Add(P("scrap", "x-window").L("A drawing in thick pencil: this house, from the yard. Every window is drawn. One more than there are.",
                "From the yard the house has one more window than it has from inside. Tonight the light was on in it."));
            L.Add(P("scrap", "x-wait").L("A notepad by the phone, Grandma's shorthand: \"Voice on the line again. Just one word. WAIT. Grandpa says it saved his life once, on the Halsey road.\"",
                "Something on the phone line said WAIT to Grandpa once, and he braked, and the lorry went through the red. He said it was the kindest thing in the house. He said it didn't do that twice."));
            L.Add(P("scrap", "x-mirror").L("A handwritten sign, taped to the back of the hall mirror: \"Don't count the people in it.\"",
                "Don't count the people in the mirror. I counted. Grandpa was right."));
            L.Add(P("scrap", "x-paralysis").L("A doctor's note: \"Sleep paralysis. Common. Harmless. The figure in the corner is the brain's best guess.\"\n\nGrandma has underlined \"guess\" twice.",
                "The doctor says the figure in the corner of the room is just the brain guessing. It guesses the same thing every night. It's getting better at it."));
            L.Add(P("scrap", "x-attic").L("Grandpa's ledger: \"Attic hatch nailed shut, March. Nails pulled out from the inside, April. Nailed again. Nails gone, May. Leave it.\"",
                "There's no attic in this house. There's a hatch. Grandpa nailed it shut four times."));


            // ======== more parts (second batch: research and invention side by side)

            // WHO
            L.Add(P("who", "twin").T("child", "family", "boy", "secret").Only(Ke, H).W(0.8f)
                .V("name", "your brother", "rel", "the twin you were never told about", "he", "he", "his", "his", "him", "him", "hers", "his", "walk", "small",
                   "smell", "the inside of a cupboard", "voice", "your own voice, younger, from the next room", "room", "bedroom", "hands", "hands exactly like yours")
                .L("", "There's a second birth certificate in Grandma's bureau. Same date. Same parents. One word on it that isn't on mine.")
                .Props("rubber_duck_toy", "standing_picture_frame_01"));
            L.Add(P("who", "dog").T("animal", "family", "small").W(0.6f)
                .V("name", "Bramble", "rel", "the dog", "he", "he", "his", "his", "him", "him", "hers", "his", "walk", "claws",
                   "smell", "wet dog and woodsmoke", "voice", "a dog whining at a closed door", "room", "hall", "hands", "his muddy paws")
                .L("", "Bramble slept across my door every night I lived here. Something still does. I can hear it settle, and sigh, and the boards take its weight.")
                .Props("wooden_bowl_01", "wicker_basket_01"));
            L.Add(P("who", "builder").T("adult", "man", "stranger", "old", "house").Only(H).W(0.9f)
                .V("name", "Elias Cole", "rel", "the man who built the house", "he", "he", "his", "his", "him", "him", "hers", "his", "walk", "boots",
                   "smell", "sawdust and lamp oil", "voice", "a man counting under his breath, measuring", "room", "cellar", "hands", "his split-nailed hands")
                .L("", "The man who built the house in 1911 kept adding to it after he sold it. The new owners would find a room they hadn't had. He was dead by then. It didn't stop.")
                .Props("metal_tool_chest", "wooden_ladder", "hatchet"));
            L.Add(P("who", "doctor").T("adult", "man", "stranger", "carer").Only(K, Ke).W(0.8f)
                .V("name", "Dr Ames", "rel", "the doctor who signed the certificates", "he", "he", "his", "his", "him", "him", "hers", "his", "walk", "shoes",
                   "smell", "ether and peppermint", "voice", "\"and how are we sleeping?\"", "room", "bedroom", "hands", "his cold, quick hands")
                .L("", "Dr Ames signed both certificates the same morning, without coming upstairs. Grandpa paid him in cash, at the kitchen table, and he didn't count it.")
                .Props("magnifying_glass_01", "round_spectacles", "bleach_bottle"));

            // WHAT happened
            L.Add(P("what", "fever").T("illness", "slow", "bed").Needs("child").W(1.2f)
                .L("A thermometer in a glass case, the mercury stuck at the top. On the label, in Grandma's hand: \"the last one\".",
                   "{Name} had a fever that wouldn't break, the winter the roads closed. They kept {him} cool with wet cloths for nine days. I was told to play quietly. I played very quietly."));
            L.Add(P("what", "ice").T("water", "sudden", "outside", "cold").Bans("fire", "illness")
                .L("A pair of skates, the laces knotted together, hung on a nail in the cellar. One blade is bent nearly double.",
                   "The stock pond froze that year, and {name} went out on it, and the ice held, and held, and then it didn't. In the dream I can hear it still: the long creak first, under everything."));
            L.Add(P("what", "lockedin").T("trapped", "cellar", "secret").Bans("child", "lodger").W(0.9f)
                .L("The cellar door, photographed close. Scratches on the inside, low down, and a name carved under them.\n\nOn the back of the photograph: \"Three days. We thought {he} had gone to town.\"",
                   "They locked the cellar for the winter not knowing {name} was down there. Three days. The house is very quiet in winter. You'd think someone would have heard."));
            L.Add(P("what", "road").T("sudden", "outside", "car").Needs("adult").Bans("water", "fire").W(0.7f)
                .L("A police report about the Halsey road, the bend by the elms. \"Driver not found. Car still running when we arrived. Door open. Headlights on, pointing at the house.\"",
                   "{Name} went off the Halsey road at the bend. They found the car with the engine running and the lights pointed at this house. They never found {him}. The lights, I think, are still on."));
            L.Add(P("what", "quarrel").T("guilt", "sudden", "stairs").Only(K, Ke).Bans("child")
                .L("A torn-off sheet: two sets of handwriting, arguing, one getting bigger and wilder. The last line is just a long scrawl down the page, as if the pen was knocked.",
                   "There was a row. I remember shouting, and a door, and the top of the stairs. Then I remember the hall clock, very loud, and nobody else saying anything ever again."));

            // WHY they stay
            L.Add(P("why", "residual").T("repeating", "notperson").Bans("alive").W(1.1f)
                .L("Grandpa's notebook: \"Same time every night, 11.52. Same route: bed, hall, kitchen, back door. It doesn't see us. It's not a ghost. It's a recording. The house is playing it back.\"",
                   "It doesn't know I'm here. It isn't looking for anyone. It's the last day, playing over and over in the walls like a record with the needle stuck, and I keep walking into it."));
            L.Add(P("why", "guarding").T("protective", "secret").Bans("hunger")
                .L("A note in a child's writing, slid under a door: \"Don't go in the cellar. {Name} says.\"",
                   "{He} isn't trying to scare me. {He}'s standing in front of something. Every time I get close to the cellar door, {he}'s there first."));
            L.Add(P("why", "invited").T("invited", "notperson").Only(H, Ke).W(0.9f)
                .L("A board game, home-made: letters burned into a breadboard, YES and NO in the corners. A glass, upside down, in the middle.\n\nUnder the glass, a child's tooth.",
                   "We asked it in. That's the part nobody tells you: you have to ask. We were children. We thought it was a game. It said its name was {name}. It wasn't."));

            // HOW it shows
            L.Add(P("sign", "s-prints").S("prints").T("sight", "water").W(1f)
                .L("wet footprints across the hall that dry as you watch",
                   "Wet footprints in the hall, bare, coming from the back door and going into my room. They were drying as I looked at them. They stopped by my bed. There's no water anywhere on the way."));

            L.Add(P("keepsake", "k-skates").Needs("water|cold").V("keepsake", "skates")
                .L("A pair of child's skates under the hall table, the blades wet. There's ice in the laces.",
                   "The skates are by the door again, dripping. I put them in the cellar. I put them in the bin. I put them in the car and drove them to the dump."));
            L.Add(P("keepsake", "k-collar").Needs("animal").V("keepsake", "collar")
                .L("A dog's collar on the hall table, the tag worn smooth by years of rubbing. It's still warm, like it's just been taken off.",
                   "The collar turns up by my bed in the mornings, the buckle still done up, as if it slipped off something very thin."));

            L.Add(P("rule", "r-prints").Needs("sign:prints").L("If there are wet prints in the hall, don't follow them. Don't step in them. Wait in the kitchen until they've dried."));
            L.Add(P("rule", "r-residual").Needs("repeating").L("At 11.52 be in bed. Whatever walks the hall then won't see you if you're where you're meant to be."));
            L.Add(P("rule", "r-guard").Needs("protective").L("If {he}'s standing at the cellar door, go back to bed. {He}'s not there for you. {He}'s there for what's under it."));

            L.Add(P("scrap", "x-tape2").L("Grandma's recipe card for bread. On the back, in the same pencil: \"It ate the whole loaf again. Put out two.\"",
                "Grandma baked two loaves every Sunday. We never ate the second one. In the morning there was never a second one."));
            L.Add(P("scrap", "x-wallpaper").L("A sample book of wallpapers with one page cut out. Pencilled on the stub: \"the one that moves\".",
                "There's one wallpaper in this house that moves when you're not looking straight at it. I won't say which room. You'll know."));
            L.Add(P("scrap", "x-clocks").L("An inventory of clocks in the house, eleven of them. Ten are ticked off. Beside the eleventh: \"don't wind\".",
                "There are eleven clocks in this house. Ten of them I can find."));

            return L;
        }
    }
}
