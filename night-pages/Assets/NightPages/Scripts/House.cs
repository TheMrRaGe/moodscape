using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    public class Room
    {
        public string name;
        public Vector3 center;
        public Bounds bounds;
        public Light light;
        public float baseIntensity;
        public Vector3 notePoint;
        public Door door;
        public GameObject seal;        // a stretch of wall that can replace the doorway ("the door is gone")
        public float darkUntil;        // a haunting can kill this room's light for a while
        public bool lit;               // its lamp is switched on
        public Light extra;            // a second lamp on the same switch (the hall has two)
        public Interactable lightSwitch;
    }

    /// <summary>
    /// The grandparents' house. An ordinary old house, kept the way they kept it: floral wallpaper, rugs,
    /// family photographs, Grandpa's armchair, Grandma's rocking chair, a quilt on the bed. Nothing about it
    /// looks haunted; that is the point. Furniture uses the downloaded models when they are installed and
    /// plain boxes when they aren't. Small things can be picked up and moved.
    /// Layout: a central hall with a study, kitchen, bedroom and living room, and a cellar underneath.
    /// </summary>
    public partial class House : MonoBehaviour
    {
        public static House I;

        public readonly List<Room> rooms = new List<Room>();
        public readonly List<Door> doors = new List<Door>();
        public readonly Dictionary<string, Interactable> choreTargets = new Dictionary<string, Interactable>();
        public readonly List<Renderer> paintings = new List<Renderer>();
        public readonly List<Movable> movables = new List<Movable>();
        public Interactable typewriter, bed, shelf, cellar, leave, cellarStairs, cellarFloor;
        public Movable armchair, rocker;
        // Things the frights use: the hall telephone, the television, the projector, the wheelchair, the mirror...
        public Interactable phone;
        public Transform phoneBody, wardrobe, bedTop;
        public Renderer tvScreen, projection;
        public Light tvLight, projectorLight;
        public Transform projector;
        public Movable wheelchair;
        public readonly List<Movable> kitchenChairs = new List<Movable>(), food = new List<Movable>(), standingPhotos = new List<Movable>();
        public readonly List<Material> photoMats = new List<Material>();
        public Mirror hallMirror;
        public Material calendar;
        public Vector3 bedSpawn = new Vector3(4.2f, 0.05f, 3.0f);
        public Vector3 hallNorth = new Vector3(0f, 0f, 7f), hallSouth = new Vector3(0f, 0f, -7f);
        public static readonly Vector3 CellarOrigin = new Vector3(0f, -40f, 0f);
        public Vector3 cellarSpawn = CellarOrigin + new Vector3(0f, 0.05f, 3f);
        public Light fire;
        public Room hall;
        public Door frontDoor;
        AudioSource clockTick, fireLoop;
        float nextSettle;

        const float H = 2.8f, T = 0.2f, Gap = 1.2f, DoorH = 2.1f;
        static readonly Color Wood = new Color(0.36f, 0.24f, 0.15f), DarkWood = new Color(0.2f, 0.13f, 0.09f),
            Cream = new Color(0.86f, 0.82f, 0.72f), Dark = new Color(0.08f, 0.07f, 0.07f);

        class Window { public Material pane; public Light glow; }
        readonly List<Material> shades = new List<Material>();
        readonly List<Vector3> shadeAt = new List<Vector3>();
        readonly List<Window> windows = new List<Window>();
        /// <summary>Every window: its centre and the direction into the room (the hallucinations use these).</summary>
        public readonly List<KeyValuePair<Vector3, Vector3>> windowSpots = new List<KeyValuePair<Vector3, Vector3>>();
        readonly List<Vector3> nav = new List<Vector3>();
        readonly List<int[]> navLinks = new List<int[]>();
        Material paintMat;
        Light cellarBulb;
        float bright = 1f;
        int time, day = 1;
        float settleAt = -1f;

        void Awake() { I = this; }

        // ---------------------------------------------------------------- the layout: which corner each room is in

        /// <summary>The four rooms off the hall. Each is drawn once (below) in the corner named here, and each playthrough
        /// puts them in a shuffled order: a room in a different corner is mirrored, so everything in it keeps its place
        /// relative to its own door, walls and windows (the bed still against the far wall, the desk under the window).</summary>
        public static readonly string[] RoomNames = { "study", "kitchen", "bedroom", "living room" };
        static readonly Vector2[] DrawnIn = { new Vector2(-1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, 1f), new Vector2(1f, -1f) };
        static readonly string[] CornerNames = { "front left", "back left", "front right", "back right" };   // as seen from the drive

        /// <summary>Force a layout (0-23) instead of a random one; -1 = random. Tests set this; so does -nplayout N.</summary>
        public static int ForceLayout = -1;
        public int Layout { get; private set; }
        public string LayoutDescription { get; private set; }
        readonly Dictionary<string, Vector2> mirror = new Dictionary<string, Vector2>();
        float mx = 1f, mz = 1f;           // the mirroring in force while a room is being built
        /// <summary>Where the chimney meets the outside wall (the fireplace moves with the living room).</summary>
        public Vector3 chimney;
        /// <summary>Furniture the layout check looks at: big things that shouldn't block doors, windows or each other.</summary>
        public readonly List<GameObject> furniture = new List<GameObject>();
        readonly List<Collider> walls = new List<Collider>();

        void ChooseLayout()
        {
            int pick = ForceLayout >= 0 ? ForceLayout % 24 : Random.Range(0, 24);
            Layout = pick;
            // The pick-th permutation of the four corners.
            var corners = new List<int> { 0, 1, 2, 3 };
            var order = new int[4];
            int k = pick;
            for (int i = 0; i < 4; i++)
            {
                int f = Factorial(3 - i), idx = k / f;
                k %= f;
                order[i] = corners[idx];
                corners.RemoveAt(idx);
            }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                Vector2 to = DrawnIn[order[i]], from = DrawnIn[i];
                mirror[RoomNames[i]] = new Vector2(to.x / from.x, to.y / from.y);
                sb.Append(i == 0 ? "" : ", ").Append(RoomNames[i]).Append(" ").Append(CornerNames[order[i]]);
            }
            LayoutDescription = sb.ToString();
        }

        static int Factorial(int n) { return n <= 1 ? 1 : n * Factorial(n - 1); }

        void Use(string room) { var m = mirror[room]; mx = m.x; mz = m.y; }
        void Plain() { mx = mz = 1f; }
        Vector3 P(Vector3 v) { return new Vector3(v.x * mx, v.y, v.z * mz); }
        Vector3 P(float x, float y, float z) { return new Vector3(x * mx, y, z * mz); }
        float Yaw(float y)
        {
            if (mx > 0f && mz > 0f) return y;
            float r = y * Mathf.Deg2Rad;
            return Mathf.Atan2(Mathf.Sin(r) * mx, Mathf.Cos(r) * mz) * Mathf.Rad2Deg;
        }

        /// <summary>A point given as it is in the room's drawing, where it is this playthrough.</summary>
        public Vector3 InRoom(string room, Vector3 drawn)
        {
            Vector2 m;
            if (!mirror.TryGetValue(room, out m)) return drawn;
            return new Vector3(drawn.x * m.x, drawn.y, drawn.z * m.y);
        }

        public Room Find(string room) { return rooms.Find(r => r.name == room); }

        // ---------------------------------------------------------------- placing things (all in the current room's drawing)

        static Vector3 Floor(Vector3 boxPos, Vector3 boxSize) { return new Vector3(boxPos.x, boxPos.y - boxSize.y / 2f, boxPos.z); }

        GameObject Box(string name, Vector3 pos, Vector3 size, Color c, bool solid = true) { return Build.Box(name, transform, P(pos), size, c, solid); }
        GameObject Box(string name, Vector3 pos, Vector3 size, Material m, bool solid = true) { return Build.Box(name, transform, P(pos), size, m, solid); }

        /// <summary>A fixed piece of furniture: the model if it's installed, otherwise a box of the same size.</summary>
        GameObject Furn(string model, string name, Vector3 pos, Vector3 size, Color c, float yaw = 0f)
        {
            // Nobody sets furniture down dead square: a degree or three off, more for the free-standing pieces.
            bool againstWall = false;
            foreach (var w in new[] { "Clock", "Fridge", "Cabinet", "Wardrobe", "Bookshelf", "Dresser", "Radio", "Console", "Heater", "Sideboard", "Shelves", "Desk", "Pantry", "Hall table", "Nightstand" })
                if (name.Contains(w)) againstWall = true;
            yaw += Lived(againstWall ? 0.8f : 3.5f);
            string asked = model;
            model = Catalogue.Swap(model);
            // A swapped piece is held to the footprint the room was planned for (the original may overhang a little).
            float fit = model != asked ? 1.0f : 1.25f;
            GameObject hit, made;
            if (model != null && Models.Place(model, transform, P(Floor(pos, size)), ModelSizes.Height(model, size.y), Yaw(yaw), out hit, true, Mathf.Max(size.x, size.z) * fit) != null)
            {
                hit.name = name;
                made = hit;
            }
            else made = Build.Box(name, transform, P(pos), size, c);
            furniture.Add(made);
            return made;
        }

        /// <summary>Something that can be picked up and moved.</summary>
        Movable Loose(string model, string name, Vector3 floorPoint, float height, Vector3 fallbackSize, Color c, float yaw, float mass)
        {
            string asked = model;
            model = Catalogue.Swap(model);
            if (model != asked && model != null) height = ModelSizes.Height(model, height);
            // Put down by a person: chairs left where someone got up (turned, pushed back a little), small things at
            // whatever angle they landed and a finger's width off where they 'should' be.
            if (mass >= 4f) { yaw += Lived(12f); floorPoint += new Vector3(Random.Range(-0.06f, 0.06f), 0f, Random.Range(-0.06f, 0.06f)); }
            else if (mass < 3f) { yaw += Lived(35f); floorPoint += new Vector3(Random.Range(-0.025f, 0.025f), 0f, Random.Range(-0.025f, 0.025f)); }
            Vector3 at = P(floorPoint);
            yaw = Yaw(yaw);
            Movable m = model == null ? null : ModelsLoose.Place(model, transform, at, ModelSizes.Height(model, height), yaw, mass, Mathf.Max(fallbackSize.x, fallbackSize.z) * 1.3f);
            if (m == null)
            {
                var g = Build.Box(name, transform, at + Vector3.up * fallbackSize.y / 2f, fallbackSize, c);
                g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                m = Movable.Attach(g, mass);
            }
            m.gameObject.name = name;
            movables.Add(m);
            // A set came apart into its pieces: every one is its own thing to pick up.
            if (model != null)
                foreach (var piece in ModelsLoose.LastPieces) { movables.Add(piece); }
            if (mass >= 4f) furniture.Add(m.gameObject);       // chairs and boxes count as furniture for the layout check
            return m;
        }

        /// <summary>Clutter: a small thing at its real size, sitting on whatever surface is at `at`.</summary>
        Movable Thing(string model, Vector3 at, float yaw = 0f, float mass = 0.4f, float maxWidth = 0f)
        {
            var size = ModelSizes.All.ContainsKey(model) ? ModelSizes.All[model] : new Vector3(0.15f, 0.15f, 0.15f);
            if (maxWidth > 0f) size = size * Mathf.Min(1f, maxWidth / Mathf.Max(size.x, size.z));
            return Loose(model, model.Replace('_', ' '), at, size.y, size, new Color(0.5f, 0.45f, 0.4f), yaw, mass);
        }

        /// <summary>A fixed thing at its real size: on a wall, on the floor, wherever `at` is.</summary>
        GameObject Fixed(string model, Vector3 at, float yaw = 0f, float maxWidth = 0f, bool onFloor = true)
        {
            var size = ModelSizes.All.ContainsKey(model) ? ModelSizes.All[model] : new Vector3(0.4f, 0.4f, 0.4f);
            if (maxWidth > 0f) size = size * Mathf.Min(1f, maxWidth / Mathf.Max(size.x, size.z));
            GameObject hit;
            var wrap = Models.Place(model, transform, P(at), size.y, Yaw(yaw + (onFloor ? Lived(2f) : 0f)), out hit, true, Mathf.Max(size.x, size.z) * 1.05f);
            if (wrap != null)
            {
                if (onFloor) furniture.Add(hit);
                // On a wall: hung by eye, never quite level (turned about the way it faces).
                else wrap.transform.Rotate(Vector3.forward, Lived(2.5f), Space.Self);
                return hit;
            }
            return null;
        }

        /// <summary>A small human error: an angle within +-`degrees`, more often small than large.</summary>
        static float Lived(float degrees) { return (Random.value + Random.value - 1f) * degrees; }

        /// <summary>The top of a placed piece of furniture: where clutter sits on it.</summary>
        static float Top(GameObject g, float fallback)
        {
            if (g == null) return fallback;
            var c = g.GetComponent<Collider>();
            return c != null ? c.bounds.max.y : fallback;
        }

        static readonly Color AgedPaper = new Color(0.72f, 0.66f, 0.58f);     // decades of lamp smoke and sun

        void Rug(Vector3 center, float w, float d)
        {
            // A worn wool rug: a dark border and a faded field, both from the photographed carpet.
            var border = Tex.Real("dirty_carpet", w, d, 0.6f, new Color(0.42f, 0.27f, 0.22f), Tex.Rug, 0.05f, 0.8f);
            var field = Tex.Real("dirty_carpet", w - 0.24f, d - 0.24f, 0.6f, new Color(0.85f, 0.5f, 0.4f), Tex.Rug, 0.05f, 0.8f);
            Box("Rug", new Vector3(center.x, 0.010f, center.z), new Vector3(w, 0.016f, d), border, false);
            Box("Rug", new Vector3(center.x, 0.012f, center.z), new Vector3(w - 0.24f, 0.018f, d - 0.24f), field, false);
        }

        // A picture on a wall. `normal` points into the room. Live paintings show what Moodscape paints.
        void Picture(Vector3 pos, Vector3 normal, float w, float h, Material m, bool live)
        {
            pos = P(pos); normal = P(normal);
            bool alongZ = Mathf.Abs(normal.x) > 0.5f;
            Vector3 frame = alongZ ? new Vector3(0.05f, h + 0.12f, w + 0.12f) : new Vector3(w + 0.12f, h + 0.12f, 0.05f);
            Vector3 inner = alongZ ? new Vector3(0.05f, h, w) : new Vector3(w, h, 0.05f);
            var fr = Build.Box("Frame", transform, pos, frame, DarkWood, false);
            var g = Build.Box(live ? "Painting" : "Photograph", transform, pos + normal * 0.015f, inner, m, false);
            // Hung by eye: a degree or two off level.
            float tilt = Lived(2.2f);
            fr.transform.RotateAround(pos, normal, tilt);
            g.transform.RotateAround(pos, normal, tilt);
            KeyValuePair<string, Texture2D> pt;
            if (m != null && paintedOn.TryGetValue(m, out pt)) Painted.Hang(g.GetComponent<Renderer>(), pt.Key, pt.Value);
            if (live) paintings.Add(g.GetComponent<Renderer>());
        }

        readonly Dictionary<Material, KeyValuePair<string, Texture2D>> paintedOn = new Dictionary<Material, KeyValuePair<string, Texture2D>>();

        Material PhotoMat(int which)
        {
            var m = Build.Mat(Color.white);
            m.mainTexture = Tex.Photo(which);
            // Most runs, the painter's pictures instead: old photographs, and here and there a portrait.
            string kind = which % 3 == 2 ? "portraits" : "photos";
            if (Painted.Count(kind) > 0 && Catalogue.Roll() < 0.8)
            {
                // The ones the fear engine expects to get to you, mostly.
                var brain = GameDirector.I != null && GameDirector.I.fear != null ? GameDirector.I.fear.brain : null;
                var tex = Painted.Choose(kind, brain, new System.Random(Catalogue.Next(1 << 30)));
                m.mainTexture = tex;
                paintedOn[m] = new KeyValuePair<string, Texture2D>(kind, tex);
            }
            photoMats.Add(m);
            return m;
        }

        // ---------------------------------------------------------------- small things made here (no model for them)

        // An old rotary telephone: base, dial, handset in its cradle.
        Transform RotaryPhone(Vector3 at, float yaw)
        {
            var root = new GameObject("Telephone").transform;
            root.SetParent(transform, false);
            root.localPosition = P(at);
            root.localRotation = Quaternion.Euler(0f, Yaw(yaw), 0f);
            var black = Build.Mat(new Color(0.03f, 0.03f, 0.03f), 0f, 0.7f);
            Build.Box("Phone base", root, new Vector3(0f, 0.045f, 0f), new Vector3(0.2f, 0.09f, 0.23f), black, false);
            var dial = Build.Prim(PrimitiveType.Cylinder, "Dial", root, new Vector3(0f, 0.08f, 0.05f), new Vector3(0.12f, 0.005f, 0.12f), Build.Mat(new Color(0.85f, 0.83f, 0.78f), 0f, 0.5f), false);
            dial.transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);
            var hs = Build.Prim(PrimitiveType.Capsule, "Handset", root, new Vector3(0f, 0.12f, -0.03f), new Vector3(0.06f, 0.12f, 0.06f), black, false);
            hs.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var col = Build.Box("Telephone", root, new Vector3(0f, 0.08f, 0f), new Vector3(0.24f, 0.16f, 0.26f), black);
            col.GetComponent<Renderer>().enabled = false;
            phone = Build.Usable(col, "Answer the telephone");
            return root;
        }

        // Unity's cylinder comes with a rounded collider; a tin or an ashtray needs a flat bottom to stand on.
        static void FlatBottom(GameObject g)
        {
            var cap = g.GetComponent<CapsuleCollider>();
            if (cap != null) Object.DestroyImmediate(cap);
            var box = g.AddComponent<BoxCollider>();
            box.size = new Vector3(0.92f, 2f, 0.92f);
        }

        // A thin round of white lace under whatever stands on it.
        void Doily(Vector3 at, float d)
        {
            Build.Prim(PrimitiveType.Cylinder, "Doily", transform, P(at) + Vector3.up * 0.002f, new Vector3(d, 0.002f, d), Build.Mat(new Color(0.92f, 0.9f, 0.84f), 0f, 0.05f), false);
        }

        // A glass ashtray, and what's in it.
        void Ashtray(Vector3 at)
        {
            var tray = Build.Prim(PrimitiveType.Cylinder, "Ashtray", transform, P(at) + Vector3.up * 0.017f, new Vector3(0.13f, 0.015f, 0.13f), Build.Mat(new Color(0.55f, 0.42f, 0.25f), 0f, 0.9f), true);
            var butt = Build.Box("Cigarette end", tray.transform, Vector3.zero, Vector3.one, new Color(0.85f, 0.8f, 0.7f), false);
            butt.transform.position = P(at) + new Vector3(0.02f, 0.036f, 0.01f);
            butt.transform.localScale = new Vector3(0.05f / 0.13f, 0.012f / 0.015f, 0.012f / 0.13f);
            FlatBottom(tray);
            movables.Add(Movable.Attach(tray, 0.4f));
        }

        // A stack of old magazines.
        void Magazines(Vector3 at, int n)
        {
            var rng = new System.Random(n * 31 + (int)(at.x * 10f));
            Color[] covers = { new Color(0.7f, 0.2f, 0.15f), new Color(0.85f, 0.8f, 0.6f), new Color(0.2f, 0.35f, 0.55f), new Color(0.9f, 0.88f, 0.84f), new Color(0.35f, 0.5f, 0.3f) };
            for (int i = 0; i < n; i++)
            {
                var g = Build.Box("Magazine", transform, P(at) + new Vector3(0f, 0.006f + i * 0.012f, 0f), new Vector3(0.21f, 0.008f, 0.28f), covers[rng.Next(covers.Length)], true);
                g.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 30f - 15f, 0f);
                movables.Add(Movable.Attach(g, 0.15f));
            }
        }

        // The month on the kitchen wall, with the days she crossed off.
        static Texture2D CalendarTex(int crossed)
        {
            const int W = 64, Hh = 84;
            var t = new Texture2D(W, Hh, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color[W * Hh];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.93f, 0.91f, 0.85f);
            for (int y = Hh - 30; y < Hh - 2; y++) for (int x = 2; x < W - 2; x++) px[y * W + x] = new Color(0.55f, 0.6f, 0.5f);   // the picture of the month
            for (int y = Hh - 36; y < Hh - 32; y++) for (int x = 6; x < W - 6; x++) px[y * W + x] = new Color(0.65f, 0.1f, 0.08f);  // the month's name
            int day = 0;
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 7; col++)
                {
                    int x0 = 4 + col * 8, y0 = Hh - 46 - row * 9;
                    for (int y = y0; y < y0 + 7; y++) for (int x = x0; x < x0 + 7; x++)
                        if (x == x0 || y == y0) px[y * W + x] = new Color(0.6f, 0.58f, 0.54f);
                    px[(y0 + 5) * W + x0 + 2] = new Color(0.2f, 0.2f, 0.2f);
                    day++;
                    if (day <= crossed)
                        for (int k = 1; k < 6; k++) { px[(y0 + k) * W + x0 + k] = new Color(0.1f, 0.1f, 0.12f); px[(y0 + k) * W + x0 + 6 - k] = new Color(0.1f, 0.1f, 0.12f); }
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>The hall clock's ticking, on or off (it stops, sometimes).</summary>
        public void Clock(bool on) { if (clockTick != null) { if (on && !clockTick.isPlaying) clockTick.Play(); else if (!on) clockTick.Stop(); } }

        /// <summary>Cross off days on the kitchen calendar (a fright can cross off all of them).</summary>
        public void CrossOff(int days) { if (calendar != null) calendar.mainTexture = CalendarTex(days); }

        // A window on an outer wall: a pane that glows with the time of day, lace curtains, and the light it lets in.
        // The second window in a room lets light in without casting shadows (eight shadowed lights would be too many).
        void AddWindow(Vector3 pos, Vector3 normal, bool shadows = true)
        {
            pos = P(pos); normal = P(normal);
            bool alongZ = Mathf.Abs(normal.x) > 0.5f;
            var pane = Build.Mat(new Color(0.8f, 0.85f, 0.9f), 1f);
            Vector3 size = alongZ ? new Vector3(0.04f, 1.2f, 1.1f) : new Vector3(1.1f, 1.2f, 0.04f);
            Build.Box("Window", transform, pos, size, pane, false);
            Vector3 along = alongZ ? Vector3.forward : Vector3.right;
            Build.Box("Sash", transform, pos + normal * 0.02f, new Vector3(0.05f, 1.26f, 0.05f), Cream, false);
            Build.Box("Sash", transform, pos + normal * 0.02f, alongZ ? new Vector3(0.05f, 0.05f, 1.16f) : new Vector3(1.16f, 0.05f, 0.05f), Cream, false);
            Build.Box("Sill", transform, pos + normal * 0.07f - Vector3.up * 0.65f, alongZ ? new Vector3(0.16f, 0.05f, 1.3f) : new Vector3(1.3f, 0.05f, 0.16f), Cream, false);
            var lace = new Color(0.9f, 0.88f, 0.8f);
            Build.Box("Curtain", transform, pos + normal * 0.06f + along * 0.62f, alongZ ? new Vector3(0.04f, 1.5f, 0.28f) : new Vector3(0.28f, 1.5f, 0.04f), lace, false);
            Build.Box("Curtain", transform, pos + normal * 0.06f - along * 0.62f, alongZ ? new Vector3(0.04f, 1.5f, 0.28f) : new Vector3(0.28f, 1.5f, 0.04f), lace, false);
            Build.Box("Curtain rail", transform, pos + normal * 0.07f + Vector3.up * 0.76f, alongZ ? new Vector3(0.04f, 0.04f, 1.6f) : new Vector3(1.6f, 0.04f, 0.04f), DarkWood, false);
            // Daylight through the glass: a soft beam angled down into the room.
            var glow = Build.Lamp("Window light", transform, pos + normal * 0.12f + Vector3.up * 0.45f, new Color(0.88f, 0.92f, 1f), 1f, 9f, shadows);
            glow.type = LightType.Spot;
            glow.spotAngle = 125f;
            glow.innerSpotAngle = 40f;
            glow.shadowStrength = 0.85f;
            glow.transform.rotation = Quaternion.LookRotation(normal * 1f + Vector3.down * 0.8f);
            windows.Add(new Window { pane = pane, glow = glow });
            windowSpots.Add(new KeyValuePair<Vector3, Vector3>(pos, normal));
        }

        /// <summary>The glass of the window nearest a point (the hallucinations draw on it).</summary>
        public Material PaneNear(Vector3 p)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < windowSpots.Count; i++)
            {
                float d = (windowSpots[i].Key - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best >= 0 ? windows[best].pane : null;
        }

        // A ceiling lamp with a fabric shade.
        void Shade(Vector3 pos)
        {
            Box("Lamp cord", pos + Vector3.up * 0.2f, new Vector3(0.02f, 0.4f, 0.02f), Dark, false);
            var m = Build.Mat(new Color(0.78f, 0.66f, 0.48f), 0.5f, 0.1f);
            shades.Add(m);
            shadeAt.Add(P(pos));
            var shade = Build.Prim(PrimitiveType.Cylinder, "Lamp shade", transform, P(pos), new Vector3(0.42f, 0.12f, 0.42f), m, false);
            shade.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;      // the bulb is inside it
        }

        // ---------------------------------------------------------------- the build

        // This run's floor and wallpaper (see PickSurfaces).
        string floorTex = "old_wood_floor", wallpaperTex;
        Color paperTint;

        static readonly string[] FloorChoices = { "old_wood_floor", "dark_wooden_planks", "diagonal_parquet", "plank_flooring", "plank_flooring_02", "brown_planks_05" };
        static readonly string[] PaperChoices = { "Wallpaper001A", "Wallpaper001B", "Wallpaper001C", "Wallpaper002A", "Wallpaper002B", "Wallpaper002C" };

        void PickSurfaces()
        {
            var options = new List<string>();
            foreach (var f in FloorChoices) if (Tex.Has(f)) options.Add(f);
            floorTex = options.Count > 0 ? options[Catalogue.Next(options.Count)] : "old_wood_floor";
            options.Clear();
            foreach (var w in PaperChoices) if (Tex.Has(w)) options.Add(w);
            // Now and then the old generated paper; mostly one of the real ones, aged a different amount each time.
            wallpaperTex = options.Count > 0 && Catalogue.Roll() < 0.8 ? options[Catalogue.Next(options.Count)] : null;
            float age = (float)Catalogue.Roll();
            paperTint = Color.Lerp(new Color(0.92f, 0.88f, 0.8f), new Color(0.7f, 0.64f, 0.55f), age);
        }

        Material Paper(float u, float v)
        {
            if (wallpaperTex != null) return Tex.Real(wallpaperTex, u, v, 1.0f, paperTint, Tex.Wallpaper, 0.08f, 0.6f);
            return Tex.Mat(Tex.Wallpaper, u, v, 1.2f, AgedPaper);
        }

        public void BuildAll(Material paintingMat)
        {
            paintMat = paintingMat;
            Catalogue.NewRun(Random.Range(1, int.MaxValue));
            PickSurfaces();
            ChooseLayout();
            Plain();
            var t = transform;
            // Floors: old boards in the rooms, worn boards in the hall. Real photo textures. (The kitchen tiles go with the kitchen.)
            Build.Box("Floor", t, new Vector3(0f, -0.1f, 0f), new Vector3(15.4f, 0.2f, 16.4f), Tex.Real(floorTex, 15.4f, 16.4f, 2.2f, new Color(0.85f, 0.78f, 0.72f), Tex.Boards, 0.25f));
            Build.Box("Hall floor", t, new Vector3(0f, 0.002f, 0f), new Vector3(2.9f, 0.004f, 15.9f), Tex.Real("wood_floor_worn", 2.9f, 15.9f, 2.2f, new Color(0.8f, 0.72f, 0.65f), Tex.Boards, 0.25f), false);
            Build.Box("Ceiling", t, new Vector3(0f, H + 0.1f, 0f), new Vector3(15.4f, 0.2f, 16.4f), Tex.Real("white_plaster_rough_01", 15.4f, 16.4f, 2.5f, new Color(0.95f, 0.92f, 0.86f), Tex.Plaster));

            // Outer walls.
            WallZ(-7.5f, -8f, 8f);
            WallZ(7.5f, -8f, 8f);
            WallX(8f, -7.5f, 7.5f, 0f);            // the front door
            WallX(-8f, -7.5f, 7.5f);
            // Hall walls, with a doorway into each room.
            WallZ(-1.5f, -8f, 8f, -4f, 4f);
            WallZ(1.5f, -8f, 8f, -4f, 4f);
            // Walls between the front and back rooms.
            WallX(0f, -7.5f, -1.5f);
            WallX(0f, 1.5f, 7.5f);

            hall = AddRoom("hall", new Vector3(0f, 0f, 0f), new Vector3(3f, H, 16f), new Vector3(-1.15f, 0.97f, -2.1f), 0f, 0f, Vector3.zero);

            BuildHall();
            BuildStudy();
            BuildKitchen();
            BuildLiving();
            BuildBedroom();
            Plain();
            PorchKit();
            Switches();
            BuildCellar();
            BuildClutter();
            Scatter();
            BuildDirt();
            BuildNav();

            SetTime(0, 1);
            settleAt = Time.time + 2.5f;
            GameDirector.I.Log("house: this run's things: " + Catalogue.Swapped + " pieces of furniture swapped for others, floor '" + floorTex + "', wallpaper '" + (wallpaperTex ?? "generated") + "', " + scattered + " small things scattered");
            GameDirector.I.Log("house: layout " + Layout + " of 24 (" + LayoutDescription + "); " + Models.Installed + " downloaded models in use, " +
                               Models.Missing + " missing (boxes used instead), " + movables.Count + " things you can move");
            foreach (var problem in CheckLayout()) GameDirector.I.Log("house: LAYOUT PROBLEM: " + problem);
        }

        int scattered;

        // Small things on top of the furniture: different on every run. Each table, cabinet and shelf with a free
        // top gets none to three of them, from what belongs in that room.
        void Scatter()
        {
            scattered = 0;
            var tops = new List<Bounds>();
            foreach (var f in furniture)
            {
                if (f == null) continue;
                var col = f.GetComponent<Collider>();
                if (col == null) continue;
                Bounds b = col.bounds;
                string n = f.name.ToLowerInvariant();
                if (n.Contains("bed") || n.Contains("clock") || n.Contains("stove") || n.Contains("heater") || n.Contains("sofa") || n.Contains("chair") || n.Contains("wardrobe") || n.Contains("fridge")) continue;
                float top = b.max.y - transform.position.y;
                if (top < 0.4f || top > 1.35f || b.size.x * b.size.z < 0.12f) continue;
                tops.Add(b);
            }
            foreach (var b in tops)
            {
                int count = Catalogue.Next(4);
                var inRoom = RoomAt(b.center);
                string room = inRoom != null ? inRoom.name : "hall";
                for (int i = 0; i < count; i++)
                {
                    string role = room == "kitchen" ? (Catalogue.Roll() < 0.5 ? Catalogue.Kitchen : Catalogue.Food)
                                : Catalogue.Roll() < 0.35 ? Catalogue.Vase : Catalogue.Roll() < 0.6 ? Catalogue.Decor : Catalogue.Lamp;
                    string id = Catalogue.Any(role);
                    if (id == null) continue;
                    Vector3 s = Catalogue.SizeOf(id);
                    if (s.x > b.size.x * 0.6f || s.z > b.size.z * 0.6f || s.y > 0.6f) continue;
                    // Somewhere on the top, clear of the edges; checked against what's already there.
                    Vector3 at = new Vector3(b.center.x + ((float)Catalogue.Roll() - 0.5f) * (b.size.x - s.x - 0.08f),
                                             b.max.y + 0.005f, b.center.z + ((float)Catalogue.Roll() - 0.5f) * (b.size.z - s.z - 0.08f));
                    if (Physics.CheckBox(at + Vector3.up * (s.y / 2f + 0.01f), new Vector3(s.x, s.y, s.z) * 0.45f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    var m = ModelsLoose.Place(id, transform, transform.InverseTransformPoint(at), s.y, (float)Catalogue.Roll() * 360f, 0.5f, Mathf.Max(s.x, s.z) * 1.2f);
                    if (m == null) continue;
                    m.gameObject.name = id.Replace('_', ' ');
                    movables.Add(m);
                    foreach (var piece in ModelsLoose.LastPieces) movables.Add(piece);
                    scattered++;
                }
            }
        }

        void BuildHall()
        {
            Plain();
            var t = transform;
            hall.extra = Build.Lamp("Hall light south", t, new Vector3(0f, 2.5f, -5f), new Color(1f, 0.86f, 0.66f), 1f, 9f);
            Shade(new Vector3(0f, 2.45f, 5f));
            Shade(new Vector3(0f, 2.45f, -5f));
            Rug(new Vector3(0f, 0f, 1.5f), 1.3f, 6f);

            var clock = Furn("vintage_grandfather_clock_01", "Clock", new Vector3(1.15f, 1.05f, 0f), new Vector3(0.5f, 2.1f, 0.5f), Wood, Models.Face(-1f, 0f));
            HallKit(clock);
            clockTick = Sfx.Loop("clocktick", t, 0.32f, true);
            clockTick.transform.position = clock.transform.position + Vector3.up * 0.6f;
            clockTick.maxDistance = 14f;

            Furn("ClassicConsole_01", "Hall table", new Vector3(-1.12f, 0.45f, -2.1f), new Vector3(0.5f, 0.9f, 1.3f), Wood, Models.Face(1f, 0f));
            standingPhotos.Add(Loose("standing_picture_frame_01", "Photo of your grandparents", new Vector3(-1.15f, 0.92f, -1.7f), 0.22f, new Vector3(0.05f, 0.22f, 0.17f), Wood, Models.Face(1f, 0f), 0.4f));
            Loose("ceramic_vase_01", "Vase", new Vector3(-1.15f, 0.92f, -2.5f), 0.3f, new Vector3(0.16f, 0.3f, 0.16f), new Color(0.5f, 0.6f, 0.65f), 0f, 0.8f);

            // The front door: a real door, out onto the porch and the yard.
            var doorMat = Tex.Real("rough_pine_door", 1f, 1f, 1f, new Color(0.62f, 0.66f, 0.58f), null, 0.2f);
            frontDoor = Door.Create(t, new Vector3(-0.6f, 0f, 8f), 95f, "front", Wood, 90f, doorMat, 1.16f);
            frontDoor.Snap(false);
            doors.Add(frontDoor);
            Build.Box("Doormat", t, new Vector3(0f, 0.012f, 7.3f), new Vector3(0.9f, 0.02f, 0.55f), new Color(0.35f, 0.28f, 0.18f), false);
            Build.Box("Frame", t, new Vector3(-0.63f, DoorH / 2f, 8f), new Vector3(0.06f, DoorH, T + 0.06f), Cream, false);
            Build.Box("Frame", t, new Vector3(0.63f, DoorH / 2f, 8f), new Vector3(0.06f, DoorH, T + 0.06f), Cream, false);
            Build.Box("Frame", t, new Vector3(0f, DoorH + 0.03f, 8f), new Vector3(1.32f, 0.06f, T + 0.06f), Cream, false);
            var hook = Build.Box("Coat on its hook", t, new Vector3(1.05f, 1.45f, 7.82f), new Vector3(0.36f, 0.8f, 0.14f), new Color(0.22f, 0.24f, 0.32f));
            leave = Build.Usable(hook, "Take your coat and leave the house");

            var cellarDoor = Build.Box("Cellar door", t, new Vector3(0f, 1.05f, -7.86f), new Vector3(1.0f, 2.1f, 0.1f), DarkWood);
            Build.Box("Cellar door knob", t, new Vector3(0.36f, 1.0f, -7.78f), new Vector3(0.06f, 0.06f, 0.06f), new Color(0.7f, 0.6f, 0.3f), false);
            cellar = Build.Usable(cellarDoor, "The cellar door");

            Picture(new Vector3(-1.38f, 1.6f, 1f), Vector3.right, 1.2f, 0.85f, paintMat, true);
            Picture(new Vector3(1.38f, 1.6f, 2.2f), Vector3.left, 0.4f, 0.5f, PhotoMat(0), false);
            Picture(new Vector3(1.38f, 1.65f, -1.6f), Vector3.left, 0.35f, 0.45f, PhotoMat(1), false);
            Picture(new Vector3(1.38f, 1.5f, -2.3f), Vector3.left, 0.3f, 0.38f, PhotoMat(2), false);

            // The telephone table, with the phone on it (it rings, sometimes).
            var phoneTable = Fixed("side_table_tall_01", new Vector3(-1.13f, 0f, 5.5f), Models.Face(1f, 0f));
            phoneBody = RotaryPhone(new Vector3(-1.13f, Top(phoneTable, 0.76f), 5.5f), Models.Face(1f, 0f));
            Magazines(new Vector3(-1.1f, 0f, 6.1f), 3);           // the phone book and old directories on the floor beside it

            // The mirror over the hall table: it shows the hall as it is. Mostly.
            Fixed("ornate_mirror_01", new Vector3(-1.39f, 1.0f, -2.1f), Models.Face(1f, 0f), 0.95f, false);
            var glass = Build.Prim(PrimitiveType.Quad, "Mirror glass", transform, P(-1.34f, 1.72f, -2.1f), new Vector3(0.54f, 0.94f, 1f), Build.Mat(Color.black), false);
            glass.transform.localRotation = Quaternion.LookRotation(P(-1f, 0f, 0f));
            hallMirror = Mirror.Attach(glass.GetComponent<Renderer>());

            // Boots by the door, moving boxes not yet unpacked, a clock on the wall.
            Thing("rubber_boots", new Vector3(-0.95f, 0f, 7.35f), Models.Face(0f, -1f), 1.2f, 0.6f);
            Thing("fishermans_hat", new Vector3(-1.12f, 0.92f, -2.55f), 30f, 0.2f);
            Thing("vintage_flashlight", new Vector3(-1.1f, 0.92f, -1.4f), 80f, 0.4f);
            Thing("cardboard_box_01", new Vector3(1.0f, 0f, 6.1f), 8f, 4f);
            Thing("cardboard_box_01", new Vector3(1.0f, 0.36f, 6.05f), -12f, 3f);
            Thing("cardboard_box_01", new Vector3(0.85f, 0f, 5.35f), 20f, 4f);
            Fixed("wall_clock", new Vector3(-1.37f, 1.75f, 3.0f), Models.Face(1f, 0f), 0f, false);
        }

        // Each room below is drawn in its own corner (study front left, kitchen back left, bedroom front right,
        // living room back right). AddRoom and every placing helper mirror it into wherever it is this playthrough.

        void BuildStudy()
        {
            Use("study");
            AddRoom("study", new Vector3(-4.5f, 0f, 4f), new Vector3(6f, H, 8f), new Vector3(-6.3f, 0.86f, 3.1f), -1.5f, 4f, new Vector3(-4.3f, 0f, 5.2f));
            Shade(new Vector3(-4.5f, 2.45f, 4f));
            Rug(new Vector3(-4.6f, 0f, 4f), 2.4f, 3.2f);
            AddWindow(new Vector3(-7.38f, 1.55f, 6.2f), Vector3.right);
            AddWindow(new Vector3(-6.4f, 1.55f, 7.88f), Vector3.back, false);

            Furn("WoodenTable_01", "Desk", new Vector3(-6.3f, 0.4f, 4f), new Vector3(1.0f, 0.8f, 2.0f), Wood, Models.Face(1f, 0f));
            // The typewriter: a body, a platen, a sheet of paper.
            var tw = Box("Typewriter", new Vector3(-6.3f, 0.9f, 4f), new Vector3(0.4f, 0.16f, 0.46f), Dark);
            Build.Prim(PrimitiveType.Cylinder, "Platen", transform, P(-6.44f, 1.02f, 4f), new Vector3(0.07f, 0.24f, 0.07f), Build.Mat(Dark), false).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            typewriterPage = Box("Page", new Vector3(-6.47f, 1.14f, 4f), new Vector3(0.01f, 0.22f, 0.2f), Cream, false);
            typewriter = Build.Usable(tw, "Sit down at the typewriter");
            Loose("dining_chair_02", "Desk chair", new Vector3(-5.35f, 0f, 4f), 0.95f, new Vector3(0.45f, 0.95f, 0.45f), Wood, Models.Face(-1f, 0f), 5f);
            Loose("vintage_oil_lamp", "Oil lamp", new Vector3(-6.45f, 0.81f, 4.75f), 0.42f, new Vector3(0.14f, 0.42f, 0.14f), new Color(0.7f, 0.6f, 0.3f), 0f, 1f);
            Build.Lamp("Desk lamp light", transform, P(-6.3f, 1.4f, 4.6f), new Color(1f, 0.8f, 0.5f), 0.8f, 4f);

            var sh = Furn("wooden_bookshelf_worn", "Bookshelf", new Vector3(-4.5f, 1.05f, 7.62f), new Vector3(1.6f, 2.1f, 0.5f), Wood, Models.Face(0f, -1f));
            shelf = Build.Usable(sh, "Read the manuscript so far");
            Furn("vintage_radio_transceiver", "Radio", new Vector3(-2.2f, 0.95f, 7.5f), new Vector3(0.6f, 0.4f, 0.4f), Dark, Models.Face(0f, -1f));
            furniture.Add(Box("Radio stand", new Vector3(-2.2f, 0.375f, 7.5f), new Vector3(0.7f, 0.75f, 0.45f), Wood));
            Picture(new Vector3(-7.38f, 1.8f, 3.6f), Vector3.right, 1.1f, 0.8f, paintMat, true);
            Picture(new Vector3(-3f, 1.6f, 0.12f), Vector3.forward, 0.5f, 0.4f, PhotoMat(3), false);

            // The writer's desk, half-unpacked.
            Thing("office_notepads", new Vector3(-6.05f, 0.81f, 3.5f), 80f, 0.2f);
            Thing("vintage_lighter", new Vector3(-6.0f, 0.81f, 4.62f), 0f, 0.05f);
            Thing("pocket_watch", new Vector3(-6.55f, 0.81f, 3.6f), 0f, 0.1f);
            Thing("metal_trash_can", new Vector3(-5.6f, 0f, 5.55f), 0f, 2f, 0.32f);
            Thing("cardboard_box_01", new Vector3(-2.9f, 0f, 1.0f), -10f, 4f);
            Thing("cardboard_box_01", new Vector3(-2.35f, 0f, 1.55f), 35f, 4f);
            Thing("vintage_suitcase", new Vector3(-6.7f, 0f, 7.0f), 0f, 6f, 1.0f);
            var shelves = Fixed("painted_wooden_shelves", new Vector3(-4.2f, 0f, 0.3f), Models.Face(0f, 1f), 1.6f);
            Thing("marble_bust_01", new Vector3(-4.2f, Top(shelves, 1.2f), 0.3f), Models.Face(0f, 1f), 3f);

            // Grandpa's day bed, where he napped after lunch.
            Fixed("vintage_day_bed", new Vector3(-6.25f, 0f, 0.6f), Models.Face(0f, 1f));
            // The home-movie projector on its table, pointed at the far wall.
            var ptable = Fixed("small_wooden_table_01", new Vector3(-1.95f, 0f, 6.3f), Models.Face(-1f, 0f));
            float pt = Top(ptable, 0.53f);
            var proj = Fixed("filmstrip_projector_8mm", new Vector3(-1.95f, pt, 6.3f), Models.Face(0f, -1f), 0f, false);
            projector = proj != null ? proj.transform : Box("Projector", new Vector3(-1.95f, pt + 0.15f, 6.3f), new Vector3(0.25f, 0.3f, 0.35f), Dark).transform;
            var screen = Build.Prim(PrimitiveType.Quad, "Projection", transform, P(-2.45f, 1.45f, 0.115f), new Vector3(1.5f, 1.1f, 1f), Build.Mat(Color.black, 1f), false);
            screen.transform.localRotation = Quaternion.LookRotation(P(0f, 0f, -1f));
            projection = screen.GetComponent<Renderer>();
            projection.enabled = false;
            projectorLight = Build.Lamp("Projector light", transform, P(-1.95f, pt + 0.2f, 5.9f), new Color(1f, 0.92f, 0.78f), 0f, 7f);
            projectorLight.type = LightType.Spot;
            projectorLight.spotAngle = 28f;
            projectorLight.transform.rotation = Quaternion.LookRotation(P(-0.01f, 0f, -1f));
            projectorLight.enabled = false;

            // What's on the desk besides the typewriter.
            Thing("cassette_player", new Vector3(-5.95f, 0.81f, 4.4f), Models.Face(1f, 0f), 0.4f);
            Thing("vintage_stapler", new Vector3(-6.6f, 0.81f, 4.45f), 40f, 0.3f);
            Ashtray(new Vector3(-5.95f, 0.81f, 3.1f));
            Thing("cigarette_pack", new Vector3(-6.1f, 0.81f, 3.05f), 70f, 0.05f);
            Thing("binder_notebook", new Vector3(-6.3f, 0.81f, 4.85f), 100f, 0.4f);
            ClockFace.Make(transform, W(new Vector3(-5.3f, 2.05f, 0.11f)), Wn(Vector3.forward), 0.13f, new Color(0.94f, 0.91f, 0.84f), new Color(0.3f, 0.2f, 0.12f));
            StudyKit();
        }

        void BuildKitchen()
        {
            Use("kitchen");
            AddRoom("kitchen", new Vector3(-4.5f, 0f, -4f), new Vector3(6f, H, 8f), new Vector3(-6.95f, 0.96f, -5.9f), -1.5f, -4f, new Vector3(-4.6f, 0f, -1.7f));
            Box("Kitchen floor", new Vector3(-4.5f, 0.002f, -4f), new Vector3(5.9f, 0.004f, 7.9f), Tex.Real("floor_tiles_06", 5.9f, 7.9f, 1.6f, new Color(0.92f, 0.9f, 0.85f), Tex.Plaster, 0.35f), false);
            Shade(new Vector3(-4.5f, 2.45f, -4f));
            AddWindow(new Vector3(-7.38f, 1.6f, -2.2f), Vector3.right);
            AddWindow(new Vector3(-3.25f, 1.6f, -7.88f), Vector3.forward, false);

            // Cabinets along the outside wall with a worktop, a sink and the kettle.
            Box("Wall cupboards", new Vector3(-7.2f, 1.9f, -5.4f), new Vector3(0.4f, 0.7f, 3f), new Color(0.72f, 0.74f, 0.62f));
            for (int i = 0; i < 6; i++)
                Box("Cabinet knob", new Vector3(-6.58f, 0.6f, -6.6f + i * 0.78f), new Vector3(0.03f, 0.03f, 0.03f), new Color(0.7f, 0.6f, 0.3f), false);

            var table = Furn("round_wooden_table_02", "Kitchen table", new Vector3(-4f, 0.375f, -4.4f), new Vector3(1.0f, 0.75f, 1.0f), Wood);
            kitchenChairs.Add(Loose("dining_chair_02", "Kitchen chair", new Vector3(-4f, 0f, -3.55f), 0.95f, new Vector3(0.45f, 0.95f, 0.45f), Wood, Models.Face(0f, -1f), 5f));
            kitchenChairs.Add(Loose("dining_chair_02", "Kitchen chair", new Vector3(-4.85f, 0f, -4.4f), 0.95f, new Vector3(0.45f, 0.95f, 0.45f), Wood, Models.Face(1f, 0f), 5f));
            kitchenChairs.Add(Loose("dining_chair_02", "Kitchen chair", new Vector3(-3.15f, 0f, -4.4f), 0.95f, new Vector3(0.45f, 0.95f, 0.45f), Wood, Models.Face(-1f, 0f), 5f));
            Loose("antique_ceramic_vase_01", "Flower vase", new Vector3(-4f, 0.76f, -4.4f), 0.28f, new Vector3(0.16f, 0.28f, 0.16f), new Color(0.75f, 0.7f, 0.6f), 0f, 0.7f);

            var pantry = Furn("drawer_cabinet", "Pantry cupboard", new Vector3(-4.6f, 0.95f, -7.62f), new Vector3(1.2f, 1.9f, 0.5f), Cream, Models.Face(0f, 1f));
            var heater = Furn("electric_stove", "Heater", new Vector3(-2.1f, 0.4f, -1.2f), new Vector3(0.5f, 0.8f, 0.6f), new Color(0.75f, 0.73f, 0.68f), Models.Face(-1f, 0f));
            var plant = Furn("potted_plant_02", "Ferns", new Vector3(-2.2f, 0.45f, -7.2f), new Vector3(0.7f, 0.9f, 0.7f), new Color(0.2f, 0.36f, 0.16f));
            ferns = WateredPlant(plant, "the ferns");
            Box("Fuse box", new Vector3(-4.5f, 1.55f, -0.2f), new Vector3(0.45f, 0.6f, 0.14f), new Color(0.5f, 0.52f, 0.5f));
            Picture(new Vector3(-3f, 1.6f, -0.12f), Vector3.back, 0.4f, 0.4f, PhotoMat(4), false);

            // The worktop, the table, the floor.
            float worktop = 0.925f;
            Thing("wooden_bowl_01", new Vector3(-7.0f, worktop, -2.62f), 0f, 0.6f);
            food.Add(Thing("food_apple_01", new Vector3(-7.02f, worktop + 0.06f, -2.59f), 0f, 0.15f));
            food.Add(Thing("food_apple_01", new Vector3(-6.95f, worktop + 0.12f, -2.67f), 40f, 0.15f));
            food.Add(Thing("food_apple_01", new Vector3(-7.07f, worktop + 0.12f, -2.69f), 90f, 0.15f));
            food.Add(Thing("bananas", new Vector3(-6.85f, worktop, -6.58f), 70f, 0.4f));
            Thing("lemon", new Vector3(-6.75f, worktop, -6.8f), 0f, 0.1f);
            Thing("yellow_onion", new Vector3(-7.12f, worktop, -6.6f), 0f, 0.15f);
            Thing("yellow_onion", new Vector3(-7.0f, worktop, -6.72f), 60f, 0.15f);
            Thing("wooden_spoon", new Vector3(-6.75f, worktop, -3.95f), 20f, 0.1f);
            Thing("ceramic_vase_02", new Vector3(-7.2f, worktop, -2.38f), 0f, 0.7f);
            Thing("tea_set_01", new Vector3(-4.0f, Top(table, 0.76f), -4.4f), 15f, 1.5f, 0.8f);
            Thing("pot_enamel_01", new Vector3(-2.1f, Top(heater, 0.8f), -1.25f), 0f, 1f);
            Thing("wicker_basket_01", new Vector3(-6.0f, 0f, -7.35f), 25f, 0.8f);
            Thing("mousetrap", new Vector3(-6.2f, 0f, -7.72f), 10f, 0.1f);
            Thing("metal_trash_can", new Vector3(-2.05f, 0f, -6.4f), 0f, 3f);
            Thing("wine_bottles_01", new Vector3(-4.6f, Top(pantry, 1.9f), -7.62f), 0f, 1.5f, 0.6f);
            Fixed("vintage_telephone_wall_clock", new Vector3(-1.66f, 1.55f, -6.6f), Models.Face(-1f, 0f), 0f, false);

            // The fridge in the corner, humming, with a bowl on top.
            var fridgeMat = Build.Mat(new Color(0.86f, 0.84f, 0.76f), 0f, 0.55f);
            furniture.Add(Box("Fridge", new Vector3(-7.02f, 0.83f, -0.6f), new Vector3(0.72f, 1.66f, 0.68f), fridgeMat));
            Box("Fridge door line", new Vector3(-6.655f, 1.2f, -0.6f), new Vector3(0.01f, 0.01f, 0.66f), new Color(0.35f, 0.35f, 0.33f), false);
            Box("Fridge handle", new Vector3(-6.64f, 1.0f, -0.32f), new Vector3(0.03f, 0.28f, 0.035f), new Color(0.75f, 0.76f, 0.78f), false);
            var hum = Sfx.Loop("fridge", transform, 0.12f, true);
            fridgeHum = hum;
            hum.transform.position = P(-7.0f, 1.5f, -0.6f);
            hum.maxDistance = 7f;
            Thing("wooden_bowl_02", new Vector3(-7.02f, 1.66f, -0.6f), 0f, 0.4f);
            // The calendar, with the days crossed off up to today.
            calendar = Build.Mat(Color.white);
            calendar.mainTexture = CalendarTex(9);
            Build.Box("Calendar", transform, P(-5.6f, 1.45f, -0.115f), new Vector3(0.3f, 0.4f, 0.01f), calendar, false);
            // Canisters at the back of the worktop: flour, sugar, coffee, tea.
            string[] canisters = { "Coffee", "Tea", "Sugar", "Flour" };
            for (int i = 0; i < 4; i++)
            {
                float r = 0.075f - i * 0.008f, h = 0.24f - i * 0.03f;
                // The coffee tin is the brown one nearest the kettle, with a cream label round it.
                bool coffee = canisters[i] == "Coffee";
                float jx = Random.Range(-0.025f, 0.02f), jz = Random.Range(-0.02f, 0.02f);
                var can = Build.Prim(PrimitiveType.Cylinder, canisters[i] + " canister", transform, P(-7.22f + jx, worktop + h / 2f + 0.003f, -3.75f - i * 0.17f + jz), new Vector3(r * 2f, h / 2f, r * 2f), Build.Mat(coffee ? new Color(0.32f, 0.2f, 0.12f) : new Color(0.88f, 0.86f, 0.8f), 0f, 0.6f), true);
                if (coffee) Build.Prim(PrimitiveType.Cylinder, "Label", can.transform, Vector3.zero, new Vector3(1.02f, 0.35f, 1.02f), Build.Mat(new Color(0.9f, 0.85f, 0.7f), 0f, 0.3f), false);
                var lid = Build.Prim(PrimitiveType.Cylinder, "Lid", can.transform, Vector3.zero, Vector3.one, Build.Mat(new Color(0.6f, 0.12f, 0.1f), 0f, 0.6f), false);
                lid.transform.position = P(-7.22f + jx, worktop + h + 0.01f, -3.75f - i * 0.17f + jz);
                can.transform.Rotate(0f, Random.Range(0f, 360f), 0f);
                lid.transform.localScale = new Vector3(1.02f, 0.01f / (h / 2f), 1.02f);
                FlatBottom(can);
                movables.Add(Movable.Attach(can, 0.5f));
            }

            // His last cigarette, at his place at the table.
            float kt2 = Top(table, 0.76f);
            Ashtray(new Vector3(-3.68f, kt2, -4.12f));
            KitchenKit();
        }

        void BuildLiving()
        {
            Use("living room");
            AddRoom("living room", new Vector3(4.5f, 0f, -4f), new Vector3(6f, H, 8f), new Vector3(7.15f, 1.3f, -3.3f), 1.5f, -4f, new Vector3(3.6f, 0f, -2.4f));
            Shade(new Vector3(4.5f, 2.45f, -4f));
            Rug(new Vector3(4.95f, 0f, -4f), 3.0f, 2.6f);
            AddWindow(new Vector3(4.5f, 1.55f, -7.88f), Vector3.forward);
            AddWindow(new Vector3(7.38f, 1.55f, -1.7f), Vector3.left, false);

            // The fireplace, with the mantel Grandma kept everything on.
            var fp = Box("Fireplace", new Vector3(7.2f, 0.6f, -4f), new Vector3(0.5f, 1.2f, 1.9f), new Color(0.42f, 0.24f, 0.2f));
            furniture.Add(fp);
            Box("Firebox", new Vector3(6.96f, 0.4f, -4f), new Vector3(0.06f, 0.7f, 1.0f), Dark, false);
            Box("Mantel", new Vector3(7.1f, 1.23f, -4f), new Vector3(0.7f, 0.07f, 2.2f), DarkWood);
            Box("Chimney breast", new Vector3(7.3f, 2.0f, -4f), new Vector3(0.3f, 1.5f, 1.5f), Tex.Mat(Tex.Wallpaper, 1.5f, 1.5f, 1.2f, AgedPaper));
            chimney = P(7.5f, 0f, -4f);

            fire = Build.Lamp("Fire", transform, P(6.6f, 0.5f, -4f), new Color(1f, 0.5f, 0.18f), 1f, 6f);
            fireLoop = Sfx.Loop("fire", transform, 0.4f, true);
            fireLoop.transform.position = P(6.9f, 0.4f, -4f);
            fireLoop.maxDistance = 12f;
            Loose("mantel_clock_01", "Mantel clock", new Vector3(7.1f, 1.27f, -4f), 0.22f, new Vector3(0.14f, 0.22f, 0.3f), Wood, Models.Face(-1f, 0f), 1.2f);
            standingPhotos.Add(Loose("standing_picture_frame_02", "Photo of your parents", new Vector3(7.1f, 1.27f, -4.6f), 0.24f, new Vector3(0.05f, 0.24f, 0.18f), Wood, Models.Face(-1f, 0f), 0.4f));
            Loose("wooden_candlestick", "Candlestick", new Vector3(7.1f, 1.27f, -3.35f), 0.22f, new Vector3(0.08f, 0.22f, 0.08f), Wood, 0f, 0.5f);
            Picture(new Vector3(7.13f, 2.0f, -4f), Vector3.left, 0.9f, 0.7f, paintMat, true);

            // The seating faces the fire, set back from the door so there is a clear way in behind the sofa.
            armchair = Loose("ArmChair_01", "Grandpa's armchair", new Vector3(6.1f, 0f, -5.65f), 0.95f, new Vector3(0.85f, 0.95f, 0.85f), new Color(0.35f, 0.4f, 0.28f), Models.Face(0.3f, 1f), 9f);
            rocker = Loose("Rockingchair_01", "Grandma's rocking chair", new Vector3(6.1f, 0f, -2.3f), 1.0f, new Vector3(0.6f, 1.0f, 0.8f), Wood, Models.Face(0.3f, -1f), 7f);
            Furn("sofa_03", "Sofa", new Vector3(3.75f, 0.42f, -4.1f), new Vector3(0.95f, 0.85f, 2.3f), new Color(0.5f, 0.42f, 0.36f), Models.Face(1f, 0f));
            var coffee = Furn("CoffeeTable_01", "Coffee table", new Vector3(5.0f, 0.23f, -4.1f), new Vector3(0.6f, 0.46f, 1.1f), Wood);
            Loose(null, "Grandma's knitting", new Vector3(5.0f, 0.47f, -4.4f), 0.12f, new Vector3(0.22f, 0.12f, 0.22f), new Color(0.6f, 0.3f, 0.35f), 20f, 0.3f);
            Loose("painted_wooden_stool", "Footstool", new Vector3(4.55f, 0f, -5.75f), 0.4f, new Vector3(0.36f, 0.4f, 0.36f), Wood, 0f, 2.5f);
            Picture(new Vector3(3.2f, 1.6f, -0.12f), Vector3.back, 0.5f, 0.6f, PhotoMat(5), false);
            Picture(new Vector3(4.0f, 1.7f, -0.12f), Vector3.back, 0.35f, 0.4f, PhotoMat(6), false);

            // Grandma's things.
            Thing("throw_pillows_01", new Vector3(3.8f, 0.5f, -4.1f), Models.Face(1f, 0f), 0.6f, 0.5f);
            float ct = Top(coffee, 0.46f);
            Thing("carved_wooden_elephant", new Vector3(5.15f, ct, -3.75f), 200f, 0.6f);
            Thing("round_spectacles", new Vector3(4.9f, ct, -3.85f), 30f, 0.05f);
            Thing("postcard_set_01", new Vector3(5.05f, ct, -4.6f), 10f, 0.1f);
            Thing("magnifying_glass_01", new Vector3(4.8f, ct, -4.45f), 120f, 0.2f);
            var side = Fixed("side_table_01", new Vector3(6.75f, 0f, -1.2f), Models.Face(-1f, -1f));
            Thing("chess_set", new Vector3(6.75f, Top(side, 0.6f), -1.2f), 0f, 1f, 0.45f);
            Fixed("spinning_wheel_01", new Vector3(3.3f, 0f, -7.15f), Models.Face(0.3f, 1f));
            Thing("wicker_basket_02", new Vector3(6.55f, 0f, -6.35f), 0f, 0.6f);
            Thing("brass_candleholders", new Vector3(7.1f, 1.27f, -4.95f), 0f, 0.6f, 0.3f);
            Fixed("hanging_picture_frame_01", new Vector3(1.64f, 1.35f, -2.55f), Models.Face(1f, 0f), 0f, false);

            // The television on its table, where Grandpa could see it from his chair.
            var tvTable = Fixed("small_wooden_table_01", new Vector3(4.6f, 0f, -0.42f), Models.Face(0f, -1f));
            float tt = Top(tvTable, 0.53f);
            Fixed("Television_01", new Vector3(4.6f, tt, -0.42f), Models.Face(0f, -1f), 0f, false);
            var scr = Build.Prim(PrimitiveType.Quad, "TV screen", transform, P(4.58f, tt + 0.25f, -0.665f), new Vector3(0.36f, 0.27f, 1f), Build.Mat(Color.black, 1f), false);
            scr.transform.localRotation = Quaternion.LookRotation(P(0f, 0f, 1f));
            tvScreen = scr.GetComponent<Renderer>();
            tvScreen.enabled = false;
            tvLight = Build.Lamp("TV glow", transform, P(4.6f, tt + 0.3f, -1.0f), new Color(0.6f, 0.7f, 1f), 0f, 4.5f);
            tvLight.enabled = false;
            Doily(new Vector3(4.6f, tt, -0.42f), 0.5f);

            // Grandma's china cabinet against the hall wall, a plate on top.
            var china = Fixed("vintage_cabinet_01", new Vector3(1.86f, 0f, -6.0f), Models.Face(1f, 0f), 1.4f);
            Thing("carved_wooden_plate", new Vector3(1.86f, Top(china, 1.55f), -6.0f), 0f, 0.4f);
            ParlourPlantGo = Fixed("potted_plant_01", new Vector3(2.05f, 0f, -7.45f), 0f);
            Doily(new Vector3(5.0f, ct, -4.1f), 0.38f);
            Ashtray(new Vector3(4.72f, ct, -4.05f));
            Magazines(new Vector3(5.15f, 0f, -6.55f), 4);           // a pile on the floor by his chair
            Fixed("hanging_picture_frame_03", new Vector3(1.64f, 1.35f, -1.6f), Models.Face(1f, 0f), 0f, false);
            LivingKit(fp);
        }

        void BuildBedroom()
        {
            Use("bedroom");
            AddRoom("bedroom", new Vector3(4.5f, 0f, 4f), new Vector3(6f, H, 8f), new Vector3(3.2f, 0.62f, 7.1f), 1.5f, 4f, new Vector3(4.4f, 0f, 3.6f));
            bedSpawn = P(4.2f, 0.05f, 3.0f);
            Shade(new Vector3(4.5f, 2.45f, 4f));
            Rug(new Vector3(4.4f, 0f, 5.3f), 1.6f, 2.4f);
            AddWindow(new Vector3(7.38f, 1.55f, 2.2f), Vector3.left);
            AddWindow(new Vector3(4.25f, 1.55f, 7.88f), Vector3.back, false);

            // Your old bed, with the quilt she made.
            GameObject bedHit;
            if (Models.Place("old_bed_frame", transform, P(6.3f, 0f, 5.7f), 1.0f, Yaw(Models.Face(0f, -1f)), out bedHit, false, 2.3f) == null)
            {
                Box("Bed frame", new Vector3(6.3f, 0.2f, 5.7f), new Vector3(1.2f, 0.4f, 2.1f), DarkWood);
                Box("Headboard", new Vector3(6.3f, 0.6f, 6.78f), new Vector3(1.2f, 1.2f, 0.08f), DarkWood);
            }
            var quilt = Build.Mat(Color.white);
            quilt.mainTexture = Tex.Quilt;
            var b = Box("Bed", new Vector3(6.3f, 0.47f, 5.6f), new Vector3(1.0f, 0.22f, 1.9f), quilt);
            furniture.Add(b);
            var pillow = Box("Pillow", new Vector3(6.3f, 0.63f, 6.35f), new Vector3(0.7f, 0.12f, 0.4f), new Color(0.9f, 0.88f, 0.82f), false);
            bed = Build.Usable(b, "Go to bed");
            bedTop = b.transform;
            BedroomKit(b, pillow);

            Furn("ClassicNightstand_01", "Nightstand", new Vector3(5.3f, 0.3f, 6.9f), new Vector3(0.5f, 0.6f, 0.45f), Wood, Models.Face(0f, -1f));
            Loose("alarm_clock_01", "Alarm clock", new Vector3(5.3f, 0.61f, 6.9f), 0.14f, new Vector3(0.12f, 0.14f, 0.08f), new Color(0.7f, 0.6f, 0.3f), Models.Face(0f, -1f), 0.4f);
            furniture.Add(Box("Bedside table", new Vector3(3.2f, 0.3f, 7.1f), new Vector3(0.5f, 0.6f, 0.5f), Wood));
            wardrobe = Furn("painted_wooden_cabinet_02", "Wardrobe", new Vector3(2.15f, 1.05f, 1.2f), new Vector3(0.7f, 2.1f, 1.1f), Cream, Models.Face(1f, 0f)).transform;
            var dresserGo = Furn("vintage_wooden_drawer_01", "Dresser", new Vector3(3.55f, 0.45f, 0.62f), new Vector3(1.4f, 0.9f, 0.55f), Wood, Models.Face(0f, 1f));
            // The mirror over the dresser shows what Moodscape paints, like the paintings do.
            Picture(new Vector3(3.55f, 1.6f, 0.12f), Vector3.forward, 0.7f, 0.9f, paintMat, true);
            Picture(new Vector3(7.38f, 1.6f, 5.9f), Vector3.left, 0.4f, 0.5f, PhotoMat(7), false);

            // The boxes you haven't unpacked, his spectacles, a chair.
            Thing("cardboard_box_01", new Vector3(2.35f, 0f, 2.55f), 5f, 4f);
            Thing("cardboard_box_01", new Vector3(2.35f, 0.36f, 2.6f), 70f, 3f);
            Thing("vintage_suitcase", new Vector3(6.3f, 0f, 4.15f), 90f, 6f, 0.9f);
            Thing("round_spectacles", new Vector3(5.2f, 0.62f, 6.85f), 0f, 0.05f);
            Thing("office_notepads", new Vector3(3.2f, 0.62f, 7.05f), 15f, 0.2f);
            Fixed("painted_wooden_chair_01", new Vector3(4.55f, 0f, 1.25f), Models.Face(-0.6f, 1f));
            Fixed("hanging_picture_frame_02", new Vector3(1.64f, 1.4f, 6.4f), Models.Face(1f, 0f), 0f, false);

            // The picture over the bed, her slippers beside it, his crutches in the corner, her wheelchair by the window.
            Fixed("fancy_picture_frame_02", new Vector3(6.3f, 1.45f, 7.86f), Models.Face(0f, -1f), 0.55f, false);
            Loose(null, "Grandma's slippers", new Vector3(5.5f, 0f, 4.95f), 0.06f, new Vector3(0.22f, 0.06f, 0.27f), new Color(0.55f, 0.4f, 0.45f), 8f, 0.3f);
            Fixed("vintage_crutches_01", new Vector3(7.12f, 0f, 7.55f), Models.Face(-1f, -1f));
            wheelchair = Loose("wheelchair_01", "Grandma's wheelchair", new Vector3(2.5f, 0f, 5.4f), 1.1f, new Vector3(0.82f, 1.1f, 1.09f), new Color(0.25f, 0.25f, 0.27f), Models.Face(1f, 0f), 15f);
            float dt = Top(dresserGo, 0.9f);
            Thing("rubber_duck_toy", new Vector3(3.1f, dt, 0.62f), Models.Face(0f, 1f), 0.2f);
            Thing("potted_plant_04", new Vector3(4.0f, dt, 0.62f), 0f, 0.4f);
            Doily(new Vector3(3.55f, dt, 0.62f), 0.4f);
            BuildBathroom();
        }

        // The cellar is its own space under the house. You reach it through the cellar door once it opens.
        void BuildCellar()
        {
            var root = new GameObject("Cellar").transform;
            root.SetParent(transform, false);
            root.localPosition = CellarOrigin;
            var conc = Tex.Mat(Tex.Concrete, 9f, 9f, 2.5f, Color.white);
            Build.Box("Floor", root, new Vector3(0f, -0.1f, 0f), new Vector3(9.4f, 0.2f, 9.4f), conc);
            Build.Box("Ceiling", root, new Vector3(0f, 2.3f, 0f), new Vector3(9.4f, 0.2f, 9.4f), Build.Mat(new Color(0.15f, 0.12f, 0.1f)));
            Build.Box("W", root, new Vector3(-4.6f, 1.1f, 0f), new Vector3(0.2f, 2.2f, 9.4f), Tex.Mat(Tex.Concrete, 9f, 2.2f, 2.5f, new Color(0.8f, 0.8f, 0.8f)));
            Build.Box("E", root, new Vector3(4.6f, 1.1f, 0f), new Vector3(0.2f, 2.2f, 9.4f), Tex.Mat(Tex.Concrete, 9f, 2.2f, 2.5f, new Color(0.8f, 0.8f, 0.8f)));
            Build.Box("N", root, new Vector3(0f, 1.1f, 4.6f), new Vector3(9.4f, 2.2f, 0.2f), Tex.Mat(Tex.Concrete, 9f, 2.2f, 2.5f, new Color(0.8f, 0.8f, 0.8f)));
            Build.Box("S", root, new Vector3(0f, 1.1f, -4.6f), new Vector3(9.4f, 2.2f, 0.2f), Tex.Mat(Tex.Concrete, 9f, 2.2f, 2.5f, new Color(0.8f, 0.8f, 0.8f)));
            // The stairs you came down.
            for (int i = 0; i < 6; i++)
                Build.Box("Stair", root, new Vector3(0f, 0.15f + i * 0.3f, 3.6f + i * 0.15f), new Vector3(1.1f, 0.3f, 0.9f - i * 0.1f), DarkWood);
            var stairs = Build.Box("Stairs up", root, new Vector3(0f, 1.2f, 4.42f), new Vector3(1.1f, 2.2f, 0.1f), Dark);
            cellarStairs = Build.Usable(stairs, "Go back upstairs");
            // Shelves of preserving jars, the workbench, and a patch of floor that is newer than the rest.
            Build.Box("Shelves", root, new Vector3(-4.3f, 1.0f, -1f), new Vector3(0.4f, 2.0f, 4f), Wood);
            for (int i = 0; i < 14; i++)
                Build.Prim(PrimitiveType.Cylinder, "Jar", root, new Vector3(-4.05f, 0.75f + (i % 2) * 0.6f, -2.7f + (i / 2) * 0.5f), new Vector3(0.14f, 0.1f, 0.14f),
                    Build.Mat(i % 3 == 0 ? new Color(0.5f, 0.2f, 0.15f) : new Color(0.55f, 0.5f, 0.25f), 0f, 0.8f), false);
            Build.Box("Workbench", root, new Vector3(3.9f, 0.45f, -1f), new Vector3(0.9f, 0.9f, 2.4f), Wood);
            Build.Box("Spade", root, new Vector3(4.35f, 0.75f, 1.2f), new Vector3(0.06f, 1.5f, 0.2f), new Color(0.3f, 0.3f, 0.32f), false);
            var patch = Build.Box("Newer concrete", root, new Vector3(0.4f, 0.006f, -2.2f), new Vector3(2.2f, 0.012f, 1.5f), new Color(0.55f, 0.55f, 0.53f));
            cellarFloor = Build.Usable(patch, "Look at the floor");
            cellarBulb = Build.Lamp("Bare bulb", root, new Vector3(0f, 1.95f, -0.5f), new Color(1f, 0.85f, 0.6f), 2.2f, 10f, true);
            Build.Prim(PrimitiveType.Sphere, "Bulb", root, new Vector3(0f, 2.0f, -0.5f), new Vector3(0.08f, 0.1f, 0.08f), Build.Mat(new Color(1f, 0.9f, 0.6f), 2f), false);
        }

        // ---------------------------------------------------------------- walls and rooms

        // A room, in the current drawing's coordinates (mirrored into its corner). The door is hung on the same side of
        // the doorway as in the drawing, so what is clear of its swing in the drawing is clear of it everywhere.
        Room AddRoom(string name, Vector3 center, Vector3 size, Vector3 notePoint, float doorX, float doorZ, Vector3 navMiddle)
        {
            var r = new Room { name = name, center = P(center), notePoint = P(notePoint) };
            r.bounds = new Bounds(r.center + new Vector3(0f, H / 2f, 0f), size);
            r.baseIntensity = 1.2f;
            r.light = Build.Lamp(name + " light", transform, r.center + new Vector3(0f, 2.4f, name == "hall" ? 5f : 0f),
                new Color(1f, 0.87f, 0.68f), r.baseIntensity, 10f, true);
            if (doorX != 0f)
            {
                Vector3 hinge = P(doorX, 0f, doorZ - Gap / 2f);
                float x = doorX * mx, z = doorZ * mz;
                float baseYaw = mz > 0f ? 0f : 180f;                         // the closed door runs from the hinge across the doorway
                float into = (x < 0f ? -95f : 95f) * (mz > 0f ? 1f : -1f);   // and every door opens into its room
                r.door = Door.Create(transform, hinge, into, name, new Color(0.8f, 0.76f, 0.66f), baseYaw);
                doors.Add(r.door);
                r.seal = Build.Box("Seal " + name, transform, new Vector3(x, DoorH / 2f, z), new Vector3(T + 0.04f, DoorH, Gap),
                    Paper(Gap, DoorH));
                r.seal.SetActive(false);
                // The door frame.
                Build.Box("Frame", transform, new Vector3(x, DoorH / 2f, z - Gap / 2f - 0.03f), new Vector3(T + 0.06f, DoorH, 0.06f), Cream, false);
                Build.Box("Frame", transform, new Vector3(x, DoorH / 2f, z + Gap / 2f + 0.03f), new Vector3(T + 0.06f, DoorH, 0.06f), Cream, false);
                Build.Box("Frame", transform, new Vector3(x, DoorH + 0.03f, z), new Vector3(T + 0.06f, 0.06f, Gap + 0.12f), Cream, false);
                // Two waypoints: just inside the door, and the middle of the room.
                roomNav.Add(new[] { new Vector3(x + Mathf.Sign(x) * 1.0f, 0f, z), P(navMiddle) });
            }
            rooms.Add(r);
            return r;
        }

        // A wall running north-south at a fixed x, with optional doorways centred on the given z values.
        void WallZ(float x, float z0, float z1, params float[] gaps)
        {
            float from = z0;
            var sorted = new List<float>(gaps);
            sorted.Sort();
            foreach (float g in sorted)
            {
                Segment(new Vector3(x, H / 2f, (from + g - Gap / 2f) / 2f), new Vector3(T, H, g - Gap / 2f - from), true);
                Segment(new Vector3(x, (DoorH + H) / 2f, g), new Vector3(T, H - DoorH, Gap), false);
                from = g + Gap / 2f;
            }
            Segment(new Vector3(x, H / 2f, (from + z1) / 2f), new Vector3(T, H, z1 - from), true);
        }

        // A wall running east-west at a fixed z, with optional doorways centred on the given x values.
        void WallX(float z, float x0, float x1, params float[] gaps)
        {
            float from = x0;
            var sorted = new List<float>(gaps);
            sorted.Sort();
            foreach (float g in sorted)
            {
                Segment(new Vector3((from + g - Gap / 2f) / 2f, H / 2f, z), new Vector3(g - Gap / 2f - from, H, T), true);
                Segment(new Vector3(g, (DoorH + H) / 2f, z), new Vector3(Gap, H - DoorH, T), false);
                from = g + Gap / 2f;
            }
            Segment(new Vector3((from + x1) / 2f, H / 2f, z), new Vector3(x1 - from, H, T), true);
        }

        void Segment(Vector3 pos, Vector3 size, bool skirting)
        {
            if (size.x <= 0.01f || size.z <= 0.01f) return;
            float len = Mathf.Max(size.x, size.z);
            var paper = Paper(len, size.y);
            if (wallpaperTex == null && Tex.Has("white_plaster_rough_01"))
            {
                var bump = Tex.Real("white_plaster_rough_01", len, size.y, 2.5f, Color.white);
                Tex.Bump(paper, (Texture2D)bump.GetTexture("_BumpMap"), bump.GetTextureScale("_BumpMap"), 0.35f);
            }
            var wall = Build.Box("Wall", transform, pos, size, paper);
            var wc = wall.GetComponent<Collider>();
            if (wc != null) walls.Add(wc);
            if (!skirting) return;
            Vector3 thick = size.x > size.z ? new Vector3(size.x, 0.12f, size.z + 0.04f) : new Vector3(size.x + 0.04f, 0.12f, size.z);
            Build.Box("Skirting", transform, new Vector3(pos.x, 0.06f, pos.z), thick, Cream, false);
            Vector3 rail = size.x > size.z ? new Vector3(size.x, 0.05f, size.z + 0.03f) : new Vector3(size.x + 0.03f, 0.05f, size.z);
            Build.Box("Picture rail", transform, new Vector3(pos.x, 2.25f, pos.z), rail, Cream, false);
        }

        // ---------------------------------------------------------------- does the layout make sense?

        /// <summary>The problems the last layout check found (empty when everything makes sense).</summary>
        public readonly List<string> problems = new List<string>();

        static List<Collider> Solid(GameObject g)
        {
            var list = new List<Collider>();
            if (g == null) return list;
            foreach (var c in g.GetComponentsInChildren<Collider>()) if (c != null && !c.isTrigger && c.enabled) list.Add(c);
            return list;
        }

        /// <summary>
        /// Checks that the furniture makes sense where this layout has put it: every door swings open without hitting
        /// anything, every doorway has a clear way in, no window is blocked by something tall, everything you use has
        /// somewhere to stand beside it, and no two pieces of furniture are inside each other or the walls. Returns
        /// the problems in plain words (and keeps them in `problems`).
        /// </summary>
        public List<string> CheckLayout()
        {
            Physics.SyncTransforms();
            problems.Clear();
            var cols = new List<KeyValuePair<string, Collider>>();
            foreach (var f in furniture)
                foreach (var c in Solid(f)) cols.Add(new KeyValuePair<string, Collider>(f.name, c));
            System.Func<Vector3, string> inside = p =>
            {
                foreach (var kv in cols)
                    if ((kv.Value.ClosestPoint(p) - p).sqrMagnitude < 1e-6f) return kv.Key;
                return null;
            };
            System.Action<string> add = msg => { if (!problems.Contains(msg)) problems.Add(msg); };

            foreach (var r in rooms)
            {
                var d = r.door;
                if (d == null) continue;
                Vector3 hinge = d.transform.position;
                // The door's swing, from shut to fully open.
                string hit = null;
                for (float a = 0f; a <= Mathf.Abs(d.openAngle) && hit == null; a += 5f)
                {
                    Vector3 dir = Quaternion.Euler(0f, d.baseYaw + a * Mathf.Sign(d.openAngle), 0f) * Vector3.forward;
                    for (float s = 0.15f; s <= 1.12f && hit == null; s += 0.1f)
                        foreach (float y in new[] { 0.3f, 1.0f, 1.8f })
                            if ((hit = inside(hinge + dir * s + Vector3.up * y)) != null) break;
                }
                if (hit != null) add(r.name + ": the door hits the " + hit + " when it opens");
                // A clear way in, a metre deep and the width of a person.
                Vector3 mid = hinge + Quaternion.Euler(0f, d.baseYaw, 0f) * Vector3.forward * (Gap / 2f);
                float inward = Mathf.Sign(r.center.x - mid.x);
                hit = null;
                for (float depth = 0.2f; depth <= 1.1f && hit == null; depth += 0.15f)
                    for (float side = -0.35f; side <= 0.36f && hit == null; side += 0.175f)
                        foreach (float y in new[] { 0.3f, 1.0f })
                            if ((hit = inside(new Vector3(mid.x + inward * depth, y, mid.z + side))) != null) break;
                if (hit != null) add(r.name + ": the " + hit + " is in the way just inside the door");
            }

            // Windows: nothing tall in front of the glass.
            foreach (var w in windowSpots)
            {
                Vector3 along = Mathf.Abs(w.Value.x) > 0.5f ? Vector3.forward : Vector3.right;
                string hit = null;
                for (float u = -0.45f; u <= 0.46f && hit == null; u += 0.15f)
                    for (float v = -0.35f; v <= 0.5f && hit == null; v += 0.2f)
                        hit = inside(w.Key + w.Value * 0.3f + along * u + Vector3.up * v);
                if (hit != null) add(RoomAt(w.Key).name + ": the " + hit + " blocks a window");
            }

            // Everything you use needs somewhere to stand beside it.
            var uses = new Dictionary<string, Interactable> { { "typewriter", typewriter }, { "bed", bed }, { "bookshelf", shelf }, { "cellar door", cellar }, { "coat hook", leave } };
            foreach (var kv in choreTargets) uses[kv.Key + " (chore)"] = kv.Value;
            foreach (var kv in uses)
            {
                if (kv.Value == null) continue;
                var cs = Solid(kv.Value.gameObject);
                Vector3 c = cs.Count > 0 ? cs[0].bounds.center : kv.Value.transform.position;
                Room room = RoomAt(c);
                bool ok = false;
                for (float dist = 0.55f; dist <= 1.45f && !ok; dist += 0.3f)
                    for (int k = 0; k < 16 && !ok; k++)
                    {
                        Vector3 dir = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                        Vector3 p = new Vector3(c.x, 0f, c.z) + dir * dist;
                        if (Outside(p) || RoomAt(p) != room) continue;
                        if (Physics.CheckCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.6f, 0.22f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        float reach = cs.Count > 0 ? (cs[0].ClosestPoint(p + Vector3.up * 1f) - (p + Vector3.up * 1f)).magnitude : dist;
                        ok = reach < 1.2f;
                    }
                if (!ok) add(room.name + ": nowhere to stand to use the " + kv.Key);
            }

            // Nothing inside anything else, or inside a wall.
            for (int i = 0; i < cols.Count; i++)
            {
                for (int j = i + 1; j < cols.Count; j++)
                {
                    if (cols[i].Key == cols[j].Key) continue;
                    Vector3 dir; float dist;
                    var a = cols[i].Value; var b = cols[j].Value;
                    if (!a.bounds.Intersects(b.bounds)) continue;
                    if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out dir, out dist) && dist > 0.06f)
                        add(RoomAt(a.bounds.center).name + ": the " + cols[i].Key + " and the " + cols[j].Key + " are inside each other (" + (dist * 100f).ToString("0") + " cm)");
                }
                foreach (var w in walls)
                {
                    Vector3 dir; float dist;
                    var a = cols[i].Value;
                    if (w == null || !a.bounds.Intersects(w.bounds)) continue;
                    if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, w, w.transform.position, w.transform.rotation, out dir, out dist) && dist > 0.05f)
                        add(RoomAt(a.bounds.center).name + ": the " + cols[i].Key + " goes " + (dist * 100f).ToString("0") + " cm into the wall");
                }
            }
            return problems;
        }

        public Room RoomAt(Vector3 p)
        {
            foreach (var r in rooms)
                if (r != hall && r.bounds.Contains(new Vector3(p.x, 1f, p.z))) return r;
            return hall;
        }

        public bool InCellar(Vector3 p) { return p.y < CellarOrigin.y + 10f && p.y > CellarOrigin.y - 5f; }

        /// <summary>Out of doors: on the porch, in the yard or in the woods.</summary>
        public bool Outside(Vector3 p) { return !InCellar(p) && (Mathf.Abs(p.x) > 7.6f || Mathf.Abs(p.z) > 8.1f); }

        // ---------------------------------------------------------------- finding a way through the house

        readonly List<Vector3[]> roomNav = new List<Vector3[]>();

        void BuildNav()
        {
            // 0-4: the hall, front to back. Then for each room: just inside its door, and its middle.
            nav.AddRange(new[] { new Vector3(0f, 0f, 7f), new Vector3(0f, 0f, 4f), new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, -4f), new Vector3(0f, 0f, -7f) });
            for (int i = 0; i < 4; i++) navLinks.Add(new[] { i, i + 1 });
            foreach (var rn in roomNav)
            {
                int door = nav.Count;
                nav.Add(rn[0]);
                nav.Add(rn[1]);
                navLinks.Add(new[] { rn[0].z > 0f ? 1 : 3, door });
                navLinks.Add(new[] { door, door + 1 });
            }
            // The bathroom opens off the bedroom: outside its door (in the bedroom) to the bedroom's middle, then in.
            if (bath != null)
            {
                int mid = -1;
                for (int i = 5; i < nav.Count; i++) { var r = RoomAt(nav[i]); if (r != null && r.name == "bedroom" && (i - 5) % 2 == 1) mid = i; }
                int o = nav.Count;
                nav.Add(bathNavOutside);
                nav.Add(bathNavInside);
                if (mid >= 0) navLinks.Add(new[] { mid, o });
                navLinks.Add(new[] { o, o + 1 });
                roomNav.Add(new[] { bathNavOutside, bathNavInside });
            }
        }

        /// <summary>The middle of a room, as its waypoint has it (a clear spot to cross the room from).</summary>
        public Vector3? Middle(Room r)
        {
            foreach (var rn in roomNav) if (RoomAt(rn[1]) == r) return rn[1];
            return null;
        }

        int Nearest(Vector3 p)
        {
            Room room = RoomAt(p);
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < nav.Count; i++)
            {
                if (RoomAt(nav[i]) != room) continue;       // never pick a point on the far side of a wall
                float d = (nav[i] - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Waypoints from one place in the house to another, through the doorways.</summary>
        public List<Vector3> Path(Vector3 from, Vector3 to)
        {
            int a = Nearest(from), b = Nearest(to);
            var prev = new int[nav.Count];
            for (int i = 0; i < prev.Length; i++) prev[i] = -1;
            var queue = new Queue<int>();
            queue.Enqueue(a);
            prev[a] = a;
            while (queue.Count > 0)
            {
                int n = queue.Dequeue();
                if (n == b) break;
                foreach (var l in navLinks)
                {
                    int other = l[0] == n ? l[1] : l[1] == n ? l[0] : -1;
                    if (other < 0 || prev[other] >= 0) continue;
                    prev[other] = n;
                    queue.Enqueue(other);
                }
            }
            var path = new List<Vector3>();
            if (prev[b] < 0) { path.Add(to); return path; }
            for (int n = b; n != a; n = prev[n]) path.Insert(0, nav[n]);
            path.Insert(0, nav[a]);
            path.Add(new Vector3(to.x, 0f, to.z));
            return path;
        }

        public Vector3 FarPoint(Vector3 from)
        {
            Vector3 best = nav[0];
            float bestD = -1f;
            foreach (var p in nav)
            {
                float d = (p - from).sqrMagnitude;
                if (d > bestD) { bestD = d; best = p; }
            }
            return best;
        }

        // ---------------------------------------------------------------- time of day

        /// <summary>Legacy (tests, coming up from the cellar): 0 = mid-morning, 1 = evening with every lamp on.</summary>
        public void SetTime(int newTime, int newDay)
        {
            SetHour(newTime == 0 ? 10.5f : 20.5f, newDay);
            if (newTime == 1) foreach (var r in rooms) r.lit = true;
        }

        /// <summary>How much daylight there is (0 at night, 1 at midday on day 1).</summary>
        public float Sun { get; private set; }
        float lampLevel = 0.95f;
        int outdoorTime = -1;

        /// <summary>The light for an hour of the day (7.5 = half past seven). Each day of the run is a little dimmer.</summary>
        public void SetHour(float hour, int newDay)
        {
            day = newDay;
            float h = hour % 24f;
            float sun = Mathf.Clamp01(Mathf.InverseLerp(5.9f, 8.0f, h)) * Mathf.Clamp01(Mathf.InverseLerp(19.2f, 16.2f, h));
            sun = sun * sun * (3f - 2f * sun) * (1f - 0.07f * (day - 1));
            Sun = sun;
            // Low sun is golden: the first hour or two of the morning and the late afternoon.
            float gold = sun > 0.01f ? (h < 12f ? Mathf.Clamp01(Mathf.InverseLerp(10f, 7.2f, h)) : Mathf.Clamp01(Mathf.InverseLerp(15.2f, 18.6f, h))) : 0f;
            time = sun > 0.3f ? 0 : 1;
            lampLevel = 0.95f - 0.05f * (day - 1);
            bright = lampLevel;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            Color dayAmb = new Color(0.115f, 0.112f, 0.11f) * (1f - 0.06f * (day - 1)), nightAmb = new Color(0.018f, 0.018f, 0.026f);
            RenderSettings.ambientLight = Color.Lerp(nightAmb, Color.Lerp(dayAmb, dayAmb * new Color(1.15f, 0.95f, 0.78f), gold * 0.6f), Mathf.Clamp01(sun * 1.4f));
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = Color.Lerp(new Color(0.008f, 0.008f, 0.012f), new Color(0.07f, 0.068f, 0.065f), Mathf.Clamp01(sun * 1.4f));
            RenderSettings.fogDensity = (0.018f + 0.006f * (day - 1)) * Mathf.Lerp(1.5f, 1f, Mathf.Clamp01(sun * 1.4f));
            if (Player.I != null) Player.I.cam.backgroundColor = RenderSettings.fogColor;
            if (Outdoors.I != null && (time != outdoorTime || newDay != day)) { outdoorTime = time; Outdoors.I.SetTime(time, day); }
            Color golden = new Color(1f, 0.78f, 0.5f);
            foreach (var w in windows)
            {
                Color dayC = Color.Lerp(new Color(0.75f, 0.8f, 0.86f), new Color(0.95f, 0.75f, 0.52f), gold);
                Color c = Color.Lerp(new Color(0.03f, 0.04f, 0.08f), Color.Lerp(new Color(0.42f, 0.45f, 0.5f), dayC, sun), Mathf.Clamp01(sun * 3f));
                w.pane.color = c;
                w.pane.SetColor("_EmissionColor", c * (sun > 0.02f ? 1.1f * sun + 0.15f : 0.25f));
                w.glow.intensity = Mathf.Max(0.15f, 2.4f * sun);
                w.glow.color = Color.Lerp(new Color(0.35f, 0.45f, 0.85f), Color.Lerp(new Color(0.86f, 0.9f, 1f), golden, gold), Mathf.Clamp01(sun * 3f));
            }
        }

        /// <summary>Make a light switch on a wall (`normal` points out of the wall into the room).</summary>
        Interactable LightSwitch(Room r, Vector3 at, Vector3 normal)
        {
            bool onX = Mathf.Abs(normal.x) > 0.5f;            // the wall runs along z
            var plate = Build.Box("Light switch", transform, at, onX ? new Vector3(0.012f, 0.12f, 0.08f) : new Vector3(0.08f, 0.12f, 0.012f), Cream, false);
            var col = plate.AddComponent<BoxCollider>();
            col.size = onX ? new Vector3(4f, 1.6f, 1.8f) : new Vector3(1.8f, 1.6f, 4f);        // easy to hit with a glance
            var toggle = Build.Box("Switch", transform, at + normal * 0.01f, onX ? new Vector3(0.012f, 0.032f, 0.02f) : new Vector3(0.02f, 0.032f, 0.012f), Dark, false);
            System.Action show = () => toggle.transform.localPosition = at + normal * 0.01f + Vector3.up * (r.lit ? -0.012f : 0.012f);
            show();
            var it = Build.Usable(plate, "");
            it.promptFn = () => r.lit ? "Switch the light off" : "Switch the light on";
            it.use = () =>
            {
                r.lit = !r.lit;
                Sfx.At("click", plate.transform.position, 0.55f, r.lit ? 1.1f : 0.9f);
                show();
                if (Switched != null) Switched(r, r.lit);
            };
            switchShows.Add(show);
            r.lightSwitch = it;
            return it;
        }

        readonly List<System.Action> switchShows = new List<System.Action>();

        /// <summary>Put every switch's toggle where its lamp says (after something other than a hand changed them).</summary>
        public void ShowSwitches() { foreach (var s in switchShows) s(); }

        /// <summary>Every flick of a light switch (the learner keeps count).</summary>
        public static System.Action<Room, bool> Switched;

        /// <summary>Switches beside every door (rooms and both ends of the hall). Called once the rooms exist.</summary>
        void Switches()
        {
            Plain();
            foreach (var r in rooms)
            {
                if (r == hall || r.door == null || r.name == "bathroom") continue;
                // The doorway is in the hall wall at the room's own z; the switch goes just inside, past the latch side.
                Vector3 hinge = r.door.transform.localPosition;
                float inside = Mathf.Sign(r.center.x - hinge.x);
                float latch = Mathf.Sign(r.center.z - hinge.z);
                Vector3 at = new Vector3(hinge.x + inside * (T / 2f + 0.007f), 1.2f, r.center.z + latch * (Gap / 2f + 0.24f));
                LightSwitch(r, at, new Vector3(inside, 0f, 0f));
            }
            LightSwitch(hall, new Vector3(0.78f, 1.2f, 8f - T / 2f - 0.007f), Vector3.back);
            LightSwitch(hall, new Vector3(-0.68f, 1.2f, -8f + T / 2f + 0.007f), Vector3.forward);
        }

        void Update()
        {
            float now = Time.time;
            foreach (var r in rooms)
            {
                // Daylight from the windows (a little bounced fill), and the lamp if it's switched on.
                float fill = Sun * (r == hall ? 0.45f : 0.16f);
                float b = Mathf.Max(fill, r.lit ? lampLevel : 0f);
                float target = now < r.darkUntil ? 0f : r.baseIntensity * b;
                r.light.intensity = Mathf.MoveTowards(r.light.intensity, target, Time.deltaTime * 4f);
                if (r.extra != null) r.extra.intensity = Mathf.MoveTowards(r.extra.intensity, now < r.darkUntil ? 0f : b, Time.deltaTime * 4f);
            }
            for (int i = 0; i < shades.Count && i < shadeAt.Count; i++)
            {
                var r = RoomAt(shadeAt[i]);
                bool on = r != null && r.lit && now >= r.darkUntil;
                shades[i].SetColor("_EmissionColor", shades[i].color * (on ? 0.55f : 0.05f));
            }
            if (fire != null) fire.intensity = (0.75f + 0.3f * Mathf.PerlinNoise(now * 3f, 0.5f)) * Mathf.Max(0.4f, bright);
            KitTick();
            // An old house settles: a creak in the boards or the walls every so often, more of them at night.
            if (now > nextSettle)
            {
                nextSettle = now + Random.Range(time == 0 ? 25f : 12f, time == 0 ? 70f : 40f);
                var r = rooms[Random.Range(0, rooms.Count)];
                Sfx.At("settle", r.center + new Vector3(Random.Range(-2f, 2f), Random.Range(0.2f, 2.6f), Random.Range(-2f, 2f)), time == 0 ? 0.35f : 0.55f, 0.85f + Random.value * 0.3f);
            }
            if (fireLoop != null) fireLoop.volume = fireLit ? (time == 0 ? 0.25f : 0.45f) : 0f;
            if (cellarBulb != null) cellarBulb.intensity = Mathf.PerlinNoise(now * 7f, 3f) > 0.2f ? 2.2f : 0.6f;
            // Once everything dropped into place has come to rest, that is where it "belongs".
            if (settleAt > 0f && now > settleAt)
            {
                settleAt = -1f;
                foreach (var m in movables) if (m != null && !m.held) m.RememberHome();
            }
        }
    }
}
