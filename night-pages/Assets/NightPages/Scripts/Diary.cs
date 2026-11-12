using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NightPages
{
    /// <summary>Who comes to the house this run (character creation).</summary>
    [System.Serializable]
    public class Character
    {
        public string name = "";
        public int address = 2;        // 0 she, 1 he, 2 they
        public int skin = 2;           // index into SkinTones
        public int clothes = 0;        // index into Clothes
        public int coat = 0;           // index into Coats

        public static readonly string[] AddressNames = { "she / her", "he / him", "they / them" };
        public static readonly string[] SkinNames = { "very fair", "fair", "light brown", "brown", "dark brown", "very dark" };
        public static readonly Color[] SkinTones =
        {
            new Color(0.94f, 0.82f, 0.74f), new Color(0.88f, 0.72f, 0.6f), new Color(0.76f, 0.58f, 0.44f),
            new Color(0.6f, 0.42f, 0.3f), new Color(0.43f, 0.29f, 0.2f), new Color(0.3f, 0.2f, 0.14f)
        };
        public static readonly string[] ClothesNames = { "an old grey jumper", "a green cardigan", "a blue work shirt", "a black sweatshirt" };
        public static readonly Color[] ClothesColours =
        {
            new Color(0.32f, 0.31f, 0.3f), new Color(0.22f, 0.3f, 0.22f), new Color(0.24f, 0.32f, 0.45f), new Color(0.09f, 0.09f, 0.1f)
        };
        public static readonly string[] CoatNames = { "navy wool", "brown waxed cotton", "dark green", "black", "camel" };
        public static readonly Color[] CoatColours =
        {
            new Color(0.16f, 0.18f, 0.27f), new Color(0.33f, 0.26f, 0.17f), new Color(0.17f, 0.24f, 0.18f), new Color(0.08f, 0.08f, 0.09f), new Color(0.6f, 0.48f, 0.32f)
        };
        public static readonly string[] SuggestNames =
        {
            "Alex", "Sam", "Jo", "Robin", "Morgan", "Ellis", "Rowan", "Casey", "Frances", "Leslie",
            "Marian", "Hollis", "Jude", "Avery", "Noel", "Billie", "Kit", "Lee", "Dana", "Shay"
        };

        public string Name { get { return string.IsNullOrEmpty(name.Trim()) ? "you" : name.Trim(); } }
        /// <summary>"as a boy", "as a girl", "as a child": how the family remembers you small.</summary>
        public string Young { get { return address == 0 ? "as a girl" : address == 1 ? "as a boy" : "as a child"; } }
        public Character Copy() { return (Character)MemberwiseClone(); }
    }

    /// <summary>
    /// The morning diary: the run saved each time you wake, so Continue puts you back at that morning. Hardcore keeps
    /// one diary and burns it when a dream is lost (as losing a dream already sends you back to day one).
    /// </summary>
    [System.Serializable]
    public class Diary
    {
        [System.Serializable] public class TaleSave { public string who, what, why, explain, act, keepsake, year, season; public List<string> signs = new List<string>(), rules = new List<string>(), scraps = new List<string>(); }
        [System.Serializable] public class Line { public int day, passage, option; }

        public int version = 1;
        public string saved;
        public int calendar, runDays, truth, houseSeed, layout, wearSeed, rewriteSeed;
        public string job;
        public int difficulty;
        public float grip;
        public int clues, cluesSpawned, tagFace, tagAway, tagBlame, dreamDeaths;
        public bool cellarOpened;
        public int rungOpen, rungShown;
        public int[] rungDay = new int[6];
        public List<string> found = new List<string>(), deduced = new List<string>(), deducedWrong = new List<string>();
        public Character who = new Character();
        public TaleSave tale = new TaleSave();
        public List<TaleSave> older = new List<TaleSave>();
        public List<Line> written = new List<Line>();

        static string PathOf { get { return System.IO.Path.Combine(Application.persistentDataPath, "nightpages-diary.json"); } }

        public static bool Exists { get { try { return File.Exists(PathOf); } catch { return false; } } }

        public static Diary Read()
        {
            try { return File.Exists(PathOf) ? JsonUtility.FromJson<Diary>(File.ReadAllText(PathOf)) : null; }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] the diary couldn't be read: " + e.Message); return null; }
        }

        public void Write()
        {
            saved = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            try { File.WriteAllText(PathOf, JsonUtility.ToJson(this, false)); }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't write the diary: " + e.Message); }
        }

        public static void Burn()
        {
            try { if (File.Exists(PathOf)) File.Delete(PathOf); } catch { }
        }

        /// <summary>One line for the title's Continue: "Day 4 of 7. You work as a transcriber."</summary>
        public string Summary
        {
            get { return "Day " + calendar + " of " + runDays + ". " + (who != null && who.name.Trim().Length > 0 ? who.name.Trim() + ", " : "") + "working as " + Job.NameOf(job).ToLowerInvariant() + "."; }
        }

        public static TaleSave Of(Tale t)
        {
            var s = new TaleSave();
            if (t == null) return s;
            s.who = Id(t.who); s.what = Id(t.what); s.why = Id(t.why); s.explain = Id(t.explain); s.act = Id(t.act); s.keepsake = Id(t.keepsake);
            s.year = t.Var("year", ""); s.season = t.Var("season", "");
            foreach (var p in t.signs) s.signs.Add(p.id);
            foreach (var p in t.rules) s.rules.Add(p.id);
            foreach (var p in t.scraps) s.scraps.Add(p.id);
            return s;
        }

        static string Id(Tale.Piece p) { return p != null ? p.id : null; }
    }
}
