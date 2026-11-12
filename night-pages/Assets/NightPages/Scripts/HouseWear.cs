using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The house wears, day by day, the way a house does when one person is living in it badly: cups that don't
    /// get washed, crumpled pages that didn't work, bottles, a newspaper nobody read, takeaway cartons. Each morning
    /// a little more of it (by the walls and on the floor by where you sit, never in a doorway). And from the middle
    /// of the week the house itself starts to give: wallpaper lifting at a seam in the hall, a crack coming down from
    /// the ceiling, by the end a hole in the plaster by the cellar door with the lath showing, and the plaster that
    /// came out of it on the boards.
    /// </summary>
    public partial class House
    {
        Transform wear;
        public int wearSeed;

        static readonly Dictionary<Color, Material> wearMats = new Dictionary<Color, Material>();
        static Material WM(Color c, float smooth = 0.1f)
        {
            Material m;
            if (!wearMats.TryGetValue(c, out m) || m == null) { m = Build.Mat(c, 0f, smooth); wearMats[c] = m; }
            return m;
        }

        /// <summary>The house as it is on this morning (rebuilt each day; it only ever gets worse).</summary>
        public void Wear(int day)
        {
            if (wear != null) Destroy(wear.gameObject);
            if (wearSeed == 0) wearSeed = Random.Range(1, int.MaxValue);
            wear = new GameObject("Wear").transform;
            wear.SetParent(transform, false);
            var rng = new System.Random(wearSeed);
            int litter = 0, damage = 0;
            // The litter: two or three more things a day, in the rooms you use.
            int n = Mathf.Max(0, (day - 1) * 3);
            for (int i = 0; i < n * 3 && litter < n; i++)
            {
                var r = rooms[rng.Next(rooms.Count)];
                Vector3 at;
                if (!WearSpot(r, rng, out at)) continue;
                Litter(at, rng);
                litter++;
            }
            // The house giving way.
            if (day >= 4) { Peel(new Vector3(-1.38f, 0f, -5.0f), Vector3.right, rng, 2); damage++; }
            if (day >= 5) { Crack(new Vector3(1.38f, 0f, 0.3f), Vector3.left, rng); damage++; }
            if (day >= 6) { Hole(new Vector3(-1.85f, 0f, -7.88f), Vector3.forward, rng); damage++; }
            if (day >= 6) { Peel(new Vector3(1.38f, 0f, 5.2f), Vector3.left, rng, 3); damage++; }
            if (GameDirector.I != null && (litter > 0 || damage > 0))
                GameDirector.I.Log("house: day " + day + ", the house is wearing: " + litter + " things left lying about, " + damage + " places where it's giving way");
        }

        // A clear spot on the floor near a wall of the room, away from its doorway.
        bool WearSpot(Room r, System.Random rng, out Vector3 at)
        {
            Bounds b = r.bounds;
            for (int tries = 0; tries < 6; tries++)
            {
                float x = Mathf.Lerp(b.min.x + 0.3f, b.max.x - 0.3f, (float)rng.NextDouble());
                float z = Mathf.Lerp(b.min.z + 0.3f, b.max.z - 0.3f, (float)rng.NextDouble());
                // Pushed to a wall (things end up at the edges).
                if (rng.Next(2) == 0) x = rng.Next(2) == 0 ? b.min.x + 0.35f : b.max.x - 0.35f;
                else z = rng.Next(2) == 0 ? b.min.z + 0.35f : b.max.z - 0.35f;
                at = new Vector3(x, 0f, z);
                if (r.door != null && Vis.Level(r.door.Middle - at).magnitude < 1.4f) continue;
                if (r == hall && Mathf.Abs(x) < 0.6f) continue;
                if (Physics.CheckBox(at + Vector3.up * 0.15f, new Vector3(0.14f, 0.12f, 0.14f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                return true;
            }
            at = Vector3.zero;
            return false;
        }

        void Litter(Vector3 at, System.Random rng)
        {
            float R(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }
            Vector3 lp = transform.InverseTransformPoint(at);
            switch (rng.Next(7))
            {
                case 0: case 1:     // pages that didn't work, balled up
                    for (int k = 0; k < 1 + rng.Next(3); k++)
                        Build.Prim(PrimitiveType.Sphere, "Crumpled page", wear, lp + new Vector3(R(-0.2f, 0.2f), 0.04f, R(-0.2f, 0.2f)), new Vector3(R(0.06f, 0.09f), R(0.05f, 0.08f), R(0.06f, 0.09f)), WM(new Color(0.86f, 0.84f, 0.77f)), false).transform.rotation = Random.rotation;
                    break;
                case 2:             // a mug, cold, a ring of something in the bottom
                {
                    var mug = Build.Prim(PrimitiveType.Cylinder, "Mug", wear, lp + Vector3.up * 0.05f, new Vector3(0.085f, 0.05f, 0.085f), WM(new Color(0.75f, 0.72f, 0.66f), 0.5f), false);
                    Build.Prim(PrimitiveType.Cylinder, "Dregs", wear, lp + Vector3.up * 0.096f, new Vector3(0.07f, 0.002f, 0.07f), WM(new Color(0.12f, 0.07f, 0.04f), 0.7f), false);
                    break;
                }
                case 3:             // a bottle, on its side
                {
                    var bt = new GameObject("Bottle").transform;
                    bt.SetParent(wear, false);
                    bt.localPosition = lp + Vector3.up * 0.04f;
                    bt.localRotation = Quaternion.Euler(90f, R(0f, 360f), 0f);
                    var glass = WM(new Color(0.15f, 0.22f, 0.12f), 0.85f);
                    Build.Prim(PrimitiveType.Cylinder, "Body", bt, new Vector3(0f, 0.1f, 0f), new Vector3(0.075f, 0.1f, 0.075f), glass, false);
                    Build.Prim(PrimitiveType.Cylinder, "Neck", bt, new Vector3(0f, 0.25f, 0f), new Vector3(0.028f, 0.055f, 0.028f), glass, false);
                    break;
                }
                case 4:             // a newspaper, still folded
                    Build.Box("Newspaper", wear, lp + Vector3.up * 0.006f, new Vector3(0.3f, 0.012f, 0.42f), WM(new Color(0.66f, 0.64f, 0.58f)), false).transform.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                    break;
                case 5:             // a takeaway carton
                    Build.Box("Carton", wear, lp + Vector3.up * 0.04f, new Vector3(0.16f, 0.08f, 0.12f), WM(new Color(0.85f, 0.83f, 0.8f)), false).transform.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                    break;
                default:            // a plate with crusts on it
                    Build.Prim(PrimitiveType.Cylinder, "Plate", wear, lp + Vector3.up * 0.008f, new Vector3(0.24f, 0.006f, 0.24f), WM(new Color(0.8f, 0.79f, 0.74f), 0.5f), false);
                    Build.Box("Crust", wear, lp + new Vector3(0.03f, 0.02f, 0f), new Vector3(0.1f, 0.015f, 0.03f), WM(new Color(0.5f, 0.35f, 0.18f)), false).transform.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                    break;
            }
        }

        // Wallpaper lifting at the seams: strips hinged at the top, curling out.
        void Peel(Vector3 wallAt, Vector3 inward, System.Random rng, int strips)
        {
            Vector3 along = Vector3.Cross(Vector3.up, inward);
            for (int i = 0; i < strips; i++)
            {
                float top = 2.2f + (float)rng.NextDouble() * 0.5f, len = 0.5f + (float)rng.NextDouble() * 0.7f;
                var pivot = new GameObject("Peeling paper").transform;
                pivot.SetParent(wear, false);
                pivot.position = transform.TransformPoint(wallAt + along * (i * 0.42f) + inward * 0.012f + Vector3.up * top);
                pivot.rotation = Quaternion.LookRotation(inward) * Quaternion.Euler(-(10f + (float)rng.NextDouble() * 25f), 0f, 0f);
                Build.Box("Strip", pivot, new Vector3(0f, -len / 2f, 0.004f), new Vector3(0.4f, len, 0.003f), Paper(0.4f, len), false);
                Build.Box("Strip back", pivot, new Vector3(0f, -len / 2f, 0.008f), new Vector3(0.39f, len * 0.98f, 0.002f), WM(new Color(0.55f, 0.5f, 0.42f)), false);
                var bare = Build.Box("Bare plaster", wear, Vector3.zero, Vector3.one, WM(new Color(0.68f, 0.64f, 0.56f)), false);
                bare.transform.position = transform.TransformPoint(wallAt + along * (i * 0.42f) + inward * 0.006f + Vector3.up * (top - len / 2f));
                bare.transform.rotation = Quaternion.LookRotation(inward);
                bare.transform.localScale = new Vector3(0.42f, len, 0.004f);
            }
        }

        // A crack coming down from the ceiling: a dark zig-zag.
        void Crack(Vector3 wallAt, Vector3 inward, System.Random rng)
        {
            Vector3 along = Vector3.Cross(Vector3.up, inward);
            Vector3 p = wallAt + inward * 0.008f + Vector3.up * 2.78f;
            for (int i = 0; i < 9; i++)
            {
                float seg = 0.14f + (float)rng.NextDouble() * 0.12f, ang = -35f + (float)rng.NextDouble() * 70f;
                Vector3 dir = Quaternion.AngleAxis(ang, inward) * Vector3.down;
                var c = Build.Box("Crack", wear, Vector3.zero, Vector3.one, WM(new Color(0.06f, 0.05f, 0.045f)), false);
                c.transform.position = transform.TransformPoint(p + dir * seg / 2f);
                c.transform.rotation = Quaternion.LookRotation(inward, dir);
                c.transform.localScale = new Vector3(0.008f + i * 0.0006f, seg, 0.003f);
                p += dir * seg;
            }
        }

        // A hole in the plaster with the lath behind, and what came out of it on the floor.
        void Hole(Vector3 wallAt, Vector3 inward, System.Random rng)
        {
            Vector3 along = Vector3.Cross(Vector3.up, inward);
            Vector3 centre = wallAt + inward * 0.012f + Vector3.up * 0.75f + along * 0.9f;
            var dark = Build.Box("Hole", wear, Vector3.zero, Vector3.one, WM(new Color(0.015f, 0.012f, 0.01f)), false);
            dark.transform.position = transform.TransformPoint(centre);
            dark.transform.rotation = Quaternion.LookRotation(inward);
            dark.transform.localScale = new Vector3(0.55f, 0.45f, 0.01f);
            for (int i = 0; i < 5; i++)
            {
                if (rng.Next(4) == 0) continue;
                var lath = Build.Box("Lath", wear, Vector3.zero, Vector3.one, WM(new Color(0.42f, 0.3f, 0.18f)), false);
                lath.transform.position = transform.TransformPoint(centre - inward * 0.004f + Vector3.up * (-0.18f + i * 0.09f));
                lath.transform.rotation = Quaternion.LookRotation(inward) * Quaternion.Euler(0f, 0f, -3f + (float)rng.NextDouble() * 6f);
                lath.transform.localScale = new Vector3(0.5f, 0.035f, 0.012f);
            }
            for (int i = 0; i < 10; i++)
            {
                var ch = Build.Box("Plaster", wear, Vector3.zero, Vector3.one, WM(new Color(0.78f, 0.75f, 0.69f)), false);
                ch.transform.position = transform.TransformPoint(wallAt + inward * (0.08f + (float)rng.NextDouble() * 0.5f) + along * (0.9f + (float)rng.NextDouble() * 0.6f - 0.3f) + Vector3.up * 0.02f);
                ch.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                ch.transform.localScale = new Vector3(0.03f + (float)rng.NextDouble() * 0.1f, 0.02f + (float)rng.NextDouble() * 0.03f, 0.03f + (float)rng.NextDouble() * 0.08f);
            }
        }
    }
}
