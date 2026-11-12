using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The studio's look at the house's dirt, clutter, liquids and physics: photographs of the marks and the things
    /// left about, then each kind of accident made to happen and checked (a plate dropped breaks into pieces and a
    /// sweeping job; a glass knocked off the nightstand leaves a puddle; a kettle poured on the floor shows its stream
    /// and pools; the kitchen table bumped rattles what's on it; wet and muddy soles leave prints; spills wipe up).
    /// </summary>
    public partial class GameDirector
    {
        /// <summary>Studio runs do the mess pass instead of the chores pass.</summary>
        public static bool StudioMess = true;

        Movable FindLoose(string name, string room)
        {
            foreach (var m in house.movables)
            {
                if (m == null || m.name != name) continue;
                var r = house.RoomAt(m.transform.position);
                if (room == null || (r != null && r.name == room)) return m;
            }
            return null;
        }

        IEnumerator MessStills(StringBuilder summary)
        {
            var h = house;
            int before = shots.Count;
            clock.JumpTo(10f * 60f);
            h.SetHour(10f, day);
            foreach (var r in h.rooms) r.lit = true;
            yield return Settle(3f);
            Log("mess stills: " + h.clutterCount + " things left about, " + h.dirtMarks + " marks, " + h.dust.Count + " dusty surfaces, " + Physical.All.Count + " physical things");
            if (h.clutterCount < 25) Problem("clutter: only " + h.clutterCount + " things placed");
            if (h.dirtMarks < 40) Problem("dirt: only " + h.dirtMarks + " marks found a surface");
            if (h.dust.Count < 4) Problem("dust: only " + h.dust.Count + " dusty surfaces");

            // ---- the dirt and the clutter, room by room
            yield return Pose(R("kitchen", -5.4f, -4.6f), R("kitchen", -7.3f, 1.0f, -5.0f), 12f, "kitchen: splashback, worktop rings, glass by the sink");
            yield return Pose(R("kitchen", -4.0f, -2.7f), R("kitchen", -4.0f, 0.75f, -4.4f), 30f, "kitchen table: milk, paper, rings");
            yield return Pose(R("kitchen", -3.6f, -2.4f), R("kitchen", -1.6f, 1.1f, -1.2f), 10f, "kitchen: grease behind the heater");
            yield return Pose(R("kitchen", -5.8f, -5.6f), R("kitchen", -7.4f, 2.7f, -7.9f), -30f, "kitchen: cobweb in the corner");
            yield return Pose(R("kitchen", -5.6f, -6.0f), R("kitchen", -7.1f, 0.4f, -7.2f), 25f, "kitchen: broom, mop, dustpan, bleach");
            yield return Pose(R("living room", 4.7f, -4.0f), R("living room", 7.0f, 0.9f, -4.0f), 8f, "living room: soot over the fire, ash on the hearth");
            yield return Pose(R("living room", 6.0f, -2.9f), R("living room", 7.1f, 1.3f, -4.3f), 12f, "living room: dust and a cold cup on the mantel");
            yield return Pose(R("living room", 4.4f, -6.5f), R("living room", 5.6f, 0f, -5.6f), 38f, "living room: his slippers, sherry glass, the stain by his chair");
            yield return Pose(R("living room", 3.4f, -3.0f), R("living room", 5.0f, 0.46f, -4.1f), 35f, "living room: coffee table rings");
            yield return Pose(R("study", -4.9f, 2.9f), R("study", -6.2f, 0.8f, 4.0f), 28f, "study desk: cold tea, whisky, pages, rings");
            yield return Pose(R("study", -4.2f, 4.3f), R("study", -5.6f, 0f, 5.5f), 40f, "study: screwed-up pages round the bin");
            yield return Pose(R("study", -5.0f, 5.4f), R("study", -7.3f, 0.4f, 7.6f), 18f, "study: damp corner, books on the floor");
            yield return Pose(R("study", -5.0f, 3.5f), R("study", -6.3f, 2.8f, 4.0f), -55f, "study: nicotine on the ceiling");
            var study = h.Find("study");
            if (study != null && study.lightSwitch != null)
                yield return Pose(R("study", -2.6f, 5.3f), study.lightSwitch.transform.position, 4f, "study: hands round the light switch");
            yield return Pose(R("bedroom", 5.2f, 3.4f), R("bedroom", 5.2f, 2.8f, 5.2f), -62f, "bedroom: the ceiling stain");
            yield return Pose(R("bedroom", 4.4f, 5.9f), R("bedroom", 5.3f, 0.6f, 6.9f), 30f, "bedroom: nightstand, glass of water");
            yield return Pose(R("bedroom", 4.6f, 1.9f), R("bedroom", 3.55f, 0.9f, 0.62f), 25f, "bedroom: dust on the dresser");
            if (h.bath.door != null) h.bath.door.Snap(true);
            yield return Pose(R("bedroom", 5.5f, 2.2f), R("bedroom", 7.3f, 1.4f, 0.4f), 2f, "bathroom: mildew over the bath");
            yield return Pose(R("bedroom", 6.3f, 2.0f), R("bedroom", 5.2f, 0.8f, 1.5f), 35f, "bathroom: basin, soap, rust under the tap");
            yield return Pose(new Vector3(0f, 0f, 5.4f), new Vector3(0f, 0f, 7.4f), 38f, "hall: mud inside the front door, shoes");
            yield return Pose(new Vector3(0f, 0f, -5.0f), new Vector3(-1.4f, 0.12f, -6.5f), 30f, "hall: scuffed skirting");

            // ---- a plate dropped on the kitchen floor
            int brokenBefore = CountNamed("Broken ");
            var plate = Build.Prim(PrimitiveType.Cylinder, "Test plate", h.transform, h.transform.InverseTransformPoint(R("kitchen", -4.6f, 1.3f, -2.4f)), new Vector3(0.23f, 0.007f, 0.23f), Build.Mat(new Color(0.92f, 0.9f, 0.84f), 0f, 0.6f), true);
            var pm = Movable.Attach(plate, 0.35f);
            h.movables.Add(pm);
            pm.Launch(Vector3.down * 3.5f, Random.insideUnitSphere * 3f);
            yield return Settle(1.6f);
            int broke = CountNamed("Broken ") - brokenBefore;
            if (pm != null && broke == 0) Problem("physics: a plate dropped from 1.3 m at 3.5 m/s didn't break");
            Log("mess stills: plate drop: " + (broke > 0 ? "broke" : "didn't break"));
            yield return Pose(R("kitchen", -4.6f, -0.9f), R("kitchen", -4.6f, 0f, -2.4f), 45f, "a plate dropped: the pieces");
            var sweep = chores.today.Find(c => c.mess && c.text.Contains("broken"));
            if (broke > 0 && sweep == null) Problem("physics: the broken plate didn't go on the list");

            // ---- the glass of water knocked off the nightstand
            int puddles = Puddle.All.Count;
            var glass = FindLoose("Glass of water", "bedroom");
            if (glass == null) Problem("liquids: no glass of water by the bed");
            else
            {
                glass.Wake();
                // A hand catching it near the top as it reaches past: it goes over and off the edge.
                Vector3 push = (R("bedroom", 5.3f, 6.2f) - R("bedroom", 5.3f, 6.9f)).normalized;
                glass.Body.AddForceAtPosition(push * 0.22f, glass.transform.position + Vector3.up * 0.05f, ForceMode.Impulse);
                yield return Settle(2.5f);
                Vector3 at = glass != null ? glass.transform.position : R("bedroom", 5.4f, 0f, 6.5f);
                Log("mess stills: glass knocked over: " + (glass == null ? "broke" : "on its side, tilt " + Vector3.Angle(glass.transform.up, Vector3.up).ToString("0")) + "; puddles " + puddles + " -> " + Puddle.All.Count);
                if (Puddle.All.Count <= puddles) Problem("liquids: knocking the glass over made no puddle");
                yield return Pose(Vis.Level(at) + (Vis.Level(R("bedroom", 4.4f, 5.6f)) - Vis.Level(at)).normalized * 1.3f, at, 48f, "the glass knocked off the nightstand: water on the floor");
            }

            // ---- the milk knocked over on the kitchen table
            var milk = FindLoose("Milk bottle", "kitchen");
            if (milk == null) Problem("liquids: no milk bottle on the table");
            else
            {
                var inMilk = milk.GetComponent<Contents>();
                Log("mess stills: milk bottle at " + milk.transform.position.ToString("F2") + ", kinematic " + milk.Body.isKinematic + ", holds " + (inMilk != null ? inMilk.litres.ToString("0.00") + " l" : "nothing"));
                milk.Wake();
                milk.Body.MoveRotation(Quaternion.Euler(0f, 0f, 92f) * milk.transform.rotation);
                milk.transform.rotation = Quaternion.Euler(0f, 0f, 92f) * milk.transform.rotation;
                yield return Settle(1.5f);
                if (milk == null) Log("mess stills: the milk bottle rolled off the table and broke (its spill still counts)");
                else Log("mess stills: milk bottle after: tilt " + Vector3.Angle(milk.transform.up, Vector3.up).ToString("0") + ", holds " + (inMilk != null ? inMilk.litres.ToString("0.00") : "-") + ", at " + milk.transform.position.ToString("F2"));
                bool spilt = false;
                foreach (var p in Puddle.All) if (p != null && p.kind == "milk") spilt = true;
                if (!spilt) Problem("liquids: the milk bottle on its side didn't spill");
                yield return Pose(R("kitchen", -4.0f, -2.7f), R("kitchen", -4.1f, 0.75f, -4.2f), 30f, "milk spilt on the kitchen table");
            }

            // ---- the kettle poured out on the floor: the stream, then the pool
            Vector3 spout = R("kitchen", -3.6f, 0.95f, -5.6f);
            Puddle poured = null;
            float acc = 0f;
            player.Teleport(R("kitchen", -3.6f, -4.1f), 180f);
            player.LookPitch(28f);
            if (Look.I != null) Look.I.Cut();
            for (float t = 0f; t < 1.6f; t += Time.deltaTime)
            {
                Liquid.Stream(spout, "water");
                acc += Time.deltaTime * 0.25f;
                if (acc > 0.03f) { poured = Liquid.Spill(spout, acc, "water") ?? poured; acc = 0f; }
                yield return null;
            }
            Liquid.Stream(spout, "water");
            Shot("pouring the kettle out on the floor: the stream");
            yield return null;
            if (poured == null) Problem("liquids: pouring onto the floor made no puddle");
            yield return Pose(R("kitchen", -3.6f, -4.3f), R("kitchen", -3.6f, 0f, -5.6f), 50f, "the poured-out kettle: a puddle on the tiles");

            // ---- wet soles: walk through it and on toward the door
            if (poured != null)
            {
                Vector3 p0 = poured.transform.position;
                Vector3 toDoor = (R("kitchen", -1.6f, 0f, -4f) - p0); toDoor.y = 0f;
                for (int i = 0; i < 9; i++) Liquid.Step(p0 + toDoor.normalized * (i * 0.6f), toDoor.normalized, false, false);
                yield return Pose(p0 + toDoor * 0.85f + Vector3.Cross(Vector3.up, toDoor.normalized) * 0.6f, p0 + toDoor * 0.45f, 45f, "wet footprints from the puddle");
            }

            // ---- mud: in from the grass, across the hall
            Liquid.Soles("mud", 9);       // as if in off the wet grass
            for (int i = 0; i < 9; i++) Liquid.Step(new Vector3(0.1f * (i % 2), 0f, 7.0f - i * 0.62f), Vector3.back, false, false);
            if (chores.today.Find(c => c.id == "mudprints") == null) Problem("liquids: muddy prints in the hall didn't go on the list");
            Log("mess stills: " + Liquid.prints + " footprints made");
            yield return Pose(new Vector3(0.6f, 0f, 3.2f), new Vector3(0f, 0f, 5.6f), 50f, "muddy footprints in from the garden");

            // ---- the kitchen table bumped: what's on it rattles
            // The table as it is this run (it may be a swapped piece, lower or smaller than the drawing's).
            Bounds tb = new Bounds(R("kitchen", -4f, 0.4f, -4.4f), new Vector3(1f, 0.8f, 1f));
            foreach (var f in h.furniture) if (f != null && f.name == "Kitchen table" && f.GetComponent<Collider>() != null) tb = f.GetComponent<Collider>().bounds;
            var table = new List<Movable>();
            var was = new List<Vector3>();
            foreach (var m in h.movables)
                if (m != null && !m.Heavy && tb.Contains(new Vector3(m.transform.position.x, tb.center.y, m.transform.position.z)) && m.transform.position.y > tb.max.y - 0.05f) { table.Add(m); was.Add(m.transform.position); }
            foreach (var m in h.movables)
                if (m != null && (Vis.Level(m.transform.position) - Vis.Level(R("kitchen", -4f, -4.4f))).magnitude < 1.2f)
                    Log("mess stills: near the kitchen table: " + m.name + " at " + m.transform.position.ToString("F2") + (m.Body != null && m.Body.isKinematic ? " (resting)" : ""));
            Log("mess stills: kitchen table centre " + R("kitchen", -4f, -4.4f).ToString("F2"));
            int shook = Physical.Jostle(tb.center, Mathf.Max(tb.extents.x, tb.extents.z) + 0.1f, 0.6f, tb.extents.y - 0.06f);
            yield return Settle(1.5f);
            float moved = 0f;
            for (int i = 0; i < table.Count; i++) if (table[i] != null) moved = Mathf.Max(moved, (table[i].transform.position - was[i]).magnitude);
            Log("mess stills: bumped the kitchen table: " + shook + " things shook, the most moved " + (moved * 100f).ToString("0") + " cm");
            if (shook == 0 || moved < 0.005f) Problem("physics: bumping the kitchen table moved nothing on it");
            yield return Pose(R("kitchen", -4.0f, -2.7f), R("kitchen", -4.0f, 0.75f, -4.4f), 30f, "kitchen table after a bump");

            // ---- wiping up: the cloth on a puddle, and the dust off the mantel
            if (poured != null)
            {
                var wipe = poured.GetComponent<Work>();
                if (wipe == null || !wipe.Takes("rag")) Problem("liquids: the puddle can't be wiped with the cloth");
                else { wipe.Add(wipe.need); if (!poured.Gone) Problem("liquids: wiping the puddle didn't clear it"); }
            }
            if (h.dust.Count > 0)
            {
                h.dust[0].Add(h.dust[0].need * 0.5f);
                yield return Pose(R("living room", 6.0f, -2.9f), R("living room", 7.1f, 1.3f, -4.3f), 12f, "the mantel half dusted");
                foreach (var w in h.dust) if (w != null) w.Add(w.need);
                if (!h.Dusted) Problem("dust: wiping every surface didn't finish the dusting");
            }

            // ---- the ceiling stain on a later day
            h.DirtDay(6);
            yield return Pose(R("bedroom", 5.2f, 3.4f), R("bedroom", 5.2f, 2.8f, 5.2f), -62f, "bedroom ceiling stain, day 6");

            foreach (var c in chores.today) if (c.mess) Log("mess stills: on the list: " + c.text);
            summary.Append("  \"mess_shots\": ").Append(shots.Count - before).Append(",\n");
            Log("mess stills: " + (shots.Count - before) + " pictures");
        }

        static int CountNamed(string prefix)
        {
            int n = 0;
            foreach (var g in Object.FindObjectsOfType<Work>()) if (g.name.StartsWith(prefix) && g.gameObject.activeInHierarchy) n++;
            return n;
        }

        // A point in a room's drawing, at a height.
        Vector3 R(string room, float x, float y, float z) { return house.InRoom(room, new Vector3(x, 0f, z)) + Vector3.up * y; }
    }
}
