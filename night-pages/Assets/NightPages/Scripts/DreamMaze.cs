using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The dream as a maze of rooms that won't hold still. A square grid of rooms, each joined to some of its
    /// neighbours by doorways. You start in one; the way out (a lit door, with your bedside clock ticking behind it)
    /// is in the room furthest away. You have until the night runs out.
    ///
    /// It changes, in the manner of Layers of Fear: only where you aren't looking. Doorways you can't see close up and
    /// others open (the door you came through may not be there when you turn round); rooms you've left are
    /// refurnished, repapered, or are a different room altogether when you come back. And in the manner of Silent
    /// Hill: as the night runs out, the wallpaper comes away in sheets and what's under it is rust and black mould.
    /// However it changes, there is always a way from where you stand to the way out: hard, never impossible.
    ///
    /// Pages are scattered through it: this run's story, written from what the storyteller has actually done to you.
    /// Reading one buys a little time and lights the next doorways toward the way out for a while.
    /// </summary>
    public partial class DreamMaze
    {
        public const float Pitch = 8f, H = 3f, Gap = 1.4f, DoorH = 2.2f;

        internal class Side { public GameObject solid, door, glow; public int type = -1; }
        /// <summary>Somewhere to hide: a locker or a cabinet to shut yourself in, or a table to get under.</summary>
        public class HideSpot
        {
            public string what;             // "locker", "cabinet", "table"
            public bool under;              // under it (a table: only any good in the dark), not inside it
            public Vector3 inside, outside; // where you are when hidden, and where you step back out to
            public float yaw;               // the way you face while hidden (out into the room)
            public Collider solid;
            public Transform root;
            internal Cell cell;
        }

        internal class Cell
        {
            public int shape, blocked;          // blocked: which corner quarters are filled in (bit 0: +x, bit 1: +z, as Corner())
            public float height = H;            // this room's ceiling
            public readonly List<Renderer> extra = new List<Renderer>();     // other wall-papered surfaces (corner blocks, upper walls)
            public bool lit, held;            // held: something is happening in it; it isn't rearranged or refurnished meanwhile
            public Light lamp;
            public GameObject slab, ceiling;
            public readonly List<HideSpot> hides = new List<HideSpot>();
            public int x, z, theme, kind;
            public Transform root, contents;
            public Side[] sides = new Side[4];          // 0 east (+x), 1 west, 2 north (+z), 3 south
            public Renderer floor;
            public Interactable page;
            public string dressed;               // what Dress made of it (for tests), or null
        }
        class Edge { public Cell a, b; public int sideA, sideB; public bool open, merged; public int type; public Vector3 mid; public float glowUntil, sealedUntil; }

        readonly GameDirector g;
        readonly Transform root;
        readonly System.Random rng;
        readonly int n, day;
        readonly Cell[,] cells;
        readonly List<Edge> edges = new List<Edge>();
        readonly Material paintMat;
        Cell exitCell, lastCell;
        Transform exit;
        AudioSource tick;
        float nextShift;
        int rot;                                       // 0..3: how far the walls have gone
        public int shifts, pagesRead, pagesPlaced, exitMoves;
        /// <summary>Called when the dreamer picks up batteries.</summary>
        public System.Action onBattery;

        /// <summary>Called when the dreamer uses a hiding place.</summary>
        public System.Action<HideSpot> onHide;
        public int Size { get { return n; } }

        static readonly Color[] Papers =
        {
            new Color(0.72f, 0.66f, 0.58f), new Color(0.5f, 0.58f, 0.5f), new Color(0.6f, 0.36f, 0.34f),
            new Color(0.56f, 0.56f, 0.62f), new Color(0.7f, 0.62f, 0.42f),
        };
        static readonly Color[] Floors =
        {
            new Color(0.2f, 0.13f, 0.09f), new Color(0.26f, 0.08f, 0.07f), new Color(0.16f, 0.17f, 0.16f), new Color(0.1f, 0.1f, 0.11f),
        };
        static readonly Dictionary<int, Material> wallMats = new Dictionary<int, Material>();
        static readonly Color Warm = new Color(1f, 0.8f, 0.5f);

        public static void ResetStatics() { wallMats.Clear(); }

        public DreamMaze(GameDirector director, Transform dreamRoot, int size, int day, int seed, Material painting)
        {
            g = director;
            n = size;
            this.day = day;
            rng = new System.Random(seed);
            paintMat = painting;
            cells = new Cell[n, n];
            root = new GameObject("Maze").transform;
            root.SetParent(dreamRoot, false);

            // You start in a corner-ish room; the root is placed so that room's middle is where the dreamer stands.
            int sx = rng.Next(n), sz = rng.Next(n);
            root.localPosition = -Local(sx, sz);

            float span = n * Pitch;
            // (Each room has its own floor and ceiling: in a dream either can go.)
            Build.Box("Under everything", root, new Vector3(0f, -60f, 0f), new Vector3(span + 0.4f, 0.2f, span + 0.4f), Color.black);

            for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                    cells[x, z] = MakeCell(x, z);
            for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (x + 1 < n) edges.Add(new Edge { a = cells[x, z], b = cells[x + 1, z], sideA = 0, sideB = 1, mid = Local(x, z) + new Vector3(Pitch / 2f, 0f, 0f) });
                    if (z + 1 < n) edges.Add(new Edge { a = cells[x, z], b = cells[x, z + 1], sideA = 2, sideB = 3, mid = Local(x, z) + new Vector3(0f, 0f, Pitch / 2f) });
                }
            foreach (var e in edges) if (rng.Next(100) < 10) e.merged = true;        // halls: two rooms with no wall between
            Connect(null);
            lastCell = cells[sx, sz];

            // The way out goes in the room that is the longest walk from the start.
            exitCell = Furthest(lastCell, null);
            foreach (var c in cells) Furnish(c);
            BuildExit();
            PlaceStoryRooms();
            PlacePages();
            nextShift = Time.time + 18f;
            g.Log("dream maze: " + n + " by " + n + " rooms, the way out is " + Distance(lastCell, exitCell) + " rooms' walk away, " + pagesPlaced + " pages in it");
        }

        // ---------------------------------------------------------------- geometry

        Vector3 Local(int x, int z) { return new Vector3((x - (n - 1) / 2f) * Pitch, 0f, (z - (n - 1) / 2f) * Pitch); }
        Vector3 World(Vector3 local) { return root.TransformPoint(local); }
        public Vector3 Centre(int x, int z) { return World(Local(x, z)); }

        Cell CellAt(Vector3 world)
        {
            Vector3 l = root.InverseTransformPoint(world);
            int x = Mathf.Clamp(Mathf.FloorToInt(l.x / Pitch + n / 2f), 0, n - 1), z = Mathf.Clamp(Mathf.FloorToInt(l.z / Pitch + n / 2f), 0, n - 1);
            return cells[x, z];
        }

        Material WallMat(int theme, int level)
        {
            // One material for each wallpaper. The rot isn't painted on: it's the wall shader, live, and how far it has
            // got is one number for the whole maze (see Tick).
            int key = theme;
            Material m;
            if (wallMats.TryGetValue(key, out m) && m != null) return m;
            var rotShader = Shader.Find("NightPages/Rot");
            m = rotShader != null ? new Material(rotShader) : Build.Mat(Color.white, 0f, 0.06f);
            if (rotShader == null) Debug.LogWarning("[NightPages] the Rot shader is missing: the maze walls won't rot");
            m.mainTexture = MazeTextures.Wall(Papers[theme % Papers.Length], 0, theme * 7);
            // Most rooms are papered with one of the real wallpapers (tinted toward the room's colour); some keep the drawn one.
            string[] real = { "Wallpaper001A", "Wallpaper001B", "Wallpaper001C", "Wallpaper002A", "Wallpaper002B", "Wallpaper002C" };
            string pick = real[(theme * 5 + 3) % real.Length];
            // (The painter's wallpapers came out too soft to cover a whole wall; its portraits, photos, drawings and
            // smears are used instead. Kept here, off, for when they're better.)
            Texture2D paintedPaper = null;
            if (paintedPaper != null)
            {
                m.mainTexture = paintedPaper;
                m.SetColor("_Color", new Color(0.85f, 0.82f, 0.78f));
            }
            else if (theme % 4 != 0 && Tex.Has(pick))
            {
                var photo = Tex.Real(pick, Pitch, H, 1f, Color.white);
                m.mainTexture = photo.mainTexture;
                m.SetColor("_Color", Color.Lerp(Color.white, Papers[theme % Papers.Length], 0.45f) * 0.9f);
            }
            string texName = m.mainTexture != null ? m.mainTexture.name : "";
            m.mainTextureScale = texName.StartsWith("Wallpaper0") ? new Vector2(Pitch / 1.1f, H / 1.1f)
                               : paintedPaper != null ? new Vector2(Pitch / 1.6f, H / 1.6f) : new Vector2(Pitch / 2.4f, H / 2.4f);
            wallMats[key] = m;
            return m;
        }

        Cell MakeCell(int x, int z)
        {
            var c = new Cell { x = x, z = z, theme = rng.Next(Papers.Length) };
            c.root = new GameObject("Room " + x + "," + z).transform;
            c.root.SetParent(root, false);
            c.root.localPosition = Local(x, z);
            var wall = WallMat(c.theme, 0);
            c.slab = Build.Box("Room floor", c.root, new Vector3(0f, -0.1f, 0f), new Vector3(Pitch, 0.2f, Pitch), Floors[rng.Next(Floors.Length)]);
            c.floor = c.slab.GetComponent<Renderer>();
            c.ceiling = Build.Box("Room ceiling", c.root, new Vector3(0f, H + 0.1f, 0f), new Vector3(Pitch, 0.2f, Pitch), new Color(0.16f, 0.15f, 0.14f));
            float half = Pitch / 2f - 0.05f;
            for (int s = 0; s < 4; s++)
            {
                bool alongZ = s < 2;                                   // east and west walls run north-south
                float sign = (s == 0 || s == 2) ? 1f : -1f;
                Vector3 at = alongZ ? new Vector3(sign * half, 0f, 0f) : new Vector3(0f, 0f, sign * half);
                var side = new Side();
                side.solid = Build.Box("Wall", c.root, at + Vector3.up * H / 2f, alongZ ? new Vector3(0.1f, H, Pitch) : new Vector3(Pitch, H, 0.1f), wall);
                c.sides[s] = side;
            }
            Shape(c, wall);
            return c;
        }

        // ---------------------------------------------------------------- rooms of different shapes

        // Not every room is a plain square box. Some have a corner filled in (an L), or two (a dog-leg); some have
        // pillars; some have their corners cut off (eight-sided); some are low, some are tall. And about one wall
        // in ten between two rooms isn't there at all, which makes halls two or more rooms long.
        void Shape(Cell c, Material wall)
        {
            int r = rng.Next(100);
            c.shape = r < 38 ? 0 : r < 53 ? 1 : r < 61 ? 2 : r < 71 ? 3 : r < 81 ? 4 : r < 91 ? 5 : 6;
            System.Action<int> block = q =>
            {
                c.blocked |= 1 << q;
                var p = Corner(q, 2.42f);
                c.extra.Add(Build.Box("Block", c.root, p + Vector3.up * H / 2f, new Vector3(3.0f, H, 3.0f), wall).GetComponent<Renderer>());
            };
            switch (c.shape)
            {
                case 1: block(rng.Next(4)); break;
                case 2: { int q = rng.Next(4); block(q); block(3 - q); break; }
                case 3:
                    for (int i = 0; i < 4; i++) c.extra.Add(Build.Box("Pillar", c.root, Corner(i, 1.7f) + Vector3.up * H / 2f, new Vector3(0.45f, H, 0.45f), wall).GetComponent<Renderer>());
                    break;
                case 4:
                    c.height = 2.35f;
                    c.ceiling.transform.localPosition = new Vector3(0f, c.height + 0.1f, 0f);
                    break;
                case 5:
                {
                    c.height = 4.6f;
                    c.ceiling.transform.localPosition = new Vector3(0f, c.height + 0.1f, 0f);
                    float half = Pitch / 2f - 0.05f, up = c.height - H;
                    for (int s = 0; s < 4; s++)
                    {
                        bool alongZ = s < 2;
                        float sign = (s == 0 || s == 2) ? 1f : -1f;
                        Vector3 at = alongZ ? new Vector3(sign * half, 0f, 0f) : new Vector3(0f, 0f, sign * half);
                        c.extra.Add(Build.Box("Upper wall", c.root, at + Vector3.up * (H + up / 2f), alongZ ? new Vector3(0.1f, up, Pitch) : new Vector3(Pitch, up, 0.1f), wall).GetComponent<Renderer>());
                    }
                    break;
                }
                case 6:
                    c.blocked = 15;
                    for (int i = 0; i < 4; i++)
                    {
                        var d = Build.Box("Cut corner", c.root, Corner(i, 2.8f) + Vector3.up * H / 2f, new Vector3(3.3f, H, 0.12f), wall);
                        d.transform.localRotation = Quaternion.Euler(0f, ((i & 1) == 0) == ((i & 2) == 0) ? -45f : 45f, 0f);
                        c.extra.Add(d.GetComponent<Renderer>());
                    }
                    break;
            }
        }

        bool Blocked(Cell c, Vector3 at)
        {
            if (Mathf.Abs(at.x) < 1.4f || Mathf.Abs(at.z) < 1.4f) return c.shape == 3 && Mathf.Abs(Mathf.Abs(at.x) - 1.7f) < 0.7f && Mathf.Abs(Mathf.Abs(at.z) - 1.7f) < 0.7f;
            int q = (at.x > 0f ? 1 : 0) | (at.z > 0f ? 2 : 0);
            return (c.blocked & (1 << q)) != 0;
        }

        // ---------------------------------------------------------------- doorways of different kinds

        // 0 a plain framed doorway; 1 an arch; 2 wide double doors, standing open; 3 a tall narrow slit;
        // 4 an ordinary door, ajar; 5 a curtain of hanging strips you push through.
        static readonly float[] DoorGap = { 1.4f, 1.6f, 2.6f, 0.9f, 1.4f, 1.4f };
        static readonly float[] DoorTop = { 2.2f, 2.5f, 2.3f, 2.8f, 2.2f, 2.2f };
        static readonly string[] DoorName = { "a plain doorway", "an arch", "double doors", "a narrow slit", "a door left ajar", "a curtain" };

        int RollDoor()
        {
            int r = rng.Next(100);
            return r < 22 ? 0 : r < 58 ? 4 : r < 68 ? 1 : r < 79 ? 2 : r < 90 ? 5 : 3;
        }

        // Build (or rebuild) one room's side of a doorway of the given kind. `leaves`: this side carries the door
        // leaves or curtain (only one side of each doorway does).
        void SetDoor(Cell c, int s, int type, bool leaves)
        {
            var side = c.sides[s];
            if (side.type == type && side.door != null) return;
            if (side.door != null) Object.Destroy(side.door);
            side.type = type;
            bool alongZ = s < 2;
            float sign = (s == 0 || s == 2) ? 1f : -1f, half = Pitch / 2f - 0.05f;
            Vector3 at = alongZ ? new Vector3(sign * half, 0f, 0f) : new Vector3(0f, 0f, sign * half);
            Vector3 inward = alongZ ? new Vector3(-sign, 0f, 0f) : new Vector3(0f, 0f, -sign);
            System.Func<float, float, Vector3> size = (len, h) => alongZ ? new Vector3(0.1f, h, len) : new Vector3(len, h, 0.1f);
            System.Func<float, float, float, Vector3> part = (len, h, thick) => alongZ ? new Vector3(thick, h, len) : new Vector3(len, h, thick);
            System.Func<float, Vector3> along = d => alongZ ? new Vector3(0f, 0f, d) : new Vector3(d, 0f, 0f);
            float g = DoorGap[type], top = DoorTop[type], jamb = (Pitch - g) / 2f;
            var wall = WallMat(c.theme, rot);
            var door = new GameObject("Wall with doorway").transform;
            door.SetParent(c.root, false);
            Build.Box("Wall", door, at + along(-(g + jamb) / 2f) + Vector3.up * H / 2f, size(jamb, H), wall);
            Build.Box("Wall", door, at + along((g + jamb) / 2f) + Vector3.up * H / 2f, size(jamb, H), wall);
            if (top < H - 0.02f) Build.Box("Wall", door, at + Vector3.up * (top + H) / 2f, size(g, H - top), wall);
            System.Action<Material> frame = m =>
            {
                Build.Box("Frame", door, at + along(-g / 2f - 0.03f) + Vector3.up * top / 2f, part(0.06f, top, 0.14f), m, false);
                Build.Box("Frame", door, at + along(g / 2f + 0.03f) + Vector3.up * top / 2f, part(0.06f, top, 0.14f), m, false);
                Build.Box("Frame", door, at + Vector3.up * (top + 0.03f), part(g + 0.12f, 0.06f, 0.14f), m, false);
            };
            var cream = Build.Mat(new Color(0.5f, 0.47f, 0.4f));
            var darkWood = Build.Mat(new Color(0.14f, 0.09f, 0.06f), 0f, 0.25f);
            switch (type)
            {
                case 0: frame(cream); break;
                case 1:
                    // The top corners filled in on the slant: an arch.
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var fillet = Build.Box("Wall", door, at + along(k * (g / 2f - 0.14f)) + Vector3.up * (top - 0.14f), size(0.62f, 0.62f), wall, false);
                        fillet.transform.localRotation = alongZ ? Quaternion.Euler(45f, 0f, 0f) : Quaternion.Euler(0f, 0f, 45f);
                    }
                    break;
                case 2:
                    frame(darkWood);
                    if (leaves)
                    {
                        var steel = Build.Mat(new Color(0.2f, 0.25f, 0.24f), 0f, 0.5f);
                        for (int k = -1; k <= 1; k += 2)
                        {
                            // Each leaf swung right back against... not quite: stopped part-way, as if something went through.
                            var pivot = new GameObject("Hinge").transform;
                            pivot.SetParent(door, false);
                            pivot.localPosition = at + along(k * g / 2f);
                            var leaf = Build.Box("Door leaf", pivot, inward * (g / 4f), alongZ ? new Vector3(g / 2f, top - 0.04f, 0.05f) : new Vector3(0.05f, top - 0.04f, g / 2f), steel, false);
                            leaf.transform.localPosition += Vector3.up * top / 2f;
                            Build.Box("Window", pivot, inward * (g / 4f) + Vector3.up * 1.55f, alongZ ? new Vector3(0.3f, 0.45f, 0.06f) : new Vector3(0.06f, 0.45f, 0.3f), new Color(0.02f, 0.03f, 0.03f), false);
                            pivot.localRotation = Quaternion.Euler(0f, k * (alongZ ? -sign : sign) * (12f + rng.Next(22)), 0f);
                        }
                    }
                    break;
                case 3: break;
                case 4:
                    frame(cream);
                    if (leaves)
                    {
                        Color[] paints = { new Color(0.55f, 0.54f, 0.5f), new Color(0.16f, 0.26f, 0.2f), new Color(0.3f, 0.1f, 0.09f), new Color(0.14f, 0.09f, 0.06f) };
                        Material paint;
                        if (Tex.Has("rough_pine_door") && rng.Next(3) > 0) paint = Tex.Real("rough_pine_door", 1f, 1f, 1f, Color.Lerp(paints[rng.Next(paints.Length)], Color.white, 0.55f), null, 0.2f);
                        else paint = Build.Mat(paints[rng.Next(paints.Length)], 0f, 0.3f);
                        // A real door, on a real hinge: you open it by hand (see Door). It swings into this room.
                        float into = (alongZ ? -sign : sign) * 92f;
                        var d = Door.Create(door, at + along(-g / 2f), into, "maze", paints[0], alongZ ? 0f : 90f, paint, g - 0.04f, top - 0.06f);
                        // Shut, a crack open, or standing open: a different door every time.
                        int how = rng.Next(100);
                        if (how < 45) d.Snap(false);
                        else if (how < 75) d.Ajar(0.08f + (float)rng.NextDouble() * 0.25f);
                        else d.Ajar(0.5f + (float)rng.NextDouble() * 0.5f);
                        doors.Add(d);
                    }
                    break;
                case 5:
                    Build.Box("Rail", door, at + Vector3.up * (top - 0.02f), part(g, 0.04f, 0.05f), darkWood, false);
                    if (leaves)
                    {
                        var cloth = Build.Mat(new Color(0.22f, 0.05f, 0.05f));
                        const int strips = 9;
                        for (int i = 0; i < strips; i++)
                        {
                            float w = g / strips;
                            var strip = Build.Box("Strip", door, at + along(-g / 2f + (i + 0.5f) * w) + Vector3.up * (top / 2f + 0.04f), part(w - 0.012f, top - 0.1f, 0.012f), cloth, false);
                            strip.transform.localRotation = Quaternion.Euler(0f, rng.Next(-9, 10), 0f);
                        }
                    }
                    break;
            }
            // The outline that lights when a page shows the way.
            var glow = new GameObject("Way glow").transform;
            glow.SetParent(door, false);
            var lit = Build.Mat(Warm, 1.6f);
            Build.Box("Glow", glow, at + along(-g / 2f + 0.02f) + Vector3.up * top / 2f, part(0.04f, top, 0.16f), lit, false);
            Build.Box("Glow", glow, at + along(g / 2f - 0.02f) + Vector3.up * top / 2f, part(0.04f, top, 0.16f), lit, false);
            Build.Box("Glow", glow, at + Vector3.up * (top - 0.02f), part(g, 0.04f, 0.16f), lit, false);
            glow.gameObject.SetActive(false);
            side.door = door.gameObject;
            side.glow = glow.gameObject;
        }

        void Paper(Cell c)
        {
            var wall = WallMat(c.theme, rot);
            foreach (var s in c.sides)
            {
                s.solid.GetComponent<Renderer>().sharedMaterial = wall;
                if (s.door != null) foreach (var r in s.door.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.name == "Wall") r.sharedMaterial = wall;
            }
            foreach (var r in c.extra) if (r != null) r.sharedMaterial = wall;
        }

        void Apply(Edge e)
        {
            Side sa = e.a.sides[e.sideA], sb = e.b.sides[e.sideB];
            if (e.merged)
            {
                // No wall here at all: the two rooms are one.
                sa.solid.SetActive(false); sb.solid.SetActive(false);
                if (sa.door != null) sa.door.SetActive(false);
                if (sb.door != null) sb.door.SetActive(false);
                return;
            }
            bool show = e.open && e.sealedUntil <= Time.time;
            if (show) { SetDoor(e.a, e.sideA, e.type, true); SetDoor(e.b, e.sideB, e.type, false); }
            sa.solid.SetActive(!show); if (sa.door != null) sa.door.SetActive(show);
            sb.solid.SetActive(!show); if (sb.door != null) sb.door.SetActive(show);
        }

        // ---------------------------------------------------------------- which rooms join

        // Join every room into one maze. Edges not in `free` keep the state they have (null: all are free).
        void Connect(HashSet<Edge> free)
        {
            var parent = new Dictionary<Cell, Cell>();
            System.Func<Cell, Cell> find = null;
            find = c => { Cell p; if (!parent.TryGetValue(c, out p) || p == c) return c; p = find(p); parent[c] = p; return p; };
            foreach (var e in edges)
            {
                if (e.merged) { e.open = true; parent[find(e.a)] = find(e.b); }
                else if (free == null || free.Contains(e)) e.open = false;
                else if (e.open) parent[find(e.a)] = find(e.b);
            }
            var order = new List<Edge>();
            foreach (var e in edges) if (!e.merged && (free == null || free.Contains(e))) order.Add(e);
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = order[i]; order[i] = order[j]; order[j] = t; }
            foreach (var e in order)
            {
                Cell ra = find(e.a), rb = find(e.b);
                if (ra != rb) { e.open = true; e.type = RollDoor(); parent[ra] = rb; }
                else if (rng.NextDouble() < 0.1) { e.open = true; e.type = RollDoor(); }       // a few loops, so it isn't one long corridor of dead ends
            }
            foreach (var e in edges) Apply(e);
        }

        Dictionary<Cell, Cell> Walk(Cell from)
        {
            var prev = new Dictionary<Cell, Cell> { { from, from } };
            var q = new Queue<Cell>();
            q.Enqueue(from);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var e in edges)
                {
                    if (!e.open) continue;
                    Cell o = e.a == c ? e.b : e.b == c ? e.a : null;
                    if (o == null || prev.ContainsKey(o)) continue;
                    prev[o] = c;
                    q.Enqueue(o);
                }
            }
            return prev;
        }

        int Distance(Cell from, Cell to)
        {
            var prev = Walk(from);
            if (!prev.ContainsKey(to)) return -1;
            int d = 0;
            for (var c = to; c != from; c = prev[c]) d++;
            return d;
        }

        Cell Furthest(Cell from, System.Func<Cell, bool> ok)
        {
            var prev = Walk(from);
            Cell best = from;
            int bestD = -1;
            foreach (var c in prev.Keys)
            {
                if (ok != null && !ok(c)) continue;
                int d = 0;
                for (var k = c; k != from; k = prev[k]) d++;
                if (d > bestD || (d == bestD && rng.Next(2) == 0)) { bestD = d; best = c; }
            }
            return best;
        }

        Edge Between(Cell a, Cell b)
        {
            foreach (var e in edges) if ((e.a == a && e.b == b) || (e.a == b && e.b == a)) return e;
            return null;
        }

        /// <summary>Where to walk next to reach the way out from `pos` (the next doorway, then the room past it, then the door itself).</summary>
        public Vector3 Guide(Vector3 pos)
        {
            var here = CellAt(pos);
            if (here == exitCell) return exit.position;
            var prev = Walk(exitCell);
            if (!prev.ContainsKey(here)) return exit.position;
            return Through(pos, here, prev[here]);
        }

        public int RoomsToExit(Vector3 pos) { return Distance(CellAt(pos), exitCell); }
        public Vector3 ExitPosition { get { return exit.position; } }

        /// <summary>A place on the floor of some room between min and max metres from `from`.</summary>
        public Vector3 RandomPoint(Vector3 from, float min, float max, bool preferUnseen, Camera cam)
        {
            Vector3 best = from;
            float bestScore = float.MaxValue;
            for (int i = 0; i < 30; i++)
            {
                var c = cells[rng.Next(n), rng.Next(n)];
                Vector3 p = World(Local(c.x, c.z) + new Vector3((float)rng.NextDouble() * 4.4f - 2.2f, 0f, (float)rng.NextDouble() * 4.4f - 2.2f));
                float d = (p - from).magnitude;
                float score = d < min ? min - d : d > max ? d - max : 0f;
                if (preferUnseen && Vis.Sees(cam, p + Vector3.up * 1.5f)) score += 6f;
                if (score < bestScore) { bestScore = score; best = p; }
                if (score <= 0f) break;
            }
            return best;
        }

        // ---------------------------------------------------------------- what's in a room

        static readonly string[] Furniture =
        {
            "dining_chair_02", "round_wooden_table_02", "drawer_cabinet", "vintage_wooden_drawer_01", "ClassicConsole_01",
            "vintage_grandfather_clock_01", "ArmChair_01", "Rockingchair_01", "painted_wooden_chair_01", "potted_plant_01",
        };
        static readonly float[] FurnitureH = { 0.95f, 0.75f, 1.9f, 0.9f, 0.9f, 2.1f, 0.95f, 1.05f, 0.9f, 0.9f };

        // Is this spot on the floor needed for getting into a hiding place?
        bool NearHide(Cell c, Vector3 at)
        {
            foreach (var h in c.hides)
            {
                Vector3 spot = h.root.localPosition, front = c.root.InverseTransformPoint(h.outside);
                front.y = 0f;
                if ((spot - at).magnitude < 1.5f || (front - at).magnitude < 1.5f) return true;
            }
            return false;
        }

        GameObject Thing(Cell c, int which, Vector3 at, float yaw, bool onCeiling = false)
        {
            return ThingId(c, Furniture[which], FurnitureH[which], at, yaw, onCeiling);
        }

        // One of a role, from the catalogue (any of the downloaded models in it), at its real height.
        GameObject Role(Cell c, string role, Vector3 at, float yaw, bool onCeiling = false)
        {
            var id = Catalogue.Any(role, rng);
            if (id == null) return null;
            return ThingId(c, id, Mathf.Min(2.3f, ModelSizes.Height(id, 0.9f)), at, yaw, onCeiling);
        }

        GameObject ThingId(Cell c, string id, float height, Vector3 at, float yaw, bool onCeiling = false)
        {
            if (!onCeiling && NearHide(c, at)) return null;        // never furniture in front of somewhere to hide
            if (Blocked(c, at)) return null;                       // that corner is filled in
            GameObject hit;
            var go = Models.Place(id, c.contents, onCeiling ? Vector3.zero : at, height, yaw, out hit, !onCeiling, 2.2f);
            if (go == null)
            {
                // The model isn't installed: a plain dark block the same size stands in.
                go = Build.Box("Furniture", c.contents, at + Vector3.up * height / 2f, new Vector3(0.6f, height, 0.6f), new Color(0.2f, 0.13f, 0.09f), !onCeiling);
                if (onCeiling) go.transform.localPosition = new Vector3(at.x, c.height - height / 2f, at.z);
                return go;
            }
            if (onCeiling)
            {
                // Standing on the ceiling as if that were the floor.
                var flip = new GameObject("Flipped").transform;
                flip.SetParent(c.contents, false);
                flip.localPosition = new Vector3(at.x, c.height, at.z);
                flip.localRotation = Quaternion.Euler(180f, 0f, 0f);
                go.transform.SetParent(flip, false);
            }
            return go;
        }

        // Corners and wall-middles are clear of the doorways (those are in the middle of each wall, so furniture keeps to the corners).
        Vector3 Corner(int i, float inset = 2.7f) { return new Vector3((i & 1) == 0 ? -inset : inset, 0f, (i & 2) == 0 ? -inset : inset); }

        // A room that was something once: a parlour, a dining room, a bedroom, a study, a storeroom, a classroom,
        // a barber's. Each from its own roles, so the same kind of room is never furnished the same way twice.
        void FurnishAs(Cell c, int theme)
        {
            // Facing into the room from a corner.
            System.Func<Vector3, float> inward = p => Models.Face(-p.x, -p.z);
            switch (theme)
            {
                case 0:     // parlour
                    Role(c, Catalogue.Sofa, Corner(0, 2.6f), inward(Corner(0)));
                    Role(c, Catalogue.Armchair, Corner(3, 2.7f), inward(Corner(3)));
                    Role(c, Catalogue.SmallTable, Corner(1, 2.8f), 0f);
                    Role(c, Catalogue.Plant, Corner(2, 3.1f), 0f);
                    break;
                case 1:     // dining
                {
                    Role(c, Catalogue.Table, new Vector3(1.6f, 0f, 1.6f), rng.Next(4) * 90f);
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 off = new Vector3(k < 2 ? (k == 0 ? -0.9f : 0.9f) : 0f, 0f, k >= 2 ? (k == 2 ? -0.8f : 0.8f) : 0f);
                        Role(c, Catalogue.Chair, new Vector3(1.6f, 0f, 1.6f) + off * 1.1f, Models.Face(-off.x, -off.z));
                    }
                    Role(c, Catalogue.Cabinet, Corner(1, 3.0f), inward(Corner(1)));
                    break;
                }
                case 2:     // bedroom, without the bed
                    Role(c, Catalogue.Tall, Corner(0, 3.0f), inward(Corner(0)));
                    Role(c, Catalogue.Cabinet, Corner(3, 3.0f), inward(Corner(3)));
                    Role(c, Catalogue.Chair, Corner(1, 2.6f), inward(Corner(1)) + 30f);
                    Role(c, Catalogue.Head, Corner(2, 3.6f) + Vector3.up * 1.5f, inward(Corner(2)), false);
                    break;
                case 3:     // study
                    Role(c, Catalogue.Table, Corner(1, 2.5f), inward(Corner(1)));
                    Role(c, Catalogue.Chair, Corner(1, 1.8f), inward(Corner(1)) + 180f);
                    Role(c, Catalogue.Tall, Corner(2, 3.0f), inward(Corner(2)));
                    Role(c, Catalogue.Lamp, Corner(0, 3.2f), 0f);
                    break;
                case 4:     // storeroom
                    for (int i = 0; i < 4; i++) { Role(c, Catalogue.Clutter, Corner(i, 3.1f), rng.Next(360)); Role(c, Catalogue.Clutter, Corner(i, 2.6f), rng.Next(360)); }
                    Role(c, Catalogue.Odd, Corner(rng.Next(4), 2.4f), rng.Next(360));
                    break;
                case 5:     // classroom: rows of desks facing a board
                    for (int row = 0; row < 2; row++)
                        for (int col = -1; col <= 1; col += 2)
                        {
                            var d = new Vector3(col * 2.4f, 0f, -1.6f + row * 2.0f);
                            if (Blocked(c, d)) continue;
                            ThingId(c, "SchoolDesk_01", 0.88f, d, 0f);
                            ThingId(c, "SchoolChair_01", 1.0f, d + new Vector3(0f, 0f, -0.75f), 0f);
                        }
                    ThingId(c, "standing_chalkboard_01", 1.5f, new Vector3(2.4f, 0f, 2.9f), 180f);
                    break;
                default:    // one chair, in the middle, facing the wall
                    ThingId(c, rng.Next(2) == 0 ? "BarberShopChair_01" : "GreenChair_01", 1.3f, new Vector3(0.6f, 0f, -0.5f), rng.Next(4) * 90f);
                    break;
            }
        }

        void Furnish(Cell c)
        {
            if (c.held) return;
            if (c.contents != null) Object.Destroy(c.contents.gameObject);
            c.contents = new GameObject("Contents").transform;
            c.contents.SetParent(c.root, false);
            c.page = null;
            c.dressed = null;
            c.hides.Clear();
            // Most rooms were somebody's room once (furnished, lived in, left); the rest are the dream's own strange rooms.
            int rk = rng.Next(100);
            c.kind = c == exitCell ? 0 : rk < 8 ? 0 : rk < 56 ? 1 : rk < 63 ? 3 : rk < 70 ? 4 : rk < 76 ? 5 : rk < 85 ? 6 : rk < 91 ? 7 : 8;
            if (c.shape == 3 && c.kind == 4) c.kind = 1;          // (no ring of chairs round the pillars)
            // About half the rooms have a working bulb. The rest are dark: you carry very little light of your own.
            c.lit = c == exitCell || rng.Next(100) < 50;
            c.lamp = null;
            if (c.lit)
            {
                Build.Prim(PrimitiveType.Sphere, "Bulb", c.contents, new Vector3(0f, c.height - 0.12f, 0f), new Vector3(0.09f, 0.11f, 0.09f), Build.Mat(new Color(1f, 0.85f, 0.6f), 1.2f), false);
                c.lamp = Build.Lamp("Room light", c.contents, new Vector3(0f, c.height - 0.4f, 0f), new Color(1f, 0.86f, 0.66f), 2.6f, 9.5f);
            }
            Marks(c);
            // Batteries, now and then: on the floor by a wall, or on something.
            if (c != exitCell && rng.Next(100) < 22) PutBatteries(c);
            // Most rooms have somewhere to hide, against a wall away from the doorways.
            if (c != exitCell && rng.Next(100) < 70) AddHide(c, rng.Next(3), rng.Next(4));
            if (c != exitCell && rng.Next(100) < 25) AddHide(c, 2, rng.Next(4));
            switch (c.kind)
            {
                case 0: if (c != exitCell) DressLight(c); break;          // empty: just the room, and what's been left in it
                case 1:                                                  // somebody's room, furnished as some kind of room, and lived in, and left
                case 2:
                    Dress(c, rng.Next(7));
                    break;
                case 3:                                                  // the same furniture, on the ceiling
                    for (int i = 0; i < 4; i++) Thing(c, rng.Next(5), Corner(i, 2.2f), rng.Next(4) * 90f, true);
                    break;
                case 4:                                                  // chairs in a ring, all facing the middle
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * Mathf.PI * 2f / 7f;
                        var p = new Vector3(Mathf.Cos(a) * 1.9f, 0f, Mathf.Sin(a) * 1.9f);
                        Thing(c, 0, p, Models.Face(-p.x, -p.z));
                    }
                    break;
                case 5:                                                  // people standing with their faces to the wall
                    for (int i = 0; i < 4; i++)
                    {
                        if (rng.Next(4) == 0 || Blocked(c, Corner(i, 3.1f))) continue;
                        var f = Build.Figure("Turned away", c.contents);
                        Vector3 p = Corner(i, 3.1f);
                        f.localPosition = p;
                        f.localRotation = Quaternion.LookRotation(new Vector3(p.x, 0f, p.z));
                    }
                    break;
                case 6:                                                  // a gallery: the painting, on every wall, twice
                    DressLight(c);
                    for (int s = 0; s < 4; s++)
                        for (int k = -1; k <= 1; k += 2)
                        {
                            bool alongZ = s < 2;
                            float sign = (s == 0 || s == 2) ? 1f : -1f, half = Pitch / 2f - 0.13f;
                            Vector3 at = alongZ ? new Vector3(sign * half, 1.55f, k * 2.4f) : new Vector3(k * 2.4f, 1.55f, sign * half);
                            string galleryKind = rng.Next(2) == 0 ? "portraits" : "paintings";
                            var gtex = Painted.Choose(galleryKind, Brain, rng);
                            var pic = Painted.Picture(gtex, new Color(0.85f, 0.82f, 0.78f)) ?? paintMat;
                            Build.Box("Frame", c.contents, at - (alongZ ? new Vector3(sign * 0.01f, 0f, 0f) : new Vector3(0f, 0f, sign * 0.01f)), alongZ ? new Vector3(0.04f, 1.22f, 1.22f) : new Vector3(1.22f, 1.22f, 0.04f), new Color(0.12f, 0.08f, 0.05f), false);
                            var gr = Build.Box("Painting", c.contents, at, alongZ ? new Vector3(0.05f, 1.1f, 1.1f) : new Vector3(1.1f, 1.1f, 0.05f), pic, false);
                            Painted.Hang(gr.GetComponent<Renderer>(), galleryKind, gtex);
                            // A picture light over each one (the paintings are dark: without it they're black squares).
                            Vector3 into = alongZ ? new Vector3(-sign, 0f, 0f) : new Vector3(0f, 0f, -sign);
                            Build.Box("Picture light", c.contents, at + Vector3.up * 0.68f + into * 0.08f, alongZ ? new Vector3(0.12f, 0.04f, 0.4f) : new Vector3(0.4f, 0.04f, 0.12f), new Color(0.45f, 0.36f, 0.16f), false);
                            var pl = Build.Lamp("Picture light", c.contents, at + Vector3.up * 0.62f + into * 0.35f, new Color(1f, 0.85f, 0.6f), 1.1f, 1.8f);
                        }
                    break;
                case 7:                                                  // flooded: black water over the floor
                    DressLight(c);
                    Build.Box("Water", c.contents, new Vector3(0f, 0.12f, 0f), new Vector3(Pitch - 0.25f, 0.02f, Pitch - 0.25f), Build.Mat(new Color(0.01f, 0.018f, 0.02f), 0f, 0.62f), false);
                    if (rng.Next(2) == 0) Thing(c, 0, new Vector3(0.6f, 0f, -0.4f), rng.Next(360));
                    break;
                default:                                                 // doors, leaning, that go nowhere
                    var dm = Build.Mat(new Color(0.13f, 0.09f, 0.07f));
                    for (int i = 0; i < 4; i++)
                    {
                        if (NearHide(c, Corner(i, 2.2f)) || Blocked(c, Corner(i, 2.2f))) continue;
                        var d = Build.Box("Door to nowhere", c.contents, Corner(i, 2.2f) + Vector3.up * 1.05f, new Vector3(1f, 2.1f, 0.07f), dm, true);
                        d.transform.localRotation = Quaternion.Euler(0f, rng.Next(360), 0f);
                    }
                    break;
            }
        }

        void PutBatteries(Cell c)
        {
            Vector3 at = new Vector3(((float)rng.NextDouble() - 0.5f) * 5f, 0.03f, ((float)rng.NextDouble() - 0.5f) * 5f);
            if (Blocked(c, at)) return;
            var pack = new GameObject("Batteries").transform;
            pack.SetParent(c.contents, false);
            pack.localPosition = at;
            pack.localRotation = Quaternion.Euler(0f, rng.Next(360), 0f);
            var shell = Build.Mat(new Color(0.85f, 0.55f, 0.1f), 0f, 0.5f);
            var cap = Build.Mat(new Color(0.12f, 0.12f, 0.12f), 0.6f, 0.6f);
            for (int i = 0; i < 2; i++)
            {
                var b = Build.Prim(PrimitiveType.Cylinder, "Battery", pack, new Vector3(i * 0.04f - 0.02f, 0.017f, 0f), new Vector3(0.033f, 0.03f, 0.033f), shell, false);
                b.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Build.Prim(PrimitiveType.Cylinder, "Cap", pack, new Vector3(i * 0.04f - 0.02f, 0.017f, 0.032f), new Vector3(0.034f, 0.004f, 0.034f), cap, false).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            var hit = Build.Box("Batteries", pack, new Vector3(0f, 0.04f, 0f), new Vector3(0.22f, 0.1f, 0.22f), Color.black, true);
            hit.GetComponent<Renderer>().enabled = false;
            hit.GetComponent<Collider>().isTrigger = false;
            var it = Build.Usable(hit, "Take the batteries");
            bool bait = (Habits.Level(g, "takes batteries") >= 1 || Habits.Level(g, "flashlight") >= 2) && rng.NextDouble() < 0.35;
            it.use = () =>
            {
                if (onBattery != null) onBattery();
                Habits.Use(g, "takes batteries");
                Object.Destroy(pack.gameObject);
                if (bait && !c.held)
                {
                    // Left where you'd go for them: the room shuts round you.
                    Habits.trapsSprung++;
                    g.Log("habits: you always go for the batteries; these were bait, and the room shuts");
                    g.StartCoroutine(Sealed(c));
                }
            };
        }

        FearCore.Learner Brain { get { return g.fear != null ? g.fear.brain : null; } }

        /// <summary>The real doors in the maze (the hinged kind you open by hand).</summary>
        public readonly List<Door> doors = new List<Door>();

        /// <summary>Doors with their middle within `r` of `p` (on the floor plan).</summary>
        public List<Door> DoorsNear(Vector3 p, float r)
        {
            var near = new List<Door>();
            doors.RemoveAll(d => d == null);
            foreach (var d in doors)
                if (d.gameObject.activeInHierarchy && Vis.Level(d.Middle - p).magnitude < r) near.Add(d);
            return near;
        }

        /// <summary>Called when the dreamer reads a page that was left as bait.</summary>
        public System.Action onBait;

        // ---------------------------------------------------------------- what's on the walls

        // The painter's marks: writing scrawled on a wall, a stain coming down from the ceiling, a child's drawing
        // pinned up, a face in a frame. Different in every room, every night.
        void Marks(Cell c)
        {
            if (c == exitCell) return;
            for (int n = 0; n < 2; n++)
            {
                int r = rng.Next(100);
                string kind = r < 22 ? "writing" : r < 40 ? "stains" : r < 55 ? "drawings" : r < 62 ? "portraits" : null;
                if (kind == null) continue;
                var tex = kind == "portraits" || kind == "drawings" ? Painted.Choose(kind, Brain, rng) : Painted.Pick(kind, rng);
                if (tex == null) continue;
                int s = rng.Next(4);
                bool alongZ = s < 2;
                float sign = (s == 0 || s == 2) ? 1f : -1f, half = Pitch / 2f - 0.11f;
                // To one side of the doorway (or anywhere, if the wall is whole).
                float lat = (rng.Next(2) == 0 ? -1f : 1f) * (1.6f + (float)rng.NextDouble() * 1.6f);
                float w, h, y;
                if (kind == "writing") { w = 2.2f; h = 1.5f; y = 1.45f; }
                else if (kind == "stains") { w = 2.8f; h = 2.0f; y = c.height - 1.0f; }
                else if (kind == "drawings") { w = 0.42f; h = 0.32f; y = 1.35f + (float)rng.NextDouble() * 0.3f; }
                else { w = 0.6f; h = 0.6f; y = 1.6f; }
                Vector3 at = alongZ ? new Vector3(sign * half, y, lat) : new Vector3(lat, y, sign * half);
                Vector3 size = alongZ ? new Vector3(0.01f, h, w) : new Vector3(w, h, 0.01f);
                if (Blocked(c, new Vector3(at.x, 0f, at.z))) continue;
                if (kind == "writing" || kind == "stains")
                {
                    var m = Painted.Decal(tex, kind == "stains" ? 0.85f : 1f, kind == "stains" ? 0.15f : 0.35f);
                    if (m != null) Build.Box("Marks on the wall", c.contents, at, size, m, false);
                }
                else
                {
                    Vector3 inward = alongZ ? new Vector3(-sign, 0f, 0f) : new Vector3(0f, 0f, -sign);
                    if (kind == "portraits") Build.Box("Frame", c.contents, at - inward * 0.005f, size + new Vector3(alongZ ? 0.03f : 0.1f, 0.1f, alongZ ? 0.1f : 0.03f), new Color(0.12f, 0.08f, 0.05f), false);
                    var pic = Painted.Picture(tex, kind == "drawings" ? new Color(0.95f, 0.93f, 0.88f) : new Color(0.85f, 0.82f, 0.78f));
                    var pr = Build.Box(kind == "drawings" ? "Drawing" : "Portrait", c.contents, at + inward * 0.01f, size, pic, false);
                    Painted.Hang(pr.GetComponent<Renderer>(), kind, tex);
                }
            }
        }

        // ---------------------------------------------------------------- hiding places

        // kind 0: a steel locker, 1: a wooden cabinet (both: shut yourself in, look out through the slats),
        // 2: a table to get under. `where` picks one of four places, each to one side of a doorway against a wall.
        void AddHide(Cell c, int kind, int where)
        {
            foreach (var h in c.hides) if ((h.root.localPosition - Spot(where)).sqrMagnitude < 1f) return;
            Vector3 at = Spot(where);
            if (Blocked(c, at) || SideMerged(c, where & 3)) return;        // that bit of wall is filled in, or isn't there
            // It stands with its back to the nearest wall and opens toward the middle of the room.
            Vector3 outDir = Mathf.Abs(at.x) > Mathf.Abs(at.z) ? new Vector3(-Mathf.Sign(at.x), 0f, 0f) : new Vector3(0f, 0f, -Mathf.Sign(at.z));
            var t = new GameObject(kind == 0 ? "Locker" : kind == 1 ? "Cabinet" : "Table").transform;
            t.SetParent(c.contents, false);
            t.localPosition = at;
            t.localRotation = Quaternion.LookRotation(outDir);
            var spot = new HideSpot { root = t, cell = c, under = kind == 2, what = kind == 0 ? "locker" : kind == 1 ? "cabinet" : "table" };
            GameObject body;
            if (kind == 2)
            {
                var wood = Build.Mat(new Color(0.24f, 0.16f, 0.1f), 0f, 0.2f);
                Build.Box("Top", t, new Vector3(0f, 0.78f, 0f), new Vector3(1.7f, 0.06f, 0.95f), wood, false);
                for (int i = 0; i < 4; i++) Build.Box("Leg", t, new Vector3((i & 1) == 0 ? -0.78f : 0.78f, 0.375f, (i & 2) == 0 ? -0.4f : 0.4f), new Vector3(0.07f, 0.75f, 0.07f), wood, false);
                // A cloth hanging over the far side and the ends: under it, in the dark, you're a shape among shapes.
                var cloth = Build.Mat(new Color(0.3f, 0.28f, 0.25f));
                Build.Box("Cloth", t, new Vector3(0f, 0.55f, -0.47f), new Vector3(1.72f, 0.5f, 0.02f), cloth, false);
                body = Build.Box("Table collider", t, new Vector3(0f, 0.4f, 0f), new Vector3(1.7f, 0.8f, 0.95f), wood, true);
                body.GetComponent<Renderer>().enabled = false;
                spot.inside = t.TransformPoint(new Vector3(0f, 0.02f, 0f));
            }
            else
            {
                var m = kind == 0 ? Build.Mat(new Color(0.2f, 0.24f, 0.25f), 0f, 0.45f) : Build.Mat(new Color(0.2f, 0.13f, 0.08f), 0f, 0.2f);
                float w = kind == 0 ? 0.8f : 1.1f, d = 0.75f, h = 2.0f;
                Build.Box("Back", t, new Vector3(0f, h / 2f, -d / 2f), new Vector3(w, h, 0.03f), m, false);
                Build.Box("Side", t, new Vector3(-w / 2f, h / 2f, 0f), new Vector3(0.03f, h, d), m, false);
                Build.Box("Side", t, new Vector3(w / 2f, h / 2f, 0f), new Vector3(0.03f, h, d), m, false);
                Build.Box("Top", t, new Vector3(0f, h, 0f), new Vector3(w, 0.03f, d), m, false);
                // The door: solid below, slats at eye height that you can see out between.
                Build.Box("Door", t, new Vector3(0f, 0.62f, d / 2f), new Vector3(w, 1.24f, 0.03f), m, false);
                Build.Box("Door", t, new Vector3(0f, 1.93f, d / 2f), new Vector3(w, 0.14f, 0.03f), m, false);
                // Thin slats with wide gaps at eye height: you can see the room, in strips.
                for (int i = 0; i < 5; i++) Build.Box("Slat", t, new Vector3(0f, 1.32f + i * 0.125f, d / 2f), new Vector3(w, 0.022f, 0.035f), m, false);
                body = Build.Box("Hide collider", t, new Vector3(0f, h / 2f, 0f), new Vector3(w, h, d), m, true);
                body.GetComponent<Renderer>().enabled = false;
                spot.inside = t.TransformPoint(new Vector3(0f, 0.02f, -0.05f));
            }
            spot.solid = body.GetComponent<Collider>();
            spot.outside = t.TransformPoint(new Vector3(0f, 0.05f, kind == 2 ? 1.15f : 1.05f));
            spot.yaw = Mathf.Atan2(outDir.x, outDir.z) * Mathf.Rad2Deg + root.eulerAngles.y;
            var it = Build.Usable(body, kind == 2 ? "Get under the table" : "Hide in the " + spot.what);
            it.use = () => { if (onHide != null) onHide(spot); };
            c.hides.Add(spot);
        }

        // Four places against the walls, each well to one side of that wall's doorway.
        static Vector3 Spot(int i)
        {
            const float wall = Pitch / 2f - 0.6f, off = 2.2f;
            switch (i & 3)
            {
                case 0: return new Vector3(wall, 0f, off);
                case 1: return new Vector3(-wall, 0f, -off);
                case 2: return new Vector3(-off, 0f, wall);
                default: return new Vector3(off, 0f, -wall);
            }
        }

        /// <summary>The nearest hiding place by walking distance (within `rooms` rooms), or null.</summary>
        public HideSpot NearestHide(Vector3 pos, int rooms, bool preferInside = false)
        {
            var here = CellAt(pos);
            var prev = Walk(here);
            HideSpot best = null;
            float bestD = float.MaxValue;
            foreach (var c in prev.Keys)
            {
                int d = 0;
                for (var k = c; k != here; k = prev[k]) d++;
                if (d > rooms) continue;
                foreach (var h in c.hides)
                {
                    float score = d * Pitch + (c == here ? (h.outside - pos).magnitude : 4f) + (preferInside && h.under ? 100f : 0f);
                    if (score < bestD) { bestD = score; best = h; }
                }
            }
            return best;
        }

        public List<HideSpot> HidesIn(Vector3 pos) { return CellAt(pos).hides; }

        /// <summary>A hiding place of this kind ("locker", "cabinet", "table") within `rooms` rooms of `pos` but not in its room, or null.</summary>
        public HideSpot HideOfKind(Vector3 pos, string what, int rooms)
        {
            var here = CellAt(pos);
            var prev = Walk(here);
            var options = new List<HideSpot>();
            foreach (var c in prev.Keys)
            {
                if (c == here || c.held) continue;
                int d = 0;
                for (var k = c; k != here; k = prev[k]) d++;
                if (d > rooms) continue;
                foreach (var h in c.hides) if (h.what == what) options.Add(h);
            }
            return options.Count > 0 ? options[rng.Next(options.Count)] : null;
        }

        /// <summary>The middle of a dark room near `pos` (by walking), or null.</summary>
        public Vector3? DarkRoomNear(Vector3 pos, int rooms)
        {
            var here = CellAt(pos);
            var prev = Walk(here);
            var options = new List<Cell>();
            foreach (var c in prev.Keys)
            {
                if (c.lit) continue;
                int d = 0;
                for (var k = c; k != here; k = prev[k]) d++;
                if (d <= rooms) options.Add(c);
            }
            if (options.Count == 0) return null;
            var pick = options[rng.Next(options.Count)];
            return World(Local(pick.x, pick.z));
        }
        public bool Dark(Vector3 pos) { return !CellAt(pos).lit; }
        /// <summary>The height of the ceiling over `pos`, above the maze floor (0 if there isn't one just now).</summary>
        public float CeilingOver(Vector3 pos)
        {
            var c = CellAt(pos);
            return c.ceiling != null && c.ceiling.activeSelf && !c.held ? c.height : 0f;
        }

        /// <summary>The maze floor's height in the world.</summary>
        public float FloorY { get { return root.position.y; } }

        /// <summary>
        /// Somewhere on a wall of this room to climb from: the wall nearest `pos` (if it's a whole wall, in a room with
        /// a ceiling), at a spot beside the doorway, not in it. `normal` points from the wall into the room.
        /// </summary>
        public bool ClimbSpot(Vector3 pos, out Vector3 spot, out Vector3 normal)
        {
            spot = pos; normal = Vector3.forward;
            var c = CellAt(pos);
            if (c.held || c.ceiling == null || !c.ceiling.activeSelf || c.shape == 1 || c.shape == 2 || c.shape == 6) return false;
            Vector3 centre = World(Local(c.x, c.z));
            Vector3 o = pos - centre;
            int side;
            if (Mathf.Abs(o.x) > Mathf.Abs(o.z)) { side = o.x > 0f ? 0 : 1; normal = new Vector3(o.x > 0f ? -1f : 1f, 0f, 0f); }
            else { side = o.z > 0f ? 2 : 3; normal = new Vector3(0f, 0f, o.z > 0f ? -1f : 1f); }
            if (SideMerged(c, side)) return false;
            Vector3 lateral = Vector3.Cross(Vector3.up, normal);
            float lat = Vector3.Dot(o, lateral);
            if (Mathf.Abs(lat) < 1.6f) lat = lat < 0f ? -1.6f : 1.6f;                  // beside the doorway, not over it
            lat = Mathf.Clamp(lat, -Pitch / 2f + 0.8f, Pitch / 2f - 0.8f);
            spot = centre - normal * (Pitch / 2f - 0.32f) + lateral * lat;
            spot.y = root.position.y;
            foreach (var h in c.hides) if (Vis.Level(h.root.position - spot).magnitude < 1.3f) return false;
            return true;
        }

        /// <summary>Is a haunt going on in the room at `pos`?</summary>
        public bool Busy(Vector3 pos) { return CellAt(pos).held; }
        public bool SameRoom(Vector3 a, Vector3 b) { return CellAt(a) == CellAt(b); }
        public Vector3 RoomCentre(Vector3 pos) { var c = CellAt(pos); return World(Local(c.x, c.z)); }

        /// <summary>How many rooms' walk from a to b (-1 if there's no way).</summary>
        public int Rooms(Vector3 a, Vector3 b) { return Distance(CellAt(a), CellAt(b)); }

        /// <summary>Where to walk next to get from `pos` to `target` through the doorways as they are now.</summary>
        public Vector3 GuideTo(Vector3 pos, Vector3 target)
        {
            Cell here = CellAt(pos), goal = CellAt(target);
            if (here == goal) return target;
            var prev = Walk(goal);
            if (!prev.ContainsKey(here)) return target;
            return Through(pos, here, prev[here]);
        }

        // Line up with the doorway into `next`, then go through it.
        Vector3 Through(Vector3 pos, Cell here, Cell next)
        {
            var e = Between(here, next);
            Vector3 door = World(e.mid), into = World(Local(next.x, next.z));
            Vector3 toRoom = (into - door).normalized;
            Vector3 before = door - toRoom * 0.9f;
            float along = Vector3.Dot(pos - door, toRoom), across = Mathf.Abs(Vector3.Dot(pos - door, Vector3.Cross(Vector3.up, toRoom)));
            float slack = e.merged ? 3f : Mathf.Min(0.35f, DoorGap[e.type] / 2f - 0.33f);
            if (e.merged) return into;
            if (along < -1.1f || (along < 0f && across > slack))
            {
                Vector3 off = pos - before; off.y = 0f;
                if (off.magnitude > 0.3f) return before;
            }
            return door + toRoom * 1.6f;
        }

        /// <summary>A room's middle at least `rooms` rooms' walk from `from` that the camera can't see: where something can arrive from.</summary>
        public Vector3 FarUnseen(Vector3 from, int rooms, Camera cam)
        {
            var here = CellAt(from);
            var prev = Walk(here);
            Cell best = null;
            int bestScore = int.MinValue;
            foreach (var c in prev.Keys)
            {
                int d = 0;
                for (var k = c; k != here; k = prev[k]) d++;
                Vector3 w = World(Local(c.x, c.z));
                if (d < 2 || Vis.Sees(cam, w + Vector3.up * 1.4f)) continue;
                int score = -Mathf.Abs(d - rooms) * 10 + rng.Next(6);
                if (score > bestScore) { bestScore = score; best = c; }
            }
            if (best == null) best = Furthest(here, null);
            return World(Local(best.x, best.z));
        }

        // ---------------------------------------------------------------- the way out

        void BuildExit()
        {
            exit = new GameObject("Exit door").transform;
            exit.SetParent(root, false);
            Build.Box("Post", exit, new Vector3(-0.6f, 1.1f, 0f), new Vector3(0.12f, 2.2f, 0.12f), Warm, false, 2f);
            Build.Box("Post", exit, new Vector3(0.6f, 1.1f, 0f), new Vector3(0.12f, 2.2f, 0.12f), Warm, false, 2f);
            Build.Box("Lintel", exit, new Vector3(0f, 2.26f, 0f), new Vector3(1.32f, 0.12f, 0.12f), Warm, false, 2f);
            Build.Box("Light inside", exit, new Vector3(0f, 1.1f, 0f), new Vector3(1.08f, 2.2f, 0.03f), new Color(1f, 0.93f, 0.8f), false, 1.2f);
            Build.Lamp("Door glow", exit, new Vector3(0f, 1.4f, 0f), Warm, 1.8f, 10f);
            // Your bedside clock, ticking on the other side of it: something to walk toward.
            tick = Sfx.Loop("clocktick", exit, 0.9f, true);
            if (tick != null) { tick.transform.localPosition = new Vector3(0f, 1.2f, 0f); tick.maxDistance = Pitch * 3.2f; }
            SeatExit();
        }

        void SeatExit()
        {
            exit.localPosition = Local(exitCell.x, exitCell.z) + new Vector3(0f, 0f, 1.2f);
            exit.localRotation = Quaternion.identity;
        }

        /// <summary>The way out isn't here after all (once, on later nights): it's in another room, far off, when you get close.</summary>
        public bool MoveExit(Vector3 playerPos, Camera cam)
        {
            var here = CellAt(playerPos);
            var to = Furthest(here, c => c != exitCell && c != here && !Vis.Sees(cam, World(Local(c.x, c.z)) + Vector3.up * 1.4f));
            if (to == here || to == exitCell) return false;
            var old = exitCell;
            exitCell = to;
            Furnish(to);
            SeatExit();
            exitMoves++;
            g.Log("dream maze: the way out moved from room " + old.x + "," + old.z + " to " + to.x + "," + to.z + " (" + Distance(here, to) + " rooms' walk)");
            return true;
        }

        // ---------------------------------------------------------------- pages

        void PlacePages()
        {
            var lore = Lore.Pages(g, day, rng);
            var rooms = new List<Cell>();
            foreach (var c in cells) if (c != exitCell && c != lastCell && c.page == null) rooms.Add(c);
            int want = Mathf.Min(lore.Count, 2 + n / 2);
            for (int i = 0; i < want && rooms.Count > 0; i++)
            {
                int k = rng.Next(rooms.Count);
                var c = rooms[k];
                rooms.RemoveAt(k);
                PutPage(c, lore[i]);
            }
        }

        void PutPage(Cell c, string text)
        {
            var go = Build.Box("Page", c.root, new Vector3((float)rng.NextDouble() * 2f - 1f, 0.03f, (float)rng.NextDouble() * 2f - 1f), new Vector3(0.22f, 0.02f, 0.3f), new Color(0.95f, 0.92f, 0.8f), true, 0.9f);
            go.transform.localRotation = Quaternion.Euler(0f, rng.Next(360), 0f);
            Build.Lamp("Page glow", go.transform, new Vector3(0f, 0.4f, 0f), new Color(0.95f, 0.9f, 0.75f), 0.5f, 2.5f);
            var it = Build.Usable(go, "Read the page");
            bool read = false;
            // You stop to read everything: some of them are left out for you to stop at.
            bool bait = Habits.Level(g, "reads pages") >= 1 && rng.NextDouble() < 0.25 + 0.12 * Habits.Level(g, "reads pages");
            it.use = () =>
            {
                if (!read)
                {
                    Habits.Use(g, "reads pages");
                    if (bait)
                    {
                        Habits.trapsSprung++;
                        g.Log("habits: you always stop to read; this page was left there for you to stop at");
                        if (onBait != null) onBait();
                    }
                }
                int left = RoomsToExit(g.player.transform.position);
                g.ReadDreamPage(text, read ? null : (left <= 1 ? "The ticking is very close." : "The ticking is " + left + " rooms from here. The doorways toward it are lit, for now."));
                if (read) return;
                read = true;
                pagesRead++;
                LightTheWay(g.player.transform.position, 28f);
                g.dream.AddTime(12f);
                go.GetComponent<Renderer>().sharedMaterial = Build.Mat(new Color(0.5f, 0.48f, 0.42f));
            };
            c.page = it;
            pagesPlaced++;
        }

        // The next few doorways toward the way out glow warm for a while (and can't close while they do).
        void LightTheWay(Vector3 from, float seconds)
        {
            var prev = Walk(exitCell);
            var c = CellAt(from);
            for (int i = 0; i < 3 && c != exitCell && prev.ContainsKey(c); i++)
            {
                var next = prev[c];
                var e = Between(c, next);
                if (e != null) e.glowUntil = Time.time + seconds;
                c = next;
            }
            Glow();
        }

        void Glow()
        {
            foreach (var e in edges)
            {
                bool on = e.glowUntil > Time.time;
                var ga = e.a.sides[e.sideA].glow; var gb = e.b.sides[e.sideB].glow;
                if (ga != null) ga.SetActive(on);
                if (gb != null) gb.SetActive(on);
            }
        }

        bool SideMerged(Cell c, int s)
        {
            foreach (var e in edges) if (e.merged && ((e.a == c && e.sideA == s) || (e.b == c && e.sideB == s))) return true;
            return false;
        }

        bool AnyMerged(Cell c) { for (int s = 0; s < 4; s++) if (SideMerged(c, s)) return true; return false; }

        /// <summary>The nearest room (by walking) that has loose furniture in it and nothing going on, or null.</summary>
        public Vector3? FurnishedRoom(Vector3 from)
        {
            var here = CellAt(from);
            var prev = Walk(here);
            Cell best = null;
            int bestD = int.MaxValue;
            foreach (var c in prev.Keys)
            {
                if (c == exitCell || c.held || Loose(c).Count < 3) continue;
                int d = 0;
                for (var k = c; k != here; k = prev[k]) d++;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best == null ? (Vector3?)null : World(Local(best.x, best.z));
        }

        // ---------------------------------------------------------------- it changes

        /// <summary>
        /// Every frame. `left01` is how much of the night is left (1 at the start, 0 when it runs out): the rooms change
        /// more often as it goes, and the walls rot.
        /// </summary>
        public void Tick(Vector3 playerPos, Camera cam, float left01)
        {
            var here = CellAt(playerPos);
            bool entered = here != lastCell;
            lastCell = here;
            // The rot: nothing for the first third of the night, then it creeps, everywhere, until the walls are mostly it.
            Shader.SetGlobalFloat("_NPRot", Mathf.Clamp01((0.68f - left01) / 0.68f) * 0.8f);      // (some paper always shows through)
            Shader.SetGlobalFloat("_NPFloorY", root.position.y);
            int wantRot = left01 > 0.6f ? 0 : left01 > 0.35f ? 1 : left01 > 0.15f ? 2 : 3;
            if (wantRot != rot)
            {
                rot = wantRot;
                // The walls you can see stay as they are for now; the rest are worse the next time you look.
                foreach (var c in cells) if (c != here && !Vis.Sees(cam, World(Local(c.x, c.z)) + Vector3.up * 1.4f)) Paper(c);
                g.Log("dream maze: the walls are going (stage " + rot + " of 3)");
            }
            else if (entered && rot > 0) Paper(here);

            bool glowChanged = false;
            foreach (var e in edges) if (e.glowUntil > 0f && e.glowUntil < Time.time) { e.glowUntil = 0f; glowChanged = true; }
            if (glowChanged) Glow();

            // It rearranges itself on a timer that shortens through the night, and sometimes the moment you step
            // into a new room (so the doorway behind you is the one that goes).
            if (Time.time > nextShift || (entered && rng.NextDouble() < 0.3))
            {
                nextShift = Time.time + Mathf.Lerp(9f, 22f, left01) - Mathf.Min(4f, day * 0.5f);
                Shift(playerPos, cam);
            }
        }

        /// <summary>Everything you can't see is up for change: which rooms join, and what the rooms you've left are now.</summary>
        public void Shift(Vector3 playerPos, Camera cam)
        {
            var here = CellAt(playerPos);
            var free = new HashSet<Edge>();
            var before = new Dictionary<Edge, bool>();
            foreach (var e in edges)
            {
                before[e] = e.open;
                Vector3 w = World(e.mid);
                bool near = (w - playerPos).sqrMagnitude < 2.2f * 2.2f;         // never shut a doorway on the dreamer
                if (e.merged || near || e.glowUntil > Time.time || e.a.held || e.b.held || e.sealedUntil > Time.time) continue;
                if (Vis.Sees(cam, w + Vector3.up * 1.1f) || Vis.Sees(cam, w + Vector3.up * 0.3f)) continue;
                if (rng.NextDouble() < 0.3) free.Add(e);            // a few at a time: it shifts, it doesn't reshuffle
            }
            if (free.Count == 0) return;
            Connect(free);
            if (Distance(here, exitCell) < 0)
            {
                // (Can't happen while every free edge may open, but never leave the dreamer cut off.)
                foreach (var e in edges) { e.open = before[e]; Apply(e); }
                g.Log("dream maze: a change would have cut the dreamer off; left as it was");
                return;
            }
            int doors = 0, rooms = 0;
            foreach (var e in edges) if (before[e] != e.open) doors++;
            // Rooms out of sight and not next door become something else.
            foreach (var c in cells)
            {
                if (c == here || c == exitCell || c.page != null || c.held) continue;
                Vector3 w = World(Local(c.x, c.z));
                if ((w - playerPos).magnitude < Pitch * 1.3f || Vis.Sees(cam, w + Vector3.up * 1.4f)) continue;
                if (rng.NextDouble() > 0.18) continue;
                if (rng.Next(3) == 0) c.theme = rng.Next(Papers.Length);
                Paper(c);
                Furnish(c);
                rooms++;
            }
            shifts++;
            if (doors > 0) Sfx.At("creak", playerPos + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f).normalized * 9f, 0.35f, 0.55f);
            g.Log("dream maze: it changed where you weren't looking: " + doors + " doorways opened or closed, " + rooms + " rooms became something else; the way out is now " + Distance(here, exitCell) + " rooms' walk");
        }

        public void Destroy()
        {
            alive = false;
            if (tick != null) Object.Destroy(tick.gameObject);
        }
    }

    /// <summary>
    /// Wall surfaces for the dream maze, painted in code: striped wallpaper in a given colour, at four stages of ruin.
    /// Stage 0 is whole. From stage 1 the paper lifts and comes away in sheets (ragged patches with a pale curled
    /// edge); what's underneath is rust, damp plaster and black mould running down in streaks. Stage 3 is mostly that.
    /// (This is the pre-made set the maze draws on; a set generated by an image model can replace it file for file.)
    /// </summary>
    public static class MazeTextures
    {
        const int N = 256;
        static readonly Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();

        public static void ResetStatics() { cache.Clear(); }

        static float Fbm(float x, float y, float seed)
        {
            float s = 0f, a = 0.5f;
            for (int o = 0; o < 4; o++) { s += Mathf.PerlinNoise(x + seed, y - seed * 0.7f) * a; x *= 2.03f; y *= 2.03f; a *= 0.5f; }
            return s / 0.9375f;
        }

        public static Texture2D Wall(Color paper, int stage, int seed)
        {
            int key = seed * 100 + stage * 10 + Mathf.RoundToInt(paper.r * 9f);
            Texture2D t;
            if (cache.TryGetValue(key, out t) && t != null) return t;
            t = new Texture2D(N, N, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, name = "Maze wall " + stage };
            var px = new Color[N * N];
            float cut = stage == 0 ? 2f : stage == 1 ? 0.62f : stage == 2 ? 0.5f : 0.38f;      // how much of the paper is gone
            float sd = seed * 3.7f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    // The paper: stripes, a small repeating motif, age.
                    float stripe = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 12f)) > 0.82f ? 0.82f : 1f;
                    float motif = (Mathf.Sin(u * Mathf.PI * 24f) * Mathf.Sin(v * Mathf.PI * 16f)) > 0.86f ? 0.78f : 1f;
                    float age = 0.82f + 0.18f * Fbm(u * 3f, v * 3f, sd);
                    Color c = paper * stripe * motif * age;
                    // Damp coming down from the top, worse at each stage.
                    float damp = Mathf.Clamp01((v - 0.55f) * 2f) * Fbm(u * 5f, v * 1.5f, sd + 9f) * stage * 0.45f;
                    c = Color.Lerp(c, new Color(0.16f, 0.13f, 0.09f), damp);
                    // Where it has come away.
                    float peel = Fbm(u * 2.6f, v * 2.1f, sd + 21f) + 0.12f * Fbm(u * 11f, v * 11f, sd + 5f);
                    if (peel > cut)
                    {
                        float deep = Mathf.Clamp01((peel - cut) * 9f);
                        float rust = Fbm(u * 9f, v * 9f, sd + 33f);
                        float streak = Fbm(u * 26f, v * 1.4f, sd + 41f);
                        Color under = Color.Lerp(new Color(0.07f, 0.06f, 0.055f), new Color(0.36f, 0.15f, 0.06f), rust * rust * 1.4f);
                        under = Color.Lerp(under, new Color(0.02f, 0.025f, 0.02f), Mathf.Clamp01(streak * 1.6f - 0.6f));     // mould running down
                        Color edge = Color.Lerp(paper * 1.25f, Color.white, 0.25f);                                           // the curl of torn paper
                        c = deep < 0.25f ? Color.Lerp(edge, under, deep * 4f) : under;
                    }
                    else if (stage > 0 && peel > cut - 0.035f) c *= 0.7f;                                                     // the shadow where it's lifting
                    px[y * N + x] = c;
                }
            t.SetPixels(px);
            t.Apply(true);
            cache[key] = t;
            return t;
        }
    }

    /// <summary>
    /// The pages found in the dream maze. They are this run's story, put together from what has actually happened in
    /// it: which truth the run drew, what the storyteller last did to you and what it has learned frightens you,
    /// what you do when you're frightened, what the thing looks like tonight, which night it is.
    /// </summary>
    public static class Lore
    {
        public static List<string> Pages(GameDirector g, int day, System.Random rng)
        {
            var pages = new List<string>();
            string fearKind = Safe(() => g.fear.TopFamily(), "the dark");
            string look = Safe(() => g.fear.DescribeLook(g.fear.Look()), "tall and thin").ToLowerInvariant();
            string first = look.Split(',')[0].Trim();
            string last = g.teller != null && g.teller.LastFired != null ? g.teller.LastFired.label : null;
            string refuge = Safe(() => g.teller.tactics.Most("hide in ", 0.3f), null);
            bool runs = Safe(() => g.teller.tactics.Level("run outside") > 0 ? "y" : null, null) != null;

            pages.Add("Night " + day + ".\n\nI have started counting the rooms. There are more of them than the house has. " +
                      "When I count again the number is different, and I don't think I am the one miscounting.");
            pages.Add("It was " + first + " tonight.\n\nIt wasn't, yesterday. I have stopped describing it to myself, because every time I get the description right, it changes to something I haven't written down.");
            if (!string.IsNullOrEmpty(last))
                pages.Add("Today: " + last.ToLowerInvariant() + ".\n\nI wrote that down as if it were a chore. Wind the clock. Water the ferns. " + char.ToUpperInvariant(last[0]) + last.Substring(1).ToLowerInvariant() + ".\n\nGrandma kept lists like this. I found one. Half the things on it were not chores.");
            pages.Add("It knows about " + fearKind.ToLowerInvariant() + ".\n\nI never told anyone about that. I was careful not to even look at it for long. That is how it knows: it watched what I was careful about.");
            if (!string.IsNullOrEmpty(refuge))
                pages.Add("When it gets bad I " + refuge.Trim() + ".\n\nIt has noticed. Something was moved in there today, only slightly, the way you'd leave a chair pulled out for a guest.");
            else if (runs)
                pages.Add("When it gets bad I go outside.\n\nIt lets me. I think it likes the look of me through the window, standing in the yard, deciding to come back in.");
            switch (g.truth)
            {
                case Truth.Killer:
                    pages.Add("The cellar floor is level. I was proud of that.\n\nThat is a strange thing to remember being proud of, and I can't remember why I was down there with a spade. I remember the weight of it. I remember being tired in a satisfied way.");
                    pages.Add("They ask in town whether anyone has heard from her. I say no, and it's true, and I say it too quickly.");
                    break;
                case Truth.Keepers:
                    pages.Add("Mother's handwriting, on the back of the calendar: 'It only wants looking after. We were chosen for it. You will be too.'\n\nThe date on it is the week before I was born.");
                    pages.Add("They didn't leave. I keep finding their coats. You don't leave without your coat, not in this country, not in that month.");
                    break;
                default:
                    pages.Add("The house has one more window from the outside than it has from the inside. I have counted from the yard four times.\n\nTonight I found the room it belongs to. This is that room. All of these are.");
                    pages.Add("It isn't haunted. Nothing died here. It is just a house that has been looked at for so long that it learned to look back.");
                    break;
            }
            pages.Add("If you're reading this you're asleep. Follow the ticking. That's the clock by the bed, the real one.\n\nDon't stay to read all of these. I did.");
            // This run's ghost story, in the dreamer's own words: a few pieces of it every night, never all of it.
            if (g.tale != null)
            {
                var told = g.tale.Pages();
                for (int i = 0; i < 3 && told.Count > 0; i++) { int k = rng.Next(told.Count); pages.Add(told[k]); told.RemoveAt(k); }
                if (g.tale.rules.Count > 0 && rng.Next(3) == 0) pages.Add(g.tale.RulesNote());
                // A long stay: the older story underneath starts to show through.
                if (g.day >= 3)
                    foreach (var o in g.older)
                    {
                        var deeper = o.Pages();
                        if (deeper.Count > 0) pages.Add(deeper[rng.Next(deeper.Count)]);
                    }
            }
            // Tonight's order is tonight's own.
            for (int i = pages.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = pages[i]; pages[i] = pages[j]; pages[j] = t; }
            return pages;
        }

        static string Safe(System.Func<string> f, string fallback)
        {
            try { var s = f(); return string.IsNullOrEmpty(s) ? fallback : s; }
            catch { return fallback; }
        }
    }
}
