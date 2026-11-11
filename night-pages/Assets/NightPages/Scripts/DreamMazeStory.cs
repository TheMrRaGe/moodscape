using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The dream tells the run's story (the Tale) a night at a time, in rooms built for it.
    ///
    /// The first nights, a MEMORY: the room the house remembers them in (their kitchen, their bedroom, his study, the
    /// hall where the dog slept), as it was when they were alive, lamp on, their things out, a chair pulled back as
    /// if they'd just got up; and their walk, somewhere just out of sight, going round and round it.
    ///
    /// From the middle of the week, WHAT HAPPENED: the place it happened, the way a dream keeps a place. A sickroom
    /// with the monitor still beeping and a chair in the corner facing the bed. The cellar stairs going up into the
    /// dark with a slipper on the fourth step and a shape lying at the bottom. The woods, indoors. A patch of new
    /// concrete with a spade beside it. A pond frozen over the floor. A kitchen black with smoke. A nest in the wall.
    ///
    /// And WHY they stay: two places set at a table with one chair pulled out for you; a chair facing the doorway
    /// with a lamp left on beside it; the floor chalked with crosses; a typewriter typing on its own; plates licked
    /// clean in a row; the same few seconds, played over and over by figures that don't see you.
    ///
    /// Each story room has its page, in the dreamer's own words, so the room and the words arrive together. Story
    /// rooms are never rearranged while the maze shifts round them.
    /// </summary>
    public partial class DreamMaze
    {
        public int storyRooms;

        void PlaceStoryRooms()
        {
            var tale = g.tale;
            if (tale == null || tale.who == null) return;
            var beats = new List<string>();
            if (day <= 2) beats.Add("memory");
            else if (day <= 4) { beats.Add("event"); if (rng.Next(2) == 0) beats.Add("memory"); }
            else { beats.Add("event"); beats.Add("why"); }
            var rooms = new List<Cell>();
            foreach (var c in cells)
                if (c != exitCell && c != lastCell && c.shape != 6 && Distance(lastCell, c) >= 2) rooms.Add(c);
            foreach (var beat in beats)
            {
                if (rooms.Count == 0) break;
                var c = rooms[rng.Next(rooms.Count)];
                rooms.Remove(c);
                string what = StoryRoom(c, beat);
                if (what == null) continue;
                storyRooms++;
                g.Log("dream maze: a story room (" + beat + "): " + what);
            }
        }

        // Clear a room for a set piece: its contents go, its hiding places stay out (the story room is the story).
        void Clear(Cell c)
        {
            if (c.contents != null) Object.Destroy(c.contents.gameObject);
            c.contents = new GameObject("Contents").transform;
            c.contents.SetParent(c.root, false);
            c.hides.Clear();
            c.kind = 1;
        }

        string StoryRoom(Cell c, string beat)
        {
            var tale = g.tale;
            Clear(c);
            c.lit = true;
            c.lamp = Build.Lamp("Room light", c.contents, new Vector3(0f, c.height - 0.4f, 0f), new Color(1f, 0.82f, 0.6f), 2.2f, 9.5f);
            Build.Prim(PrimitiveType.Sphere, "Bulb", c.contents, new Vector3(0f, c.height - 0.12f, 0f), new Vector3(0.09f, 0.11f, 0.09f), Build.Mat(new Color(1f, 0.85f, 0.6f), 1.2f), false);
            string text = null, what = null;
            switch (beat)
            {
                case "memory":
                {
                    string room = tale.Var("room", "kitchen");
                    string purpose = room == "kitchen" ? "kitchen" : room == "study" ? "study" : room == "bedroom" ? "bedroom" : room == "cellar" ? "storeroom" : "parlour";
                    Dress(c, 0, purpose, "left");
                    Keepsake(c);
                    // Their walk, round and round the room, always just behind the walls.
                    var walk = new GameObject("Their walk").AddComponent<StoryWalk>();
                    walk.transform.SetParent(c.contents, false);
                    walk.style = tale.Var("walk", "shoes");
                    walk.radius = Pitch / 2f + 0.6f;
                    text = tale.who.lines.Length > 1 ? tale.Fill(tale.who.lines[1]) : null;
                    what = "the " + room + " as it was, when " + tale.Var("name", "they") + " was alive";
                    break;
                }
                case "event":
                    what = Event(c, tale.what != null ? tale.what.id : "nothing");
                    text = tale.what != null && tale.what.lines.Length > 1 ? tale.Fill(tale.what.lines[1]) : null;
                    break;
                case "why":
                    what = Why(c, tale.why != null ? tale.why.id : "waiting");
                    text = tale.why != null && tale.why.lines.Length > 1 ? tale.Fill(tale.why.lines[1]) : null;
                    break;
            }
            c.dressed = "story: " + beat;
            if (!string.IsNullOrEmpty(text)) PutPage(c, text);
            return what;
        }

        // Something of theirs, on a surface or at the foot of a wall.
        void Keepsake(Cell c)
        {
            var props = g.tale.Props();
            if (props.Count == 0) return;
            string id = props[rng.Next(props.Count)];
            Vector3 s;
            if (!ModelSizes.All.TryGetValue(id, out s)) return;
            GameObject hit;
            Vector3 at = new Vector3(R(-1.2f, 1.2f), 0f, R(-1.2f, 1.2f));
            if (Blocked(c, at)) return;
            Models.Place(id, c.contents, at, s.y, R(0f, 360f), out hit, s.y > 0.5f, Mathf.Max(s.x, s.z) * 1.2f);
        }

        GameObject Model(Cell c, string id, Vector3 at, float yaw, float height = 0f, bool solid = true)
        {
            if (height <= 0f) height = ModelSizes.Height(id, 0.8f);
            GameObject hit;
            return Models.Place(id, c.contents, at, height, yaw, out hit, solid, 2.4f);
        }

        // ---------------------------------------------------------------- what happened

        string Event(Cell c, string id)
        {
            var t = c.contents;
            switch (id)
            {
                case "longdeath":
                case "fever":
                {
                    Dress(c, 0, "sickroom", "left");
                    // The monitor, still keeping time; a chair in the corner, turned to face the bed.
                    var beep = Sfx.Loop("beep", t, 0.35f, true);
                    beep.transform.localPosition = new Vector3(0f, 1f, 0f);
                    beep.maxDistance = 14f;
                    Vector3 corner = Corner(rng.Next(4), 3.2f);
                    if (!Blocked(c, corner)) Model(c, "painted_wooden_chair_01", corner, Models.Face(-corner.x, -corner.z), 0.95f);
                    return id == "fever" ? "the sickroom, a cold cloth folded on the chair, the monitor still going" : "the back bedroom, the monitor still beeping, a chair in the corner facing the bed";
                }
                case "stairsfall":
                case "quarrel":
                {
                    // The stairs, going up into the dark through the ceiling. Something at the bottom.
                    var wood = M(new Color(0.24f, 0.16f, 0.1f), 0.2f);
                    int steps = Mathf.FloorToInt(c.height / 0.19f) + 1;
                    for (int i = 0; i < steps; i++)
                        Build.Box("Stair", t, new Vector3(-0.6f, 0.095f + i * 0.19f, -2.6f + i * 0.26f), new Vector3(1.1f, 0.19f, 0.28f), wood, true);
                    Build.Box("Dark at the top", t, new Vector3(-0.6f, c.height - 0.02f, -2.6f + steps * 0.26f - 0.6f), new Vector3(1.2f, 0.05f, 1.6f), M(new Color(0.005f, 0.005f, 0.005f)), false);
                    // Fourth step from the top: an X, scratched in, and a slipper.
                    int fourth = Mathf.Max(0, steps - 4);
                    Vector3 s4 = new Vector3(-0.6f, 0.19f * (fourth + 1) + 0.002f, -2.6f + fourth * 0.26f);
                    for (int k = -1; k <= 1; k += 2) Build.Box("X", t, s4, new Vector3(0.32f, 0.004f, 0.025f), M(new Color(0.5f, 0.08f, 0.06f)), false).transform.localRotation = Quaternion.Euler(0f, k * 40f, 0f);
                    Build.Box("Slipper", t, s4 + new Vector3(0.3f, 0.03f, 0.02f), new Vector3(0.1f, 0.06f, 0.26f), M(new Color(0.55f, 0.4f, 0.45f)), false).transform.localRotation = Quaternion.Euler(0f, 30f, 8f);
                    // At the bottom, lying where it landed, turned away from you.
                    var f = Build.Figure("At the bottom of the stairs", t);
                    f.localPosition = new Vector3(-0.3f, 0.12f, -3.2f);
                    f.localRotation = Quaternion.Euler(0f, 70f, 0f) * Quaternion.Euler(-90f, 0f, 0f);
                    if (id == "quarrel") for (int i = 0; i < 6; i++) Sheet(t, new Vector3(R(-1.5f, 1.5f), 0.003f, R(-2.5f, 0f)), R(0f, 360f));
                    return "the stairs going up into the dark, a slipper on the fourth step from the top, and something at the bottom";
                }
                case "walkedout":
                case "road":
                {
                    // The woods, indoors: trunks through the floor, roots, ferns, cold light, a torch on the ground still on.
                    string[] trees = { "dead_tree_trunk", "dead_tree_trunk_02", "pine_roots", "fern_02", "dry_branches_medium_01", "tree_stump_01" };
                    for (int i = 0; i < 9; i++)
                    {
                        Vector3 at = new Vector3(R(-3.4f, 3.4f), 0f, R(-3.4f, 3.4f));
                        if (Blocked(c, at) || (Mathf.Abs(at.x) < 0.9f || Mathf.Abs(at.z) < 0.9f)) continue;
                        string id2 = trees[rng.Next(trees.Length)];
                        Model(c, id2, at, R(0f, 360f), id2.StartsWith("dead_tree") ? c.height * 1.05f : 0f);
                    }
                    Build.Box("Leaf litter", t, new Vector3(0f, 0.006f, 0f), new Vector3(Pitch - 0.3f, 0.01f, Pitch - 0.3f), M(new Color(0.16f, 0.12f, 0.08f)), false);
                    c.lamp.color = new Color(0.6f, 0.7f, 0.95f);
                    c.lamp.intensity = 1.2f;
                    var torch = Model(c, "vintage_flashlight", new Vector3(1.2f, 0f, -1.4f), 30f, 0.08f, false);
                    var beam = Build.Lamp("Torch beam", t, new Vector3(1.2f, 0.08f, -1.4f), new Color(1f, 0.92f, 0.75f), 2.5f, 7f);
                    beam.type = LightType.Spot; beam.spotAngle = 40f; beam.transform.localRotation = Quaternion.Euler(4f, 30f, 0f);
                    if (id == "road")
                    {
                        // Headlights through the trees, pointing at you.
                        for (int k = -1; k <= 1; k += 2)
                        {
                            var hl = Build.Lamp("Headlight", t, new Vector3(k * 0.7f, 0.7f, 3.6f), new Color(1f, 0.95f, 0.8f), 3f, 12f);
                            hl.type = LightType.Spot; hl.spotAngle = 35f; hl.transform.localRotation = Quaternion.Euler(3f, 180f, 0f);
                        }
                        return "the woods, indoors, and a car's headlights through the trees, pointing at you";
                    }
                    return "the woods, indoors, and a torch on the ground, still on";
                }
                case "underfloor":
                {
                    Dress(c, 0, "storeroom", "derelict");
                    Build.Box("Newer concrete", t, new Vector3(0.4f, 0.012f, -0.6f), new Vector3(2.2f, 0.024f, 1.6f), M(new Color(0.58f, 0.58f, 0.56f), 0.3f), false);
                    Model(c, "rusted_spade_01", new Vector3(1.8f, 0f, -0.2f), 20f, 1.1f);
                    for (int i = 0; i < 3; i++) Build.Box("Cement bag", t, new Vector3(-1.2f + i * 0.08f, 0.09f + i * 0.17f, 1.3f), new Vector3(0.45f, 0.16f, 0.7f), M(new Color(0.55f, 0.5f, 0.42f)), true);
                    Build.Box("A shoe, at the edge", t, new Vector3(1.48f, 0.04f, -1.35f), new Vector3(0.1f, 0.08f, 0.26f), M(new Color(0.12f, 0.08f, 0.06f), 0.4f), false).transform.localRotation = Quaternion.Euler(0f, -20f, 0f);
                    return "a patch of new concrete, the spade beside it, and a shoe at the edge of it";
                }
                case "kept":
                {
                    Dress(c, 0, "parlour", "kept");
                    // Bread and milk on the floor, a blanket folded beside them; chairs turned to face a blank wall.
                    Build.Prim(PrimitiveType.Cylinder, "Bowl of milk", t, new Vector3(0f, 0.04f, 1.6f), new Vector3(0.22f, 0.04f, 0.22f), M(new Color(0.9f, 0.88f, 0.82f), 0.5f), false);
                    Build.Box("Bread", t, new Vector3(0.3f, 0.05f, 1.6f), new Vector3(0.22f, 0.1f, 0.12f), M(new Color(0.6f, 0.42f, 0.22f)), false);
                    Build.Box("Folded blanket", t, new Vector3(-0.4f, 0.06f, 1.7f), new Vector3(0.5f, 0.12f, 0.4f), M(new Color(0.4f, 0.2f, 0.18f)), false);
                    return "the good blanket and a bowl of milk put out on the floor, for something";
                }
                case "inwalls":
                {
                    Dress(c, 0, "storeroom", "hoard");
                    // A hatch open low in a wall, a nest in front of it, the boy's photographs pinned up around it.
                    Vector3 at, inward; int w;
                    if (WallPoint(new Plan { c = c }, 0.6f, out at, out inward, out w))
                    {
                        Build.Box("Hatch", t, at + inward * 0.01f + Vector3.up * 0.4f, Rotated(w, 0.6f, 0.6f, 0.02f), M(new Color(0.01f, 0.01f, 0.01f)), false);
                        for (int k = 0; k < 4; k++)
                            Build.Box("Blanket", t, at + inward * (0.6f + k * 0.1f) + Vector3.up * (0.05f + k * 0.05f), new Vector3(0.9f, 0.08f, 0.7f), M(new Color(0.3f, 0.26f, 0.22f)), false).transform.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                        for (int k = 0; k < 8; k++) Can(t, at + inward * R(0.4f, 1.6f) + AlongDir(w) * R(-1f, 1f), rng.Next(2) == 0);
                        for (int k = 0; k < 5; k++)
                        {
                            var tex = Painted.Pick("photos", rng);
                            var pic = Painted.Picture(tex, new Color(0.85f, 0.85f, 0.82f));
                            if (pic == null) continue;
                            Build.Box("Your photograph", t, at + inward * 0.012f + Vector3.up * R(1.0f, 1.6f) + AlongDir(w) * R(-0.9f, 0.9f), Rotated(w, 0.16f, 0.2f, 0.004f), pic, false);
                        }
                    }
                    return "a hatch in the wall, a nest of blankets and empty tins in front of it, your school photographs pinned up round it";
                }
                case "fire":
                {
                    Dress(c, 0, "kitchen", "derelict");
                    // Everything black: the walls smoked, the floor ash, embers glowing.
                    foreach (var r in t.GetComponentsInChildren<Renderer>()) { var m = r.material; if (m.HasProperty("_Color")) m.color *= 0.18f; }
                    Build.Box("Ash", t, new Vector3(0f, 0.008f, 0f), new Vector3(Pitch - 0.3f, 0.012f, Pitch - 0.3f), M(new Color(0.08f, 0.075f, 0.07f)), false);
                    var ember = Build.Lamp("Embers", t, new Vector3(0f, 0.3f, 0f), new Color(1f, 0.35f, 0.1f), 1.6f, 5f);
                    c.lamp.intensity = 0.4f;
                    return "a kitchen gone black with smoke, the embers still glowing";
                }
                case "drowned":
                case "ice":
                {
                    if (id == "ice")
                    {
                        Build.Box("Ice", t, new Vector3(0f, 0.04f, 0f), new Vector3(Pitch - 0.25f, 0.06f, Pitch - 0.25f), M(new Color(0.55f, 0.65f, 0.72f), 0.95f), true);
                        // The hole, with black water in it.
                        Build.Prim(PrimitiveType.Cylinder, "The hole in the ice", t, new Vector3(R(-1f, 1f), 0.072f, R(-1f, 1f)), new Vector3(1.1f, 0.002f, 0.9f), M(new Color(0.005f, 0.01f, 0.012f), 0.9f), false);
                        c.lamp.color = new Color(0.7f, 0.8f, 1f);
                        return "the pond, frozen over the floor, and the hole in it";
                    }
                    Build.Box("Water", t, new Vector3(0f, 0.15f, 0f), new Vector3(Pitch - 0.25f, 0.02f, Pitch - 0.25f), M(new Color(0.01f, 0.018f, 0.02f), 0.75f), false);
                    // Wet prints going in, none coming out.
                    for (int i = 0; i < 6; i++) Build.Prim(PrimitiveType.Sphere, "Wet footprint", t, new Vector3((i % 2 == 0 ? 0.11f : -0.11f), 0.165f, -3.4f + i * 0.6f), new Vector3(0.1f, 0.003f, 0.25f), M(new Color(0.05f, 0.04f, 0.035f), 0.9f), false);
                    return "the floor under black water, and footprints going into it";
                }
                case "lockedin":
                {
                    Dress(c, 0, "storeroom", "derelict");
                    // Scratches low on a wall, a name carved under them.
                    Vector3 at, inward; int w;
                    if (WallPoint(new Plan { c = c }, 0.5f, out at, out inward, out w))
                        for (int k = 0; k < 14; k++)
                        {
                            var sc = Build.Box("Scratch", t, at + inward * 0.006f + Vector3.up * R(0.15f, 0.8f) + AlongDir(w) * R(-0.4f, 0.4f), Rotated(w, 0.008f, R(0.15f, 0.4f), 0.004f), M(new Color(0.75f, 0.7f, 0.62f)), false);
                            sc.transform.localRotation = Quaternion.AngleAxis(R(-12f, 12f), inward);
                        }
                    return "a room with scratches low on the wall, as high as someone sitting";
                }
                default:
                {
                    // Nothing happened: an ordinary room, and someone sitting in it, facing the wall, waiting.
                    Dress(c, 0, "parlour", "kept");
                    Vector3 corner = Corner(rng.Next(4), 3.0f);
                    if (!Blocked(c, corner))
                    {
                        Model(c, "painted_wooden_chair_02", corner, Models.Face(corner.x, corner.z), 0.95f);
                        var f = Build.Figure("Sitting, facing the wall", t);
                        f.localPosition = corner + new Vector3(0f, -0.4f, 0f);
                        f.localRotation = Quaternion.LookRotation(new Vector3(corner.x, 0f, corner.z));
                    }
                    return "an ordinary room, and someone sitting in the corner, facing the wall";
                }
            }
        }

        // ---------------------------------------------------------------- why they stay

        string Why(Cell c, string id)
        {
            var t = c.contents;
            switch (id)
            {
                case "notknow":
                {
                    Dress(c, 0, "dining", "left");
                    Model(c, "tea_set_01", new Vector3(0f, 0f, 0f), 0f, 0f, false);
                    return "two places set at the table, and a chair pulled out for you";
                }
                case "waiting":
                {
                    // A chair facing a doorway, a lamp on beside it, a coat over the back.
                    Vector3 seat = new Vector3(0f, 0f, -1.6f);
                    Model(c, "ArmChair_01", seat, Models.Face(0f, 1f), 0.95f);
                    var lamp = Build.Lamp("The lamp left on", t, seat + new Vector3(0.9f, 1.2f, 0f), new Color(1f, 0.8f, 0.55f), 1.6f, 4f);
                    Build.Prim(PrimitiveType.Cylinder, "Lamp stand", t, seat + new Vector3(0.9f, 0.55f, 0f), new Vector3(0.05f, 0.55f, 0.05f), M(new Color(0.3f, 0.25f, 0.15f)), false);
                    Build.Box("Coat", t, seat + new Vector3(0f, 0.8f, -0.35f), new Vector3(0.6f, 0.7f, 0.1f), M(new Color(0.18f, 0.2f, 0.26f)), false);
                    c.lamp.intensity = 0.6f;
                    return "an armchair facing the doorway, a lamp left on beside it, waiting up";
                }
                case "befound":
                {
                    // Crosses chalked all over the floor, and one drawn over and over.
                    for (int i = 0; i < 18; i++)
                    {
                        Vector3 at = new Vector3(R(-3.3f, 3.3f), 0.004f, R(-3.3f, 3.3f));
                        if (Blocked(c, at)) continue;
                        float s = i == 0 ? 0.9f : R(0.25f, 0.45f);
                        for (int k = -1; k <= 1; k += 2) Build.Box("Chalk", t, at, new Vector3(s, 0.003f, 0.03f), M(new Color(0.85f, 0.84f, 0.8f)), false).transform.localRotation = Quaternion.Euler(0f, k * 45f + R(-8f, 8f), 0f);
                    }
                    return "the floor chalked all over with crosses, one of them gone over and over";
                }
                case "confess":
                {
                    Dress(c, 0, "study", "left");
                    var type = Sfx.Loop("typing", t, 0.4f, true);
                    type.transform.localPosition = new Vector3(0f, 1f, 0f);
                    type.maxDistance = 12f;
                    return "a study, and the typewriter in it typing by itself";
                }
                case "hungry":
                {
                    for (int i = 0; i < 9; i++)
                        Build.Prim(PrimitiveType.Cylinder, "Plate, licked clean", t, new Vector3(-2.4f + i * 0.6f, 0.008f, 2.6f), new Vector3(0.24f, 0.006f, 0.24f), M(new Color(0.85f, 0.84f, 0.8f), 0.7f), false);
                    var scratch = Sfx.Loop("scratch", t, 0.35f, true);
                    scratch.transform.localPosition = new Vector3(0f, 1f, Pitch / 2f);
                    return "plates on the floor in a row, licked clean, and something busy behind the wall";
                }
                case "mine":
                {
                    // Every chair in the house, in a ring, facing the way you came in.
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI * 2f / 8f;
                        var p = new Vector3(Mathf.Cos(a) * 2.2f, 0f, Mathf.Sin(a) * 2.2f);
                        if (Blocked(c, p)) continue;
                        Model(c, Catalogue.Any(Catalogue.Chair, rng) ?? "dining_chair_02", p, Models.Face(-p.x, -p.z), 0.95f);
                    }
                    return "chairs in a ring, all turned to face the middle, where you're standing";
                }
                case "residual":
                {
                    // The last few seconds, over and over: figures caught mid-step, all walking the same way.
                    for (int i = 0; i < 3; i++)
                    {
                        var f = Build.Figure("Walking the same route", t);
                        f.localPosition = new Vector3(-2.4f + i * 2.2f, 0f, R(-0.4f, 0.4f));
                        f.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    }
                    return "figures caught mid-step, all walking the same route, the same way";
                }
                case "guarding":
                {
                    var f = Build.Figure("Standing in front of it", t);
                    f.localPosition = new Vector3(0f, 0f, 2.6f);
                    f.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    Build.Box("A door in the floor", t, new Vector3(0f, 0.005f, 3.2f), new Vector3(1.2f, 0.01f, 0.9f), M(new Color(0.18f, 0.12f, 0.08f)), false);
                    return "someone standing in front of a door in the floor, facing you";
                }
                case "invited":
                {
                    Build.Box("The board", t, new Vector3(0f, 0.01f, 0f), new Vector3(0.6f, 0.02f, 0.4f), M(new Color(0.5f, 0.38f, 0.24f)), false);
                    Build.Prim(PrimitiveType.Cylinder, "The glass", t, new Vector3(0.05f, 0.06f, 0.02f), new Vector3(0.07f, 0.05f, 0.07f), M(new Color(0.6f, 0.65f, 0.66f), 0.95f), false);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI / 2f;
                        var p = new Vector3(Mathf.Cos(a) * 0.7f, 0f, Mathf.Sin(a) * 0.7f);
                        Build.Box("Cushion", t, p + Vector3.up * 0.05f, new Vector3(0.4f, 0.1f, 0.4f), M(new Color(0.35f, 0.15f, 0.15f)), false);
                    }
                    return "a home-made board on the floor, a glass upside down on it, four cushions round it where children sat";
                }
                default:
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var f = Build.Figure("Learning", t);
                        f.localPosition = new Vector3(-2.4f + i * 1.2f, 0f, 2.4f);
                        f.localRotation = Quaternion.Euler(0f, 180f + i * 6f, 0f);
                        f.localScale = Vector3.one * (0.7f + i * 0.08f);
                    }
                    return "figures in a row, each a little more like a person than the one before";
                }
            }
        }
    }

    /// <summary>Their walk, going round and round a room, just the other side of the walls.</summary>
    public class StoryWalk : MonoBehaviour
    {
        public string style = "shoes";
        public float radius = 4.6f;
        float angle, next;

        void Update()
        {
            if (Time.time < next) return;
            angle += 0.28f;
            Vector3 p = transform.TransformPoint(new Vector3(Mathf.Cos(angle) * radius, 0.1f, Mathf.Sin(angle) * radius));
            switch (style)
            {
                case "stick": Sfx.At("shuffle", p, 0.45f, 0.95f); next = Time.time + 1.15f; break;
                case "slippers": Sfx.At("shuffle", p, 0.3f, 1.3f); next = Time.time + 0.85f; break;
                case "heels": Sfx.At("tap", p, 0.35f, 1.35f); next = Time.time + 0.42f; break;
                case "boots": Sfx.At("step", p, 0.6f, 0.6f); next = Time.time + 0.75f; break;
                case "small": Sfx.At("step", p, 0.35f, 1.6f); next = Time.time + 0.3f; break;
                case "claws": Sfx.At("tap", p, 0.2f, 2.2f); Sfx.At("tap", p, 0.15f, 2.5f); next = Time.time + 0.35f; break;
                default: Sfx.At("step", p, 0.3f, 1.0f); next = Time.time + 0.55f; break;
            }
        }
    }
}
