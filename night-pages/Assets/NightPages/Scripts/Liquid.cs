using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Liquid out of where it belongs: poured, knocked over or let go of when something broke. It lands on whatever
    /// is under it and becomes a puddle that spreads with how much there is, shines while it's wet, dries slowly
    /// (water to nothing; coffee, tea, wine and mud to a stain) and can be wiped up with a cloth or a sponge.
    /// Walking through one wets your soles and you leave prints for a few steps; coming in from the grass leaves mud.
    /// </summary>
    public static class Liquid
    {
        public static void ResetStatics() { Puddle.All.Clear(); spills = 0; solesKind = null; solesLeft = 0; stream = null; streamFrame = -1; prints = 0; muddyChore = false; }
        static int spills;
        public static int prints;
        static bool muddyChore;

        public static Color ColorOf(string kind)
        {
            switch (kind)
            {
                case "coffee": return new Color(0.32f, 0.18f, 0.08f);
                case "tea": return new Color(0.5f, 0.3f, 0.12f);
                case "wine": return new Color(0.4f, 0.06f, 0.1f);
                case "milk": return new Color(0.95f, 0.94f, 0.9f);
                case "mud": return new Color(0.3f, 0.22f, 0.13f);
                case "egg": return new Color(0.95f, 0.75f, 0.2f);
                case "dishwater": return new Color(0.55f, 0.52f, 0.45f);
                case "pond water": return new Color(0.3f, 0.34f, 0.2f);
                case "whisky": return new Color(0.6f, 0.38f, 0.12f);
                case "blood": return new Color(0.32f, 0.02f, 0.03f);
                default: return new Color(0.55f, 0.57f, 0.6f);          // water: shows only as a darkening and a shine
            }
        }

        /// <summary>The colour it darkens what it's on: water shows as a darker, cooler patch, not as its own colour.</summary>
        public static Color MarkOf(string kind) { return kind == "water" ? new Color(0.42f, 0.4f, 0.38f) : ColorOf(kind); }

        /// <summary>Does it leave a mark when it dries?</summary>
        public static bool Stains(string kind) { return kind != "water"; }

        /// <summary>Liquid let go of at `from`: it falls onto whatever is below and pools there.</summary>
        public static Puddle Spill(Vector3 from, float litres, string kind) { return Spill(from, litres, kind, null, null); }
        /// <summary>Spilt out of `source` (which mustn't catch its own spill).</summary>
        public static Puddle Spill(Vector3 from, float litres, string kind, Transform source) { ignore = source; try { return Spill(from, litres, kind, null, null); } finally { ignore = null; } }
        static Transform ignore;

        /// <summary>As Spill, with the wording for the list (a haunting's mess reads differently from your own).</summary>
        public static Puddle Spill(Vector3 from, float litres, string kind, string choreText, string doneLine)
        {
            if (litres <= 0.0005f) return null;
            Vector3 at; Vector3 n; Transform on;
            if (!Below(from, out at, out n, out on)) return null;
            // Into a sink, a bath or a basin: it just goes down the drain.
            foreach (var b in Basin.All) if (b != null && b.Contains(at + Vector3.up * 0.02f)) return null;
            foreach (var p in Puddle.All)
                if (p != null && p.kind == kind && Mathf.Abs(p.transform.position.y - at.y) < 0.05f && Vis.Level(p.transform.position - at).magnitude < p.Radius * 0.8f)
                {
                    p.Add(litres);
                    return p;
                }
            var nu = Puddle.Make(at, n, on, litres, kind);
            if (litres > 0.03f) Sfx.At("splash", at, Mathf.Clamp01(0.25f + litres), 0.9f + Random.value * 0.25f);
            // A spill big enough to need wiping goes on the list.
            var g = GameDirector.I;
            if (g != null && litres >= 0.04f && kind != "water" && g.chores != null && g.house != null)
            {
                // One job per liquid per room: a bottle that rolls and dribbles makes several pools, not several chores.
                var room = g.house.RoomAt(at);
                string rn = room != null ? room.name : "";
                var house = g.house;
                System.Func<Puddle, bool> mine = q => q != null && q.kind == kind && house.RoomAt(q.transform.position) == room;
                g.chores.Mess("spill " + kind + " " + rn, choreText ?? "Wipe up the spilt " + kind + (room != null ? " (" + rn + ")" : ""),
                    () => { foreach (var q in Puddle.All) if (mine(q) && !q.Gone) return false; return true; },
                    () => { foreach (var q in Puddle.All) if (mine(q) && !q.Gone) return q.transform.position; return at; },
                    doneLine ?? "You get down and wipe it up. The cloth comes away darker than the " + kind + " should have made it.");
            }
            if (g != null) g.Log("spill: " + litres.ToString("0.00") + " l of " + kind + (on != null ? " on " + on.name : ""));
            return nu;
        }

        /// <summary>The first solid surface straight down (not water, not things being carried).</summary>
        static bool Below(Vector3 from, out Vector3 at, out Vector3 n, out Transform on)
        {
            at = from; n = Vector3.up; on = null;
            var hits = Physics.RaycastAll(from + Vector3.up * 0.02f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                var m = h.collider.GetComponentInParent<Movable>();
                if (m != null && (m.held || (Hands.I != null && Hands.I.Holding(m)))) continue;
                if (h.collider.GetComponentInParent<Player>() != null) continue;
                if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                if (h.collider.name.StartsWith("Piece of") || h.collider.name == "Bit") continue;
                if (h.distance < best) { best = h.distance; at = h.point; n = h.normal; on = h.collider.transform; }
            }
            return best < float.MaxValue;
        }

        // ---------------------------------------------------------------- the stream while pouring

        static LineRenderer stream;
        static int streamFrame = -1;

        /// <summary>Draw this frame's stream from a spout down to what it lands on. Call every frame while pouring.</summary>
        public static Vector3 Stream(Vector3 from, string kind)
        {
            Vector3 at, n; Transform on;
            if (!Below(from, out at, out n, out on)) at = from + Vector3.down * 1f;
            if (stream == null)
            {
                var g = new GameObject("Pouring");
                stream = g.AddComponent<LineRenderer>();
                stream.positionCount = 6;
                stream.useWorldSpace = true;
                stream.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                stream.numCapVertices = 2;
                var m = Build.Mat(Color.white, 0f, 0.95f);
                Work.Fade(m);
                stream.sharedMaterial = m;
                g.AddComponent<StreamHider>();
            }
            var c = ColorOf(kind);
            c.a = kind == "water" ? 0.45f : 0.85f;
            stream.sharedMaterial.color = c;
            stream.startWidth = 0.014f;
            stream.endWidth = 0.008f;
            // A little arc off the lip, then straight down, wobbling.
            Vector3 lip = from;
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                Vector3 p = Vector3.Lerp(lip, at, t);
                p += new Vector3(Mathf.Sin(Time.time * 37f + i) * 0.002f, 0f, Mathf.Cos(Time.time * 29f + i) * 0.002f) * t;
                stream.SetPosition(i, p);
            }
            stream.enabled = true;
            streamFrame = Time.frameCount;
            return at;
        }

        class StreamHider : MonoBehaviour
        {
            void LateUpdate() { if (stream != null && streamFrame < Time.frameCount - 1) stream.enabled = false; }
        }

        // ---------------------------------------------------------------- wet and muddy feet

        static string solesKind;
        static int solesLeft;
        static bool lastLeft;

        /// <summary>Give the player wet or muddy soles (for tests, and anything that soaks your feet).</summary>
        public static void Soles(string kind, int steps) { solesKind = kind; solesLeft = steps; }

        /// <summary>Called on each footstep. Picks up wet or mud from where you stand; leaves a print if your soles are wet.</summary>
        public static void Step(Vector3 feet, Vector3 forward, bool outside, bool porch)
        {
            // Standing in a puddle?
            foreach (var p in Puddle.All)
                if (p != null && p.Wet > 0.2f && Mathf.Abs(p.transform.position.y - feet.y) < 0.15f && Vis.Level(p.transform.position - feet).magnitude < p.Radius)
                {
                    solesKind = p.kind == "water" ? "water" : p.kind == "mud" ? "mud" : p.kind;
                    solesLeft = 7;
                    Sfx.At("squelch", feet, 0.3f, 0.9f + Random.value * 0.2f);
                    p.Trodden();
                    return;
                }
            if (outside)
            {
                // The grass is wet most mornings and evenings, and the beds by the path are earth.
                float h = DayClock.I != null ? DayClock.I.Hour : 12f;
                if (h < 10f || h > 18f || Random.value < 0.25f) { solesKind = "mud"; solesLeft = 9; }
                return;
            }
            if (porch) { if (solesKind == "mud") solesLeft = Mathf.Max(0, solesLeft - 2); return; }     // the mat takes some of it
            if (solesLeft <= 0 || solesKind == null) return;
            solesLeft--;
            lastLeft = !lastLeft;
            Print(feet, forward, lastLeft, solesKind, Mathf.Clamp01(0.35f + solesLeft * 0.1f));
            if (solesLeft <= 0) solesKind = null;
        }

        /// <summary>A footprint laid by something other than your own feet (a haunting). Returns its cleaning job (null for water, which dries).</summary>
        public static Work PrintAt(Vector3 feet, Vector3 forward, bool left, string kind, float strength) { return Print(feet, forward, left, kind, strength, false); }

        static void Print(Vector3 feet, Vector3 forward, bool left, string kind, float strength) { Print(feet, forward, left, kind, strength, true); }

        static Work Print(Vector3 feet, Vector3 forward, bool left, string kind, float strength, bool yours)
        {
            Vector3 hp, hn; Transform hon;
            if (!Below(feet + Vector3.up * 0.3f + Vis.Flat(forward).normalized * 0.05f, out hp, out hn, out hon) || hn.y < 0.8f || hp.y < feet.y - 0.3f) return null;
            var hit = new { point = hp, normal = hn };
            Vector3 side = Vector3.Cross(Vector3.up, Vis.Flat(forward).normalized) * (left ? -0.09f : 0.09f);
            float yaw = Vector3.SignedAngle(Vector3.forward, Vis.Flat(forward), Vector3.up);
            var holder = GameDirector.I != null && GameDirector.I.house != null ? GameDirector.I.house.transform : null;
            var r = NightPages.Stains.Mark(holder, hit.point + side, Vector3.up, 0.1f, 0.27f, "shoe", MarkOf(kind), strength * (kind == "water" ? 0.6f : 0.9f), -yaw + (left ? 4f : -4f), left ? 0 : 1);
            if (r == null) return null;
            prints++;
            if (kind == "water")
            {
                // Water prints dry where they were made.
                r.gameObject.AddComponent<Dry>().Begin(r, 45f + Random.value * 30f);
                return null;
            }
            var w = NightPages.Stains.Cleanable(r, "Wipe", "the " + (kind == "mud" ? "muddy" : kind + "-stained") + " footprint", 90f, "rag", "sponge", "mop");
            if (!yours) return w;
            if (kind == "mud" && !muddyChore && GameDirector.I != null && GameDirector.I.chores != null)
            {
                muddyChore = true;
                var mine = new List<Work>();
                trail = mine;
                GameDirector.I.chores.Mess("mudprints", "Wipe up the muddy footprints you trod in",
                    () => { foreach (var x in mine) if (x != null && !x.Finished) return false; muddyChore = false; return true; },
                    () => { foreach (var x in mine) if (x != null && !x.Finished) return x.transform.position; return feet; },
                    "Your own footprints, every one. You count them as you go, and lose count.");
            }
            if (kind == "mud" && trail != null) trail.Add(w);
            return w;
        }
        static List<Work> trail;

        /// <summary>A mark that fades away by itself (a wet footprint drying).</summary>
        class Dry : MonoBehaviour
        {
            Material m; float t, life, s0;
            public void Begin(Renderer r, float seconds) { m = r.material; life = seconds; s0 = m.HasProperty("_Strength") ? m.GetFloat("_Strength") : 1f; }
            void Update()
            {
                if (m == null) return;
                t += Time.deltaTime;
                if (m.HasProperty("_Strength")) m.SetFloat("_Strength", s0 * Mathf.Clamp01(1f - t / life));
                if (t > life) Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// A puddle on the floor or a table: a stain (the liquid darkening what it's on) under a wet shine. It spreads
    /// with more poured into it, dries over a few minutes, and wipes up with a cloth or a sponge.
    /// </summary>
    public class Puddle : MonoBehaviour
    {
        public static readonly List<Puddle> All = new List<Puddle>();
        public string kind = "water";
        public float litres;
        Renderer stain, sheen;
        Work wipe;
        float wet = 1f, dryRate, s0;
        bool dried;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public float Wet { get { return wet; } }
        public bool Gone { get { return this == null || !gameObject.activeInHierarchy || (wipe != null && wipe.Finished); } }
        /// <summary>How far it has spread (metres): a cupful is about a forearm across.</summary>
        public float Radius { get { return Mathf.Clamp(0.07f + Mathf.Sqrt(litres) * 0.42f, 0.05f, 1.1f); } }

        public static Puddle Make(Vector3 at, Vector3 normal, Transform on, float litres, string kind)
        {
            var holder = GameDirector.I != null && GameDirector.I.house != null ? GameDirector.I.house.transform : null;
            var g = new GameObject("Puddle (" + kind + ")");
            g.transform.SetParent(holder, true);
            g.transform.position = at;
            var p = g.AddComponent<Puddle>();
            p.kind = kind;
            p.litres = litres;
            float spin = Random.Range(0f, 360f);
            p.stain = NightPages.Stains.Mark(g.transform, at, normal, 1f, 1f, "blot", Liquid.MarkOf(kind), kind == "water" ? 0.55f : kind == "milk" ? 0.15f : 0.9f, spin);
            p.sheen = MakeSheen(g.transform, at, normal, spin);
            p.s0 = kind == "water" ? 0.55f : kind == "milk" ? 0.15f : 0.9f;
            // Water on a warm house: gone in a few minutes. Thicker things take longer and leave a mark.
            p.dryRate = kind == "water" ? 1f / 300f : kind == "mud" ? 1f / 420f : 1f / 600f;
            var area = g.AddComponent<BoxCollider>();
            area.isTrigger = true;
            area.center = new Vector3(0f, 0.03f, 0f);
            p.wipe = Work.Make(g, "Wipe up", "the spilt " + kind, WorkMode.Rub, 200f + litres * 900f, "rag", "sponge", "mop");
            p.wipe.removeWhenDone = true;
            p.wipe.progress = q => p.Wiped(q);
            p.Fit();
            return p;
        }

        static Renderer MakeSheen(Transform parent, Vector3 at, Vector3 normal, float spin)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Wet";
            Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, true);
            Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            q.transform.rotation = Quaternion.AngleAxis(spin, normal) * Quaternion.LookRotation(-normal, up);
            q.transform.position = at + normal * 0.004f;
            var r = q.GetComponent<Renderer>();
            var m = new Material(NightPages.Stains.Sheen());
            m.mainTexture = NightPages.Stains.Tex("wet", 7);        // a ragged outline, so the shine has an edge
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        public void Add(float more)
        {
            litres += more;
            wet = 1f;
            if (wipe != null) wipe.need = 200f + litres * 900f;
            Fit();
        }

        void Fit()
        {
            float d = Radius * 2f;
            if (stain != null) stain.transform.localScale = new Vector3(d, d * 0.85f, 1f);
            if (sheen != null) sheen.transform.localScale = new Vector3(d * 0.9f, d * 0.75f, 1f);
            var b = GetComponent<BoxCollider>();
            if (b != null) b.size = new Vector3(d, 0.08f, d);
        }

        /// <summary>Stepped in: a little of it goes with you, smeared.</summary>
        public void Trodden() { litres = Mathf.Max(0.01f, litres * 0.92f); }

        void Wiped(float q)
        {
            if (stain != null && stain.material.HasProperty("_Strength")) stain.material.SetFloat("_Strength", s0 * (1f - q));
            if (sheen != null) { var c = sheen.material.color; c.a = 0.22f * wet * (1f - q); sheen.material.color = c; }
        }

        void Update()
        {
            if (dried) return;
            wet = Mathf.Max(0f, wet - dryRate * Time.deltaTime);
            if (sheen != null)
            {
                var c = sheen.material.color;
                c.a = 0.22f * wet * (wipe != null ? 1f - wipe.Progress : 1f);
                sheen.material.color = c;
            }
            // Drying, water fades away; the rest dries to a ring.
            if (!Liquid.Stains(kind) && stain != null && stain.material.HasProperty("_Strength"))
                stain.material.SetFloat("_Strength", s0 * wet * (wipe != null ? 1f - wipe.Progress : 1f));
            if (wet <= 0f)
            {
                dried = true;
                if (sheen != null) sheen.enabled = false;
                if (!Liquid.Stains(kind)) { gameObject.SetActive(false); return; }
                if (stain != null && stain.material.HasProperty("_Strength")) stain.material.SetFloat("_Strength", s0 * 0.7f);
                if (wipe != null) { wipe.verb = "Scrub"; wipe.what = "the " + kind + " stain"; wipe.need *= 1.6f; }
            }
        }
    }
}
