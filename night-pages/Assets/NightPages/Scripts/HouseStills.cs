using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The house's working parts photographed and tried (part of the "studio" job): the kitchen sink, the kettle, the
    /// rack, the bed in a heap and made, the bathroom and its darkroom, the tape deck, the clocks, the watch, the fire,
    /// the dustbin; the light at different times of day. Each chore is also done step by step in code the way a player
    /// would (fill, boil, spoon, pour, drink; plug, run, wash, rack) and anything that doesn't finish is reported.
    /// </summary>
    public partial class GameDirector
    {
        /// <summary>Studio runs photograph the house first and stop there (the monster stills are skipped).</summary>
        public static bool StudioHouseOnly = true;

        /// <summary>
        /// Every furniture model, each told to face the camera, five to a row with its name over it: the ones that
        /// come in backwards or sideways show at once (their fix goes in Models.YawFix).
        /// </summary>
        IEnumerator ModelChart()
        {
            var ids = new List<string>
            {
                "painted_wooden_cabinet_02", "Rockingchair_01", "vintage_day_bed",
                "vintage_wooden_drawer_01", "painted_wooden_cabinet", "vintage_telephone_wall_clock",
            };
            var root = new GameObject("Model chart").transform;
            Vector3 o = new Vector3(400f, 0f, 400f);
            var floor = Build.Box("Chart floor", root, o + new Vector3(4.4f, -0.05f, 6f), new Vector3(20f, 0.1f, 30f), Build.Mat(new Color(0.45f, 0.45f, 0.45f)), true);
            Build.Box("Chart wall", root, o + new Vector3(4.4f, 2f, 1.9f), new Vector3(20f, 4f, 0.1f), Build.Mat(new Color(0.6f, 0.62f, 0.66f)), false);
            Font font = null;
            foreach (var fn in new[] { "LegacyRuntime.ttf", "Arial.ttf" }) { try { font = Resources.GetBuiltinResource<Font>(fn); } catch (System.Exception) { } if (font != null) break; }
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.47f);
            int rows = (ids.Count + 2) / 3;
            for (int row = 0; row < rows; row++)
            {
                var made = new List<GameObject>();
                Vector3 rowO = o + new Vector3(0f, 0f, 0f);
                for (int i = 0; i < 3; i++)
                {
                    int k = row * 3 + i;
                    if (k >= ids.Count) break;
                    string id = ids[k];
                    Vector3 at = rowO + new Vector3(i * 1.8f, 0f, 0f);
                    GameObject hit;
                    float h = ModelSizes.Height(id, 1f);
                    // Face the camera (which looks along +z from in front): "face along -z".
                    var g = Models.Place(id, root, at, Mathf.Min(h, 2.2f), Models.Face(0f, -1f), out hit, false, 1.9f);
                    if (g != null) made.Add(g);
                    var label = new GameObject("Label").transform;
                    label.SetParent(root, false);
                    label.position = at + new Vector3(0f, 2.45f, -0.3f);
                    var tm = label.gameObject.AddComponent<TextMesh>();
                    if (font != null) { tm.font = font; tm.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
                    tm.text = id + (g == null ? " (missing)" : "") + (Models.Fix(id) != 0f ? " fix " + Models.Fix(id) : "");
                    tm.fontSize = 48; tm.characterSize = 0.022f; tm.anchor = TextAnchor.MiddleCenter; tm.color = Color.black;
                    made.Add(label.gameObject);
                    var lamp = Build.Lamp("Chart light", root, at + new Vector3(0f, 2.6f, -1.6f), Color.white, 1.6f, 6f);
                    made.Add(lamp.gameObject);
                }
                yield return null; yield return null;
                // From in front, up a little and off to one side: seats, doors and screens show when a thing faces you.
                yield return Pose(rowO + new Vector3(1.8f, 0.3f, -3.6f), rowO + new Vector3(1.8f, 0.5f, 0f), 16f, "chart " + (row + 1) + ": " + string.Join(", ", ids.GetRange(row * 3, Mathf.Min(3, ids.Count - row * 3)).ToArray()), 0.5f);
                foreach (var m in made) if (m != null) Destroy(m);
                yield return null;
            }
            Destroy(root.gameObject);
        }

        IEnumerator HouseStills(StringBuilder summary)
        {
            yield return ModelChart();
            house.SetHour(10f, day);
            int before = shots.Count;
            var h = house;
            Log("house stills: layout " + h.Layout + " (" + h.LayoutDescription + "), job " + job.Name + ", list: " + chores.today.Count + " chores");
            foreach (var c in chores.today) Log("house stills: on the list: " + DayClock.Say(c.by) + " " + c.text);
            hud.ClearSubtitles();

            // ---- the rooms' furniture where it stands: does it face the right way?
            house.SetTime(0, day);
            foreach (var r in house.rooms) r.lit = true;
            yield return Settle(1f);
            yield return Pose(R("living room", 2.2f, -1.0f), R("living room", 6.0f, -4.5f) + Vector3.up * 0.6f, 14f, "living room: seating and the fire");
            yield return Pose(R("living room", 6.4f, -6.6f), R("living room", 4.6f, -0.5f) + Vector3.up * 0.6f, 10f, "living room: the television");
            yield return Pose(R("bedroom", 5.0f, 5.5f), R("bedroom", 2.15f, 1.2f) + Vector3.up * 1.0f, 6f, "bedroom: the wardrobe");
            yield return Pose(R("study", -3.0f, 3.5f), R("study", -4.5f, 7.6f) + Vector3.up * 1.0f, 6f, "study: the bookshelf");
            yield return Pose(R("study", -3.5f, 3.0f), R("study", -6.25f, 0.6f) + Vector3.up * 0.5f, 10f, "study: the day bed");
            yield return Pose(R("kitchen", -3.0f, -1.5f), R("kitchen", -3.6f, -4.2f) + Vector3.up * 0.5f, 18f, "kitchen: table and chairs");
            foreach (var r in house.rooms) r.lit = false;

            // ---- morning, 7:20: the kitchen as you find it
            clock.JumpTo(7f * 60f + 20f);
            h.SetHour(clock.Hour, day);
            yield return Settle(1.5f);
            yield return Pose(R("kitchen", -5.55f, -5.2f), R("kitchen", -7.1f, -5.0f) + Vector3.up * 0.9f, 32f, "kitchen sink, dishes, rack, tap (7.20 am)");
            yield return Pose(R("kitchen", -4.4f, -2.6f), R("kitchen", -7.0f, -4.6f) + Vector3.up * 0.9f, 16f, "kitchen worktop");
            yield return Pose(R("kitchen", -4.5f, -5.6f), R("kitchen", -2.6f, -0.12f) + Vector3.up * 2.1f, -12f, "kitchen clock over the door");

            // ---- coffee, step by step
            var k = h.kettle;
            k.liquid = 1.2f; k.holds = "water";
            k.mov.SetHeld(false);
            h.kettleBase.Place(k.mov);
            if (!h.kettleBase.Holds(k.mov)) Problem("coffee: the kettle didn't sit on its base");
            h.kettleSwitch.Set(true);
            yield return Settle(2f);
            if (h.kettleHeat <= 0f) Problem("coffee: the kettle isn't heating on its base");
            yield return Pose(R("kitchen", -5.8f, -3.0f), R("kitchen", -7.0f, -3.1f) + Vector3.up * 0.95f, 24f, "kettle on its base, boiling");
            h.kettleHeat = 0.995f;
            yield return Settle(0.6f);
            if (!h.kettleBoiled) Problem("coffee: the kettle never boiled");
            Interactable tin = null;
            foreach (var m in h.movables) if (m != null && m.name == "Coffee canister") tin = m.GetComponent<Interactable>();
            if (tin == null) Problem("coffee: no coffee canister"); else tin.use();
            if (!h.mugCoffee) Problem("coffee: spooning didn't put coffee in the mug");
            if (!h.mugFill.Ready) Problem("coffee: the mug won't take the water: " + (h.mugFill.why != null ? h.mugFill.why() : "?"));
            h.mugFill.Add(1f);
            h.myMug.GetComponent<Interactable>().use();
            yield return null;
            chores.Tick();
            var coffee = chores.today.Find(c => c.id == "coffee");
            Log("house stills: coffee chain " + (coffee != null && coffee.done ? "finished" : "NOT finished"));
            if (coffee != null && !coffee.done) Problem("coffee: drinking it didn't finish the chore");

            // ---- the washing-up, step by step
            h.plug.Set(true);
            h.tapKnob.Set(true);
            yield return Settle(7f);
            if (h.sink.level < 0.2f) Problem("dishes: the sink didn't fill (level " + h.sink.level.ToString("0.00") + ")");
            Spot sinkSpot = null;
            foreach (var s in Spot.All) if (s != null && s.name == "In the sink") sinkSpot = s;
            var dishes = new List<Movable>(h.dishes);
            int n = 0;
            foreach (var d in dishes)
            {
                if (d == null || n >= 2) continue;
                d.SetHeld(false);
                if (sinkSpot != null && sinkSpot.Wants(d)) { sinkSpot.Place(d); n++; }
            }
            if (n == 0) Problem("dishes: nothing would go in the sink");
            yield return Settle(0.5f);
            yield return Pose(R("kitchen", -5.7f, -5.2f), R("kitchen", -7.0f, -5.2f) + Vector3.up * 0.8f, 42f, "sink run, plates in the water");
            foreach (var d in dishes)
            {
                if (d == null) continue;
                var w = Tool.Scrub(d);
                if (w == null) continue;
                if (sinkSpot != null && !sinkSpot.Holds(d)) { if (sinkSpot.Wants(d)) sinkSpot.Place(d); else continue; }
                if (!w.Ready) { Problem("dishes: can't scrub " + d.name + ": " + (w.why != null ? w.why() : "?")); continue; }
                w.Add(w.need);
                d.SetHeld(true); d.SetHeld(false);
                if (h.rack.Wants(d)) h.rack.Place(d); else Problem("dishes: the rack won't take a clean " + d.name);
            }
            h.tapKnob.Set(false);
            yield return null;
            chores.Tick();
            var dishesChore = chores.today.Find(c => c.id == "dishes");
            Log("house stills: washing-up " + (dishesChore != null && dishesChore.done ? "finished" : "NOT finished (" + h.DishesLeft + " left)"));
            yield return Pose(R("kitchen", -5.6f, -5.6f), R("kitchen", -7.0f, -5.7f) + Vector3.up * 0.9f, 30f, "rack full, sink water");

            // ---- the bed
            h.RumpleBed();
            yield return Pose(R("bedroom", 4.3f, 3.6f), R("bedroom", 6.3f, 5.7f) + Vector3.up * 0.4f, 18f, "bed in a heap");
            h.quilt.Add(h.quilt.need);
            yield return Pose(R("bedroom", 4.3f, 3.6f), R("bedroom", 6.3f, 5.7f) + Vector3.up * 0.4f, 18f, "bed made");

            // ---- the bathroom
            h.bath.lit = true;
            yield return Settle(0.6f);
            yield return Pose(R("bedroom", 3.7f, 4.8f), R("bedroom", 5.75f, 2.7f) + Vector3.up * 1.2f, 4f, "bathroom door from the bedroom");
            if (h.bath.door != null) h.bath.door.Snap(true);
            yield return Pose(R("bedroom", 5.45f, 2.3f), R("bedroom", 7.1f, 0.8f) + Vector3.up * 0.8f, 14f, "bathroom: bath, board and trays");
            yield return Pose(R("bedroom", 6.5f, 2.0f), R("bedroom", 5.1f, 1.4f) + Vector3.up * 1.3f, 8f, "bathroom: basin and mirror");
            yield return Pose(R("bedroom", 6.4f, 2.1f), R("bedroom", 5.6f, 0.3f) + Vector3.up * 0.5f, 14f, "bathroom: toilet");
            // As a darkroom: blind down, safelight on, the white light off; a print in the developer.
            h.blind.SetActive(true);
            h.safelight.intensity = 1.6f;
            h.bath.lit = false;
            yield return Settle(0.8f);
            yield return Pose(R("bedroom", 5.5f, 2.3f), R("bedroom", 7.1f, 1.2f) + Vector3.up * 0.9f, 18f, "darkroom: safelight on");
            yield return Pose(R("bedroom", 6.2f, 1.9f), R("bedroom", 7.12f, 2.18f) + Vector3.up * 0.9f, 20f, "darkroom: the enlarger");
            h.blind.SetActive(false);
            h.safelight.intensity = 0f;

            // ---- the study: the tape deck, the clock, a light switch
            yield return Pose(R("study", -5.0f, 2.6f), R("study", -6.4f, 3.3f) + Vector3.up * 0.85f, 30f, "study: tape deck and headphones on the desk");
            yield return Pose(R("study", -4.4f, 5.6f), R("study", -5.3f, 0.1f) + Vector3.up * 2.0f, -6f, "study: the clock on the wall");
            var study = h.Find("study");
            if (study != null && study.lightSwitch != null)
                yield return Pose(R("study", -3.0f, 5.9f), study.lightSwitch.transform.position, 4f, "study: light switch by the door");
            yield return Pose(R("study", -4.8f, 4.0f), R("study", -6.3f, 4.0f) + Vector3.up * 1.0f, 18f, "study: typewriter");

            // ---- the hall: the clock's dial and key, the watch
            yield return Pose(new Vector3(-0.65f, 0f, -0.9f), h.hallClock.position + Vector3.up * 1.7f, -12f, "hall clock dial and winding key");
            h.ClockRunDown(clock.minutes - 300f);
            h.clockKey.Add(11f);
            if (!h.clockWound) Problem("clock: eleven turns didn't wind it");
            Watch.ForceRaise = true;
            yield return Pose(new Vector3(0f, 0f, 3.5f), new Vector3(0f, 0f, -6f), 5f, "your watch (Tab)", 1.2f);
            Watch.ForceRaise = false;

            // ---- the fire: logs in the grate, struck
            foreach (var l in h.logs) if (l != null && h.grate.Count < 3) { l.SetHeld(false); if (h.grate.Wants(l)) h.grate.Place(l); }
            if (h.grate.Count < 2) Problem("fire: the grate took " + h.grate.Count + " logs");
            h.matches.use();
            yield return Settle(3f);
            if (!h.fireLit) Problem("fire: striking a match didn't light it");
            yield return Pose(R("living room", 4.6f, -4.0f), R("living room", 7.0f, -4.0f) + Vector3.up * 0.5f, 10f, "fire laid and lit");

            // ---- the porch: the dustbin
            yield return Pose(new Vector3(0.9f, 0f, 10.6f), new Vector3(2.75f, 0.5f, 8.55f), 18f, "the dustbin by the front step", 1f);

            // ---- the light through the day
            foreach (var hr in new[] { 7.25f, 12f, 17.6f, 19.5f })
            {
                clock.JumpTo(hr * 60f);
                h.SetHour(hr, day);
                if (hr >= 19f) { h.Find("kitchen").lit = true; }
                yield return Settle(1.2f);
                yield return Pose(R("kitchen", -3.0f, -1.2f), R("kitchen", -7.0f, -3.4f) + Vector3.up * 1.2f, 6f, "kitchen at " + DayClock.Say(hr * 60f));
            }
            yield return Pose(new Vector3(0f, 0f, 6.6f), new Vector3(0f, 1.2f, -6f), 4f, "hall at 7.30 pm, lamps off");
            house.hall.lit = true;
            yield return Settle(1f);
            yield return Pose(new Vector3(0f, 0f, 6.6f), new Vector3(0f, 1.2f, -6f), 4f, "hall at 7.30 pm, lamp on");

            summary.Append("  \"house_shots\": ").Append(shots.Count - before).Append(",\n");
            Log("house stills: " + (shots.Count - before) + " pictures");
        }
    }
}
