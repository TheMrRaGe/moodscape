using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Dressing the dream's rooms so they read as rooms people lived in, and then left.
    ///
    /// A room has a purpose (parlour, dining room, bedroom, study, kitchen, storeroom, classroom, nursery, sickroom)
    /// and a state (kept under dust sheets; left as if someone just stepped out; turned over in a hurry; hoarded
    /// to the ceiling; or falling apart). Furniture is laid out the way furniture is: the big pieces against walls,
    /// in the groups they belong in (a bed with its head to the wall and a nightstand by it; a sofa facing a low
    /// table; a table with its chairs round it, one pushed back), never in the way of a doorway or a hiding place,
    /// never through each other. Then the layers that make it real: the small things on every surface that fit the
    /// room, the clutter that collects along walls and in corners (not in the middle, where people walk), the
    /// rubbish, and the damage: holes kicked in the plaster with the lath showing, chunks of it on the floor,
    /// wallpaper peeling away in strips, a ceiling board come down, stains. Now and then one of the small scenes
    /// a room tells on its own (a dinner set for four and never eaten; toys lined up, pointing at the cupboard; a
    /// chair in the corner facing the wall; a nest of blankets and empty tins). And the story's things: what
    /// belonged to whoever the house remembers turns up where it would have been kept.
    /// </summary>
    public partial class DreamMaze
    {
        static readonly string[] Purposes = { "parlour", "dining", "bedroom", "study", "storeroom", "classroom", "kitchen", "nursery", "sickroom" };
        static readonly string[] States = { "kept", "left", "hurried", "hoard", "derelict" };

        class Plan
        {
            public Cell c;
            public string purpose, state;
            public float decay;
            public readonly List<Rect> taken = new List<Rect>();
            public readonly List<Bounds> tops = new List<Bounds>();       // surfaces things can stand on (world)
            public readonly List<GameObject> big = new List<GameObject>();
            public int pieces, small, rubbish, damage;
        }

        public int dressedRooms, dressedThings;

        static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
        static Material M(Color c, float smooth = 0.1f)
        {
            Material m;
            if (!mats.TryGetValue(c, out m) || m == null) { m = Build.Mat(c, 0f, smooth); mats[c] = m; }
            return m;
        }

        float R(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }
        T One<T>(T[] a) { return a[rng.Next(a.Length)]; }

        // ---------------------------------------------------------------- choosing

        string PickPurpose(int theme)
        {
            // The story's mood leans the dream toward its own rooms: a sickroom, a nursery, a hoard.
            string mood = g.tale != null ? g.tale.Mood : "kept";
            int r = rng.Next(100);
            if (mood == "sickroom" && r < 22) return "sickroom";
            if (mood == "nursery" && r < 22) return "nursery";
            if (r < 6) return "sickroom";
            if (r < 12) return "nursery";
            if (r < 22) return "kitchen";
            return Purposes[rng.Next(Purposes.Length - 3)];      // (parlour .. classroom; kitchen, nursery and sickroom come above)
        }

        string PickState()
        {
            string mood = g.tale != null ? g.tale.Mood : "kept";
            // Later nights, more of it is falling down.
            int w0 = 18, w1 = 24, w2 = 20, w3 = 10 + (mood == "hoard" ? 25 : 0), w4 = 14 + day * 4;
            if (mood == "hurried") w2 += 20;
            int r = rng.Next(w0 + w1 + w2 + w3 + w4);
            if ((r -= w0) < 0) return "kept";
            if ((r -= w1) < 0) return "left";
            if ((r -= w2) < 0) return "hurried";
            if ((r -= w3) < 0) return "hoard";
            return "derelict";
        }

        // ---------------------------------------------------------------- the room

        void Dress(Cell c, int theme, string purpose = null, string state = null)
        {
            var p = new Plan { c = c, purpose = purpose ?? PickPurpose(theme), state = state ?? PickState() };
            p.decay = p.state == "derelict" ? R(0.65f, 1f) : p.state == "hoard" ? R(0.35f, 0.6f) : p.state == "hurried" ? R(0.3f, 0.55f) : p.state == "kept" ? R(0.15f, 0.3f) : R(0.05f, 0.25f);
            if (c.shape == 6) p.decay *= 0.5f;          // the eight-sided rooms stay bare and strange
            Anchors(p);
            Fill(p);
            RugUnder(p);
            Walls(p);
            Surfaces(p);
            Floor(p);
            Rubbish(p);
            Damage(p);
            Scene(p);
            StoryThings(p);
            if (p.state == "kept") Sheets(p);
            c.dressed = p.purpose + " (" + p.state + ")";
            dressedRooms++;
            dressedThings += p.pieces + p.small + p.rubbish + p.damage;
            if (dressedRooms <= 40)
            {
                float covered = 0f;
                foreach (var r in p.taken) covered += r.width * r.height;
                g.Log("dream maze: dressed a room as a " + p.purpose + " (" + p.state + ", decay " + p.decay.ToString("0.00") + "): " + p.pieces + " pieces (" + (covered / (Pitch * Pitch) * 100f).ToString("0") + "% of the floor), " + p.small + " small things, " + p.rubbish + " rubbish, " + p.damage + " damage");
            }
        }

        /// <summary>
        /// Tests: places to stand and look at the dressed rooms from (a corner by a doorway, eye height, looking across
        /// the room), with what each room is. The dark ones get a working light while this is used.
        /// </summary>
        public List<KeyValuePair<string, KeyValuePair<Vector3, Vector3>>> RoomViews(int max, bool light)
        {
            var l = new List<KeyValuePair<string, KeyValuePair<Vector3, Vector3>>>();
            foreach (var c in cells)
            {
                if (string.IsNullOrEmpty(c.dressed) || l.Count >= max) continue;
                Vector3 centre = World(Local(c.x, c.z));
                // Stand just inside a corner that isn't filled in, looking across at the opposite corner.
                Vector3 eye = Vector3.zero;
                bool found = false;
                for (int q = 0; q < 4 && !found; q++)
                {
                    Vector3 corner = Corner(q, 3.1f);
                    if (Blocked(c, corner)) continue;
                    eye = World(Local(c.x, c.z) + corner) + Vector3.up * 1.65f;
                    found = true;
                }
                if (!found) eye = World(Local(c.x, c.z) + new Vector3(0f, 0f, -3.2f)) + Vector3.up * 1.65f;
                if (light && !c.lit)
                {
                    var lamp = Build.Lamp("Test light", c.contents, new Vector3(0f, c.height - 0.4f, 0f), new Color(1f, 0.9f, 0.75f), 2.2f, 9f);
                }
                l.Add(new KeyValuePair<string, KeyValuePair<Vector3, Vector3>>(c.dressed, new KeyValuePair<Vector3, Vector3>(eye, centre + Vector3.up * 0.8f)));
            }
            return l;
        }

        /// <summary>A room that isn't furnished (empty, flooded, strange): still the rubbish and the damage of a place left alone.</summary>
        void DressLight(Cell c)
        {
            var p = new Plan { c = c, purpose = "empty", state = PickState() };
            if (p.state == "hoard") p.state = "derelict";
            p.decay = p.state == "derelict" ? R(0.6f, 1f) : R(0.1f, 0.45f);
            Rubbish(p);
            Damage(p);
            dressedThings += p.rubbish + p.damage;
        }

        // ---------------------------------------------------------------- where things can go

        // Doorways are in the middle of each wall; the way from each to the middle of the room is kept clear.
        static bool InLane(Vector2 min, Vector2 max, float half)
        {
            return (min.x < half && max.x > -half) || (min.y < half && max.y > -half);
        }

        bool Free(Plan p, Vector3 at, Vector2 halfSize, float yaw, bool lanes = true, float laneHalf = 0.95f)
        {
            bool sideways = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.7f;
            Vector2 h = sideways ? new Vector2(halfSize.y, halfSize.x) : halfSize;
            float diag = Mathf.Abs(Mathf.Sin(2f * yaw * Mathf.Deg2Rad));
            if (diag > 0.3f) h = Vector2.one * Mathf.Max(halfSize.x, halfSize.y);
            Vector2 min = new Vector2(at.x - h.x, at.z - h.y), max = new Vector2(at.x + h.x, at.z + h.y);
            const float inner = Pitch / 2f - 0.1f;
            if (min.x < -inner || min.y < -inner || max.x > inner || max.y > inner) return false;
            if (lanes && InLane(min, max, laneHalf)) return false;
            // Every corner of the footprint clear of filled-in corners and pillars.
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? min.x : max.x, 0f, (i & 2) == 0 ? min.y : max.y);
                if (Blocked(p.c, corner)) return false;
            }
            if (Blocked(p.c, at)) return false;
            if (c_nearHide(p.c, at, Mathf.Max(h.x, h.y))) return false;
            var r = new Rect(min, max - min);
            foreach (var t in p.taken) if (t.Overlaps(r)) return false;
            return true;
        }

        // Somewhere to hide, and the floor in front of it, stay clear.
        bool c_nearHide(Cell c, Vector3 at, float reach)
        {
            foreach (var hs in c.hides)
            {
                Vector3 spot = hs.root.localPosition, front = c.root.InverseTransformPoint(hs.outside);
                front.y = 0f;
                if (Vis.Level(spot - at).magnitude < 0.9f + reach || Vis.Level(front - at).magnitude < 0.7f + reach) return true;
            }
            return false;
        }

        void Take(Plan p, Vector3 at, Vector2 halfSize, float yaw, float margin = 0.08f)
        {
            bool sideways = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.7f;
            Vector2 h = sideways ? new Vector2(halfSize.y, halfSize.x) : halfSize;
            h += Vector2.one * margin;
            p.taken.Add(new Rect(at.x - h.x, at.z - h.y, h.x * 2f, h.y * 2f));
        }

        // A model's footprint (half width, half depth) at a given height.
        static Vector2 Half(string id, float height)
        {
            Vector3 s;
            if (!ModelSizes.All.TryGetValue(id, out s) || s.y < 1e-3f) return new Vector2(0.35f, 0.35f);
            float k = height / s.y;
            return new Vector2(s.x * k / 2f, s.z * k / 2f);
        }

        // Place a model if it fits; returns the placed object (and records its footprint and its top).
        GameObject Put(Plan p, string id, Vector3 at, float yaw, float height = 0f, bool lanes = true, bool solid = true, float tilt = 0f)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (height <= 0f) height = Mathf.Min(2.3f, ModelSizes.Height(id, 0.9f));
            Vector2 half = Half(id, height);
            if (half.x > 1.3f || half.y > 1.3f) return null;
            if (!Free(p, at, half, yaw, lanes)) return null;
            GameObject hit;
            var go = Models.Place(id, p.c.contents, at, height, yaw, out hit, solid && tilt == 0f, 2.6f);
            if (go == null) return null;
            if (tilt != 0f)
            {
                // Knocked over: on its side, where it fell.
                go.transform.localRotation = Quaternion.Euler(0f, yaw, tilt);
                go.transform.localPosition += Vector3.up * Mathf.Min(half.x, half.y) * 0.9f;
            }
            Take(p, at, half, yaw);
            if (hit != null)
            {
                var col = hit.GetComponent<Collider>();
                if (col != null) { float top = col.bounds.max.y - p.c.contents.position.y; if (top > 0.4f && top < 1.35f && tilt == 0f) p.tops.Add(col.bounds); }
            }
            p.big.Add(go);
            p.pieces++;
            return go;
        }

        GameObject PutRole(Plan p, string role, Vector3 at, float yaw, bool lanes = true)
        {
            for (int tries = 0; tries < 3; tries++)
            {
                var id = Catalogue.Any(role, rng);
                var go = Put(p, id, at, yaw, 0f, lanes);
                if (go != null) return go;
            }
            return null;
        }

        // A spot against wall `w` (0 +x, 1 -x, 2 +z, 3 -z), `along` it, for something `depth` deep: and the yaw to face the room.
        static Vector3 WallSpot(int w, float along, float depth, out float yaw)
        {
            float d = Pitch / 2f - 0.1f - depth / 2f - 0.02f;
            switch (w)
            {
                case 0: yaw = Models.Face(-1f, 0f); return new Vector3(d, 0f, along);
                case 1: yaw = Models.Face(1f, 0f); return new Vector3(-d, 0f, along);
                case 2: yaw = Models.Face(0f, -1f); return new Vector3(along, 0f, d);
                default: yaw = Models.Face(0f, 1f); return new Vector3(along, 0f, -d);
            }
        }

        static Vector3 Inward(int w) { return w == 0 ? Vector3.left : w == 1 ? Vector3.right : w == 2 ? Vector3.back : Vector3.forward; }
        static Vector3 AlongDir(int w) { return w < 2 ? Vector3.forward : Vector3.right; }

        // Against some free wall: try walls and positions until one fits.
        GameObject AgainstWall(Plan p, string id, float height = 0f, int wall = -1, float along = float.NaN, bool lanes = true)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (height <= 0f) height = Mathf.Min(2.3f, ModelSizes.Height(id, 0.9f));
            Vector2 half = Half(id, height);
            for (int tries = 0; tries < 10; tries++)
            {
                int w = wall >= 0 ? wall : rng.Next(4);
                if (SideMerged(p.c, w)) continue;
                float a = float.IsNaN(along) ? (rng.Next(2) == 0 ? -1f : 1f) * R(1.1f + half.x, 3.85f - half.x) : along;
                float yaw;
                Vector3 at = WallSpot(w, a, half.y * 2f, out yaw);
                var go = Put(p, id, at, yaw, height, lanes);
                if (go != null) return go;
            }
            return null;
        }

        GameObject AgainstWallRole(Plan p, string role, int wall = -1)
        {
            for (int tries = 0; tries < 3; tries++) { var go = AgainstWall(p, Catalogue.Any(role, rng), 0f, wall); if (go != null) return go; }
            return null;
        }

        // ---------------------------------------------------------------- 1. the big pieces, in their groups

        void Anchors(Plan p)
        {
            switch (p.purpose)
            {
                case "parlour":
                {
                    // A sofa against a wall, a low table in front of it, an armchair angled toward both, a lamp, a cabinet.
                    var sofa = AgainstWallRole(p, Catalogue.Sofa);
                    if (sofa != null)
                    {
                        Vector3 s = sofa.transform.localPosition, f = sofa.transform.localRotation * (Models.FrontIsPlusZ ? Vector3.forward : Vector3.back);
                        f = Vis.Level(f).normalized;
                        Put(p, Catalogue.Any(Catalogue.SmallTable, rng), s + f * 1.05f, sofa.transform.localEulerAngles.y);
                        Vector3 side = Vector3.Cross(Vector3.up, f);
                        Vector3 arm = s + f * 1.5f + side * (rng.Next(2) == 0 ? 1.4f : -1.4f);
                        Put(p, Catalogue.Any(Catalogue.Armchair, rng), arm, Models.Face((s + f * 1.05f - arm).x, (s + f * 1.05f - arm).z));
                    }
                    AgainstWallRole(p, Catalogue.Cabinet);
                    AgainstWall(p, Catalogue.Any(Catalogue.Plant, rng));
                    if (rng.Next(2) == 0) AgainstWallRole(p, Catalogue.Tall);
                    break;
                }
                case "dining":
                {
                    Vector3 mid = new Vector3(rng.Next(2) == 0 ? 2.2f : -2.2f, 0f, rng.Next(2) == 0 ? 2.2f : -2.2f);
                    var table = Put(p, One(new[] { "round_wooden_table_01", "round_wooden_table_02", "WoodenTable_03", "wooden_table_02", "painted_wooden_table" }), mid, rng.Next(2) * 90f, 0.76f);
                    if (table != null)
                    {
                        string chair = Catalogue.Any(Catalogue.Chair, rng);
                        for (int k = 0; k < 4; k++)
                        {
                            Vector3 off = k == 0 ? Vector3.right : k == 1 ? Vector3.left : k == 2 ? Vector3.forward : Vector3.back;
                            bool pushed = rng.Next(4) == 0, fallen = (p.state == "hurried" || p.state == "derelict") && rng.Next(3) == 0;
                            Vector3 at = mid + off * (0.82f + (pushed ? 0.45f : 0f));
                            Put(p, chair, at, Models.Face(-off.x, -off.z) + (pushed ? R(-25f, 25f) : 0f), 0.95f, true, true, fallen ? (rng.Next(2) == 0 ? 90f : -90f) : 0f);
                        }
                    }
                    AgainstWallRole(p, Catalogue.Cabinet);
                    AgainstWallRole(p, Catalogue.Tall);
                    break;
                }
                case "bedroom":
                case "sickroom":
                {
                    // The bed: head to a wall, a nightstand beside it.
                    string bed = p.purpose == "sickroom" ? One(new[] { "old_bed_frame", "vintage_day_bed" }) : One(new[] { "GothicBed_01", "old_bed_frame", "vintage_day_bed" });
                    var b = AgainstWall(p, bed, bed == "GothicBed_01" ? 1.5f : 1.0f);
                    if (b != null)
                    {
                        Vector3 bp = b.transform.localPosition;
                        Vector3 f = Vis.Level(b.transform.localRotation * (Models.FrontIsPlusZ ? Vector3.forward : Vector3.back)).normalized;
                        Vector3 side = Vector3.Cross(Vector3.up, f);
                        Vector2 hb = Half(bed, bed == "GothicBed_01" ? 1.5f : 1.0f);
                        Vector3 back = bp - f * (hb.y - 0.3f);
                        // A mattress (the models are frames): a pale slab on top, a pillow; stripped in a hurried room.
                        if (bed == "old_bed_frame")
                        {
                            var sheet = p.state == "hurried" || p.state == "derelict" ? new Color(0.45f, 0.4f, 0.33f) : new Color(0.82f, 0.8f, 0.74f);
                            var mat = Build.Box("Mattress", p.c.contents, bp + Vector3.up * 0.45f, Vector3.one, M(sheet), false);
                            mat.transform.localRotation = b.transform.localRotation;
                            mat.transform.localScale = new Vector3(hb.x * 1.7f, 0.2f, hb.y * 1.75f);
                        }
                        Put(p, One(new[] { "ClassicNightstand_01", "painted_wooden_nightstand" }), back + side * (hb.x + 0.35f), b.transform.localEulerAngles.y, 0.6f);
                        if (p.purpose == "sickroom")
                        {
                            Put(p, "wheelchair_01", bp + f * (hb.y + 0.8f) - side * 0.8f, Models.Face(side.x, side.z), 1.1f);
                            Put(p, Catalogue.Any(Catalogue.Chair, rng), back - side * (hb.x + 0.5f) + f * 0.4f, Models.Face(side.x, side.z), 0.95f);
                        }
                    }
                    AgainstWallRole(p, Catalogue.Tall);
                    AgainstWallRole(p, Catalogue.Cabinet);
                    if (rng.Next(2) == 0) AgainstWall(p, Catalogue.Any(Catalogue.Chair, rng));
                    break;
                }
                case "study":
                {
                    // A desk against a wall, its chair pulled out; shelves; a lamp.
                    string desk = One(new[] { "WoodenTable_01", "WoodenTable_02", "painted_wooden_table", "SchoolDesk_01" });
                    var d = AgainstWall(p, desk, 0.78f);
                    if (d != null)
                    {
                        Vector3 f = Vis.Level(d.transform.localRotation * (Models.FrontIsPlusZ ? Vector3.forward : Vector3.back)).normalized;
                        Put(p, Catalogue.Any(Catalogue.Chair, rng), d.transform.localPosition + f * R(0.7f, 1.0f), Models.Face(-f.x, -f.z) + R(-30f, 30f), 0.95f, true, true,
                            p.state == "hurried" && rng.Next(2) == 0 ? 90f : 0f);
                    }
                    AgainstWallRole(p, Catalogue.Tall);
                    AgainstWallRole(p, Catalogue.Tall);
                    AgainstWallRole(p, Catalogue.Cabinet);
                    break;
                }
                case "kitchen":
                {
                    // Cupboards and a worktop along one wall; a small table and two chairs; the stove.
                    int w = rng.Next(4);
                    if (!SideMerged(p.c, w))
                    {
                        for (int k = -1; k <= 1; k += 2)
                        {
                            float yaw;
                            Vector3 at = WallSpot(w, k * 2.55f, 0.62f, out yaw);
                            if (!Free(p, at, new Vector2(0.75f, 0.31f), yaw)) continue;
                            var unit = Build.Box("Kitchen units", p.c.contents, at + Vector3.up * 0.44f, Vector3.one, M(new Color(0.62f, 0.64f, 0.55f)), true);
                            unit.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                            unit.transform.localScale = new Vector3(1.5f, 0.88f, 0.62f);
                            var top = Build.Box("Worktop", p.c.contents, at + Vector3.up * 0.9f, Vector3.one, M(new Color(0.36f, 0.3f, 0.25f)), false);
                            top.transform.localRotation = unit.transform.localRotation;
                            top.transform.localScale = new Vector3(1.54f, 0.04f, 0.66f);
                            Take(p, at, new Vector2(0.75f, 0.31f), yaw);
                            p.tops.Add(top.GetComponent<Renderer>().bounds);
                            p.pieces++;
                        }
                    }
                    AgainstWall(p, "electric_stove", 0.85f);
                    var t = Put(p, One(new[] { "round_wooden_table_01", "small_wooden_table_01", "painted_wooden_table" }), new Vector3(R(1.8f, 2.6f) * (rng.Next(2) == 0 ? 1 : -1), 0f, R(1.8f, 2.6f) * (rng.Next(2) == 0 ? 1 : -1)), rng.Next(4) * 90f, 0.76f);
                    if (t != null)
                    {
                        Vector3 tp = t.transform.localPosition;
                        Put(p, Catalogue.Any(Catalogue.Chair, rng), tp + Vector3.right * 0.8f, Models.Face(-1f, 0f), 0.95f);
                        Put(p, Catalogue.Any(Catalogue.Chair, rng), tp + Vector3.forward * 0.8f, Models.Face(0f, -1f) + R(-20f, 20f), 0.95f, true, true, p.state != "kept" && rng.Next(3) == 0 ? 90f : 0f);
                    }
                    break;
                }
                case "storeroom":
                {
                    // Shelving along the walls, boxes and crates stacked under and beside it.
                    for (int i = 0; i < 3; i++) AgainstWall(p, One(new[] { "steel_frame_shelves_02", "wooden_display_shelves_01", "Shelf_01", "painted_wooden_shelves" }));
                    for (int i = 0; i < 4; i++) AgainstWall(p, One(new[] { "wooden_crate_01", "cardboard_box_01", "Barrel_01", "barrel_03", "metal_tool_chest" }));
                    AgainstWall(p, "wooden_ladder");
                    break;
                }
                case "classroom":
                {
                    for (int row = 0; row < 2; row++)
                        for (int col = -1; col <= 1; col += 2)
                        {
                            var at = new Vector3(col * 2.4f, 0f, -1.9f + row * 2.1f);
                            bool fallen = p.state != "kept" && p.state != "left" && rng.Next(4) == 0;
                            Put(p, "SchoolDesk_01", at, 0f, 0.8f, true, true, fallen ? 90f : 0f);
                            Put(p, "SchoolChair_01", at + new Vector3(0f, 0f, -0.7f), 0f, 0.9f);
                        }
                    Put(p, "standing_chalkboard_01", new Vector3(2.4f, 0f, 3.2f), 180f, 1.5f);
                    AgainstWallRole(p, Catalogue.Tall, 3);
                    break;
                }
                case "nursery":
                {
                    // A small bed, a chest of drawers, a little chair, a rocking chair where someone sat with them.
                    var b = AgainstWall(p, "vintage_day_bed", 0.8f);
                    AgainstWall(p, One(new[] { "vintage_wooden_drawer_01", "painted_wooden_cabinet", "drawer_cabinet" }));
                    AgainstWall(p, "Rockingchair_01", 1.0f);
                    AgainstWall(p, One(new[] { "painted_wooden_stool", "wooden_stool_01", "chinese_stool" }), 0.35f);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- 1a. filling the room out

        // These rooms are big (eight metres a side): one group of furniture in a corner leaves it bare. Real rooms fill
        // out along the walls: more of what belongs in that kind of room, against the walls, until about a fifth of the
        // floor is taken (a third in a hoard, less in a ruin).
        void Fill(Plan p)
        {
            float target = p.state == "hoard" ? 0.32f : p.state == "derelict" ? 0.14f : p.state == "hurried" ? 0.19f : 0.22f;
            if (p.c.shape == 1) target *= 0.8f; else if (p.c.shape == 2) target *= 0.6f;
            string[] roles;
            switch (p.purpose)
            {
                case "parlour": roles = new[] { Catalogue.Armchair, Catalogue.SmallTable, Catalogue.Cabinet, Catalogue.Tall, Catalogue.Plant, Catalogue.Stool, Catalogue.Sofa }; break;
                case "dining": roles = new[] { Catalogue.Cabinet, Catalogue.Chair, Catalogue.Chair, Catalogue.Tall, Catalogue.SmallTable, Catalogue.Plant }; break;
                case "bedroom": case "sickroom": roles = new[] { Catalogue.Cabinet, Catalogue.Tall, Catalogue.Chair, Catalogue.SmallTable, Catalogue.Armchair, Catalogue.Stool, Catalogue.Clutter }; break;
                case "study": roles = new[] { Catalogue.Tall, Catalogue.Tall, Catalogue.Cabinet, Catalogue.Armchair, Catalogue.SmallTable, Catalogue.Chair, Catalogue.Clutter }; break;
                case "kitchen": roles = new[] { Catalogue.Cabinet, Catalogue.Tall, Catalogue.Stool, Catalogue.Clutter, Catalogue.Chair }; break;
                case "nursery": roles = new[] { Catalogue.Cabinet, Catalogue.Stool, Catalogue.Chair, Catalogue.SmallTable, Catalogue.Clutter }; break;
                case "classroom": roles = new[] { Catalogue.Tall, Catalogue.Cabinet, Catalogue.Clutter, Catalogue.Chair }; break;
                default: roles = new[] { Catalogue.Tall, Catalogue.Clutter, Catalogue.Clutter, Catalogue.Cabinet, Catalogue.Odd }; break;
            }
            float area = Pitch * Pitch;
            for (int tries = 0; tries < 60; tries++)
            {
                float covered = 0f;
                foreach (var r in p.taken) covered += r.width * r.height;
                if (covered / area >= target) break;
                string role = roles[rng.Next(roles.Length)];
                GameObject go;
                if (rng.Next(100) < 35)
                {
                    // Free-standing, out in a quarter of the room (not every piece lives against a wall).
                    Vector3 at = new Vector3((rng.Next(2) == 0 ? -1f : 1f) * R(1.5f, 2.9f), 0f, (rng.Next(2) == 0 ? -1f : 1f) * R(1.5f, 2.9f));
                    go = Put(p, Catalogue.Any(role, rng), at, Models.Face(-at.x, -at.z) + R(-40f, 40f));
                }
                else go = AgainstWallRole(p, role);
                if (go != null && (p.state == "hurried" || p.state == "derelict") && rng.Next(5) == 0)
                    go.transform.localRotation *= Quaternion.Euler(0f, R(-25f, 25f), 0f);         // shoved askew
            }
        }

        // ---------------------------------------------------------------- 1b. a rug, and the walls

        // A rug under the room's main group (the sofa and its table, the dining table, the bed's foot).
        void RugUnder(Plan p)
        {
            if (p.purpose == "storeroom" || p.purpose == "classroom" || p.purpose == "kitchen" || p.big.Count == 0 || rng.Next(100) < 25) return;
            Bounds b = new Bounds();
            bool any = false;
            foreach (var go in p.big)
            {
                if (go == null) continue;
                Vector3 lp = p.c.contents.InverseTransformPoint(go.transform.position);
                if (!any) { b = new Bounds(lp, Vector3.zero); any = true; } else if (Vis.Level(lp - b.center).magnitude < 2.4f) b.Encapsulate(lp);
            }
            if (!any) return;
            float w = Mathf.Clamp(b.size.x + 1.4f, 1.6f, 3.2f), d = Mathf.Clamp(b.size.z + 1.4f, 1.4f, 2.6f);
            Vector3 at = new Vector3(b.center.x, 0f, b.center.z);
            if (Blocked(p.c, at)) return;
            var field = One(new[] { new Color(0.42f, 0.16f, 0.14f), new Color(0.2f, 0.24f, 0.34f), new Color(0.38f, 0.32f, 0.2f), new Color(0.25f, 0.3f, 0.22f) }) * (p.state == "derelict" ? 0.6f : 1f);
            Material m = Tex.Has("dirty_carpet") ? Tex.Real("dirty_carpet", w, d, 0.6f, field * 1.6f, null, 0.05f, 0.8f) : M(field);
            Build.Box("Rug", p.c.contents, at + Vector3.up * 0.008f, new Vector3(w, 0.012f, d), M(field * 0.55f), false);
            Build.Box("Rug", p.c.contents, at + Vector3.up * 0.011f, new Vector3(w - 0.2f, 0.012f, d - 0.2f), m, false);
            // A rug in a falling-apart room is rucked up at one corner.
            if (p.state == "derelict" || p.state == "hurried")
                Build.Box("Rucked rug", p.c.contents, at + new Vector3(w * 0.4f, 0.06f, d * 0.35f), new Vector3(0.5f, 0.012f, 0.4f), m, false).transform.localRotation = Quaternion.Euler(25f, 20f, 0f);
            p.small++;
        }

        // What hangs on the walls: pictures, a clock, a mirror gone dark, a boarded-up window; and in a dark room
        // that was left in a hurry, a lamp somebody left on.
        void Walls(Plan p)
        {
            int n = p.state == "hoard" ? 1 : 1 + rng.Next(3);
            string[] hangs = { "hanging_picture_frame_01", "hanging_picture_frame_02", "hanging_picture_frame_03", "fancy_picture_frame_01", "fancy_picture_frame_02", "wall_clock", "ornate_mirror_01", "dartboard", "bull_head" };
            for (int i = 0; i < n; i++)
            {
                Vector3 at, inward; int w;
                if (!WallPoint(p, 0.5f, out at, out inward, out w)) continue;
                string id = p.purpose == "nursery" ? "hanging_picture_frame_0" + (1 + rng.Next(3)) : p.purpose == "classroom" ? "wall_clock" : One(hangs);
                Vector3 s;
                if (!ModelSizes.All.TryGetValue(id, out s)) continue;
                float h = Mathf.Min(s.y, 0.9f);
                GameObject hit;
                var go = Models.Place(id, p.c.contents, at + inward * 0.02f + Vector3.up * (1.45f - h * 0.5f), h, Models.Face(inward.x, inward.z), out hit, false, 0.9f);
                if (go == null) continue;
                // Some of them hang crooked.
                if (p.state != "kept" && rng.Next(3) == 0) go.transform.localRotation *= Quaternion.Euler(0f, 0f, R(-12f, 12f));
                p.small++;
            }
            // A window boarded up from inside: the dark behind the gaps between the planks.
            if (p.state == "derelict" || (p.state == "hurried" && rng.Next(3) == 0))
            {
                Vector3 at, inward; int w;
                if (WallPoint(p, 0.7f, out at, out inward, out w))
                {
                    Build.Box("Window (dark)", p.c.contents, at + inward * 0.006f + Vector3.up * 1.5f, Rotated(w, 1.0f, 1.2f, 0.01f), M(new Color(0.02f, 0.025f, 0.03f), 0.6f), false);
                    for (int k = 0; k < 4; k++)
                    {
                        var plank = Build.Box("Plank", p.c.contents, at + inward * 0.035f + Vector3.up * (1.05f + k * 0.3f), Rotated(w, 1.25f, 0.17f, 0.025f), M(new Color(0.38f, 0.3f, 0.2f)), false);
                        plank.transform.localRotation = Quaternion.AngleAxis(R(-7f, 7f), inward);
                    }
                    p.damage++;
                }
            }
            // A lamp left on, low and warm, in a dark room someone left.
            if (!p.c.lit && (p.state == "left" || p.state == "hurried") && p.tops.Count > 0 && rng.Next(2) == 0)
            {
                var b = p.tops[rng.Next(p.tops.Count)];
                Vector3 at = p.c.contents.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
                GameObject hit;
                if (Models.Place(One(new[] { "vintage_oil_lamp", "desk_lamp_arm_01", "Lantern_01" }), p.c.contents, at, 0.4f, R(0f, 360f), out hit, false, 0.35f) != null)
                    Build.Lamp("A lamp left on", p.c.contents, at + Vector3.up * 0.35f, new Color(1f, 0.72f, 0.42f), 1.1f, 4.5f);
            }
        }

        // ---------------------------------------------------------------- 2. what's on every surface

        string[] SurfaceThings(string purpose)
        {
            switch (purpose)
            {
                case "dining": case "kitchen": return new[] { "wooden_bowl_01", "wooden_bowl_02", "pot_enamel_01", "brass_pot_01", "jug_01", "metal_jug", "tea_set_01", "can_rusted", "CheeseBox_01", "wine_bottles_01", "brass_goblets" };
                case "study": case "classroom": return new[] { "office_notepads", "binder_notebook", "decorative_book_set_01", "book_encyclopedia_set_01", "vintage_stapler", "magnifying_glass_01", "desk_lamp_arm_01", "vintage_radio_transceiver", "cassette_player" };
                case "bedroom": return new[] { "alarm_clock_01", "round_spectacles", "standing_picture_frame_01", "standing_picture_frame_02", "vintage_oil_lamp", "ceramic_vase_01", "pocket_watch", "decorative_book_set_01" };
                case "sickroom": return new[] { "bleach_bottle", "multi_cleaner_bottle", "alarm_clock_01", "round_spectacles", "jug_01", "vintage_oil_lamp" };
                case "nursery": return new[] { "rubber_duck_toy", "Ukulele_01", "standing_picture_frame_01", "carved_wooden_elephant", "horse_statue_01" };
                case "storeroom": return new[] { "cleaner_tin_01", "all_purpose_cleaner", "plastic_bottle_gallon", "can_rusted", "russian_food_cans_01", "seeding_tray_01", "metal_jug" };
                default: return new[] { "mantel_clock_01", "ceramic_vase_02", "brass_vase_01", "brass_candleholders", "wooden_candlestick", "carved_wooden_elephant", "chess_set", "standing_picture_frame_02", "decorative_book_set_01" };
            }
        }

        void Surfaces(Plan p)
        {
            var pool = SurfaceThings(p.purpose);
            foreach (var b in p.tops)
            {
                int n = p.state == "kept" ? rng.Next(2) : p.state == "hoard" ? 2 + rng.Next(3) : rng.Next(4);
                for (int i = 0; i < n; i++)
                {
                    string id = One(pool);
                    Vector3 s;
                    if (!ModelSizes.All.TryGetValue(id, out s)) continue;
                    if (s.x > b.size.x * 0.7f || s.z > b.size.z * 0.7f || s.y > 0.6f) continue;
                    Vector3 at = new Vector3(b.center.x + R(-0.5f, 0.5f) * (b.size.x - s.x - 0.06f), b.max.y + 0.004f, b.center.z + R(-0.5f, 0.5f) * (b.size.z - s.z - 0.06f));
                    if (Physics.CheckBox(at + Vector3.up * (s.y / 2f + 0.01f), s * 0.45f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    GameObject hit;
                    if (Models.Place(id, p.c.contents, p.c.contents.InverseTransformPoint(at), s.y, R(0f, 360f), out hit, false, Mathf.Max(s.x, s.z) * 1.2f) != null) p.small++;
                }
                // Papers on desks and tables: always some, more when it's been gone through.
                int papers = p.purpose == "study" || p.purpose == "classroom" ? 2 + rng.Next(3) : p.state == "hurried" ? rng.Next(3) : rng.Next(2);
                for (int i = 0; i < papers; i++)
                {
                    Vector3 at = new Vector3(b.center.x + R(-0.4f, 0.4f) * b.size.x, b.max.y + 0.003f + i * 0.002f, b.center.z + R(-0.4f, 0.4f) * b.size.z);
                    Sheet(p.c.contents, p.c.contents.InverseTransformPoint(at), R(0f, 360f));
                    p.small++;
                }
            }
        }

        // ---------------------------------------------------------------- 3. what collects on the floor

        string[] FloorThings(Plan p)
        {
            var l = new List<string> { "cardboard_box_01", "wicker_basket_01", "vintage_suitcase", "plastic_bottle_gallon", "rubber_boots", "wooden_crate_01" };
            switch (p.purpose)
            {
                case "kitchen": case "dining": l.AddRange(new[] { "metal_trash_can", "wooden_bucket_01", "plastic_bottle_gallon", "bleach_bottle" }); break;
                case "storeroom": l.AddRange(new[] { "Barrel_01", "wooden_crate_01", "propane_tank", "old_tyre", "metal_tool_chest", "watering_can_metal_01" }); break;
                case "bedroom": case "sickroom": l.AddRange(new[] { "vintage_suitcase", "wicker_basket_02", "rubber_boots" }); break;
                case "nursery": l.AddRange(new[] { "rubber_duck_toy", "wicker_basket_02" }); break;
                case "study": l.AddRange(new[] { "cardboard_box_01", "metal_trash_can", "book_encyclopedia_set_01" }); break;
            }
            if (p.state == "hoard" || p.state == "derelict") l.AddRange(new[] { "trashbag", "trashbag", "russian_food_cans_01", "can_rusted", "cardboard_box_01" });
            return l.ToArray();
        }

        void Floor(Plan p)
        {
            int n = p.state == "kept" ? 1 + rng.Next(2) : p.state == "left" ? 2 + rng.Next(3) : p.state == "hurried" ? 4 + rng.Next(5) : p.state == "hoard" ? 12 + rng.Next(10) : 4 + rng.Next(6);
            var pool = FloorThings(p);
            float lane = p.state == "hoard" ? 0.7f : 0.95f;
            for (int i = 0; i < n * 3 && n > 0; i++)
            {
                string id = One(pool);
                float h = Mathf.Min(1.2f, ModelSizes.Height(id, 0.4f));
                Vector2 half = Half(id, h);
                // Along a wall, or in a corner: where things end up.
                int w = rng.Next(4);
                if (SideMerged(p.c, w)) continue;
                float along = (rng.Next(2) == 0 ? -1f : 1f) * R(lane + half.x, 3.85f - half.x);
                float yaw;
                Vector3 at = WallSpot(w, along, half.y * 2f + R(0f, 0.25f), out yaw);
                yaw += R(-25f, 25f);
                if (!Free(p, at, half, yaw, true, lane)) continue;
                GameObject hit;
                if (Models.Place(id, p.c.contents, at, h, yaw, out hit, half.x > 0.25f, 1.4f) == null) continue;
                Take(p, at, half, yaw, 0.02f);
                p.small++;
                // Hoards stack: a box on a box.
                if (p.state == "hoard" && id == "cardboard_box_01" && rng.Next(2) == 0)
                    Models.Place(id, p.c.contents, at + Vector3.up * h, h * R(0.8f, 1f), yaw + R(-15f, 15f), out hit, false, 1.4f);
                if (--n <= 0) break;
            }
        }

        // ---------------------------------------------------------------- 4. rubbish

        void Rubbish(Plan p)
        {
            int n = Mathf.RoundToInt(p.decay * 22f) + (p.state == "hoard" ? 14 : 0) + (p.state == "kept" ? -3 : 0);
            var paper = new Color(0.82f, 0.8f, 0.72f);
            for (int i = 0; i < n; i++)
            {
                // Mostly near the walls and under things; derelict rooms have it everywhere.
                Vector3 at;
                if (p.state == "derelict" && rng.Next(3) == 0) at = new Vector3(R(-3.6f, 3.6f), 0f, R(-3.6f, 3.6f));
                else
                {
                    int w = rng.Next(4);
                    float yawUnused;
                    at = WallSpot(w, R(-3.6f, 3.6f), R(0.1f, 1.4f), out yawUnused);
                }
                if (Blocked(p.c, at)) continue;
                int kind = rng.Next(10);
                Transform t = p.c.contents;
                switch (kind)
                {
                    case 0: case 1: case 2:        // crumpled paper
                        var ball = Build.Prim(PrimitiveType.Sphere, "Crumpled paper", t, at + Vector3.up * 0.035f, new Vector3(R(0.06f, 0.1f), R(0.05f, 0.08f), R(0.06f, 0.1f)), M(paper), false);
                        ball.transform.localRotation = Random.rotation;
                        break;
                    case 3: case 4:                // a sheet, a newspaper
                        Sheet(t, at + Vector3.up * 0.003f, R(0f, 360f), rng.Next(2) == 0);
                        break;
                    case 5: case 6:                // a can, standing or rolled
                        Can(t, at, rng.Next(3) > 0);
                        break;
                    case 7:                        // a bottle
                        Bottle(t, at, rng.Next(2) == 0);
                        break;
                    case 8:                        // a scrap of cardboard
                        var card = Build.Box("Cardboard", t, at + Vector3.up * 0.004f, new Vector3(R(0.2f, 0.45f), 0.006f, R(0.15f, 0.35f)), M(new Color(0.55f, 0.42f, 0.28f)), false);
                        card.transform.localRotation = Quaternion.Euler(R(-4f, 4f), R(0f, 360f), R(-4f, 4f));
                        break;
                    default:                       // a dark stain on the boards
                        var stain = Build.Prim(PrimitiveType.Cylinder, "Stain", t, at + Vector3.up * 0.002f, new Vector3(R(0.3f, 0.9f), 0.001f, R(0.3f, 0.7f)), M(new Color(0.07f, 0.05f, 0.04f), 0.5f), false);
                        stain.transform.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                        break;
                }
                p.rubbish++;
            }
        }

        void Sheet(Transform t, Vector3 at, float yaw, bool newspaper = false)
        {
            var c = newspaper ? new Color(0.62f, 0.6f, 0.55f) : new Color(0.88f, 0.86f, 0.78f);
            var s = Build.Box(newspaper ? "Newspaper" : "Paper", t, at, newspaper ? new Vector3(0.42f, 0.003f, 0.58f) : new Vector3(0.21f, 0.002f, 0.29f), M(c), false);
            s.transform.localRotation = Quaternion.Euler(R(-2f, 2f), yaw, R(-2f, 2f));
        }

        void Can(Transform t, Vector3 at, bool lying)
        {
            var c = One(new[] { new Color(0.45f, 0.3f, 0.22f), new Color(0.5f, 0.12f, 0.1f), new Color(0.6f, 0.6f, 0.58f) });
            var can = Build.Prim(PrimitiveType.Cylinder, "Can", t, at + Vector3.up * (lying ? 0.035f : 0.06f), new Vector3(0.067f, 0.06f, 0.067f), M(c, 0.4f), false);
            can.transform.localRotation = lying ? Quaternion.Euler(90f, R(0f, 360f), 0f) : Quaternion.Euler(0f, R(0f, 360f), 0f);
        }

        void Bottle(Transform t, Vector3 at, bool lying)
        {
            var glass = M(One(new[] { new Color(0.12f, 0.25f, 0.12f), new Color(0.3f, 0.18f, 0.06f), new Color(0.5f, 0.55f, 0.5f) }), 0.85f);
            var b = new GameObject("Bottle").transform;
            b.SetParent(t, false);
            b.localPosition = at + Vector3.up * (lying ? 0.04f : 0f);
            b.localRotation = lying ? Quaternion.Euler(90f, R(0f, 360f), 0f) : Quaternion.Euler(0f, R(0f, 360f), 0f);
            Build.Prim(PrimitiveType.Cylinder, "Body", b, new Vector3(0f, 0.1f, 0f), new Vector3(0.075f, 0.1f, 0.075f), glass, false);
            Build.Prim(PrimitiveType.Cylinder, "Neck", b, new Vector3(0f, 0.25f, 0f), new Vector3(0.028f, 0.055f, 0.028f), glass, false);
        }

        // ---------------------------------------------------------------- 5. damage

        void Damage(Plan p)
        {
            if (p.decay < 0.3f) return;
            // Wallpaper peeling away in strips.
            int strips = Mathf.RoundToInt((p.decay - 0.25f) * 10f);
            for (int i = 0; i < strips; i++) Peel(p);
            // Holes in the plaster, the lath showing, the plaster that came out of them on the floor below.
            if (p.decay > 0.5f) { Hole(p); if (p.decay > 0.8f && rng.Next(2) == 0) Hole(p); }
            // A board come down from the ceiling, hanging by one end; what fell with it.
            if (p.decay > 0.7f && rng.Next(100) < 45 && p.c.ceiling != null && p.c.ceiling.activeSelf) Sag(p);
            // Dust and grit: a pale skin over the floor in drifts by the walls.
            int drifts = Mathf.RoundToInt(p.decay * 4f);
            for (int i = 0; i < drifts; i++)
            {
                float yaw;
                Vector3 at = WallSpot(rng.Next(4), R(-3.2f, 3.2f), R(0.3f, 0.8f), out yaw);
                if (Blocked(p.c, at)) continue;
                var d = Build.Box("Dust", p.c.contents, at + Vector3.up * 0.003f, new Vector3(R(0.6f, 1.6f), 0.004f, R(0.3f, 0.7f)), M(new Color(0.42f, 0.4f, 0.36f)), false);
                d.transform.localRotation = Quaternion.Euler(0f, yaw + R(-10f, 10f), 0f);
                p.damage++;
            }
        }

        // A point on a wall (not in a doorway, not a filled-in corner, not behind a hiding place), the direction into the room.
        bool WallPoint(Plan p, float margin, out Vector3 at, out Vector3 inward, out int wall)
        {
            for (int tries = 0; tries < 12; tries++)
            {
                wall = rng.Next(4);
                if (SideMerged(p.c, wall)) continue;
                float along = (rng.Next(2) == 0 ? -1f : 1f) * R(1.45f + margin, 3.7f - margin);
                float yaw;
                at = WallSpot(wall, along, 0f, out yaw);
                inward = Inward(wall);
                Vector3 probe = at + inward * 0.5f;
                if (Blocked(p.c, probe) || c_nearHide(p.c, probe, margin)) continue;
                return true;
            }
            at = Vector3.zero; inward = Vector3.zero; wall = 0;
            return false;
        }

        void Peel(Plan p)
        {
            Vector3 at, inward; int w;
            if (!WallPoint(p, 0.3f, out at, out inward, out w)) return;
            float top = R(1.4f, 2.6f), len = R(0.5f, 1.3f), width = R(0.18f, 0.4f);
            // Hinged at the top, curling out from the wall.
            var pivot = new GameObject("Peeling paper").transform;
            pivot.SetParent(p.c.contents, false);
            pivot.localPosition = at + inward * 0.012f + Vector3.up * top;
            pivot.localRotation = Quaternion.LookRotation(inward) * Quaternion.Euler(-R(8f, 35f), 0f, R(-8f, 8f));
            // The paper itself (the room's own wallpaper, a little darker: its back is to the light), and its pale underside.
            Build.Box("Strip", pivot, new Vector3(0f, -len / 2f, 0.004f), new Vector3(width, len, 0.003f), WallMat(p.c.theme, rot), false);
            Build.Box("Strip back", pivot, new Vector3(0f, -len / 2f, 0.008f), new Vector3(width * 0.98f, len * 0.98f, 0.002f), M(new Color(0.5f, 0.46f, 0.38f)), false);
            // The bare plaster where it came away.
            Build.Box("Bare plaster", p.c.contents, at + inward * 0.006f + Vector3.up * (top - len / 2f), Rotated(w, width * 1.05f, len, 0.004f), M(new Color(0.66f, 0.62f, 0.55f)), false);
            p.damage++;
        }

        static Vector3 Rotated(int w, float along, float up, float thick) { return w < 2 ? new Vector3(thick, up, along) : new Vector3(along, up, thick); }

        void Hole(Plan p)
        {
            Vector3 at, inward; int w;
            if (!WallPoint(p, 0.6f, out at, out inward, out w)) return;
            float wid = R(0.45f, 1.0f), hei = R(0.4f, 0.85f), y = R(0.35f, 1.5f);
            Vector3 centre = at + inward * 0.008f + Vector3.up * (y + hei / 2f);
            Vector3 along = AlongDir(w);
            // The dark behind.
            Build.Box("Hole", p.c.contents, centre, Rotated(w, wid, hei, 0.01f), M(new Color(0.015f, 0.012f, 0.01f)), false);
            // Lath: thin strips of wood across it, a little way back, some snapped.
            int laths = Mathf.RoundToInt(hei / 0.09f);
            for (int i = 0; i < laths; i++)
            {
                if (rng.Next(4) == 0) continue;
                float ly = y + 0.05f + i * 0.09f, lw = wid * R(0.5f, 1.05f);
                var lath = Build.Box("Lath", p.c.contents, at + inward * 0.02f + Vector3.up * ly + along * R(-0.1f, 0.1f), Rotated(w, lw, 0.035f, 0.012f), M(new Color(0.42f, 0.3f, 0.18f)), false);
                lath.transform.localRotation = Quaternion.AngleAxis(R(-4f, 4f), inward);
            }
            // Ragged edges: plaster flakes round the rim.
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Vector3 rim = centre + along * Mathf.Cos(a) * wid * 0.5f + Vector3.up * Mathf.Sin(a) * hei * 0.5f + inward * 0.006f;
                var flake = Build.Box("Plaster edge", p.c.contents, rim, Rotated(w, R(0.08f, 0.18f), R(0.05f, 0.12f), 0.02f), M(new Color(0.75f, 0.72f, 0.66f)), false);
                flake.transform.localRotation = Quaternion.AngleAxis(R(0f, 360f), inward);
            }
            // What came out of it, on the floor below.
            for (int i = 0; i < 6 + rng.Next(8); i++) Chunk(p.c.contents, at + inward * R(0.1f, 0.7f) + along * R(-wid * 0.7f, wid * 0.7f));
            p.damage++;
        }

        void Chunk(Transform t, Vector3 at)
        {
            var c = Build.Box("Plaster", t, at + Vector3.up * 0.02f, new Vector3(R(0.03f, 0.16f), R(0.015f, 0.05f), R(0.03f, 0.12f)), M(new Color(0.78f, 0.75f, 0.69f)), false);
            c.transform.localRotation = Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f));
        }

        void Sag(Plan p)
        {
            float h = p.c.height;
            Vector3 at = new Vector3(R(-2.5f, 2.5f), 0f, R(-2.5f, 2.5f));
            if (Blocked(p.c, at)) return;
            Vector3 dir = rng.Next(2) == 0 ? Vector3.right : Vector3.forward;
            // A dark gap in the ceiling where it was; the board hanging from one end, its far end down near your head.
            Build.Box("Gap in the ceiling", p.c.contents, at + Vector3.up * (h - 0.004f), dir == Vector3.right ? new Vector3(1.6f, 0.006f, 0.32f) : new Vector3(0.32f, 0.006f, 1.6f), M(new Color(0.01f, 0.01f, 0.01f)), false);
            var pivot = new GameObject("Hanging board").transform;
            pivot.SetParent(p.c.contents, false);
            pivot.localPosition = at - dir * 0.8f + Vector3.up * (h - 0.02f);
            pivot.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(R(40f, 62f), 0f, R(-6f, 6f));
            Build.Box("Board", pivot, new Vector3(0f, 0f, 0.8f), new Vector3(0.3f, 0.025f, 1.6f), M(new Color(0.55f, 0.5f, 0.42f)), false);
            for (int i = 0; i < 10; i++) Chunk(p.c.contents, at + new Vector3(R(-0.7f, 0.7f), 0f, R(-0.7f, 0.7f)));
            p.damage++;
        }

        // ---------------------------------------------------------------- 6. little scenes

        void Scene(Plan p)
        {
            if (rng.Next(100) > 30) return;
            var t = p.c.contents;
            switch (p.purpose)
            {
                case "dining":
                    // Dinner, set for four and never eaten. The plates have gone grey.
                    foreach (var b in p.tops)
                    {
                        if (b.size.x < 0.8f || b.size.z < 0.8f) continue;
                        for (int k = 0; k < 4; k++)
                        {
                            Vector3 off = (k == 0 ? Vector3.right : k == 1 ? Vector3.left : k == 2 ? Vector3.forward : Vector3.back) * Mathf.Min(b.size.x, b.size.z) * 0.32f;
                            Vector3 at = t.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z)) + off;
                            Build.Prim(PrimitiveType.Cylinder, "Plate", t, at + Vector3.up * 0.008f, new Vector3(0.24f, 0.006f, 0.24f), M(new Color(0.75f, 0.74f, 0.7f), 0.5f), false);
                            Build.Prim(PrimitiveType.Cylinder, "Food", t, at + Vector3.up * 0.018f, new Vector3(0.12f, 0.008f, 0.1f), M(new Color(0.18f, 0.2f, 0.12f)), false);
                        }
                        g.Log("dream maze: a scene: dinner set for four, never eaten");
                        break;
                    }
                    break;
                case "nursery":
                {
                    // Toy blocks in a perfectly straight line across the floor, pointing at somewhere to hide.
                    Vector3 from = new Vector3(R(-1.5f, 1.5f), 0f, R(-1.5f, 1.5f)), to = p.c.hides.Count > 0 ? p.c.root.InverseTransformPoint(p.c.hides[0].outside) : new Vector3(3f, 0f, 3f);
                    Color[] toy = { new Color(0.6f, 0.1f, 0.1f), new Color(0.12f, 0.25f, 0.55f), new Color(0.7f, 0.6f, 0.15f), new Color(0.15f, 0.45f, 0.2f) };
                    for (int i = 0; i < 14; i++)
                    {
                        Vector3 at = Vector3.Lerp(from, to, i / 14f);
                        if (Blocked(p.c, at)) continue;
                        Build.Box("Toy block", t, at + Vector3.up * 0.03f, Vector3.one * 0.06f, M(toy[i % toy.Length]), false).transform.localRotation = Quaternion.LookRotation(to - from);
                    }
                    g.Log("dream maze: a scene: toy blocks lined up across the floor, pointing at a hiding place");
                    break;
                }
                case "bedroom":
                case "sickroom":
                {
                    // A chair in the corner, facing the wall, as if someone was made to sit there.
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 cpos = Corner(i, 3.2f);
                        if (Blocked(p.c, cpos)) continue;
                        if (Put(p, Catalogue.Any(Catalogue.Chair, rng), cpos, Models.Face(cpos.x, cpos.z), 0.95f) != null) { g.Log("dream maze: a scene: a chair in the corner, facing the wall"); break; }
                    }
                    break;
                }
                default:
                {
                    // A nest: blankets heaped in a corner, empty tins round it, a candle burned down.
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 cpos = Corner(i, 3.0f);
                        if (Blocked(p.c, cpos) || !Free(p, cpos, new Vector2(0.6f, 0.6f), 0f)) continue;
                        for (int k = 0; k < 4; k++)
                        {
                            var bl = Build.Box("Blanket", t, cpos + new Vector3(R(-0.3f, 0.3f), 0.06f + k * 0.05f, R(-0.3f, 0.3f)), new Vector3(R(0.7f, 1.1f), 0.08f, R(0.5f, 0.9f)), M(One(new[] { new Color(0.3f, 0.26f, 0.22f), new Color(0.28f, 0.1f, 0.1f), new Color(0.2f, 0.24f, 0.3f) })), false);
                            bl.transform.localRotation = Quaternion.Euler(R(-6f, 6f), R(0f, 360f), R(-6f, 6f));
                        }
                        for (int k = 0; k < 6; k++) Can(t, cpos + new Vector3(R(-0.9f, 0.9f), 0f, R(-0.9f, 0.9f)), rng.Next(2) == 0);
                        Take(p, cpos, new Vector2(0.6f, 0.6f), 0f);
                        g.Log("dream maze: a scene: a nest of blankets and empty tins in the corner");
                        break;
                    }
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- 7. the story's things

        void StoryThings(Plan p)
        {
            if (g.tale == null || rng.Next(100) > 30) return;
            var props = g.tale.Props();
            if (props.Count == 0) return;
            string id = props[rng.Next(props.Count)];
            Vector3 s;
            if (!ModelSizes.All.TryGetValue(id, out s)) return;
            // Small things on a surface, if there is one; big ones against a wall.
            if (s.y < 0.5f && p.tops.Count > 0)
            {
                var b = p.tops[rng.Next(p.tops.Count)];
                GameObject hit;
                if (Models.Place(id, p.c.contents, p.c.contents.InverseTransformPoint(new Vector3(b.center.x, b.max.y + 0.004f, b.center.z)), s.y, R(0f, 360f), out hit, false, Mathf.Max(s.x, s.z) * 1.2f) != null)
                    g.Log("dream maze: " + g.tale.Var("rel", "their") + "'s things: a " + id.Replace('_', ' ') + ", in the middle of a table");
            }
            else if (AgainstWall(p, id) != null) g.Log("dream maze: " + g.tale.Var("rel", "their") + "'s things: a " + id.Replace('_', ' ') + " against the wall");
        }

        // ---------------------------------------------------------------- dust sheets

        // A kept room: the furniture under sheets, the way you leave a house you mean to come back to.
        void Sheets(Plan p)
        {
            var cloth = M(new Color(0.78f, 0.76f, 0.7f));
            foreach (var go in p.big)
            {
                if (go == null || rng.Next(100) > 60) continue;
                Bounds b = new Bounds(); bool any = false;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                if (!any || b.size.y > 2.2f) continue;
                Vector3 c = p.c.contents.InverseTransformPoint(b.center);
                var sheet = Build.Box("Dust sheet", p.c.contents, c + Vector3.up * 0.02f, b.size + new Vector3(0.06f, 0.04f, 0.06f), cloth, false);
                // A sheet drapes: lower at the corners than over the middle (a second, narrower layer on top).
                Build.Box("Dust sheet", p.c.contents, c + Vector3.up * (b.size.y / 2f + 0.03f), new Vector3(b.size.x * 0.7f, 0.04f, b.size.z * 0.7f), cloth, false);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.enabled = false;
                p.damage++;
            }
        }
    }
}
