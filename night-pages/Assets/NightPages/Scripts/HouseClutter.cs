using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The small things people leave about, room by room, where they'd really be: a glass of water by the bed and one
    /// by the sink, the milk out on the kitchen table, the paper folded at the crossword, a loaf on the bread board,
    /// screwed-up pages round the study bin and a cold cup of tea by the typewriter, books stacked on the floor
    /// because the shelf is full, his slippers by his chair and a glass of sherry on the floor beside it, soap on the
    /// basin, shoes by the door. Every one is a physical thing: knocked, it falls; dropped, it may break; tipped,
    /// what's in it goes on the floor.
    /// </summary>
    public partial class House
    {
        public int clutterCount;
        public Tool mop;

        static readonly Color PaperWhite = new Color(0.9f, 0.88f, 0.82f);
        static readonly Color Newsprint = new Color(0.82f, 0.8f, 0.74f);
        static Material glassMat;

        /// <summary>The height (in the drawing) of the first real surface below `drawn`, or NaN.</summary>
        float SurfaceAt(Vector3 drawn, out Vector3 normal)
        {
            RaycastHit hit;
            normal = Vector3.up;
            if (!Surface(W(drawn), Vector3.down, 3f, out hit) || hit.normal.y < 0.85f) return float.NaN;
            normal = hit.normal;
            return transform.InverseTransformPoint(hit.point).y;
        }

        /// <summary>
        /// Meant for a table top (looked for from 1 m up or more) but found the floor: the furniture this run may be
        /// smaller or set a little elsewhere (a swapped piece). Move the point onto the nearest top within reach.
        /// </summary>
        Vector3 OntoFurniture(Vector3 drawn)
        {
            if (drawn.y < 1f) return drawn;
            Vector3 n;
            float y = SurfaceAt(drawn, out n);
            if (!float.IsNaN(y) && y > 0.25f) return drawn;
            Vector3 world = W(drawn);
            Bounds best = new Bounds();
            float bestD = 0.9f;
            bool found = false;
            foreach (var f in furniture)
            {
                if (f == null) continue;
                var c = f.GetComponent<Collider>();
                if (c == null) continue;
                var b = c.bounds;
                float top = b.max.y - transform.position.y;
                if (top < 0.3f || top > 1.6f || b.size.x < 0.2f || b.size.z < 0.2f) continue;
                Vector3 cl = b.ClosestPoint(new Vector3(world.x, b.center.y, world.z));
                float d = Vis.Level(cl - world).magnitude;
                if (d < bestD) { bestD = d; best = b; found = true; }
            }
            if (!found) return drawn;
            float m = 0.08f;
            Vector3 p = new Vector3(Mathf.Clamp(world.x, best.min.x + m, Mathf.Max(best.min.x + m, best.max.x - m)), world.y, Mathf.Clamp(world.z, best.min.z + m, Mathf.Max(best.min.z + m, best.max.z - m)));
            Vector3 local = transform.InverseTransformPoint(p);
            return new Vector3(local.x * mx, drawn.y, local.z * mz);     // back into the drawing (P is its own inverse)
        }

        /// <summary>
        /// A small made thing standing on whatever is under `drawn` (x and z; y is where to look down from).
        /// `size` is its real size in metres. Left as it was put (resting) until something knocks it.
        /// </summary>
        Movable Prop(string name, PrimitiveType shape, Vector3 drawn, Vector3 size, Material mat, float mass, float yaw = 0f, bool rest = true)
        {
            drawn = OntoFurniture(drawn);
            Vector3 n;
            float y = SurfaceAt(drawn, out n);
            if (float.IsNaN(y)) return null;
            yaw += Lived(25f);
            Vector3 at = P(new Vector3(drawn.x, y, drawn.z));
            Vector3 world = transform.TransformPoint(at);
            // Not inside something already there.
            Vector3 half = new Vector3(size.x, size.y, size.z) * 0.45f;
            Quaternion rot = transform.rotation * Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 4 && Physics.CheckBox(world + Vector3.up * (size.y / 2f + 0.004f), half, rot, ~0, QueryTriggerInteraction.Ignore); i++)
            {
                if (i == 3) return null;
                world += new Vector3(Random.Range(-0.08f, 0.08f), 0f, Random.Range(-0.08f, 0.08f));
            }
            RaycastHit under;
            if (Surface(world + Vector3.up * 0.3f, Vector3.down, 0.6f, out under) && under.normal.y > 0.85f) world.y = under.point.y;
            else return null;                                     // nudged off the edge
            at = transform.InverseTransformPoint(world);
            Vector3 scale = shape == PrimitiveType.Cylinder || shape == PrimitiveType.Capsule ? new Vector3(size.x, size.y / 2f, size.z) : size;
            var g = Build.Prim(shape, name, transform, at + Vector3.up * (size.y / 2f + 0.002f), scale, mat, true);
            g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            if (shape == PrimitiveType.Cylinder) FlatBottom(g);
            if (shape == PrimitiveType.Sphere) { var sc = g.GetComponent<SphereCollider>(); if (sc != null) sc.radius = 0.45f; }
            var m = Movable.Attach(g, mass);
            if (rest) m.Rest();
            movables.Add(m);
            clutterCount++;
            return m;
        }

        Movable Prop(string name, PrimitiveType shape, Vector3 drawn, Vector3 size, Color c, float mass, float yaw = 0f, float smooth = 0.15f)
        {
            return Prop(name, shape, drawn, size, Build.Mat(c, 0f, smooth), mass, yaw);
        }

        /// <summary>Something standing on top of another loose thing (a cup on its saucer, a book on a book, a loaf on the board).</summary>
        Movable OnTopOf(Movable below, string name, PrimitiveType shape, Vector3 size, Material mat, float mass, float yaw)
        {
            if (below == null) return null;
            Bounds b = new Bounds(below.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in below.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            Vector3 world = new Vector3(b.center.x + Random.Range(-0.012f, 0.012f), b.max.y + size.y / 2f + 0.002f, b.center.z + Random.Range(-0.012f, 0.012f));
            Vector3 scale = shape == PrimitiveType.Cylinder || shape == PrimitiveType.Capsule ? new Vector3(size.x, size.y / 2f, size.z) : size;
            var g = Build.Prim(shape, name, transform, transform.InverseTransformPoint(world), scale, mat, true);
            g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            if (shape == PrimitiveType.Cylinder) FlatBottom(g);
            var m = Movable.Attach(g, mass);
            m.Rest();
            movables.Add(m);
            clutterCount++;
            return m;
        }

        static Material GlassMat()
        {
            if (glassMat != null) return glassMat;
            glassMat = Build.Mat(new Color(0.85f, 0.9f, 0.92f, 0.3f), 0f, 0.96f);
            Work.Fade(glassMat);
            glassMat.color = new Color(0.85f, 0.9f, 0.92f, 0.3f);
            return glassMat;
        }

        /// <summary>A drinking glass with something in it (or empty).</summary>
        Movable Glass(string name, Vector3 drawn, float height, float diameter, string kind, float full)
        {
            var m = Prop(name, PrimitiveType.Cylinder, drawn, new Vector3(diameter, height, diameter), GlassMat(), 0.2f);
            if (m == null || full <= 0f) return m;
            // The drink, inside the glass (in its local space: the cylinder runs -1..1).
            var c = Liquid.ColorOf(kind);
            var drink = Build.Prim(PrimitiveType.Cylinder, "Drink", m.transform, new Vector3(0f, -1f + full, 0f), new Vector3(0.86f, full, 0.86f), Build.Mat(new Color(c.r, c.g, c.b), 0f, 0.9f), false);
            if (kind == "water")
            {
                var wm = drink.GetComponent<Renderer>().material;
                Work.Fade(wm);
                wm.color = new Color(0.7f, 0.8f, 0.85f, 0.35f);
            }
            Contents.In(m.gameObject, kind, diameter * diameter * 0.785f * height * full * 1000f, drink);      // litres
            return m;
        }

        /// <summary>A cup and its saucer, with tea (or the dregs).</summary>
        void Teacup(Vector3 drawn, Color china, string kind, float full)
        {
            var saucer = Prop("Saucer", PrimitiveType.Cylinder, drawn, new Vector3(0.14f, 0.012f, 0.14f), Build.Mat(china, 0f, 0.7f), 0.12f);
            if (saucer == null) return;
            var cup = OnTopOf(saucer, "Teacup", PrimitiveType.Cylinder, new Vector3(0.085f, 0.06f, 0.085f), Build.Mat(china, 0f, 0.7f), 0.15f, Random.Range(0f, 360f));
            if (cup == null) return;
            var handle = Build.Box("Handle", cup.transform, new Vector3(0.62f, 0.05f, 0f), Vector3.one, Build.Mat(china, 0f, 0.7f), false);
            handle.transform.localScale = new Vector3(0.2f, 0.9f, 0.14f);
            var c = Liquid.ColorOf(kind);
            var tea = Build.Prim(PrimitiveType.Cylinder, "Tea", cup.transform, new Vector3(0f, 1.0f - (1f - full) * 1.6f, 0f), new Vector3(0.84f, 0.01f, 0.84f), Build.Mat(c, 0f, 0.92f), false);
            Contents.In(cup.gameObject, kind, 0.15f * full, tea);
        }

        /// <summary>A pile of books: each its own thing, stacked a little crooked.</summary>
        void Books(Vector3 drawn, int n, float yaw)
        {
            Color[] spines = { new Color(0.45f, 0.12f, 0.1f), new Color(0.15f, 0.25f, 0.4f), new Color(0.2f, 0.32f, 0.2f), new Color(0.6f, 0.5f, 0.35f), new Color(0.25f, 0.2f, 0.18f), new Color(0.7f, 0.65f, 0.55f) };
            Movable below = null;
            for (int i = 0; i < n; i++)
            {
                float w = Random.Range(0.14f, 0.2f), d = Random.Range(0.2f, 0.27f), h = Random.Range(0.02f, 0.05f);
                var mat = Build.Mat(spines[Random.Range(0, spines.Length)], 0f, 0.2f);
                float mass = Mathf.Lerp(0.3f, 0.8f, h / 0.05f), turn = yaw + Random.Range(-12f, 12f);
                below = below == null ? Prop("Book", PrimitiveType.Cube, drawn + Vector3.up * 0.5f, new Vector3(w, h, d), mat, mass, turn)
                                      : OnTopOf(below, "Book", PrimitiveType.Cube, new Vector3(w, h, d), mat, mass, turn);
                if (below == null) return;
            }
        }

        /// <summary>Screwed-up paper: a lumpy ball.</summary>
        Movable Crumpled(Vector3 drawn)
        {
            float s = Random.Range(0.055f, 0.08f);
            var m = Prop("Crumpled page", PrimitiveType.Sphere, drawn, new Vector3(s, s * 0.85f, s * 1.1f), Build.Mat(PaperWhite, 0f, 0.05f), 0.01f, 0f);
            if (m != null) m.transform.localRotation = Random.rotation;
            return m;
        }

        void Shoes(string name, Vector3 drawn, float yaw, Color c, float length)
        {
            for (int i = 0; i < 2; i++)
            {
                Vector3 side = Quaternion.Euler(0f, yaw, 0f) * new Vector3(i == 0 ? -0.07f : 0.07f, 0f, i == 0 ? 0.02f : -0.03f);
                Prop(name, PrimitiveType.Cube, drawn + side, new Vector3(length * 0.36f, length * 0.33f, length), Build.Mat(c, 0f, 0.35f), 0.4f, yaw + (i == 0 ? -6f : 9f), false);
            }
        }

        void BuildClutter()
        {
            Physics.SyncTransforms();
            clutterCount = 0;

            // ---- the kitchen
            Use("kitchen");
            Glass("Glass of water", new Vector3(-6.72f, 1.5f, -5.7f), 0.11f, 0.07f, "water", 0.6f);
            var milk = Prop("Milk bottle", PrimitiveType.Cylinder, new Vector3(-4.2f, 1.5f, -4.05f), new Vector3(0.075f, 0.22f, 0.075f), GlassMat(), 0.6f);
            if (milk != null)
            {
                var white = Build.Prim(PrimitiveType.Cylinder, "Milk", milk.transform, new Vector3(0f, -0.25f, 0f), new Vector3(0.9f, 0.72f, 0.9f), Build.Mat(Liquid.ColorOf("milk"), 0f, 0.4f), false);
                Build.Prim(PrimitiveType.Cylinder, "Foil top", milk.transform, new Vector3(0f, 1.0f, 0f), new Vector3(0.55f, 0.03f, 0.55f), Build.Mat(new Color(0.75f, 0.72f, 0.4f), 0f, 0.85f), false);
                Contents.In(milk.gameObject, "milk", 0.4f, white);
            }
            Prop("Newspaper", PrimitiveType.Cube, new Vector3(-3.75f, 1.5f, -4.65f), new Vector3(0.3f, 0.012f, 0.4f), Newsprint, 0.25f, 15f, 0.05f);
            Prop("Tea towel", PrimitiveType.Cube, new Vector3(-4.35f, 1.5f, -4.7f), new Vector3(0.22f, 0.015f, 0.3f), new Color(0.75f, 0.78f, 0.82f), 0.1f, -20f, 0.05f);
            var board = Thing("wooden_cutting_board", new Vector3(-7.0f, Top(-7.0f, -6.15f), -6.15f), 90f, 0.6f);
            Physics.SyncTransforms();
            var loaf = OnTopOf(board, "Loaf of bread", PrimitiveType.Cube, new Vector3(0.12f, 0.1f, 0.24f), Build.Mat(new Color(0.72f, 0.5f, 0.26f), 0f, 0.1f), 0.4f, Random.Range(-20f, 20f));
            if (loaf != null) Build.Box("Crust", loaf.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.9f, 0.25f, 0.95f), Build.Mat(new Color(0.55f, 0.33f, 0.14f), 0f, 0.15f), false);
            Thing("russian_food_cans_01", new Vector3(-7.22f, Top(-7.22f, -6.95f), -6.95f), 20f, 0.4f);
            Thing("bleach_bottle", new Vector3(-6.5f, 0f, -5.85f), 30f, 0.6f);
            Thing("multi_cleaner_bottle", new Vector3(-7.25f, Top(-7.25f, -5.75f), -5.75f), 0f, 0.4f);
            Prop("Jar of spoons", PrimitiveType.Cylinder, new Vector3(-7.25f, 1.5f, -4.35f), new Vector3(0.09f, 0.13f, 0.09f), GlassMat(), 0.4f);
            // The mop, by the broom, for the floors.
            mop = Gadget("Mop", "mop", new Vector3(-7.25f, 0f, -7.2f), new Vector3(0.24f, 1.25f, 0.12f), new Color(0.82f, 0.8f, 0.72f), 1.1f);
            if (mop != null) Mop(mop);

            // ---- the hall
            Plain();
            Shoes("His shoes", new Vector3(0.95f, 0f, 7.45f), 175f, new Color(0.12f, 0.09f, 0.07f), 0.29f);
            Shoes("Your shoes", new Vector3(-0.55f, 0f, 7.25f), 160f, new Color(0.3f, 0.22f, 0.15f), 0.27f);
            Prop("Keys", PrimitiveType.Cube, new Vector3(-1.05f, 1.5f, -2.3f), new Vector3(0.07f, 0.012f, 0.03f), new Color(0.7f, 0.6f, 0.35f), 0.05f, 40f, 0.8f);
            Prop("Envelope", PrimitiveType.Cube, new Vector3(-1.12f, 1.5f, -1.95f), new Vector3(0.22f, 0.004f, 0.11f), PaperWhite, 0.02f, 10f, 0.05f);

            // ---- the study
            Use("study");
            for (int i = 0; i < 5; i++) Crumpled(new Vector3(-5.6f + Random.Range(-0.45f, 0.45f), 0.5f, 5.55f + Random.Range(-0.45f, 0.45f)));
            Crumpled(new Vector3(-6.0f, 1.5f, 4.95f));
            Books(new Vector3(-3.6f, 0f, 7.3f), 5, Random.Range(0f, 30f));
            Books(new Vector3(-3.15f, 0f, 7.35f), 3, Random.Range(0f, 30f));
            Teacup(new Vector3(-5.95f, 1.5f, 3.85f), new Color(0.88f, 0.86f, 0.8f), "tea", 0.4f);
            Glass("Whisky glass", new Vector3(-6.65f, 1.5f, 3.2f), 0.08f, 0.075f, "whisky", 0.25f);
            Prop("Pen", PrimitiveType.Cylinder, new Vector3(-6.0f, 1.5f, 4.15f), new Vector3(0.011f, 0.14f, 0.011f), new Color(0.08f, 0.08f, 0.1f), 0.02f)?.transform.Rotate(90f, 0f, 0f, Space.Self);
            for (int i = 0; i < 3; i++)
                Prop("Typed page", PrimitiveType.Cube, new Vector3(-6.0f + i * 0.04f, 1.5f, 3.6f + i * 0.05f), new Vector3(0.21f, 0.003f, 0.297f), PaperWhite, 0.01f, Random.Range(-25f, 25f), 0.05f);
            Prop("Typed page", PrimitiveType.Cube, new Vector3(-5.6f, 0.5f, 3.35f), new Vector3(0.21f, 0.003f, 0.297f), PaperWhite, 0.01f, Random.Range(0f, 360f), 0.05f);

            // ---- the living room
            Use("living room");
            Shoes("His slippers", new Vector3(5.45f, 0f, -5.05f), 100f, new Color(0.35f, 0.25f, 0.2f), 0.27f);
            Glass("Sherry glass", new Vector3(5.55f, 0.5f, -6.2f), 0.1f, 0.06f, "wine", 0.5f);
            Teacup(new Vector3(7.05f, 1.6f, -4.35f), new Color(0.8f, 0.85f, 0.88f), "tea", 0.1f);     // on the mantel, forgotten

            // ---- the bedroom and the bathroom
            Use("bedroom");
            Glass("Glass of water", new Vector3(5.45f, 1.2f, 6.78f), 0.11f, 0.07f, "water", 0.8f);
            Prop("Brown bottle", PrimitiveType.Cylinder, new Vector3(5.12f, 1.2f, 7.02f), new Vector3(0.04f, 0.08f, 0.04f), Build.Mat(new Color(0.35f, 0.18f, 0.06f), 0f, 0.85f), 0.08f);
            Prop("Book", PrimitiveType.Cube, new Vector3(3.15f, 1.2f, 6.98f), new Vector3(0.14f, 0.035f, 0.21f), new Color(0.2f, 0.25f, 0.38f), 0.4f, -15f);
            Prop("Cardigan", PrimitiveType.Cube, new Vector3(6.3f, 1.5f, 4.9f), new Vector3(0.38f, 0.05f, 0.3f), new Color(0.55f, 0.48f, 0.4f), 0.4f, 20f, 0.02f);
            Thing("wicker_basket_02", new Vector3(2.3f, 0f, 6.9f), 15f, 0.8f);
            Shoes("Your shoes", new Vector3(4.9f, 0f, 4.6f), -30f, new Color(0.25f, 0.18f, 0.12f), 0.27f);
            Prop("Bar of soap", PrimitiveType.Cube, new Vector3(5.12f, 1.2f, 1.36f), new Vector3(0.08f, 0.025f, 0.05f), new Color(0.92f, 0.88f, 0.75f), 0.1f, 0f, 0.6f);
            Prop("Toilet roll", PrimitiveType.Cylinder, new Vector3(5.62f, 1.2f, 0.2f), new Vector3(0.11f, 0.1f, 0.11f), new Color(0.96f, 0.95f, 0.92f), 0.1f);
            Plain();

            Physics.SyncTransforms();
            GameDirector.I.Log("house: " + clutterCount + " everyday things left about (glasses, cups, books, papers, shoes), each one physical");
        }

        /// <summary>The surface height (drawing space) at a point, for placing models that need it.</summary>
        float Top(float x, float z)
        {
            Vector3 n;
            float y = SurfaceAt(new Vector3(x, 1.6f, z), out n);
            return float.IsNaN(y) ? 0f : y;
        }

        /// <summary>The mop: a handle and a head of grey strings, built over the plain box it's handled by.</summary>
        void Mop(Tool t)
        {
            var r = t.GetComponent<Renderer>();
            if (r != null) r.enabled = false;
            var col = t.GetComponent<BoxCollider>();
            float h = col != null ? col.size.y * t.transform.lossyScale.y : 1.25f;
            Vector3 bottom = t.transform.position - Vector3.up * h * 0.5f;
            var handle = Build.Prim(PrimitiveType.Cylinder, "Handle", null, bottom + Vector3.up * (h * 0.5f + 0.08f), new Vector3(0.028f, h * 0.46f, 0.028f), Build.Mat(new Color(0.6f, 0.48f, 0.3f), 0f, 0.3f), false);
            handle.transform.SetParent(t.transform, true);
            var head = Build.Prim(PrimitiveType.Cylinder, "Mop head", null, bottom + Vector3.up * 0.08f, new Vector3(0.2f, 0.08f, 0.2f), Build.Mat(new Color(0.7f, 0.68f, 0.6f), 0f, 0.05f), false);
            head.transform.SetParent(t.transform, true);
            for (int i = 0; i < 10; i++)
            {
                float a = i * 36f * Mathf.Deg2Rad;
                var strand = Build.Box("Strand", null, bottom + new Vector3(Mathf.Cos(a) * 0.09f, 0.07f, Mathf.Sin(a) * 0.09f), new Vector3(0.02f, 0.14f, 0.02f), new Color(0.66f, 0.64f, 0.56f), false);
                strand.transform.rotation = Quaternion.Euler(Mathf.Sin(a) * 18f, 0f, -Mathf.Cos(a) * 18f);
                strand.transform.SetParent(t.transform, true);
            }
        }
    }
}
