using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Everything outside the walls: the house seen from outside (siding, roof, chimney, porch), the yard your
    /// grandparents kept, the dirt drive that disappears into the trees, and the woods around the clearing.
    /// The conifers are generated (Poly Haven's are millions of polygons each); everything at human scale uses
    /// the downloaded models and photo textures, with plain fallbacks so it always builds.
    /// </summary>
    public class Outdoors : MonoBehaviour
    {
        public static Outdoors I;
        public const float Ground = -0.45f;
        public static readonly Vector2 ClearingCentre = new Vector2(0f, 2f);

        public Interactable mailbox;
        public Vector3 driveEnd;

        Light sun, porchLight;
        Material porchBulb;
        readonly List<Material> outsideGlass = new List<Material>();
        readonly List<AudioSource> dayAmb = new List<AudioSource>(), nightAmb = new List<AudioSource>();
        readonly List<AudioLowPassFilter> muffles = new List<AudioLowPassFilter>();
        AudioSource chimes;
        readonly List<Transform> sheets = new List<Transform>();
        readonly List<Vector2> drive = new List<Vector2>();
        float[] edge;               // clearing radius by angle
        int time, day = 1;
        float outsideBlend;
        Color indoorFog, indoorAmbient;
        float indoorDensity;
        float lastWarn;
        House house;

        static readonly Color Trim = new Color(0.88f, 0.86f, 0.8f), ShutterGreen = new Color(0.16f, 0.24f, 0.18f);

        void Awake() { I = this; }

        // ---------------------------------------------------------------- small helpers

        GameObject Box(string name, Vector3 pos, Vector3 size, Material m, bool solid = true)
        {
            return Build.Box(name, transform, pos, size, m, solid);
        }

        GameObject Model(string id, Vector3 at, float yaw, bool solid = true, float scale = 1f)
        {
            GameObject hit;
            float h = ModelSizes.Height(id, 1f) * scale;
            var g = Models.Place(id, transform, at, h, yaw, out hit, solid);
            return solid ? hit : g;
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat, bool collide)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collide) g.AddComponent<MeshCollider>().sharedMesh = mesh;
            return g;
        }

        float EdgeRadius(float angle)
        {
            float a = Mathf.Repeat(angle, Mathf.PI * 2f) / (Mathf.PI * 2f) * edge.Length;
            int i = (int)a;
            return Mathf.Lerp(edge[i % edge.Length], edge[(i + 1) % edge.Length], a - i);
        }

        bool InClearing(Vector2 p, float margin)
        {
            Vector2 d = p - ClearingCentre;
            return d.magnitude < EdgeRadius(Mathf.Atan2(d.y, d.x)) + margin;
        }

        float DistToDrive(Vector2 p)
        {
            float best = 999f;
            for (int i = 0; i + 1 < drive.Count; i++)
            {
                Vector2 a = drive[i], b = drive[i + 1], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }
            return best;
        }

        // ---------------------------------------------------------------- the build

        public void BuildAll(House h)
        {
            house = h;
            var rng = new System.Random(1974);
            edge = new float[48];
            for (int i = 0; i < edge.Length; i++) edge[i] = 21f + 4f * Mathf.PerlinNoise(i * 0.37f, 7.1f) - 1.5f * Mathf.PerlinNoise(i * 1.3f, 2f);
            drive.AddRange(new[] { new Vector2(0f, 11.2f), new Vector2(0.2f, 17f), new Vector2(1.8f, 24f), new Vector2(5.5f, 31f),
                                    new Vector2(11f, 38f), new Vector2(17f, 46f), new Vector2(21f, 56f), new Vector2(23f, 70f) });
            driveEnd = new Vector3(drive[3].x, Ground, drive[3].y);

            BuildGround();
            BuildHouseShell();
            BuildPorch();
            BuildYard(rng);
            BuildWoods(rng);
            BuildEdgeOfWoods(rng);
            BuildGrass(rng);
            BuildLight();
            BuildAmbience();
            SetTime(0, 1);
        }

        void BuildGround()
        {
            var floor = Tex.Real("forest_leaves_02", 1f, 1f, 1f, new Color(0.7f, 0.68f, 0.62f), Tex.Concrete, 0.05f);
            floor.mainTextureScale = new Vector2(260f / 3f, 260f / 3f);
            if (floor.HasProperty("_BumpMap")) floor.SetTextureScale("_BumpMap", floor.mainTextureScale);
            Box("Forest floor", new Vector3(0f, Ground - 0.1f, 0f), new Vector3(260f, 0.2f, 260f), floor);

            // The clearing: a ragged disc of grass, a little above the leaf litter.
            var grass = Tex.Real("leafy_grass", 1f, 1f, 2.5f, new Color(0.78f, 0.8f, 0.7f), Tex.Plaster, 0.05f);
            var v = new List<Vector3> { new Vector3(ClearingCentre.x, Ground + 0.01f, ClearingCentre.y) };
            var uv = new List<Vector2> { ClearingCentre / 2.5f };
            var tri = new List<int>();
            for (int i = 0; i <= edge.Length; i++)
            {
                float a = i * Mathf.PI * 2f / edge.Length, r = edge[i % edge.Length];
                var p = ClearingCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                v.Add(new Vector3(p.x, Ground + 0.01f, p.y));
                uv.Add(p / 2.5f);
                if (i > 0) { tri.Add(0); tri.Add(i + 1); tri.Add(i); }
            }
            var m = new Mesh { name = "Clearing" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            MeshObject("Clearing", transform, m, grass, false);

            // The drive: two dirt ruts' worth of track, from the porch steps into the trees.
            var dirt = Tex.Real("stony_dirt_path", 1f, 1f, 3f, new Color(0.8f, 0.76f, 0.7f), Tex.Concrete, 0.05f);
            var dv = new List<Vector3>(); var duv = new List<Vector2>(); var dt = new List<int>();
            float run = 0f;
            for (int i = 0; i < drive.Count; i++)
            {
                Vector2 dir = (i + 1 < drive.Count ? drive[i + 1] - drive[i] : drive[i] - drive[i - 1]).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x) * 1.6f;
                if (i > 0) run += (drive[i] - drive[i - 1]).magnitude;
                dv.Add(new Vector3(drive[i].x - side.x, Ground + 0.025f, drive[i].y - side.y));
                dv.Add(new Vector3(drive[i].x + side.x, Ground + 0.025f, drive[i].y + side.y));
                duv.Add(new Vector2(0f, run / 3f)); duv.Add(new Vector2(1.07f, run / 3f));
                if (i > 0)
                {
                    int b = i * 2;
                    dt.AddRange(new[] { b - 2, b - 1, b, b - 1, b + 1, b });
                }
            }
            var dm = new Mesh { name = "Drive" };
            dm.SetVertices(dv); dm.SetUVs(0, duv); dm.SetTriangles(dt, 0); dm.RecalculateNormals(); dm.RecalculateBounds();
            MeshObject("Drive", transform, dm, dirt, false);
        }

        // ---------------------------------------------------------------- the house from outside

        Material Siding(float w, float h)
        {
            return Tex.Real("weathered_plank_siding", w, h, 2.4f, new Color(0.92f, 0.9f, 0.84f), Tex.Boards, 0.1f);
        }

        void BuildHouseShell()
        {
            const float top = 3.0f, wx = 7.6f, wz = 8.1f, th = 0.06f;
            // Foundation: worn concrete showing between the siding and the ground.
            Box("Foundation", new Vector3(0f, -0.27f, 0f), new Vector3(2f * wx + 0.24f, 0.5f, 2f * wz + 0.24f),
                Tex.Real("concrete_floor_worn_001", 2f * wx, 0.5f, 2f, new Color(0.75f, 0.74f, 0.7f), Tex.Concrete));

            // Siding on all four walls (the front with a gap for the door).
            float sh = top + 0.05f;
            Box("Siding W", new Vector3(-wx - th / 2f, sh / 2f - 0.05f, 0f), new Vector3(th, sh, 2f * wz + 2f * th), Siding(2f * wz, sh));
            Box("Siding E", new Vector3(wx + th / 2f, sh / 2f - 0.05f, 0f), new Vector3(th, sh, 2f * wz + 2f * th), Siding(2f * wz, sh));
            Box("Siding S", new Vector3(0f, sh / 2f - 0.05f, -wz - th / 2f), new Vector3(2f * wx, sh, th), Siding(2f * wx, sh));
            float gapL = -0.66f, gapR = 0.66f, doorTop = 2.13f;
            Box("Siding N", new Vector3((-wx + gapL) / 2f, sh / 2f - 0.05f, wz + th / 2f), new Vector3(gapL + wx, sh, th), Siding(wx, sh));
            Box("Siding N", new Vector3((wx + gapR) / 2f, sh / 2f - 0.05f, wz + th / 2f), new Vector3(wx - gapR, sh, th), Siding(wx, sh));
            Box("Siding N", new Vector3(0f, (doorTop + top) / 2f, wz + th / 2f), new Vector3(gapR - gapL, top - doorTop, th), Siding(1.3f, 0.9f));
            var trim = Build.Mat(Trim, 0f, 0.2f);
            foreach (var cx in new[] { -wx - th, wx + th })
                foreach (var cz in new[] { -wz - th, wz + th })
                    Box("Corner board", new Vector3(cx, sh / 2f - 0.05f, cz), new Vector3(0.14f, sh, 0.14f), trim, false);
            Box("Door trim", new Vector3(-0.72f, 1.06f, wz + 0.08f), new Vector3(0.12f, 2.15f, 0.04f), trim, false);
            Box("Door trim", new Vector3(0.72f, 1.06f, wz + 0.08f), new Vector3(0.12f, 2.15f, 0.04f), trim, false);
            Box("Door trim", new Vector3(0f, 2.2f, wz + 0.08f), new Vector3(1.56f, 0.14f, 0.04f), trim, false);

            // Windows from outside, exactly where the windows are inside (they move with the rooms). They glow warm
            // when the lamps are on inside.
            foreach (var w in house.windowSpots)
            {
                Vector3 inward = w.Value, p = w.Key;
                if (Mathf.Abs(inward.x) > 0.5f) p.x = -Mathf.Sign(inward.x) * (wx + th);
                else p.z = -Mathf.Sign(inward.z) * (wz + th);
                OutsideWindow(p, -inward);
            }

            // Gable roof, ridge running front to back, with the gable ends facing the drive and the back yard.
            const float pitch = 32f, over = 0.5f;
            float slope = Mathf.Tan(pitch * Mathf.Deg2Rad);
            float half = wx + over, eave = top - over * slope, rise = half * slope;
            float planeW = half / Mathf.Cos(pitch * Mathf.Deg2Rad) + 0.08f;
            var roof = Tex.Real("grey_roof_01", planeW, 2f * wz + 1.2f, 2.5f, new Color(0.62f, 0.6f, 0.58f), Tex.Concrete, 0.1f);
            for (int s = -1; s <= 1; s += 2)
            {
                var plane = Box("Roof", new Vector3(s * half / 2f, eave + rise / 2f + 0.06f, 0f), new Vector3(planeW, 0.12f, 2f * wz + 1.2f), roof);
                plane.transform.localRotation = Quaternion.Euler(0f, 0f, -s * pitch);
                var fascia = Box("Fascia", new Vector3(s * (half + 0.02f), eave - 0.05f, 0f), new Vector3(0.05f, 0.22f, 2f * wz + 1.2f), trim, false);
            }
            Box("Ridge", new Vector3(0f, eave + rise + 0.12f, 0f), new Vector3(0.28f, 0.1f, 2f * wz + 1.25f), Build.Mat(new Color(0.25f, 0.25f, 0.26f)), false);
            for (int s = -1; s <= 1; s += 2) Gable(s * (wz + th / 2f), wx + 0.02f, top, top + wx * slope, s);

            // The chimney: brick, up the outside wall behind the living-room fireplace (wherever the living room is).
            var brick = Tex.Real("dark_brick_wall", 0.9f, 5.4f, 1.6f, new Color(0.85f, 0.78f, 0.74f), Tex.Concrete, 0.1f);
            float chimX = Mathf.Sign(house.chimney.x == 0f ? 1f : house.chimney.x), chimZ = house.chimney.z == 0f ? -4f : house.chimney.z;
            Box("Chimney", new Vector3(chimX * (wx + 0.47f), (Ground + 5.0f) / 2f, chimZ), new Vector3(0.82f, 5.0f - Ground, 1.25f), brick);
            Box("Chimney cap", new Vector3(chimX * (wx + 0.47f), 5.05f, chimZ), new Vector3(0.95f, 0.1f, 1.38f), Build.Mat(new Color(0.35f, 0.34f, 0.33f)), false);
        }

        void OutsideWindow(Vector3 pos, Vector3 normal)
        {
            bool alongZ = Mathf.Abs(normal.x) > 0.5f;
            Vector3 along = alongZ ? Vector3.forward : Vector3.right;
            var glass = Build.Mat(new Color(0.12f, 0.14f, 0.16f), 0.01f, 0.92f);
            outsideGlass.Add(glass);
            var trim = Build.Mat(Trim, 0f, 0.2f);
            var shutter = Build.Mat(ShutterGreen, 0f, 0.15f);
            Vector3 thin(float a, float h, float d) { return alongZ ? new Vector3(d, h, a) : new Vector3(a, h, d); }
            Box("Window glass", pos + normal * 0.03f, thin(1.0f, 1.15f, 0.02f), glass, false);
            Box("Window frame", pos + normal * 0.05f + Vector3.up * 0.62f, thin(1.2f, 0.1f, 0.05f), trim, false);
            Box("Window frame", pos + normal * 0.05f - Vector3.up * 0.62f, thin(1.3f, 0.08f, 0.12f), trim, false);
            Box("Window frame", pos + normal * 0.05f + along * 0.56f, thin(0.08f, 1.3f, 0.05f), trim, false);
            Box("Window frame", pos + normal * 0.05f - along * 0.56f, thin(0.08f, 1.3f, 0.05f), trim, false);
            Box("Window bar", pos + normal * 0.05f, thin(1.0f, 0.04f, 0.03f), trim, false);
            Box("Window bar", pos + normal * 0.05f, thin(0.04f, 1.15f, 0.03f), trim, false);
            Box("Shutter", pos + normal * 0.05f + along * 0.92f, thin(0.55f, 1.3f, 0.04f), shutter, false);
            Box("Shutter", pos + normal * 0.05f - along * 0.92f, thin(0.55f, 1.3f, 0.04f), shutter, false);
        }

        // A triangular gable end with siding, and a louvred attic vent.
        void Gable(float z, float halfW, float y0, float y1, int facing)
        {
            // Each side gets its own three corners, so the two faces light separately.
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            for (int side = 0; side < 2; side++)
            {
                float zz = z + (side == 0 ? 0.01f : -0.01f) * facing;
                v.Add(new Vector3(-halfW, y0, zz)); v.Add(new Vector3(halfW, y0, zz)); v.Add(new Vector3(0f, y1, zz));
                uv.Add(new Vector2(-halfW / 2.4f, y0 / 2.4f)); uv.Add(new Vector2(halfW / 2.4f, y0 / 2.4f)); uv.Add(new Vector2(0f, y1 / 2.4f));
            }
            var t = new List<int> { 0, 2, 1, 3, 4, 5 };
            var m = new Mesh { name = "Gable" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            var mat = Tex.Real("weathered_plank_siding", 1f, 1f, 1f, new Color(0.92f, 0.9f, 0.84f), Tex.Boards, 0.1f);
            mat.mainTextureScale = Vector2.one;
            MeshObject("Gable", transform, m, mat, false);
            Box("Attic vent", new Vector3(0f, (y0 + y1) / 2f + 0.3f, z + facing * 0.05f), new Vector3(0.6f, 0.8f, 0.04f), Build.Mat(new Color(0.1f, 0.1f, 0.1f)), false);
            for (int i = 0; i < 6; i++)
                Box("Louvre", new Vector3(0f, (y0 + y1) / 2f + 0.0f + i * 0.12f, z + facing * 0.08f), new Vector3(0.6f, 0.03f, 0.05f), Build.Mat(Trim), false);
        }

        // ---------------------------------------------------------------- the porch

        void BuildPorch()
        {
            const float z0 = 8.16f, z1 = 10.7f, x = 3.2f, deck = -0.03f;
            var boards = Tex.Real("wood_floor_deck", 2f * x, z1 - z0, 2f, new Color(0.8f, 0.75f, 0.7f), Tex.Boards, 0.15f);
            Box("Porch deck", new Vector3(0f, deck - 0.1f, (z0 + z1) / 2f), new Vector3(2f * x, 0.2f, z1 - z0), boards);
            Box("Porch skirt", new Vector3(0f, (Ground + deck - 0.2f) / 2f, z1 - 0.05f), new Vector3(2f * x, deck - 0.2f - Ground + 0.1f, 0.05f), Build.Mat(new Color(0.25f, 0.2f, 0.16f)), false);
            var trim = Build.Mat(Trim, 0f, 0.2f);
            float postH = 2.65f;
            foreach (var px in new[] { -x + 0.08f, x - 0.08f })
            {
                Box("Porch post", new Vector3(px, deck + postH / 2f, z1 - 0.12f), new Vector3(0.14f, postH, 0.14f), trim);
                Box("Porch post", new Vector3(px, deck + postH / 2f, z0 + 0.12f), new Vector3(0.14f, postH, 0.14f), trim, false);
            }
            var proof = Box("Porch roof", new Vector3(0f, deck + postH + 0.15f, (z0 + z1) / 2f + 0.15f), new Vector3(2f * x + 0.4f, 0.1f, z1 - z0 + 0.5f),
                Tex.Real("grey_roof_01", 2f * x, z1 - z0, 2.5f, new Color(0.62f, 0.6f, 0.58f), Tex.Concrete, 0.1f), false);
            proof.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);
            // Rails along the front (leaving the steps open) and the sides.
            void Rail(Vector3 a, Vector3 b)
            {
                Vector3 mid = (a + b) / 2f, d = b - a;
                bool alongX = Mathf.Abs(d.x) > Mathf.Abs(d.z);
                Box("Rail", mid + Vector3.up * 0.9f, alongX ? new Vector3(d.magnitude, 0.06f, 0.08f) : new Vector3(0.08f, 0.06f, d.magnitude), trim, true);
                Box("Rail", mid + Vector3.up * 0.12f, alongX ? new Vector3(d.magnitude, 0.05f, 0.06f) : new Vector3(0.06f, 0.05f, d.magnitude), trim, false);
                int n = Mathf.FloorToInt(d.magnitude / 0.16f);
                for (int i = 1; i < n; i++)
                    Box("Baluster", a + d * (i / (float)n) + Vector3.up * 0.5f, new Vector3(0.04f, 0.78f, 0.04f), trim, false);
            }
            float ry = deck;
            Rail(new Vector3(-x + 0.08f, ry, z1 - 0.12f), new Vector3(-0.85f, ry, z1 - 0.12f));
            Rail(new Vector3(0.85f, ry, z1 - 0.12f), new Vector3(x - 0.08f, ry, z1 - 0.12f));
            Rail(new Vector3(-x + 0.08f, ry, z0 + 0.12f), new Vector3(-x + 0.08f, ry, z1 - 0.12f));
            Rail(new Vector3(x - 0.08f, ry, z0 + 0.12f), new Vector3(x - 0.08f, ry, z1 - 0.12f));
            // Three steps down to the yard.
            var step = Tex.Real("wood_floor_deck", 1.7f, 0.3f, 2f, new Color(0.75f, 0.7f, 0.65f), Tex.Boards, 0.15f);
            for (int i = 0; i < 3; i++)
            {
                float topY = deck - 0.14f * (i + 1);
                Box("Porch step", new Vector3(0f, (topY + Ground - 0.1f) / 2f, z1 + 0.15f + i * 0.3f), new Vector3(1.7f, topY - Ground + 0.1f, 0.3f), step);
            }
            // The porch light, by the door.
            porchBulb = Build.Mat(new Color(1f, 0.85f, 0.6f), 0.1f);
            Box("Porch lamp", new Vector3(0.98f, 2.0f, z0 + 0.08f), new Vector3(0.16f, 0.24f, 0.12f), porchBulb, false);
            porchLight = Build.Lamp("Porch light", transform, new Vector3(0.98f, 1.95f, z0 + 0.45f), new Color(1f, 0.82f, 0.55f), 0f, 9f, true);
            // What sits on the porch.
            Model("Rockingchair_01", new Vector3(-2.15f, deck, 9.55f), Models.Face(0.3f, 1f));
            Model("painted_wooden_bench", new Vector3(2.15f, deck, 8.62f), Models.Face(0f, 1f));
            Model("watering_can_metal_01", new Vector3(2.85f, deck, 10.2f), 30f, false);
            // Wind chimes in the corner.
            var chimeRoot = new Vector3(2.75f, 2.35f, 10.35f);
            Box("Chime string", chimeRoot + Vector3.up * 0.1f, new Vector3(0.01f, 0.2f, 0.01f), Build.Mat(new Color(0.3f, 0.3f, 0.3f)), false);
            var brass = Build.Mat(new Color(0.7f, 0.62f, 0.4f), 0f, 0.85f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Build.Prim(PrimitiveType.Cylinder, "Chime", transform, chimeRoot + new Vector3(Mathf.Cos(a) * 0.06f, -0.25f - 0.04f * i, Mathf.Sin(a) * 0.06f),
                    new Vector3(0.018f, 0.2f + 0.035f * i, 0.018f), brass, false);
            }
            chimes = Sfx.Loop("chimes", null, 0.35f, true);
            chimes.transform.position = chimeRoot;
            chimes.minDistance = 2f; chimes.maxDistance = 22f;
        }

        // ---------------------------------------------------------------- the yard

        void BuildYard(System.Random rng)
        {
            float G = Ground;
            // The mailbox, where the clearing meets the drive.
            var post = Box("Mailbox post", new Vector3(3.0f, G + 0.6f, 24.6f), new Vector3(0.1f, 1.2f, 0.1f), Tex.Real("pine_bark", 0.4f, 1.2f, 1f, new Color(0.7f, 0.65f, 0.6f), Tex.Boards));
            var boxGo = Box("Mailbox", new Vector3(3.0f, G + 1.28f, 24.6f), new Vector3(0.26f, 0.24f, 0.48f), Build.Mat(new Color(0.22f, 0.24f, 0.26f), 0f, 0.5f));
            Box("Mailbox flag", new Vector3(3.15f, G + 1.38f, 24.5f), new Vector3(0.02f, 0.2f, 0.06f), Build.Mat(new Color(0.6f, 0.12f, 0.1f)), false);
            mailbox = Build.Usable(boxGo, "Check the mailbox");
            house.choreTargets["mail"] = mailbox;

            // The car under its cover. Nobody has driven it in a long time.
            Model("covered_car", new Vector3(1.3f, G, 17.2f), Models.Face(-0.1f, -1f));

            // Firewood: a stacked pile against the east wall, the chopping block, the axe.
            var bark = Tex.Real("pine_bark", 0.7f, 0.9f, 1f, new Color(0.75f, 0.68f, 0.6f), Tex.Boards);
            for (int row = 0; row < 5; row++)
                for (int i = 0; i < 12; i++)
                {
                    float r = 0.09f + 0.03f * (float)rng.NextDouble();
                    var log = Build.Prim(PrimitiveType.Cylinder, "Log", transform,
                        new Vector3(9.1f + 0.04f * (float)rng.NextDouble(), G + r + row * 0.2f, -1.2f + i * 0.24f + (row % 2) * 0.12f),
                        new Vector3(r * 2f, 0.45f, r * 2f), bark, row == 0);
                    log.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            Box("Woodpile", new Vector3(9.1f, G + 0.5f, 0.2f), new Vector3(0.95f, 1.0f, 3.0f), bark, true).GetComponent<Renderer>().enabled = false;
            var stump = Model("tree_stump_01", new Vector3(11.4f, G, 0.6f), 40f, true, 0.55f);
            var stumpCol = stump != null ? stump.GetComponentInChildren<Collider>() : null;
            float stumpTop = stumpCol != null ? stumpCol.bounds.max.y : G + 0.35f;
            Model("wooden_axe", new Vector3(11.35f, stumpTop, 0.55f), 70f, false);
            Model("hatchet", new Vector3(10.4f, G, -1.7f), 15f, false);

            // Clothesline on the west side, sheets still pegged out.
            var pole = Build.Mat(new Color(0.42f, 0.4f, 0.36f), 0f, 0.3f);
            foreach (var z in new[] { -3.5f, 5.5f })
            {
                Box("Line post", new Vector3(-12.5f, G + 1.05f, z), new Vector3(0.08f, 2.1f, 0.08f), pole);
                Box("Line bar", new Vector3(-12.5f, G + 2.0f, z), new Vector3(1.1f, 0.06f, 0.06f), pole, false);
            }
            foreach (var lx in new[] { -12.95f, -12.05f })
                Box("Line", new Vector3(lx, G + 2.0f, 1f), new Vector3(0.01f, 0.01f, 9f), Build.Mat(new Color(0.85f, 0.85f, 0.82f)), false);
            var linen = Build.Mat(new Color(0.88f, 0.87f, 0.83f), 0f, 0.05f);
            foreach (var sz in new[] { -1.4f, 1.0f, 3.4f })
            {
                var pivot = new GameObject("Sheet").transform;
                pivot.SetParent(transform, false);
                pivot.localPosition = new Vector3(sz > 0f ? -12.95f : -12.05f, G + 2.0f, sz);
                Build.Box("Sheet cloth", pivot, new Vector3(0f, -0.75f, 0f), new Vector3(0.02f, 1.5f, 1.8f), linen, false);
                sheets.Add(pivot);
            }

            // The picnic table and the fire pit out back.
            Model("wooden_picnic_table", new Vector3(-6.5f, G, -15.5f), 12f);
            Model("stone_fire_pit", new Vector3(-11.5f, G, -12.5f), 0f);
            Model("painted_wooden_bench", new Vector3(-13.6f, G, -11.8f), Models.Face(1f, -0.3f));

            // Grandma's garden beds along the south wall, gone to seed; the gnome she was fond of.
            for (int i = 0; i < 3; i++) Model("planter_box_01", new Vector3(-0.2f + i * 1.15f, G, -10.4f), 0f);
            Model("garden_gnome", new Vector3(3.5f, G, -10.1f), Models.Face(0.2f, 1f));
            Model("garden_gloves_01", new Vector3(0.95f, G + 0.42f, -10.4f), 30f, false);
            Model("rusted_spade_01", new Vector3(-1.2f, G, -10.9f), 80f, false);
            Model("wooden_bucket_01", new Vector3(-1.4f, G, -10.1f), 0f);

            // Along the walls: the rain barrel, the bins, the gas, the ladder.
            Model("wine_barrel_01", new Vector3(8.15f, G, 7.55f), 0f);
            // (the bins and the ladder step aside when the chimney is on their wall)
            bool chimneyBackLeft = house.chimney.x < 0f && house.chimney.z < 0f, chimneyFrontRight = house.chimney.x > 0f && house.chimney.z > 0f;
            Model("metal_trash_can", new Vector3(-8.35f, G, chimneyBackLeft ? -2.4f : -4.8f), 0f);
            Model("trashbag", new Vector3(-8.4f, G, chimneyBackLeft ? -1.65f : -5.6f), 50f, false);
            Model("propane_tank", new Vector3(-8.45f, G, 1.3f), 0f);
            Model("wooden_ladder", new Vector3(8.6f, G, chimneyFrontRight ? 5.95f : 4.8f), 90f);
            Model("wooden_crate_01", new Vector3(-8.5f, G, -7.2f), 20f);

            // The tool shed: closed and padlocked.
            var siding = Siding(3f, 2.2f);
            var shed = new Vector3(10.2f, G, -14f);
            Box("Shed", shed + new Vector3(0f, 1.1f, 0f), new Vector3(3f, 2.2f, 2.6f), siding);
            var sroof = Box("Shed roof", shed + new Vector3(0f, 2.35f, 0f), new Vector3(3.4f, 0.08f, 3.0f),
                Tex.Real("grey_roof_01", 3.4f, 3f, 2.5f, new Color(0.55f, 0.53f, 0.5f), Tex.Concrete), false);
            sroof.transform.localRotation = Quaternion.Euler(0f, 0f, 10f);
            Box("Shed door", shed + new Vector3(-1.52f, 0.95f, 0f), new Vector3(0.04f, 1.9f, 0.9f), Tex.Real("rough_pine_door", 1f, 1f, 1f, new Color(0.55f, 0.5f, 0.45f)), false);
            Box("Padlock", shed + new Vector3(-1.56f, 1.0f, 0.36f), new Vector3(0.04f, 0.08f, 0.06f), Build.Mat(new Color(0.55f, 0.45f, 0.25f), 0f, 0.7f), false);
            Model("old_tyre", new Vector3(8.45f, G, -12.5f), 90f);

            // Dry branches and leaves blown into the yard.
            for (int i = 0; i < 10; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 12f + (float)rng.NextDouble() * 7f;
                var p = ClearingCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (DistToDrive(p) < 2.5f) continue;
                Model("dry_branches_medium_01", new Vector3(p.x, G, p.y), (float)rng.NextDouble() * 360f, false, 0.8f);
            }
        }

        // ---------------------------------------------------------------- grass

        static Texture2D blades;

        // A tuft of grass: blades drawn tapering up from the bottom edge, alpha-cut. Dark and brown at the root,
        // dull green to straw at the tips. Generated, so it costs nothing to ship.
        static Texture2D Blades()
        {
            if (blades != null) return blades;
            const int n = 128;
            blades = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.2f, 0.22f, 0.1f, 0f);
            var r = new System.Random(5);
            for (int b = 0; b < 34; b++)
            {
                float x0 = 0.12f + 0.76f * (float)r.NextDouble(), h = 0.45f + 0.55f * (float)r.NextDouble();
                float lean = ((float)r.NextDouble() - 0.5f) * 0.5f, w0 = 0.018f + 0.02f * (float)r.NextDouble();
                float dry = (float)r.NextDouble();
                for (int y = 0; y < n; y++)
                {
                    float v = y / (float)n;
                    if (v > h) break;
                    float t = v / h;
                    float cx = x0 + lean * t * t, w = w0 * (1f - t) + 0.002f;
                    for (int x = 0; x < n; x++)
                    {
                        float u = x / (float)n;
                        if (Mathf.Abs(u - cx) > w) continue;
                        Color root = new Color(0.08f, 0.07f, 0.04f), mid = Color.Lerp(new Color(0.2f, 0.27f, 0.12f), new Color(0.36f, 0.33f, 0.18f), dry);
                        Color tip = Color.Lerp(new Color(0.42f, 0.45f, 0.24f), new Color(0.58f, 0.52f, 0.32f), dry);
                        Color c = t < 0.4f ? Color.Lerp(root, mid, t / 0.4f) : Color.Lerp(mid, tip, (t - 0.4f) / 0.6f);
                        c.a = 1f;
                        px[y * n + x] = c;
                    }
                }
            }
            blades.SetPixels(px);
            blades.Apply(true);
            return blades;
        }

        static Material GrassMat()
        {
            var m = Build.Mat(Color.white, 0f, 0.05f);
            m.mainTexture = Blades();
            m.SetFloat("_Mode", 1f);
            m.SetFloat("_Cutoff", 0.45f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = 2450;
            return m;
        }

        bool GrassFree(Vector2 p)
        {
            if (Mathf.Abs(p.x) < 8.1f && Mathf.Abs(p.y) < 8.6f) return false;                        // the house
            if (Mathf.Abs(p.x) < 3.6f && p.y > 8f && p.y < 12.2f) return false;                        // the porch and steps
            if (DistToDrive(p) < 1.9f) return false;                                                   // the drive (grass grows along it)
            if (Mathf.Abs(p.x - 10.2f) < 1.8f && Mathf.Abs(p.y + 14f) < 1.6f) return false;            // the shed
            return true;
        }

        // Thousands of tufts, thickest at the edges of the yard and along the drive, sparse where people walked.
        void BuildGrass(System.Random rng)
        {
            var mat = GrassMat();
            var chunks = new Dictionary<int, (List<Vector3> v, List<Vector2> uv, List<int> t)>();
            int count = 0;
            for (int i = 0; i < 26000 && count < 9000; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rad = Mathf.Sqrt((float)rng.NextDouble()) * 30f;
                var p = ClearingCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                if (!GrassFree(p)) continue;
                float edge = EdgeRadius(Mathf.Atan2(p.y - ClearingCentre.y, p.x - ClearingCentre.x));
                float fromHouse = Mathf.Max(Mathf.Abs(p.x) - 8f, Mathf.Abs(p.y) - 8.5f);
                // Worn near the house, long by the trees and on the verge of the drive; it gives out under the canopy.
                float keep = Mathf.Clamp01(0.25f + fromHouse * 0.06f) + (DistToDrive(p) < 3.2f ? 0.4f : 0f);
                if (rad > edge + 2f) keep *= Mathf.Clamp01(1f - (rad - edge - 2f) / 5f);
                if ((float)rng.NextDouble() > keep) continue;
                float tall = 0.22f + 0.25f * (float)rng.NextDouble() + (rad > edge - 3f ? 0.25f : 0f);
                float wide = tall * (1.1f + 0.5f * (float)rng.NextDouble());
                int key = Mathf.FloorToInt((p.x + 60f) / 12f) * 100 + Mathf.FloorToInt((p.y + 60f) / 12f);
                if (!chunks.TryGetValue(key, out var c)) chunks[key] = c = (new List<Vector3>(), new List<Vector2>(), new List<int>());
                float spin = (float)rng.NextDouble() * 180f;
                Vector3 baseP = new Vector3(p.x, Ground, p.y);
                Vector3 leanV = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * tall * 0.3f;
                float u0 = (float)rng.NextDouble() < 0.5f ? 0f : 1f;           // flip some tufts so they don't all match
                for (int q = 0; q < 3; q++)
                {
                    float ang = (spin + q * 60f) * Mathf.Deg2Rad;
                    var side = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * wide * 0.5f;
                    int b = c.v.Count;
                    c.v.Add(baseP - side); c.v.Add(baseP + side); c.v.Add(baseP + side + Vector3.up * tall + leanV); c.v.Add(baseP - side + Vector3.up * tall + leanV);
                    c.uv.Add(new Vector2(u0, 0f)); c.uv.Add(new Vector2(1f - u0, 0f)); c.uv.Add(new Vector2(1f - u0, 1f)); c.uv.Add(new Vector2(u0, 1f));
                    c.t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2, b, b + 1, b + 2, b, b + 2, b + 3 });   // both faces
                }
                count++;
            }
            foreach (var c in chunks.Values)
            {
                var m = new Mesh { name = "Grass" };
                if (c.v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(c.v); m.SetUVs(0, c.uv); m.SetTriangles(c.t, 0);
                var normals = new Vector3[c.v.Count];
                for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;          // lit like the ground it grows from
                m.normals = normals;
                m.RecalculateBounds();
                var go = MeshObject("Grass", transform, m, mat, false);
                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            GameDirector.I.Log("yard: " + count + " tufts of grass");
        }

        // ---------------------------------------------------------------- the woods

        static Texture2D needles;
        public static void ResetStatics() { needles = null; blades = null; I = null; }

        static Texture2D Needles()
        {
            if (needles != null) return needles;
            const int n = 128;
            needles = new Texture2D(n, n, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float streak = Mathf.PerlinNoise(u * 40f, v * 6f);
                    float clump = Mathf.PerlinNoise(u * 6f + 3f, v * 6f);
                    float b = 0.55f + 0.6f * streak * clump;
                    px[y * n + x] = new Color(0.07f * b, 0.13f * b, 0.07f * b);
                }
            needles.SetPixels(px);
            needles.Apply(true);
            return needles;
        }

        class Chunk
        {
            public readonly List<Vector3> tv = new List<Vector3>(), fv = new List<Vector3>();
            public readonly List<Vector2> tuv = new List<Vector2>(), fuv = new List<Vector2>();
            public readonly List<int> tt = new List<int>(), ft = new List<int>();
        }

        // One conifer: a tapering trunk and ragged tiers of drooping branches, a few hundred triangles.
        static void Conifer(Chunk c, Vector3 p, float height, System.Random r)
        {
            const int sides = 7;
            float baseR = 0.12f + height * 0.012f;
            int b = c.tv.Count;
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                c.tv.Add(p + d * baseR + Vector3.down * 0.3f);
                c.tv.Add(p + d * baseR * 0.15f + Vector3.up * height * 0.95f);
                c.tuv.Add(new Vector2(i / (float)sides * 1.5f, 0f));
                c.tuv.Add(new Vector2(i / (float)sides * 1.5f, height / 2f));
                if (i < sides)
                {
                    int k = b + i * 2;
                    c.tt.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 });
                }
            }
            int tiers = 6 + (int)(height / 3.5f);
            float first = height * (0.22f + 0.12f * (float)r.NextDouble());
            float spread = 1.6f + height * 0.11f;
            const int seg = 9;
            for (int t = 0; t < tiers; t++)
            {
                float f = t / (float)(tiers - 1);
                float y = Mathf.Lerp(first, height * 0.93f, f);
                float rad = spread * (1f - f * 0.88f) * (0.85f + 0.3f * (float)r.NextDouble());
                float tierH = Mathf.Max(0.6f, (height - first) / tiers * 1.6f);
                int apex = c.fv.Count;
                c.fv.Add(p + Vector3.up * (y + tierH));
                c.fuv.Add(new Vector2(0.5f, 1f));
                float spin = (float)r.NextDouble() * Mathf.PI;
                for (int i = 0; i <= seg; i++)
                {
                    float a = spin + i * Mathf.PI * 2f / seg;
                    float jag = (i % 2 == 0 ? 1f : 0.72f) * (0.85f + 0.3f * (float)r.NextDouble());
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    c.fv.Add(p + d * rad * jag + Vector3.up * (y - 0.25f * jag));
                    c.fuv.Add(new Vector2(i / (float)seg * 3f, 0f));
                }
                int under = c.fv.Count;
                c.fv.Add(p + Vector3.up * (y + tierH * 0.3f));
                c.fuv.Add(new Vector2(0.5f, 0.6f));
                for (int i = 0; i < seg; i++)
                {
                    c.ft.AddRange(new[] { apex, apex + 2 + i, apex + 1 + i });       // the top of the tier
                    c.ft.AddRange(new[] { under, apex + 1 + i, apex + 2 + i });      // and its underside
                }
            }
        }

        void BuildWoods(System.Random rng)
        {
            var barkMat = Tex.Real("pine_bark", 1f, 1f, 1f, new Color(0.62f, 0.56f, 0.5f), Tex.Boards, 0.05f);
            barkMat.mainTextureScale = Vector2.one;
            var tones = new[] { new Color(1f, 1f, 1f), new Color(0.85f, 0.95f, 0.9f), new Color(1.1f, 1.05f, 0.95f) };
            var chunks = new Dictionary<int, Chunk>();
            var colliders = new GameObject("Tree trunks").transform;
            colliders.SetParent(transform, false);
            const float cell = 5.5f, extent = 115f;
            int count = 0;
            for (float gx = -extent; gx < extent; gx += cell)
                for (float gz = -extent; gz < extent; gz += cell)
                {
                    var p = new Vector2(gx + (float)rng.NextDouble() * cell, gz + (float)rng.NextDouble() * cell);
                    if (InClearing(p, 1.5f + 2f * (float)rng.NextDouble()) || DistToDrive(p) < 3.2f) continue;
                    float edgeDist = (p - ClearingCentre).magnitude - EdgeRadius(Mathf.Atan2(p.y - ClearingCentre.y, p.x - ClearingCentre.x));
                    float height = 13f + 11f * (float)rng.NextDouble() + Mathf.Clamp(edgeDist * 0.15f, 0f, 5f);
                    int key = Mathf.FloorToInt((p.x + extent) / 40f) * 100 + Mathf.FloorToInt((p.y + extent) / 40f);
                    Chunk c;
                    if (!chunks.TryGetValue(key, out c)) chunks[key] = c = new Chunk();
                    Conifer(c, new Vector3(p.x, Ground, p.y), height, rng);
                    count++;
                    if ((p - ClearingCentre).magnitude < 52f)
                    {
                        var col = new GameObject("Trunk").AddComponent<CapsuleCollider>();
                        col.transform.SetParent(colliders, false);
                        col.transform.localPosition = new Vector3(p.x, Ground + 1.5f, p.y);
                        col.radius = 0.35f;
                        col.height = 3f;
                    }
                }
            int ci = 0;
            foreach (var c in chunks.Values)
            {
                var tm = new Mesh { name = "Trunks" };
                tm.SetVertices(c.tv); tm.SetUVs(0, c.tuv); tm.SetTriangles(c.tt, 0); tm.RecalculateNormals(); tm.RecalculateBounds();
                MeshObject("Trunks", transform, tm, barkMat, false);
                var fm = new Mesh { name = "Branches" };
                if (c.fv.Count > 65000) fm.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                fm.SetVertices(c.fv); fm.SetUVs(0, c.fuv); fm.SetTriangles(c.ft, 0); fm.RecalculateNormals(); fm.RecalculateBounds();
                var fol = Build.Mat(tones[ci++ % tones.Length], 0f, 0.02f);
                fol.mainTexture = Needles();
                MeshObject("Branches", transform, fm, fol, false);
            }
            GameDirector.I.Log("woods: " + count + " trees in " + chunks.Count + " chunks around a clearing about " + Mathf.RoundToInt(edge[0]) + " m across");

            // An invisible edge: the trees get too close together to go any further.
            var ring = new GameObject("Edge of the woods").transform;
            ring.SetParent(transform, false);
            const int n = 72;
            const float R = 47f;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var w = new GameObject("Edge").AddComponent<BoxCollider>();
                w.transform.SetParent(ring, false);
                w.transform.localPosition = new Vector3(ClearingCentre.x + Mathf.Cos(a) * R, Ground + 2f, ClearingCentre.y + Mathf.Sin(a) * R);
                w.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                w.size = new Vector3(0.5f, 4f, 2f * Mathf.PI * R / n + 0.5f);
            }
        }

        // Fallen trunks, stumps, ferns and roots where the clearing gives way to the trees.
        void BuildEdgeOfWoods(System.Random rng)
        {
            int Placed(string id, int want, float r0, float r1, bool solid, float scaleLo, float scaleHi)
            {
                int n = 0;
                for (int tries = 0; tries < want * 6 && n < want; tries++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float r = EdgeRadius(a) + Mathf.Lerp(r0, r1, (float)rng.NextDouble());
                    var p = ClearingCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (DistToDrive(p) < 3f) continue;
                    Model(id, new Vector3(p.x, Ground, p.y), (float)rng.NextDouble() * 360f, solid, Mathf.Lerp(scaleLo, scaleHi, (float)rng.NextDouble()));
                    n++;
                }
                return n;
            }
            Placed("dead_tree_trunk", 3, -1f, 4f, true, 1f, 1.2f);
            Placed("dead_tree_trunk_02", 2, 0f, 6f, true, 0.9f, 1.1f);
            Placed("tree_stump_02", 4, -2f, 5f, true, 0.5f, 0.75f);
            Placed("fern_02", 45, -1.5f, 10f, false, 0.45f, 0.8f);
            Placed("pine_roots", 1, 2f, 6f, false, 1f, 1f);
            Placed("rock_moss_set_01", 1, 8f, 14f, true, 0.6f, 0.6f);
        }

        // ---------------------------------------------------------------- light and sound

        void BuildLight()
        {
            sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(48f, 215f, 0f);
        }

        AudioSource Ambient(string clip, Vector3 at, float minD)
        {
            var a = Sfx.Loop(clip, null, 0f, true);
            a.transform.position = at;
            a.minDistance = minD;
            a.maxDistance = 90f;
            a.time = Random.value * Mathf.Max(0.1f, a.clip != null ? a.clip.length - 0.1f : 0f);
            muffles.Add(a.gameObject.AddComponent<AudioLowPassFilter>());
            return a;
        }

        void BuildAmbience()
        {
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f + 0.4f;
                var at = new Vector3(ClearingCentre.x + Mathf.Cos(a) * 20f, Ground + 4f, ClearingCentre.y + Mathf.Sin(a) * 20f);
                if (Sfx.HasRecording("forestday")) dayAmb.Add(Ambient("forestday", at, 18f));
                if (Sfx.HasRecording("crickets")) nightAmb.Add(Ambient("crickets", at + new Vector3(3f, -3f, 2f), 14f));
            }
            muffles.Add(chimes.gameObject.AddComponent<AudioLowPassFilter>());
        }

        /// <summary>time: 0 = day, 1 = evening. Light, sky and the sounds of the woods follow it.</summary>
        public void SetTime(int newTime, int newDay)
        {
            time = newTime;
            day = newDay;
            // The house has just set its own fog and ambient light; remember them for when the player comes back in.
            indoorFog = RenderSettings.fogColor;
            indoorAmbient = RenderSettings.ambientLight;
            indoorDensity = RenderSettings.fogDensity;
            outsideBlend = -1f;     // apply on the next frame
            if (sun != null)
            {
                bool dayTime = time == 0;
                sun.color = dayTime ? new Color(0.86f, 0.9f, 0.97f) : new Color(0.42f, 0.5f, 0.78f);
                sun.intensity = dayTime ? 0.62f - 0.04f * (day - 1) : 0.07f;
            }
            if (porchLight != null) porchLight.intensity = time == 0 ? 0f : 1.3f;
            if (porchBulb != null) porchBulb.SetColor("_EmissionColor", time == 0 ? Color.black : new Color(1f, 0.8f, 0.5f) * 2f);
            foreach (var g in outsideGlass)
            {
                g.color = time == 0 ? new Color(0.12f, 0.14f, 0.16f) : new Color(0.3f, 0.2f, 0.1f);
                g.SetColor("_EmissionColor", time == 0 ? new Color(0.02f, 0.025f, 0.03f) : new Color(1f, 0.72f, 0.42f) * 0.9f);
            }
        }

        void Update()
        {
            var p = Player.I;
            if (p == null || house == null) return;
            Vector3 pos = p.transform.position;
            bool inside = !house.Outside(pos);
            float dt = Time.deltaTime;

            // The woods are loud outside and muffled through the walls.
            float muffle = inside ? 0.32f : 1f;
            foreach (var a in dayAmb) a.volume = Mathf.MoveTowards(a.volume, (time == 0 ? 0.55f : 0f) * muffle, dt * 0.3f);
            foreach (var a in nightAmb) a.volume = Mathf.MoveTowards(a.volume, (time == 1 ? 0.5f : 0f) * muffle, dt * 0.3f);
            if (chimes != null) chimes.volume = Mathf.MoveTowards(chimes.volume, (time == 0 ? 0.35f : 0.22f) * muffle, dt * 0.3f);
            foreach (var f in muffles) if (f != null) f.cutoffFrequency = Mathf.MoveTowards(f.cutoffFrequency, inside ? 900f : 22000f, dt * 30000f);

            // Outside, the light is grey daylight and the trees fade into mist; inside, the house's own dimness.
            var g = GameDirector.I;
            bool housePhase = g != null && (g.phase == Phase.Morning || g.phase == Phase.Afternoon || g.phase == Phase.Evening);
            if (housePhase && !house.InCellar(pos))
            {
                float target = inside ? 0f : 1f;
                if (outsideBlend < 0f) outsideBlend = target;
                if (outsideBlend != target || outsideBlend > 0f)
                {
                    outsideBlend = Mathf.MoveTowards(outsideBlend, target, dt / 1.2f);
                    float dim = 1f - 0.06f * (day - 1);
                    Color fogOut = time == 0 ? new Color(0.33f, 0.36f, 0.37f) * dim : new Color(0.02f, 0.025f, 0.038f);
                    Color ambOut = time == 0 ? new Color(0.42f, 0.44f, 0.46f) * dim : new Color(0.05f, 0.055f, 0.08f);
                    float densOut = (time == 0 ? 0.028f : 0.034f) + 0.003f * (day - 1);     // the trees go grey at thirty metres
                    RenderSettings.fogColor = Color.Lerp(indoorFog, fogOut, outsideBlend);
                    RenderSettings.ambientLight = Color.Lerp(indoorAmbient, ambOut, outsideBlend);
                    RenderSettings.fogDensity = Mathf.Lerp(indoorDensity, densOut, outsideBlend);
                    if (p.cam != null) p.cam.backgroundColor = RenderSettings.fogColor;
                }
            }

            // Sheets on the line move in the wind.
            float t = Time.time;
            for (int i = 0; i < sheets.Count; i++)
                sheets[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.9f + i * 1.7f) * 9f + Mathf.PerlinNoise(t * 0.6f, i) * 10f - 5f);

            // Near the edge of the trees: there's no going on.
            Vector2 d = new Vector2(pos.x, pos.z) - ClearingCentre;
            if (housePhase && d.magnitude > 44f && t - lastWarn > 12f && g.hud != null)
            {
                lastWarn = t;
                g.hud.Say("The trees close in. There's no path you'd trust out there.", 3.5f);
            }
        }
    }
}
