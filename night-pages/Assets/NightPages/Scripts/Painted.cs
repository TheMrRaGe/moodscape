using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The pictures the Moodscape painter made for the game (the "paint" job: pc-painter/paint_library.py), in
    /// Resources/NPPainted/&lt;kind&gt;/. Kinds: portraits, photos, paintings, writing, drawings, stains, wallpapers,
    /// faces, flesh. Everything that uses them falls back to what it did before when there are none.
    /// </summary>
    public static class Painted
    {
        static readonly Dictionary<string, Texture2D[]> kinds = new Dictionary<string, Texture2D[]>();
        static Shader decal;

        public static void ResetStatics() { kinds.Clear(); decal = null; tags.Clear(); hung.Clear(); loadedTags = false; }

        // ------------------------------------------------------------ what each picture is of, and what it does to you

        // From the painter's library.json: picture name ("portraits/pale_woman_2") -> its tags (its kind and motifs).
        static readonly Dictionary<string, string[]> tags = new Dictionary<string, string[]>();
        static bool loadedTags;

        static void LoadTags()
        {
            if (loadedTags) return;
            loadedTags = true;
            var ta = Resources.Load<TextAsset>("NPPainted/library");
            if (ta == null) return;
            try
            {
                var lib = JsonUtility.FromJson<Library>(ta.text);
                foreach (var p in lib.pictures)
                {
                    string key = p.file.Replace(".jpg", "");
                    tags[key] = p.tags != null && p.tags.Length > 0 ? p.tags : new[] { p.kind };
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't read the painter's library.json: " + e.Message); }
        }

        [System.Serializable] class Library { public Entry[] pictures; }
        [System.Serializable] class Entry { public string kind, name, file; public string[] tags; }

        /// <summary>The tags of a painted picture (its kind and what's in it), or just its kind.</summary>
        public static string[] TagsOf(string kind, Texture2D tex)
        {
            LoadTags();
            string[] t;
            return tex != null && tags.TryGetValue(kind + "/" + tex.name, out t) ? t : new[] { kind };
        }

        /// <summary>
        /// The picture of a kind that the fear engine expects to get to this player most (with its uncertainty mixed
        /// in, so it keeps trying others), or a random one if there's no learner.
        /// </summary>
        public static Texture2D Choose(string kind, FearCore.Learner brain, System.Random r = null)
        {
            var all = All(kind);
            if (all.Length == 0) return null;
            if (brain == null) return Pick(kind, r);
            Texture2D best = null;
            float bestV = float.MinValue;
            // A handful of candidates, not all of them: the walls shouldn't all hang the same favourite.
            for (int i = 0; i < 6; i++)
            {
                var tex = all[r != null ? r.Next(all.Length) : Random.Range(0, all.Length)];
                var tg = TagsOf(kind, tex);
                float v = brain.Draw("picture", kind + "/" + tex.name, tg);
                if (System.Array.IndexOf(tg, "learned") >= 0) v += 0.15f;      // painted for this player from what got to them
                if (v > bestV) { bestV = v; best = tex; }
            }
            return best;
        }

        // The pictures on the walls right now, so the game can tell which one you're looking at.
        class Hung { public Renderer r; public string kind, id; public string[] tags; public float looked, startedAt, before, peak; public bool judged; }
        static readonly List<Hung> hung = new List<Hung>();
        static Hung watching;

        /// <summary>Hang a painted picture on this renderer, and keep an eye on how you react to it.</summary>
        public static void Hang(Renderer r, string kind, Texture2D tex)
        {
            if (r == null || tex == null) return;
            hung.RemoveAll(h => h.r == null);
            hung.Add(new Hung { r = r, kind = kind, id = kind + "/" + tex.name, tags = TagsOf(kind, tex) });
        }

        /// <summary>
        /// Every frame (from Habits.Tick): if you've been looking at a picture for a moment, close up, watch how you
        /// react over the next few seconds and tell the learner. Looking long at faces is a habit of its own.
        /// </summary>
        public static void Watch(GameDirector g)
        {
            var cam = g.player.cam;
            if (watching != null)
            {
                if (watching.r == null) { watching = null; return; }
                watching.peak = Mathf.Max(watching.peak, Reactions.Level);
                if (Time.time - watching.startedAt > 3.5f && !watching.judged)
                {
                    watching.judged = true;
                    float y = Mathf.Clamp(watching.peak - watching.before, -1f, 1f);
                    var brain = g.fear != null ? g.fear.brain : null;
                    if (brain != null) g.Log("painter's pictures: you looked at " + watching.id + " [" + string.Join(", ", watching.tags) + "]: " + brain.Learn("picture", watching.id, watching.tags, y));
                    watching = null;
                }
                return;
            }
            Hung best = null;
            float bestA = 9f;
            foreach (var h in hung)
            {
                if (h.r == null || !h.r.isVisible || h.judged) continue;
                Vector3 c = h.r.bounds.center, to = c - cam.transform.position;
                if (to.magnitude > 4.5f) continue;
                float a = Vector3.Angle(cam.transform.forward, to);
                if (a < bestA && Vis.Sees(cam, c)) { bestA = a; best = h; }
            }
            if (best == null) return;
            best.looked += Time.deltaTime;
            if (best.looked > 0.6f)
            {
                watching = best;
                best.startedAt = Time.time;
                best.before = Reactions.Level;
                best.peak = best.before;
                if (best.kind == "portraits" || best.kind == "faces") Habits.Use(g, "stares at pictures", 0.5f);
            }
        }

        /// <summary>
        /// What the painter should paint more of, for this player: every motif the library knows, with what the
        /// learner now expects it to do to them. Written next to the game's other saved memory as
        /// np_paint_wishes.json; the "paint" job reads it and paints new pictures from the strongest motifs.
        /// </summary>
        public static void WriteWishes(FearCore.Learner brain)
        {
            if (brain == null) return;
            LoadTags();
            var motifs = new HashSet<string>();
            foreach (var kv in tags) foreach (var m in kv.Value) motifs.Add(m);
            if (motifs.Count == 0) return;
            var sb = new System.Text.StringBuilder("{\n  \"motifs\": {");
            bool first = true;
            foreach (var m in motifs)
            {
                float v = brain.Expect("picture", "motif:" + m, new[] { m });
                sb.Append(first ? "\n" : ",\n").Append("    \"").Append(m).Append("\": ").Append(v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                first = false;
            }
            sb.Append("\n  }\n}\n");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "np_paint_wishes.json"), sb.ToString()); }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't write the painter's wishes: " + e.Message); }
        }

        public static Texture2D[] All(string kind)
        {
            Texture2D[] t;
            if (!kinds.TryGetValue(kind, out t))
            {
                t = Resources.LoadAll<Texture2D>("NPPainted/" + kind);
                kinds[kind] = t;
            }
            return t;
        }

        public static int Count(string kind) { return All(kind).Length; }

        /// <summary>One picture of a kind, or null if the painter hasn't made any.</summary>
        public static Texture2D Pick(string kind, System.Random r = null)
        {
            var t = All(kind);
            if (t.Length == 0) return null;
            return t[r != null ? r.Next(t.Length) : Random.Range(0, t.Length)];
        }

        /// <summary>A material that marks the wall it's on (multiplied in; see NPDecal.shader), or null.</summary>
        public static Material Decal(Texture2D tex, float strength = 1f, float lift = 0.25f)
        {
            if (tex == null) return null;
            if (decal == null) decal = Shader.Find("NightPages/Decal");
            if (decal == null) return null;
            var m = new Material(decal) { mainTexture = tex };
            m.SetFloat("_Strength", strength);
            m.SetFloat("_Lift", lift);
            return m;
        }

        /// <summary>An ordinary lit material showing a painted picture (a portrait, a photo, a drawing on paper).</summary>
        public static Material Picture(Texture2D tex, Color tint)
        {
            if (tex == null) return null;
            var m = Build.Mat(tint, 0f, 0.15f);
            m.mainTexture = tex;
            return m;
        }
    }
}
