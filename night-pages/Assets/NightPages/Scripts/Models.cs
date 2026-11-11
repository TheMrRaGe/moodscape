using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Loads the downloaded 3D models (Tools/fetch_models.py puts them in Resources/NPModels) and fits each
    /// one into the space the greybox piece used to occupy. If a model isn't installed, the caller falls
    /// back to a plain box, so the game always runs.
    /// </summary>
    public static class Models
    {
        /// <summary>
        /// Which way the models face after Unity imports them. If chairs, the bed and cabinets all come in
        /// facing the wall, flip this one switch.
        /// </summary>
        public static bool FrontIsPlusZ = false;

        /// <summary>
        /// Models whose front isn't where the rest face (they came from different makers): the extra turn, in degrees,
        /// that makes each one face the way it's asked to. Found by photographing every model facing the camera
        /// (the "studio" job's model chart).
        /// </summary>
        public static readonly Dictionary<string, float> YawFix = new Dictionary<string, float>
        {
            { "Shelf_01", 180f },                    // its white back faced the room
            { "painted_wooden_shelves", 180f },
            { "GothicCabinet_01", 90f },             // came in sideways
            { "painted_wooden_cabinet_02", 180f },   // the wardrobe: its back faced the room
            { "Rockingchair_01", -90f },             // came in sideways
            { "vintage_day_bed", 180f }, { "vintage_wooden_drawer_01", 180f }, { "painted_wooden_cabinet", 180f },
            { "vintage_telephone_wall_clock", 180f },
            { "wheelchair_01", 180f },
            { "BarberShopChair_01", 90f },
            // These came in back to front: sofas with their backs to the fire, the TV facing the wall.
            { "sofa_03", 180f }, { "Sofa_01", 180f }, { "sofa_02", 180f }, { "painted_wooden_sofa", 180f },
            { "ArmChair_01", 180f }, { "mid_century_lounge_chair", 180f }, { "dining_chair_02", 180f },
            { "Television_01", 180f },
        };

        public static float Fix(string id) { float f; return id != null && YawFix.TryGetValue(id, out f) ? f : 0f; }

        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();
        static readonly HashSet<string> missing = new HashSet<string>();

        public static void ResetStatics() { cache.Clear(); missing.Clear(); }

        public static GameObject Load(string id)
        {
            GameObject g;
            if (cache.TryGetValue(id, out g) && g != null) return g;
            if (missing.Contains(id)) return null;
            g = Resources.Load<GameObject>("NPModels/" + id + "/" + id + "_1k");
            if (g == null) { missing.Add(id); return null; }
            cache[id] = g;
            return g;
        }

        public static int Installed { get { return cache.Count; } }
        public static int Missing { get { return missing.Count; } }

        /// <summary>Yaw that makes a model face along (dx, dz).</summary>
        public static float Face(float dx, float dz)
        {
            float yaw = Mathf.Atan2(-dx, -dz) * Mathf.Rad2Deg;
            return FrontIsPlusZ ? yaw + 180f : yaw;
        }

        static bool WorldBounds(GameObject g, out Bounds b)
        {
            b = new Bounds();
            bool any = false;
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>
        /// Stand a model on the floor at a point. It is scaled evenly so it is `height` tall, or smaller if it
        /// would be wider than `maxWidth`. Returns null if the model isn't installed. `hit` is a box collider
        /// around it, for walking into and for attaching an Interactable.
        /// </summary>
        public static GameObject Place(string id, Transform parent, Vector3 floorPoint, float height, float yaw, out GameObject hit, bool solid = true, float maxWidth = 0f, bool centre = false)
        {
            hit = null;
            var prefab = Load(id);
            if (prefab == null) return null;

            var wrap = new GameObject(id);
            wrap.transform.SetParent(parent, false);
            wrap.transform.localPosition = floorPoint;
            wrap.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var inst = Object.Instantiate(prefab, wrap.transform);      // keeps whatever rotation and scale the import gave it
            inst.name = "Model";
            float fix = Fix(id);
            if (fix != 0f) inst.transform.localRotation = Quaternion.Euler(0f, fix, 0f) * inst.transform.localRotation;
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (var a in inst.GetComponentsInChildren<Animator>()) a.enabled = false;

            Bounds b;
            if (!WorldBounds(wrap, out b) || b.size.y < 1e-5f) { Object.Destroy(wrap); return null; }
            float s = height / b.size.y;
            if (maxWidth > 0f) s = Mathf.Min(s, maxWidth / Mathf.Max(b.size.x, b.size.z, 1e-5f));
            wrap.transform.localScale = Vector3.one * s;
            WorldBounds(wrap, out b);
            Vector3 target = parent.TransformPoint(floorPoint);
            Vector3 anchor = centre ? b.center : new Vector3(b.center.x, b.min.y, b.center.z);
            wrap.transform.position += target - anchor;
            WorldBounds(wrap, out b);

            if (solid)
            {
                hit = new GameObject(id + " collider");
                hit.transform.SetParent(parent, false);
                hit.transform.position = b.center;
                hit.AddComponent<BoxCollider>().size = b.size;
            }
            return wrap;
        }
    }

    public static class ModelsLoose
    {
        /// <summary>
        /// A model the player can pick up: one object carrying the model, a box collider, a Rigidbody and a
        /// Movable. Returns null if the model isn't installed.
        /// </summary>
        /// <summary>Sets that are really several things (a tea set is a pot and cups): each piece becomes its own object.</summary>
        static readonly HashSet<string> Sets = new HashSet<string>
        {
            "tea_set_01", "wine_bottles_01", "book_encyclopedia_set_01", "chess_set", "brass_candleholders", "throw_pillows_01",
            "office_notepads", "postcard_set_01", "rubber_boots", "decorative_book_set_01"
        };

        /// <summary>The other pieces of the last set placed (the first piece is what Place returns).</summary>
        public static readonly List<Movable> LastPieces = new List<Movable>();

        public static Movable Place(string id, Transform parent, Vector3 floorPoint, float height, float yaw, float mass, float maxWidth = 0f)
        {
            LastPieces.Clear();
            GameObject unused;
            var wrap = Models.Place(id, parent, floorPoint, height, 0f, out unused, false, maxWidth);
            if (wrap == null) return null;
            Bounds b = new Bounds();
            bool any = false;
            foreach (var r in wrap.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            float s = Mathf.Max(1e-5f, wrap.transform.lossyScale.x);
            var col = wrap.AddComponent<BoxCollider>();           // measured while unrotated, so it stays snug when turned
            col.center = (b.center - wrap.transform.position) / s;
            col.size = b.size / s;
            wrap.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            // Turning it about its origin moved the footprint; put the footprint back over the floor point.
            Vector3 want = parent.TransformPoint(floorPoint);
            Vector3 have = wrap.transform.TransformPoint(col.center);
            wrap.transform.position += new Vector3(want.x - have.x, 0f, want.z - have.z);
            if (Sets.Contains(id))
            {
                var pieces = Split(wrap, parent, id, mass);
                if (pieces.Count > 1)
                {
                    for (int i = 1; i < pieces.Count; i++) LastPieces.Add(pieces[i]);
                    return pieces[0];
                }
            }
            return Movable.Attach(wrap, mass);
        }

        // Separate the set into the things it is made of: renderers whose bounds touch belong to one piece.
        static List<Movable> Split(GameObject wrap, Transform parent, string id, float mass)
        {
            var result = new List<Movable>();
            var rends = new List<Renderer>(wrap.GetComponentsInChildren<Renderer>());
            if (rends.Count < 2) return result;
            var group = new int[rends.Count];
            for (int i = 0; i < group.Length; i++) group[i] = i;
            System.Func<int, int> root = null;
            root = i => group[i] == i ? i : (group[i] = root(group[i]));
            for (int i = 0; i < rends.Count; i++)
                for (int j = i + 1; j < rends.Count; j++)
                {
                    var bi = rends[i].bounds; bi.Expand(-0.004f);
                    if (bi.Intersects(rends[j].bounds)) group[root(i)] = root(j);
                }
            var groups = new Dictionary<int, List<Renderer>>();
            for (int i = 0; i < rends.Count; i++)
            {
                List<Renderer> l;
                if (!groups.TryGetValue(root(i), out l)) groups[root(i)] = l = new List<Renderer>();
                l.Add(rends[i]);
            }
            if (groups.Count < 2 || groups.Count > 48) return result;
            float total = 0f;
            var vols = new List<float>();
            foreach (var g in groups.Values)
            {
                Bounds gb = g[0].bounds;
                foreach (var r in g) gb.Encapsulate(r.bounds);
                float v = Mathf.Max(1e-6f, gb.size.x * gb.size.y * gb.size.z);
                vols.Add(v); total += v;
            }
            int k = 0;
            foreach (var g in groups.Values)
            {
                Bounds gb = g[0].bounds;
                foreach (var r in g) gb.Encapsulate(r.bounds);
                var piece = new GameObject(id.Replace('_', ' ') + " (" + (k + 1) + ")");
                piece.transform.SetParent(parent, true);
                piece.transform.position = gb.center;
                foreach (var r in g) r.transform.SetParent(piece.transform, true);
                var c = piece.AddComponent<BoxCollider>();
                // A little inside its outline, so pieces standing close together don't shove each other over.
                float shrink = gb.size.y < 0.03f ? 0.95f : 0.84f;
                c.center = Vector3.zero;
                c.size = new Vector3(Mathf.Max(0.01f, gb.size.x * shrink), Mathf.Max(0.01f, gb.size.y), Mathf.Max(0.01f, gb.size.z * shrink));
                var mv = Movable.Attach(piece, Mathf.Max(0.05f, mass * vols[k] / total));
                mv.Rest();                                    // stays as it was arranged until someone picks it up
                result.Add(mv);
                k++;
            }
            Object.Destroy(wrap);
            return result;
        }
    }

    /// <summary>Wallpaper, floorboards, plaster and concrete, generated in code so the house needs no texture files.</summary>
    public static class Tex
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static void ResetStatics() { cache.Clear(); photos.Clear(); }

        static Texture2D Make(string name, int n, System.Func<float, float, Color> f)
        {
            Texture2D t;
            if (cache.TryGetValue(name, out t) && t != null) return t;
            t = new Texture2D(n, n, TextureFormat.RGB24, true);
            t.wrapMode = TextureWrapMode.Repeat;
            t.anisoLevel = 4;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = f(x / (float)n, y / (float)n);
            t.SetPixels(px);
            t.Apply(true);
            cache[name] = t;
            return t;
        }

        // Perlin noise that wraps, so the texture tiles without seams.
        static float N(float u, float v, float freq, float seed)
        {
            float a = Mathf.PerlinNoise(u * freq + seed, v * freq + seed);
            float b = Mathf.PerlinNoise((u - 1f) * freq + seed, v * freq + seed);
            float c = Mathf.PerlinNoise(u * freq + seed, (v - 1f) * freq + seed);
            float d = Mathf.PerlinNoise((u - 1f) * freq + seed, (v - 1f) * freq + seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        /// <summary>Grandma's wallpaper: warm cream with a faded stripe and a small flower. One tile is about 1.2 m.</summary>
        public static Texture2D Wallpaper
        {
            get
            {
                return Make("wallpaper", 256, (u, v) =>
                {
                    float stripe = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 6f)) > 0.86f ? 1f : 0f;
                    float du = Mathf.Repeat(u * 6f, 1f) - 0.5f, dv = Mathf.Repeat(v * 5f + Mathf.Floor(u * 6f) * 0.5f, 1f) - 0.5f;
                    float r = Mathf.Sqrt(du * du * 2.2f + dv * dv), ang = Mathf.Atan2(dv, du * 1.5f);
                    float flower = Mathf.Clamp01((0.2f + 0.07f * Mathf.Cos(ang * 5f) - r) * 14f);
                    float heart = Mathf.Clamp01((0.06f - r) * 30f);
                    var c = Color.Lerp(new Color(0.78f, 0.72f, 0.58f), new Color(0.7f, 0.66f, 0.55f), stripe);
                    c = Color.Lerp(c, new Color(0.62f, 0.4f, 0.38f), flower * 0.75f);
                    c = Color.Lerp(c, new Color(0.8f, 0.68f, 0.35f), heart);
                    return c * (0.92f + 0.14f * N(u, v, 22f, 3f));
                });
            }
        }

        /// <summary>A patchwork quilt.</summary>
        public static Texture2D Quilt
        {
            get
            {
                Color[] patches =
                {
                    new Color(0.62f, 0.3f, 0.28f), new Color(0.75f, 0.68f, 0.5f), new Color(0.35f, 0.45f, 0.5f),
                    new Color(0.5f, 0.55f, 0.36f), new Color(0.78f, 0.6f, 0.4f), new Color(0.45f, 0.32f, 0.42f),
                };
                return Make("quilt", 256, (u, v) =>
                {
                    int px = (int)(u * 6f), py = (int)(v * 6f);
                    var c = patches[(px * 7 + py * 13 + px * py) % patches.Length];
                    float fu = Mathf.Repeat(u * 6f, 1f), fv = Mathf.Repeat(v * 6f, 1f);
                    float seam = Mathf.Min(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv)) < 0.04f ? 0.7f : 1f;
                    float tri = (px + py) % 2 == 0 && fu > fv ? 0.85f : 1f;
                    return c * seam * tri * (0.9f + 0.2f * N(u, v, 40f, 6f));
                });
            }
        }

        /// <summary>A worn patterned rug with a border.</summary>
        public static Texture2D Rug
        {
            get
            {
                return Make("rug", 256, (u, v) =>
                {
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    var c = new Color(0.45f, 0.14f, 0.12f);
                    if (edge < 0.05f) c = new Color(0.72f, 0.62f, 0.42f);
                    else if (edge < 0.11f) c = new Color(0.16f, 0.2f, 0.3f);
                    else
                    {
                        float d = Mathf.Abs(Mathf.Repeat(u * 5f, 1f) - 0.5f) + Mathf.Abs(Mathf.Repeat(v * 7f, 1f) - 0.5f);
                        if (d < 0.22f) c = new Color(0.72f, 0.62f, 0.42f);
                        else if (d < 0.36f) c = new Color(0.16f, 0.2f, 0.3f);
                    }
                    return c * (0.8f + 0.3f * N(u, v, 50f, 4f));
                });
            }
        }

        /// <summary>An old family photograph: sepia, an oval, two figures you can't quite make out.</summary>
        public static Texture2D Photo(int which)
        {
            return Make("photo" + which, 128, (u, v) =>
            {
                float du = (u - 0.5f) * 2f, dv = (v - 0.5f) * 2f;
                float oval = du * du * 1.3f + dv * dv;
                var paper = new Color(0.8f, 0.72f, 0.55f);
                if (oval > 0.8f) return paper * (0.9f + 0.1f * N(u, v, 30f, which));
                float fig = 0f;
                for (int k = 0; k < 2 + which % 2; k++)
                {
                    float cx = -0.35f + 0.35f * k + 0.1f * (which % 3), h = 0.25f - 0.18f * ((k + which) % 2);
                    float head = (du - cx) * (du - cx) * 9f + (dv - h) * (dv - h) * 9f;
                    float body = Mathf.Abs(du - cx) < 0.16f && dv < h - 0.12f ? 1f : 0f;
                    if (head < 0.22f || body > 0f) fig = 1f;
                }
                var c = Color.Lerp(new Color(0.55f, 0.47f, 0.34f), new Color(0.2f, 0.16f, 0.11f), fig);
                return c * (0.8f + 0.3f * N(u, v, 14f, which * 3f));
            });
        }

        /// <summary>Dark floorboards. One tile is about 2 m, eight boards across.</summary>
        public static Texture2D Boards
        {
            get
            {
                return Make("boards", 256, (u, v) =>
                {
                    float board = Mathf.Floor(u * 8f);
                    float along = v + board * 0.37f;
                    float grain = N(u * 6f, along, 5f, board * 1.7f) * 0.5f + N(u * 30f, along, 4f, 9f) * 0.5f;
                    float gap = Mathf.Min(Mathf.Repeat(u * 8f, 1f), 1f - Mathf.Repeat(u * 8f, 1f)) < 0.035f ? 0.35f : 1f;
                    float end = Mathf.Abs(Mathf.Repeat(along * 1.0f, 1f) - 0.5f) > 0.492f ? 0.4f : 1f;
                    float tone = 0.75f + 0.25f * Mathf.PerlinNoise(board * 0.9f, 2f);
                    return new Color(0.3f, 0.19f, 0.11f) * (0.65f + 0.5f * grain) * gap * end * tone;
                });
            }
        }

        public static Texture2D Plaster
        {
            get
            {
                return Make("plaster", 256, (u, v) =>
                {
                    float n = N(u, v, 6f, 5f) * 0.6f + N(u, v, 28f, 2f) * 0.4f;
                    float crack = Mathf.Abs(N(u, v, 4f, 30f) - 0.5f) < 0.0025f ? 0.9f : 1f;      // hairline, not dirt
                    return new Color(0.6f, 0.58f, 0.53f) * (0.9f + 0.15f * n) * crack;
                });
            }
        }

        public static Texture2D Concrete
        {
            get
            {
                return Make("concrete", 256, (u, v) =>
                {
                    float n = N(u, v, 5f, 8f) * 0.5f + N(u, v, 40f, 1f) * 0.5f;
                    float damp = Mathf.Clamp01(N(u, v, 2f, 17f) * 1.8f - 0.8f);
                    return Color.Lerp(new Color(0.3f, 0.3f, 0.29f) * (0.7f + 0.45f * n), new Color(0.1f, 0.11f, 0.1f), damp * 0.7f);
                });
            }
        }

        static readonly Dictionary<string, Texture2D> photos = new Dictionary<string, Texture2D>();

        static Texture2D Photo(string id, string kind)
        {
            string key = id + "_" + kind;
            Texture2D t;
            if (photos.TryGetValue(key, out t)) return t;
            t = Resources.Load<Texture2D>("NPTex/" + id + "/" + key);
            photos[key] = t;
            return t;
        }

        /// <summary>True if the photo texture with this Poly Haven id was downloaded.</summary>
        public static bool Has(string id) { return Photo(id, "diff") != null; }

        /// <summary>
        /// A material from a downloaded photo texture (colour + normal map), repeated so one tile covers
        /// `tileMetres`. Falls back to `fallback` (a generated texture) when the photo isn't installed.
        /// </summary>
        public static Material Real(string id, float metresU, float metresV, float tileMetres, Color tint, Texture2D fallback = null, float smooth = 0.12f, float bump = 1f)
        {
            var diff = Photo(id, "diff");
            var m = Build.Mat(tint, 0f, smooth);
            m.mainTexture = diff != null ? diff : fallback;
            var scale = new Vector2(Mathf.Max(0.05f, metresU / tileMetres), Mathf.Max(0.05f, metresV / tileMetres));
            m.mainTextureScale = scale;
            var nor = diff != null ? Photo(id, "nor_gl") : null;
            if (nor != null) Bump(m, nor, scale, bump);
            return m;
        }

        public static void Bump(Material m, Texture2D normal, Vector2 scale, float strength)
        {
            if (!m.HasProperty("_BumpMap")) return;
            m.SetTexture("_BumpMap", normal);
            m.SetTextureScale("_BumpMap", scale);
            if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", strength);
            m.EnableKeyword("_NORMALMAP");
        }

        /// <summary>
        /// A normal map made from a height function, for the generated textures (the monster's skins).
        /// Packed so Unity's UnpackNormal reads it correctly on any platform.
        /// </summary>
        public static Texture2D NormalFrom(string name, int n, System.Func<float, float, float> height, float strength)
        {
            Texture2D t;
            if (cache.TryGetValue(name, out t) && t != null) return t;
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    h[y * n + x] = height(x / (float)n, y / (float)n);
            t = new Texture2D(n, n, TextureFormat.RGBA32, true, true);
            t.wrapMode = TextureWrapMode.Repeat;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float l = h[y * n + (x + n - 1) % n], r = h[y * n + (x + 1) % n];
                    float d = h[((y + n - 1) % n) * n + x], u = h[((y + 1) % n) * n + x];
                    var nv = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                    float nx = nv.x * 0.5f + 0.5f, ny = nv.y * 0.5f + 0.5f;
                    px[y * n + x] = new Color(nx, ny, nv.z * 0.5f + 0.5f, 1f);     // R = x and A = 1, so R*A also gives x
                }
            t.SetPixels(px);
            t.Apply(true);
            cache[name] = t;
            return t;
        }

        /// <summary>A material showing one of the textures above, repeated to suit a surface of the given size in metres.</summary>
        public static Material Mat(Texture2D t, float metresU, float metresV, float tileMetres, Color tint)
        {
            var m = Build.Mat(tint);
            m.mainTexture = t;
            m.mainTextureScale = new Vector2(Mathf.Max(0.05f, metresU / tileMetres), Mathf.Max(0.05f, metresV / tileMetres));
            return m;
        }
    }
}
