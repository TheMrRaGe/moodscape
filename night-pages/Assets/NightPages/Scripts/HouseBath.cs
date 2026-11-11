using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The bathroom: a small room off the bedroom, in the corner by the outside wall. A bath under the window with a
    /// shower curtain, a basin with a mirror over it, a toilet, a towel rail, a pull-cord light. It doubles as a
    /// darkroom (the photo-restoring job): a board across the bath with three trays, an enlarger on a little table,
    /// a red safelight on the wall, a line over the bath to peg the prints on, and a blind to black out the window.
    /// </summary>
    public partial class House
    {
        public Room bath;
        public Basin washbasin;
        public Source basinTap;
        public Transform bathMirror;
        public Light safelight, enlargerLamp;
        public Transform enlargerHead, printLine;
        public readonly List<Transform> trays = new List<Transform>();
        public GameObject blind;
        public Interactable enlarger, pullCord;
        public Vector3 bathNavOutside, bathNavInside;
        /// <summary>Where the toothbrush cup, the towel and the mirror are (the hauntings use them).</summary>
        public Transform toothCup, towel;

        // Drawn in the bedroom's drawing: the bathroom takes x 5.0 to 7.5, z 0 to 2.7.
        void BuildBathroom()
        {
            const float x0 = 5.0f, x1 = 7.5f, z0 = 0f, z1 = 2.7f, Tw = 0.12f, doorX = 5.75f, doorW = 0.8f;
            var tile = Tex.Real("floor_tiles_06", 2.5f, 2.7f, 0.9f, new Color(0.86f, 0.9f, 0.88f), Tex.Plaster, 0.5f);
            Box("Bathroom floor", new Vector3((x0 + x1) / 2f, 0.004f, (z0 + z1) / 2f), new Vector3(x1 - x0 - 0.05f, 0.006f, z1 - z0 - 0.05f), tile, false);
            // The partition walls: one along x = 5 (the bedroom side), one along z = 2.7 with the door in it.
            var paper = Paper(2.7f, H);
            var wallTile = Tex.Real("floor_tiles_06", 2.7f, 1.2f, 0.5f, new Color(0.88f, 0.92f, 0.9f), Tex.Plaster, 0.6f);
            Wall2(new Vector3(x0, H / 2f, (z0 + z1) / 2f), new Vector3(Tw, H, z1 - z0), paper);
            float a0 = x0, a1 = doorX - doorW / 2f, b0 = doorX + doorW / 2f, b1 = x1;
            Wall2(new Vector3((a0 + a1) / 2f, H / 2f, z1), new Vector3(a1 - a0, H, Tw), paper);
            Wall2(new Vector3((b0 + b1) / 2f, H / 2f, z1), new Vector3(b1 - b0, H, Tw), paper);
            Wall2(new Vector3(doorX, (2.05f + H) / 2f, z1), new Vector3(doorW, H - 2.05f, Tw), paper);
            // Tiles to shoulder height inside.
            Box("Tiles", new Vector3(x0 + Tw / 2f + 0.006f, 0.6f, (z0 + z1) / 2f), new Vector3(0.012f, 1.2f, z1 - z0 - 0.1f), wallTile, false);
            Box("Tiles", new Vector3((b0 + b1) / 2f, 0.6f, z1 - Tw / 2f - 0.006f), new Vector3(b1 - b0, 1.2f, 0.012f), wallTile, false);
            Box("Tiles", new Vector3((x0 + x1) / 2f, 0.6f, z0 + 0.11f), new Vector3(x1 - x0 - 0.1f, 1.2f, 0.012f), wallTile, false);
            Box("Tiles", new Vector3(x1 - 0.11f, 0.6f, 0.85f), new Vector3(0.012f, 1.2f, 1.6f), wallTile, false);
            Box("Door frame", new Vector3(a1 - 0.03f, 1.025f, z1), new Vector3(0.06f, 2.05f, Tw + 0.05f), Cream, false);
            Box("Door frame", new Vector3(b0 + 0.03f, 1.025f, z1), new Vector3(0.06f, 2.05f, Tw + 0.05f), Cream, false);
            Box("Door frame", new Vector3(doorX, 2.08f, z1), new Vector3(doorW + 0.12f, 0.06f, Tw + 0.05f), Cream, false);

            // The room itself, ahead of the bedroom in the list so "which room is this?" finds it first.
            var r = new Room { name = "bathroom", center = P(new Vector3((x0 + x1) / 2f, 0f, (z0 + z1) / 2f)), notePoint = P(new Vector3(5.6f, 0.78f, 0.35f)) };
            r.bounds = new Bounds(r.center + new Vector3(0f, H / 2f, 0f), new Vector3(x1 - x0 - Tw, H, z1 - z0 - Tw));
            r.baseIntensity = 0.9f;
            r.light = Build.Lamp("bathroom light", transform, r.center + new Vector3(0f, 2.45f, 0f), new Color(1f, 0.9f, 0.75f), r.baseIntensity, 5f, true);
            float hingeX = a1 + 0.01f;
            float baseYaw = mx > 0f ? 90f : -90f;
            float into = 95f * (mx > 0f ? 1f : -1f) * (mz > 0f ? 1f : -1f);
            r.door = Door.Create(transform, P(hingeX, 0f, z1), into, "bathroom", new Color(0.86f, 0.84f, 0.78f), baseYaw, null, doorW - 0.03f, 2.02f);
            doors.Add(r.door);
            rooms.Insert(0, r);
            bath = r;
            bathNavOutside = P(doorX, 0f, z1 + 0.75f);
            bathNavInside = P(doorX, 0f, 1.75f);
            Shade(new Vector3((x0 + x1) / 2f, 2.45f, (z0 + z1) / 2f));

            // A pull cord just inside the door (bathrooms have them: no switches near water).
            var cord = Box("Light pull", new Vector3(a1 + 0.18f, 1.75f, z1 - 0.25f), new Vector3(0.01f, 1.2f, 0.01f), new Color(0.9f, 0.88f, 0.8f), false);
            var bob = Box("Light pull", new Vector3(a1 + 0.18f, 1.13f, z1 - 0.25f), new Vector3(0.025f, 0.05f, 0.025f), new Color(0.85f, 0.8f, 0.7f), false);
            var cordHit = bob.AddComponent<BoxCollider>();
            cordHit.size = new Vector3(5f, 22f, 5f);
            cordHit.center = new Vector3(0f, 9f, 0f);
            pullCord = Build.Usable(bob, "");
            pullCord.promptFn = () => r.lit ? "Pull the cord (light off)" : "Pull the cord (light on)";
            pullCord.use = () =>
            {
                r.lit = !r.lit;
                Sfx.At("click", bob.transform.position, 0.45f, 1.3f);
                StartCoroutine(Tug(bob.transform, cord.transform));
                if (Switched != null) Switched(r, r.lit);
            };
            r.lightSwitch = pullCord;

            // The window over the bath (already in the outside wall), with a blind to black it out.
            blind = Box("Blackout blind", new Vector3(x1 - 0.17f, 1.55f, 2.2f), new Vector3(0.02f, 1.3f, 1.2f), new Color(0.03f, 0.03f, 0.03f), false);
            blind.SetActive(false);

            // The bath: white, on a low plinth, with taps at the end and a curtain on a rail.
            var enamel = Build.Mat(new Color(0.94f, 0.93f, 0.9f), 0f, 0.75f);
            var chrome = Build.Mat(new Color(0.78f, 0.79f, 0.8f), 0f, 0.85f);
            float bx0 = 6.72f, bx1 = 7.42f, bz0 = 0.14f, bz1 = 1.84f, bh = 0.56f;
            Box("Bath", new Vector3((bx0 + bx1) / 2f, 0.05f, (bz0 + bz1) / 2f), new Vector3(bx1 - bx0, 0.1f, bz1 - bz0), enamel);
            Box("Bath", new Vector3(bx0 + 0.03f, bh / 2f, (bz0 + bz1) / 2f), new Vector3(0.06f, bh, bz1 - bz0), enamel);
            Box("Bath", new Vector3(bx1 - 0.03f, bh / 2f, (bz0 + bz1) / 2f), new Vector3(0.06f, bh, bz1 - bz0), enamel);
            Box("Bath", new Vector3((bx0 + bx1) / 2f, bh / 2f, bz0 + 0.03f), new Vector3(bx1 - bx0, bh, 0.06f), enamel);
            Box("Bath", new Vector3((bx0 + bx1) / 2f, bh / 2f, bz1 - 0.03f), new Vector3(bx1 - bx0, bh, 0.06f), enamel);
            Build.Prim(PrimitiveType.Cylinder, "Bath tap", transform, P((bx0 + bx1) / 2f - 0.1f, bh + 0.08f, bz0 + 0.05f), new Vector3(0.03f, 0.08f, 0.03f), chrome, false);
            Build.Prim(PrimitiveType.Cylinder, "Bath tap", transform, P((bx0 + bx1) / 2f + 0.1f, bh + 0.08f, bz0 + 0.05f), new Vector3(0.03f, 0.08f, 0.03f), chrome, false);
            Box("Curtain rail", new Vector3(bx0 - 0.02f, 2.0f, (bz0 + bz1) / 2f), new Vector3(0.02f, 0.02f, bz1 - bz0), chrome, false);
            var curtainMat = Build.Mat(new Color(0.85f, 0.88f, 0.86f, 0.8f), 0f, 0.2f);
            Work.Fade(curtainMat);
            curtainMat.color = new Color(0.85f, 0.88f, 0.86f, 0.82f);
            Box("Shower curtain", new Vector3(bx0 - 0.02f, 1.25f, bz0 + 0.35f), new Vector3(0.01f, 1.5f, 0.55f), curtainMat, false);
            Box("Bath mat", new Vector3(bx0 - 0.35f, 0.012f, 1.0f), new Vector3(0.5f, 0.012f, 0.75f), new Color(0.55f, 0.62f, 0.6f), false);

            // The washbasin on the bedroom-side wall, a mirror over it, a cup with the toothbrushes.
            float wz = 1.55f;
            Box("Basin pedestal", new Vector3(x0 + 0.3f, 0.38f, wz), new Vector3(0.16f, 0.76f, 0.16f), enamel);
            Box("Washbasin", new Vector3(x0 + 0.3f, 0.8f, wz), new Vector3(0.42f, 0.08f, 0.5f), enamel);
            var basinWater = Build.Mat(new Color(0.5f, 0.58f, 0.6f, 0.55f), 0f, 0.95f);
            Work.Fade(basinWater);
            basinWater.color = new Color(0.5f, 0.58f, 0.6f, 0.55f);
            var bwGo = new GameObject("Washbasin water level");
            bwGo.transform.SetParent(transform, false);
            bwGo.transform.localPosition = P(x0 + 0.3f, 0.8f, wz);
            washbasin = bwGo.AddComponent<Basin>();
            washbasin.bottom = transform.TransformPoint(P(x0 + 0.3f, 0.79f, wz)).y;
            washbasin.depth = 0.05f;
            washbasin.inside = new Bounds(W(new Vector3(x0 + 0.3f, 0.84f, wz)), new Vector3(0.36f, 0.12f, 0.42f));
            washbasin.water = Build.Box("Basin water", transform, P(x0 + 0.3f, 0.8f, wz), new Vector3(0.34f, 0.004f, 0.4f), basinWater, false).transform;
            washbasin.water.gameObject.SetActive(false);
            var btap = Build.Prim(PrimitiveType.Cylinder, "Basin tap", transform, P(x0 + 0.11f, 0.9f, wz), new Vector3(0.025f, 0.06f, 0.025f), chrome, false);
            var bSpout = new GameObject("Basin spout").transform;
            bSpout.SetParent(transform, false);
            bSpout.localPosition = P(x0 + 0.2f, 0.95f, wz);
            Box("Basin spout", new Vector3(x0 + 0.16f, 0.955f, wz), new Vector3(0.1f, 0.02f, 0.02f), chrome, false);
            var under = Build.Box("Under the basin tap", transform, P(x0 + 0.21f, 0.88f, wz), new Vector3(0.1f, 0.12f, 0.1f), chrome, false);
            under.GetComponent<Renderer>().enabled = false;
            under.AddComponent<BoxCollider>().isTrigger = true;
            basinTap = under.AddComponent<Source>();
            basinTap.spout = bSpout;
            basinTap.basin = washbasin;
            washbasin.tap = basinTap;
            var streamMat = Build.Mat(new Color(0.75f, 0.85f, 0.9f, 0.45f), 0.15f, 0.95f);
            Work.Fade(streamMat);
            streamMat.color = new Color(0.75f, 0.85f, 0.9f, 0.45f);
            basinTap.stream = Build.Prim(PrimitiveType.Cylinder, "Running water", transform, P(x0 + 0.21f, 0.875f, wz), new Vector3(0.012f, 0.075f, 0.012f), streamMat, false);
            basinTap.stream.SetActive(false);
            var tapHit = btap.AddComponent<BoxCollider>();
            tapHit.size = new Vector3(2.5f, 1.2f, 2.5f);
            Knob.Make(btap, null, Vector3.zero, Vector3.zero, "Run the tap", "Turn the tap off", on => basinTap.Set(on));
            // The mirror: the cabinet's glass door, dark and silvered.
            var glassMat = Build.Mat(new Color(0.55f, 0.58f, 0.6f), 0f, 0.97f);
            glassMat.SetFloat("_Metallic", 0.9f);
            Box("Mirror cabinet", new Vector3(x0 + Tw / 2f + 0.06f, 1.55f, wz), new Vector3(0.12f, 0.6f, 0.46f), Cream);
            var glass = Box("Bathroom mirror", new Vector3(x0 + Tw / 2f + 0.125f, 1.55f, wz), new Vector3(0.008f, 0.54f, 0.4f), glassMat, false);
            bathMirror = glass.transform;
            Box("Shelf", new Vector3(x0 + Tw / 2f + 0.07f, 1.2f, wz), new Vector3(0.14f, 0.02f, 0.5f), Cream, false);
            var cup = Build.Prim(PrimitiveType.Cylinder, "Toothbrush cup", transform, P(x0 + Tw / 2f + 0.08f, 1.255f, wz + 0.15f), new Vector3(0.06f, 0.045f, 0.06f), Build.Mat(new Color(0.75f, 0.85f, 0.88f), 0f, 0.8f), false);
            toothCup = cup.transform;
            Box("Toothbrush", new Vector3(x0 + Tw / 2f + 0.075f, 1.32f, wz + 0.145f), new Vector3(0.01f, 0.17f, 0.012f), new Color(0.85f, 0.3f, 0.25f), false).transform.localRotation = Quaternion.Euler(8f, 0f, 6f);

            // The toilet against the back wall, by the door.
            Box("Toilet cistern", new Vector3(5.62f, 0.62f, z0 + 0.2f), new Vector3(0.42f, 0.4f, 0.18f), enamel);
            var bowl = Build.Prim(PrimitiveType.Cylinder, "Toilet", transform, P(5.62f, 0.2f, z0 + 0.5f), new Vector3(0.36f, 0.2f, 0.46f), enamel, true);
            FlatBottom(bowl);
            Box("Toilet seat", new Vector3(5.62f, 0.41f, z0 + 0.52f), new Vector3(0.38f, 0.03f, 0.44f), new Color(0.35f, 0.24f, 0.16f), false);

            // The towel rail on the partition, a towel over it.
            Box("Towel rail", new Vector3(7.0f, 1.0f, z1 - Tw / 2f - 0.05f), new Vector3(0.6f, 0.02f, 0.02f), chrome, false);
            var tw = Box("Towel", new Vector3(7.0f, 0.82f, z1 - Tw / 2f - 0.06f), new Vector3(0.5f, 0.38f, 0.03f), new Color(0.55f, 0.45f, 0.62f), false);
            towel = tw.transform;

            // ---- the darkroom: a board across the bath with three trays, an enlarger on a table, the safelight, the line.
            var board = Build.Mat(new Color(0.5f, 0.4f, 0.28f), 0f, 0.2f);
            Box("Bath board", new Vector3((bx0 + bx1) / 2f, bh + 0.015f, (bz0 + bz1) / 2f + 0.05f), new Vector3(bx1 - bx0 + 0.06f, 0.03f, 1.2f), board);
            string[] trayNames = { "Developer", "Stop bath", "Fixer" };
            Color[] trayTint = { new Color(0.85f, 0.85f, 0.78f), new Color(0.82f, 0.78f, 0.72f), new Color(0.78f, 0.8f, 0.82f) };
            for (int i = 0; i < 3; i++)
            {
                float tz = 0.6f + i * 0.38f;
                var trayMat = Build.Mat(trayTint[i], 0f, 0.6f);
                var tray = Box(trayNames[i] + " tray", new Vector3((bx0 + bx1) / 2f, bh + 0.045f, tz), new Vector3(0.36f, 0.03f, 0.3f), trayMat);
                Box("Chemistry", new Vector3((bx0 + bx1) / 2f, bh + 0.055f, tz), new Vector3(0.33f, 0.012f, 0.27f), Build.Mat(new Color(0.42f, 0.38f, 0.3f), 0f, 0.95f), false);
                trays.Add(tray.transform);
            }
            // The enlarger on a little table in the corner by the window.
            // A plain little bench of a table for it (kept small: the door swings in beside it).
            const float et = 0.74f;
            Box("Enlarger table", new Vector3(7.15f, et - 0.02f, 2.2f), new Vector3(0.48f, 0.04f, 0.5f), Build.Mat(new Color(0.42f, 0.32f, 0.22f), 0f, 0.3f));
            foreach (var lx in new[] { 6.95f, 7.35f }) foreach (var lz in new[] { 1.99f, 2.41f })
                Box("Table leg", new Vector3(lx, (et - 0.04f) / 2f, lz), new Vector3(0.04f, et - 0.04f, 0.04f), Build.Mat(new Color(0.38f, 0.28f, 0.2f), 0f, 0.3f), false);
            var dark = Build.Mat(new Color(0.08f, 0.08f, 0.08f), 0f, 0.4f);
            Box("Enlarger baseboard", new Vector3(7.12f, et + 0.012f, 2.18f), new Vector3(0.36f, 0.024f, 0.4f), Build.Mat(new Color(0.85f, 0.83f, 0.78f)), false);
            Box("Enlarger column", new Vector3(7.3f, et + 0.45f, 2.18f), new Vector3(0.04f, 0.9f, 0.04f), dark, false);
            var head = Box("Enlarger head", new Vector3(7.14f, et + 0.62f, 2.18f), new Vector3(0.22f, 0.18f, 0.2f), dark);
            enlargerHead = head.transform;
            Build.Prim(PrimitiveType.Cylinder, "Enlarger lens", transform, P(7.12f, et + 0.5f, 2.18f), new Vector3(0.07f, 0.03f, 0.07f), dark, false);
            enlargerLamp = Build.Lamp("Enlarger light", transform, P(7.12f, et + 0.46f, 2.18f), new Color(1f, 0.96f, 0.85f), 0f, 1.4f);
            enlargerLamp.type = LightType.Spot;
            enlargerLamp.spotAngle = 55f;
            enlargerLamp.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            enlarger = Build.Usable(head, "The enlarger");
            // The safelight: a red lamp on the partition over the bath.
            Box("Safelight", new Vector3(7.0f, 1.95f, z1 - Tw / 2f - 0.05f), new Vector3(0.16f, 0.12f, 0.08f), Build.Mat(new Color(0.5f, 0.05f, 0.03f), 0.8f), false);
            safelight = Build.Lamp("Safelight", transform, P(7.0f, 1.9f, z1 - 0.3f), new Color(1f, 0.12f, 0.05f), 0f, 4.5f);
            // A line across the room over the bath for the prints, with pegs.
            Box("Drying line", new Vector3((bx0 + bx1) / 2f, 1.85f, (bz0 + bz1) / 2f), new Vector3(0.006f, 0.006f, bz1 - bz0 + 0.2f), Build.Mat(new Color(0.85f, 0.85f, 0.8f)), false);
            var line = new GameObject("Print line").transform;
            line.SetParent(transform, false);
            line.localPosition = P((bx0 + bx1) / 2f, 1.85f, bz0 + 0.1f);
            printLine = line;
        }

        System.Collections.IEnumerator Tug(Transform bob, Transform cord)
        {
            Vector3 b0 = bob.localPosition, c0 = cord.localPosition;
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                float d = Mathf.Sin(t / 0.25f * Mathf.PI) * 0.06f;
                bob.localPosition = b0 + Vector3.down * d;
                cord.localPosition = c0 + Vector3.down * d;
                yield return null;
            }
            bob.localPosition = b0; cord.localPosition = c0;
        }

        // A partition wall (thinner than the house's own, papered, a skirting board along it).
        void Wall2(Vector3 drawnPos, Vector3 size, Material paper)
        {
            var w = Box("Wall", drawnPos, size, paper);
            var wc = w.GetComponent<Collider>();
            if (wc != null) walls.Add(wc);
            Vector3 thick = size.x > size.z ? new Vector3(size.x, 0.12f, size.z + 0.04f) : new Vector3(size.x + 0.04f, 0.12f, size.z);
            if (size.y > 1f) Box("Skirting", new Vector3(drawnPos.x, 0.06f, drawnPos.z), thick, Cream, false);
        }
    }
}
