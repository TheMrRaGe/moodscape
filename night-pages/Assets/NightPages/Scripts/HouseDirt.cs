using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Fifty years of living in a house: the marks it leaves. Cup rings on the tables, soot over the fire, grease
    /// behind the heater, the wall by every light switch grey from hands, scuffs along the skirting, a damp patch
    /// in the study corner, mildew over the bath and rust under its taps, a tide line of mud inside the front door,
    /// cobwebs in the corners, dust on everything nobody reaches. And a brown stain on the bedroom ceiling that is a
    /// little bigger every morning.
    ///
    /// Everything is found by looking (rays from inside the room to the wall, down to the table, up to the ceiling),
    /// so it lands on whatever is really there this run (swapped furniture, a mirrored room).
    /// The dust on the living-room furniture is a chore some days; the rest stays unless you scrub it.
    /// </summary>
    public partial class House
    {
        public readonly List<Work> dust = new List<Work>();
        Renderer ceilingStain;
        Vector3 ceilingStainBase;
        public int dirtMarks;

        static readonly Color DirtGrease = new Color(0.45f, 0.36f, 0.22f);
        static readonly Color DirtSoot = new Color(0.08f, 0.07f, 0.06f);
        static readonly Color DirtDamp = new Color(0.5f, 0.42f, 0.3f);
        static readonly Color DirtNicotine = new Color(0.72f, 0.58f, 0.3f);
        static readonly Color DirtRust = new Color(0.55f, 0.28f, 0.1f);
        static readonly Color DirtMud = new Color(0.32f, 0.24f, 0.15f);
        static readonly Color DirtHands = new Color(0.42f, 0.38f, 0.34f);
        static readonly Color DirtCoffee = new Color(0.36f, 0.22f, 0.1f);

        // ---------------------------------------------------------------- finding surfaces

        /// <summary>The first real surface along a ray: walls, floors, furniture (not loose things, triggers or switches).</summary>
        static bool Surface(Vector3 from, Vector3 dir, float dist, out RaycastHit hit)
        {
            hit = new RaycastHit();
            float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(from, dir, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<Movable>() != null) continue;
                if (h.collider.GetComponentInParent<Interactable>() != null && h.collider.GetComponentInParent<Interactable>().name.Contains("switch")) continue;
                if (h.collider.GetComponentInParent<Door>() != null) continue;
                if (h.distance < best) { best = h.distance; hit = h; }
            }
            return best < float.MaxValue;
        }

        /// <summary>A mark on a wall: look from `drawn` (inside the room) toward `toward` and mark what's there.</summary>
        Renderer OnWall(Vector3 drawn, Vector3 toward, float w, float h, string kind, Color tint, float strength, float spin = 0f, float reach = 1.2f)
        {
            RaycastHit hit;
            if (!Surface(W(drawn), Wn(toward).normalized, reach, out hit)) return null;
            if (Mathf.Abs(hit.normal.y) > 0.3f) return null;
            dirtMarks++;
            return Stains.Mark(transform, hit.point, hit.normal, w, h, kind, tint, strength, spin);
        }

        /// <summary>A mark on whatever is below `drawn` (a table top, the floor, a rug).</summary>
        Renderer OnTop(Vector3 drawn, float w, float h, string kind, Color tint, float strength, float spin = -1f, int variant = -1)
        {
            RaycastHit hit;
            if (!Surface(W(drawn), Vector3.down, 3f, out hit) || hit.normal.y < 0.8f) return null;
            dirtMarks++;
            return Stains.Mark(transform, hit.point, hit.normal, w, h, kind, tint, strength, spin < 0f ? Random.Range(0f, 360f) : spin, variant);
        }

        Renderer DustOn(Vector3 drawn, float w, float h, float alpha)
        {
            RaycastHit hit;
            if (!Surface(W(drawn), Vector3.down, 2.5f, out hit) || hit.normal.y < 0.8f) return null;
            dirtMarks++;
            return Stains.Film(transform, hit.point, hit.normal, w, h, "dust", alpha, Random.Range(-4f, 4f));
        }

        /// <summary>A cobweb in the top corner of a room: given the corner in the drawing and which way is into the room.</summary>
        void Web(Vector3 drawnCorner, float intoX, float intoZ, float size)
        {
            Vector3 cornerW = W(new Vector3(drawnCorner.x, 0f, drawnCorner.z));
            float top = transform.position.y + H - 0.01f;
            // One sheet on each of the two walls that meet there, its spokes running out from the corner.
            for (int wall = 0; wall < 2; wall++)
            {
                Vector3 toWall = wall == 0 ? new Vector3(-intoX, 0f, 0f) : new Vector3(0f, 0f, -intoZ);
                Vector3 probe = W(new Vector3(drawnCorner.x + intoX * 0.5f, H - 0.3f, drawnCorner.z + intoZ * 0.5f));
                RaycastHit hit;
                if (!Surface(probe, Wn(toWall).normalized, 1.2f, out hit) || Mathf.Abs(hit.normal.y) > 0.3f) continue;
                Vector3 n = hit.normal;
                Vector3 toCorner = Vector3.ProjectOnPlane(cornerW - hit.point, n);
                toCorner.y = 0f;
                if (toCorner.sqrMagnitude < 1e-4f) continue;
                float d = toCorner.magnitude;
                RaycastHit h2;
                if (Surface(hit.point + n * 0.05f, toCorner.normalized, 2f, out h2)) d = h2.distance;
                Vector3 c = hit.point + toCorner.normalized * Mathf.Max(0f, d - size * 0.5f);
                c.y = top - size * 0.5f;
                Vector3 right = Vector3.Cross(Vector3.up, -n);
                int variant = Vector3.Dot(toCorner, right) > 0f ? 3 : 2;
                Stains.Film(transform, c, n, size, size, "web", 0.8f, 0f, variant);
                dirtMarks++;
            }
        }

        // ---------------------------------------------------------------- the dirt

        void BuildDirt()
        {
            Physics.SyncTransforms();
            dirtMarks = 0;
            dust.Clear();
            Random.State keep = Random.state;

            // ---- the hall: mud inside the door, scuffed skirting, hands round the cellar door knob.
            Plain();
            OnTop(new Vector3(0.05f, 0.5f, 7.15f), 0.9f, 0.55f, "blot", DirtMud, 0.55f);
            OnTop(new Vector3(-0.3f, 0.5f, 6.6f), 0.5f, 0.4f, "grime", DirtMud, 0.4f);
            for (int i = 0; i < 6; i++)
            {
                float z = -6.5f + i * 2.3f + Random.Range(-0.4f, 0.4f);
                OnWall(new Vector3(0f, 0.11f, z), new Vector3(i % 2 == 0 ? -1f : 1f, 0f, 0f), Random.Range(0.6f, 1.3f), 0.16f, "scuff", DirtHands, 0.55f);
            }
            OnWall(new Vector3(0.3f, 1.0f, -7f), Vector3.back, 0.3f, 0.36f, "smudge", DirtHands, 0.7f);
            OnTop(new Vector3(0f, 0.5f, 1.5f), 1.0f, 3.2f, "grime", DirtHands, 0.25f, 0f);          // the path worn down the rug
            Web(new Vector3(-1.4f, 0f, -7.9f), 1f, 1f, 0.5f);
            Web(new Vector3(1.4f, 0f, 7.9f), -1f, -1f, 0.35f);

            // ---- the kitchen: grease behind the heater, the splashback behind the sink, a stained floor, rings on the table.
            Use("kitchen");
            OnWall(new Vector3(-2.1f, 1.15f, -1.2f), Vector3.right, 0.8f, 1.1f, "soot", DirtGrease, 0.6f);
            OnWall(new Vector3(-7.0f, 1.1f, -5.2f), Vector3.left, 0.9f, 0.4f, "grime", DirtGrease, 0.6f);
            OnWall(new Vector3(-7.0f, 1.12f, -3.0f), Vector3.left, 0.5f, 0.3f, "blot", DirtGrease, 0.4f);      // where the kettle steams
            OnWall(new Vector3(-6.9f, 1.25f, -2.2f), Vector3.left, 0.9f, 0.7f, "streak", DirtDamp, 0.45f, 0f, 1.6f); // under the window
            OnTop(new Vector3(-6.55f, 0.5f, -5.2f), 0.7f, 0.55f, "blot", new Color(0.4f, 0.4f, 0.38f), 0.35f);   // the floor in front of the sink
            OnTop(new Vector3(-2.1f, 0.3f, -2.2f), 1.2f, 0.9f, "grime", DirtHands, 0.3f);                // the doorway
            for (int i = 0; i < 4; i++) OnTop(new Vector3(-4f + Random.Range(-0.35f, 0.35f), 1.5f, -4.4f + Random.Range(-0.35f, 0.35f)), 0.09f, 0.09f, "ring", DirtCoffee, Random.Range(0.4f, 0.8f));
            OnTop(new Vector3(-6.95f, 1.5f, -3.4f), 0.1f, 0.1f, "ring", DirtCoffee, 0.7f);                 // where your mug stands every morning
            OnTop(new Vector3(-6.85f, 1.5f, -4.05f), 0.25f, 0.18f, "blot", DirtGrease, 0.4f);
            Web(new Vector3(-7.4f, 0f, -7.9f), 1f, 1f, 0.55f);
            Web(new Vector3(-1.6f, 0f, -7.9f), -1f, 1f, 0.4f);

            // ---- the study: nicotine on the ceiling over the desk, rings and ink on the desk, a damp corner.
            Use("study");
            Ceiling(new Vector3(-6.3f, 0f, 4f), 1.6f, 1.4f, DirtNicotine, 0.35f);
            for (int i = 0; i < 5; i++) OnTop(new Vector3(-6.1f + Random.Range(-0.2f, 0.25f), 1.5f, 3.3f + Random.Range(-0.2f, 1.4f)), 0.09f, 0.09f, "ring", DirtCoffee, Random.Range(0.45f, 0.85f));
            OnTop(new Vector3(-6.55f, 1.5f, 4.55f), 0.07f, 0.06f, "blot", new Color(0.05f, 0.06f, 0.12f), 0.9f);    // ink
            OnTop(new Vector3(-5.95f, 1.5f, 3.25f), 0.12f, 0.08f, "speckle", DirtSoot, 0.5f);           // ash by the ashtray
            OnWall(new Vector3(-7.0f, 0.35f, 7.4f), Vector3.left, 1.3f, 0.75f, "blot", DirtDamp, 0.55f, 180f);
            OnWall(new Vector3(-6.8f, 0.4f, 7.5f), Vector3.forward, 0.9f, 0.7f, "mildew", DirtSoot, 0.4f);
            OnTop(new Vector3(-5.35f, 0.5f, 4.0f), 0.9f, 0.7f, "grime", DirtHands, 0.3f);               // under the desk chair
            Web(new Vector3(-7.4f, 0f, 7.9f), 1f, -1f, 0.6f);
            Web(new Vector3(-1.6f, 0f, 0.1f), -1f, 1f, 0.35f);

            // ---- the living room: soot over the fire, rings on the coffee table, wine on the rug, dust.
            Use("living room");
            OnWall(new Vector3(6.5f, 0.95f, -4f), Vector3.right, 1.0f, 0.55f, "soot", DirtSoot, 0.85f);
            OnWall(new Vector3(6.6f, 1.75f, -4f), Vector3.right, 0.8f, 0.6f, "soot", DirtSoot, 0.25f);
            Ceiling(new Vector3(6.6f, 0f, -4f), 1.6f, 1.0f, DirtSoot, 0.2f);
            OnTop(new Vector3(6.4f, 0.5f, -4f), 0.9f, 1.4f, "speckle", DirtSoot, 0.6f);                 // ash on the hearth
            for (int i = 0; i < 3; i++) OnTop(new Vector3(5.0f + Random.Range(-0.2f, 0.2f), 1.0f, -4.1f + Random.Range(-0.4f, 0.4f)), 0.085f, 0.085f, "ring", DirtCoffee, Random.Range(0.35f, 0.7f));
            OnTop(new Vector3(5.55f, 0.5f, -5.35f), 0.35f, 0.28f, "blot", new Color(0.4f, 0.1f, 0.12f), 0.6f);   // by his chair
            OnTop(new Vector3(4.4f, 0.3f, -2.3f), 1.0f, 0.8f, "grime", DirtHands, 0.3f);                // the way in from the hall
            OnWall(new Vector3(4.0f, 0.95f, -4.1f), Vector3.left, 1.6f, 0.25f, "grime", DirtHands, 0.3f, 0f, 0.6f);   // where the sofa's back rubs the wall
            Web(new Vector3(7.4f, 0f, -7.9f), -1f, 1f, 0.55f);
            Web(new Vector3(1.6f, 0f, -7.9f), 1f, 1f, 0.45f);
            // Dust: the mantel, the china cabinet, the television table, the side table.
            Dust(new Vector3(7.1f, 1.6f, -4f), 0.55f, 2.0f, "the mantelpiece");
            Dust(new Vector3(1.86f, 2.3f, -6.0f), 0.45f, 0.9f, "the top of the china cabinet");
            Dust(new Vector3(4.6f, 1.2f, -0.42f), 0.5f, 0.4f, "the television");
            Dust(new Vector3(6.75f, 1.0f, -1.2f), 0.45f, 0.45f, "the side table");

            // ---- the bedroom: the ceiling stain, rings on the nightstand, dust on the dresser, scuffs.
            Use("bedroom");
            ceilingStain = Ceiling(new Vector3(5.2f, 0f, 5.0f), 0.5f, 0.45f, new Color(0.55f, 0.4f, 0.2f), 0.75f, "blot");
            if (ceilingStain != null) ceilingStainBase = ceilingStain.transform.localScale;
            OnTop(new Vector3(5.2f, 1.2f, 6.82f), 0.08f, 0.08f, "ring", new Color(0.5f, 0.5f, 0.5f), 0.5f);
            Dust(new Vector3(3.55f, 1.3f, 0.62f), 1.2f, 0.45f, "the dresser");
            DustOn(new Vector3(2.15f, 2.6f, 1.2f), 0.6f, 1.0f, 0.9f);                                // the wardrobe top, never touched
            OnWall(new Vector3(4.4f, 0.12f, 7.5f), Vector3.forward, 1.2f, 0.16f, "scuff", DirtHands, 0.5f);
            OnTop(new Vector3(5.6f, 0.3f, 4.9f), 0.6f, 0.5f, "grime", DirtHands, 0.25f);                // where you get out of bed
            Web(new Vector3(7.4f, 0f, 7.9f), -1f, -1f, 0.5f);
            Web(new Vector3(1.6f, 0f, 7.9f), 1f, -1f, 0.4f);

            // ---- the bathroom (in the bedroom's drawing): mildew over the bath, rust under the taps, scale in the basin.
            OnWall(new Vector3(7.0f, 1.55f, 0.35f), Vector3.right, 0.7f, 0.8f, "mildew", DirtSoot, 0.75f);
            OnWall(new Vector3(7.0f, 1.9f, 0.4f), Vector3.back, 0.6f, 0.5f, "mildew", DirtSoot, 0.55f);
            OnWall(new Vector3(7.0f, 0.62f, 1.0f), Vector3.right, 1.6f, 0.08f, "grime", DirtDamp, 0.6f, 0f, 0.8f);   // the grout line along the bath
            OnTop(new Vector3(7.07f, 0.9f, 0.32f), 0.12f, 0.3f, "streak", DirtRust, 0.75f, 0f);          // under the bath taps
            OnTop(new Vector3(5.2f, 1.2f, 1.55f), 0.08f, 0.16f, "streak", DirtRust, 0.6f, 90f);           // in the basin under its tap
            OnTop(new Vector3(5.62f, 0.5f, 0.85f), 0.55f, 0.45f, "blot", new Color(0.55f, 0.5f, 0.4f), 0.25f);   // the floor in front of the toilet
            Ceiling(new Vector3(6.6f, 0f, 1.0f), 1.0f, 0.9f, new Color(0.3f, 0.32f, 0.25f), 0.3f, "mildew");
            Plain();

            // ---- every light switch: the wall round it grey from fifty years of hands feeling for it in the dark.
            foreach (var r in rooms)
            {
                if (r.lightSwitch == null || r.name == "bathroom") continue;
                Vector3 at = r.lightSwitch.transform.position;
                RaycastHit wall = new RaycastHit();
                bool found = false;
                foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                {
                    RaycastHit h;
                    if (!Surface(at - d * 0.3f, d, 0.45f, out h) || Mathf.Abs(h.distance - 0.3f) > 0.06f || Vector3.Dot(h.normal, -d) < 0.9f) continue;
                    wall = h; found = true; break;
                }
                if (!found) continue;
                Stains.Mark(transform, wall.point + Vector3.up * 0.03f, wall.normal, 0.3f, 0.36f, "smudge", DirtHands, 0.6f, Random.Range(-10f, 10f));
                dirtMarks++;
            }

            Random.state = keep;
            GameDirector.I.Log("house: " + dirtMarks + " marks of fifty years' living (rings, soot, grease, damp, mildew, scuffs, webs, dust)");
        }

        Renderer Ceiling(Vector3 drawnXZ, float w, float h, Color tint, float strength, string kind = "blot")
        {
            RaycastHit hit;
            if (!Surface(W(new Vector3(drawnXZ.x, 1.6f, drawnXZ.z)), Vector3.up, 2.5f, out hit) || hit.normal.y > -0.8f) return null;
            dirtMarks++;
            return Stains.Mark(transform, hit.point, hit.normal, w, h, kind, tint, strength, Random.Range(0f, 360f));
        }

        /// <summary>A film of dust on a surface, and the job of wiping it off (with a cloth).</summary>
        void Dust(Vector3 drawn, float w, float h, string what)
        {
            var r = DustOn(drawn, w, h, 0.75f);
            if (r == null) return;
            var job = Stains.Cleanable(r, "Dust", what, 260f, "rag", "duster");
            if (job == null) return;
            job.removeWhenDone = false;
            var mat = r.material;
            job.finished = () => { if (r != null) r.enabled = false; };
            dust.Add(job);
        }

        /// <summary>The dust is back (some days it's on the list): every film thick again.</summary>
        public void Redust()
        {
            foreach (var w in dust)
            {
                if (w == null) continue;
                w.done = 0f;
                w.gameObject.SetActive(true);
                var r = w.GetComponent<Renderer>();
                if (r != null) { r.enabled = true; var c = r.material.color; c.a = 0.75f; r.material.color = c; }
            }
        }

        public bool Dusted { get { foreach (var w in dust) if (w != null && !w.Finished) return false; return true; } }
        public int DustLeft { get { int n = 0; foreach (var w in dust) if (w != null && !w.Finished) n++; return n; } }

        /// <summary>Each morning: the stain on the bedroom ceiling has spread a little further.</summary>
        public void DirtDay(int day)
        {
            if (ceilingStain == null) return;
            float k = 1f + Mathf.Max(0, day - 1) * 0.35f;
            ceilingStain.transform.localScale = new Vector3(ceilingStainBase.x * k, ceilingStainBase.y * k * Random.Range(0.9f, 1.1f), 1f);
            if (ceilingStain.material.HasProperty("_Strength")) ceilingStain.material.SetFloat("_Strength", Mathf.Min(1f, 0.75f + day * 0.04f));
            if (day >= 4) GameDirector.I.Log("house: the stain on the bedroom ceiling is " + (k * 0.5f).ToString("0.0") + " m across now");
        }
    }
}
