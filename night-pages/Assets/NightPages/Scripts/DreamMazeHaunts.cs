using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// What the dream does to you between (and during) the hunts. It's a dream: nothing has to obey physics, so these
    /// go where the waking house can't: heights, being shut in, drowning, gravity, your own size, the room looking back.
    ///
    /// Which one happens next is the fear engine's choice. Each haunt is tagged with the kinds of fear it works on;
    /// the engine's learner draws a value for each (what it expects, from this player's record and its instinct, with
    /// its own uncertainty mixed in), avoids repeating itself, and afterwards is told how you actually reacted. So the
    /// order, the mix and the emphasis are different every night of every run, and drift toward what gets to you.
    /// While it is toying with you (it has lost you and is looking), they come faster.
    /// </summary>
    public partial class DreamMaze
    {
        class Haunt
        {
            public string id, label;
            public string[] tags;
            public System.Func<Cell, Vector3, Camera, bool> can;
            public System.Func<Cell, IEnumerator> run;
            public int used;
        }

        readonly List<Haunt> haunts = new List<Haunt>();
        bool alive = true, haunting;
        float nextHaunt;
        string lastHaunt;
        float peakReaction;
        Vector3 hPos;                 // where the dreamer is, for the running haunt
        Camera hCam;
        bool hHidden;
        /// <summary>The room bulbs are out and what you carry is guttering.</summary>
        public bool Blackout { get; private set; }
        public int hauntsFired;
        public readonly List<string> hauntLog = new List<string>();

        void AddHaunt(string id, string label, string[] tags, System.Func<Cell, Vector3, Camera, bool> can, System.Func<Cell, IEnumerator> run)
        {
            haunts.Add(new Haunt { id = id, label = label, tags = tags, can = can, run = run });
        }

        void BuildHaunts()
        {
            const string Heights = "heights", Water = "water", Dark = "dark";
            AddHaunt("pit", "the floor of the next room is gone", new[] { FearTypes.Nature, Heights }, (c, p, cam) => NextUnseen(c, cam) != null, c => Pit(NextUnseen(c, hCam)));
            AddHaunt("tall", "the ceiling goes up out of sight", new[] { FearTypes.Nature, Heights, FearTypes.Unknown }, (c, p, cam) => c.shape != 5, Tall);
            AddHaunt("squeeze", "the walls and ceiling close in", new[] { FearTypes.Trapped }, (c, p, cam) => InMiddle(c, p, 2.6f) && c.shape == 0 && !AnyMerged(c), Squeeze);
            AddHaunt("sealed", "every doorway shuts", new[] { FearTypes.Trapped, FearTypes.Hunted }, (c, p, cam) => InMiddle(c, p, 2.8f) && !AnyMerged(c), Sealed);
            AddHaunt("float", "the furniture lifts off the floor", new[] { FearTypes.Uncanny }, (c, p, cam) => Loose(c).Count >= 2, Float);
            AddHaunt("flood", "the room fills with black water", new[] { FearTypes.Nature, Water, FearTypes.Trapped }, (c, p, cam) => !hHidden, Flood);
            AddHaunt("tilt", "the house leans over", new[] { FearTypes.Self, Heights }, (c, p, cam) => true, Tilt);
            AddHaunt("lightsout", "every light goes out", new[] { FearTypes.Unknown, Dark }, (c, p, cam) => true, LightsOut);
            AddHaunt("watchers", "people who come closer when you look away", new[] { FearTypes.Uncanny, FearTypes.Hunted }, (c, p, cam) => !hHidden, Watchers);
            AddHaunt("small", "you are very small", new[] { FearTypes.Self, FearTypes.Loss }, (c, p, cam) => !hHidden, Small);
            nextHaunt = Time.time + 12f;
        }

        /// <summary>Every frame. Picks and starts the next haunt when it's time.</summary>
        public void TickHaunts(Vector3 playerPos, Camera cam, float left01, bool toying, bool hidden)
        {
            if (haunts.Count == 0) BuildHaunts();
            hPos = playerPos; hCam = cam; hHidden = hidden;
            peakReaction = Mathf.Max(peakReaction, Reactions.Level);
            if (haunting || Time.time < nextHaunt) return;
            var here = CellAt(playerPos);
            if (here == exitCell || here.held) { nextHaunt = Time.time + 3f; return; }
            Haunt best = null;
            float bestV = float.MinValue;
            var brain = g.fear != null ? g.fear.brain : null;
            foreach (var h in haunts)
            {
                if (!h.can(here, playerPos, cam)) continue;
                float v = brain != null ? brain.Draw("dreamhaunt", h.id, h.tags) : (float)rng.NextDouble();
                v -= h.used * 0.35f;                                  // not the same thing over and over in one night
                if (h.id == lastHaunt) v -= 1f;
                v += (float)rng.NextDouble() * 0.15f;
                if (v > bestV) { bestV = v; best = h; }
            }
            if (best == null) { nextHaunt = Time.time + 4f; return; }
            haunting = true;
            best.used++;
            lastHaunt = best.id;
            hauntsFired++;
            hauntLog.Add(best.id);
            g.Log("dream haunt: " + best.label + (toying ? " (it is toying with you)" : "") + " [" + string.Join(", ", best.tags) + "], drawn at " + bestV.ToString("0.00"));
            g.StartCoroutine(RunHaunt(best, here, left01, toying));
        }

        /// <summary>Start a particular haunt now, if it can happen where the dreamer is (for demos and tests). The ids are in BuildHaunts.</summary>
        public bool ForceHaunt(string id, Vector3 playerPos, Camera cam)
        {
            if (haunts.Count == 0) BuildHaunts();
            hPos = playerPos; hCam = cam;
            var here = CellAt(playerPos);
            foreach (var h in haunts)
            {
                if (h.id != id) continue;
                if (here.held || !h.can(here, playerPos, cam)) return false;
                haunting = true;
                h.used++;
                lastHaunt = id;
                hauntsFired++;
                hauntLog.Add(id);
                g.Log("dream haunt (asked for): " + h.label);
                g.StartCoroutine(RunHaunt(h, here, 1f, false));
                return true;
            }
            return false;
        }

        /// <summary>Could this haunt happen right now, where the dreamer is?</summary>
        public bool CanHaunt(string id, Vector3 playerPos, Camera cam)
        {
            if (haunts.Count == 0) BuildHaunts();
            hPos = playerPos; hCam = cam;
            var here = CellAt(playerPos);
            if (here.held || here == exitCell) return false;
            foreach (var h in haunts) if (h.id == id) return h.can(here, playerPos, cam);
            return false;
        }

        /// <summary>No haunts of its own choosing for this long.</summary>
        public void HoldHaunts(float seconds) { nextHaunt = Time.time + seconds; }

        IEnumerator RunHaunt(Haunt h, Cell here, float left01, bool toying)
        {
            float before = Reactions.Level;
            peakReaction = before;
            yield return h.run(here);
            if (!alive) yield break;
            // What it did to you goes back to the learner: this is how the next night's choices differ.
            float y = Mathf.Clamp(peakReaction - before, -1f, 1f);
            var brain = g.fear != null ? g.fear.brain : null;
            if (brain != null) g.Log("dream haunt: " + brain.Learn("dreamhaunt", h.id, h.tags, y));
            haunting = false;
            nextHaunt = Mathf.Max(nextHaunt, Time.time) + (toying ? Random.Range(4f, 8f) : Mathf.Lerp(9f, 22f, left01) + Random.Range(0f, 5f));
        }

        // ---------------------------------------------------------------- helpers

        bool InMiddle(Cell c, Vector3 p, float within) { return Vis.Level(World(Local(c.x, c.z)) - p).magnitude < within; }

        // The next room on the way out, if you can't see into it and nothing else is going on in it.
        Cell NextUnseen(Cell here, Camera cam)
        {
            var prev = Walk(exitCell);
            if (!prev.ContainsKey(here) || here == exitCell) return null;
            var next = prev[here];
            if (next == exitCell || next.held || next.page != null) return null;
            if (cam != null && Vis.Sees(cam, World(Local(next.x, next.z)) + Vector3.up * 0.1f)) return null;
            return next;
        }

        // The furniture and loose things in a room (not its hiding places or its light).
        List<Transform> Loose(Cell c)
        {
            var list = new List<Transform>();
            if (c.contents == null) return list;
            foreach (Transform t in c.contents)
            {
                string n = t.name;
                if (n == "Bulb" || n == "Room light" || n == "Locker" || n == "Cabinet" || n == "Table" || n == "Water" || n == "Painting" || n.EndsWith("collider")) continue;
                list.Add(t);
            }
            return list;
        }

        Cell Here { get { return CellAt(hPos); } }
        bool Seen(Vector3 world) { return hCam != null && Vis.Sees(hCam, world); }

        IEnumerator Wait(float seconds, System.Func<bool> stop = null)
        {
            for (float t = 0f; t < seconds && alive; t += Time.deltaTime)
            {
                if (stop != null && stop()) yield break;
                yield return null;
            }
        }

        // ---------------------------------------------------------------- heights

        // The next room has no floor: a black drop, and planks from each doorway to the middle. (You can't actually
        // fall: it's a dream. But step off the planks and you sink into nothing, slow, until you get back on.)
        IEnumerator Pit(Cell c)
        {
            if (c == null) yield break;
            c.held = true;
            var pit = new GameObject("Pit").transform;
            pit.SetParent(c.root, false);
            c.floor.enabled = false;
            if (c.contents != null) c.contents.gameObject.SetActive(false);
            var black = Build.Mat(new Color(0.012f, 0.012f, 0.015f));
            var wood = Build.Mat(new Color(0.3f, 0.22f, 0.15f), 0f, 0.15f);
            float half = Pitch / 2f - 0.05f, deep = 40f;
            // The shaft: the room's walls carry on down, getting darker.
            Build.Box("Shaft", pit, new Vector3(half, -deep / 2f, 0f), new Vector3(0.1f, deep, Pitch), black, false);
            Build.Box("Shaft", pit, new Vector3(-half, -deep / 2f, 0f), new Vector3(0.1f, deep, Pitch), black, false);
            Build.Box("Shaft", pit, new Vector3(0f, -deep / 2f, half), new Vector3(Pitch, deep, 0.1f), black, false);
            Build.Box("Shaft", pit, new Vector3(0f, -deep / 2f, -half), new Vector3(Pitch, deep, 0.1f), black, false);
            // A light a long way down, so the depth reads.
            Build.Box("Far below", pit, new Vector3(0.6f, -deep + 1f, -0.4f), new Vector3(0.5f, 0.05f, 0.5f), new Color(0.9f, 0.3f, 0.2f), false, 2f);
            var planks = new List<Vector3>();              // local directions that have a plank
            foreach (var e in edges)
            {
                if (!e.open || (e.a != c && e.b != c)) continue;
                Vector3 d = (e.mid - Local(c.x, c.z)).normalized;
                planks.Add(d);
                var p = Build.Box("Plank", pit, d * (Pitch / 4f) + Vector3.up * 0.005f, Mathf.Abs(d.x) > 0.5f ? new Vector3(Pitch / 2f + 0.2f, 0.04f, 0.9f) : new Vector3(0.9f, 0.04f, Pitch / 2f + 0.2f), wood, false);
                p.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 4f - 2f, 0f);
            }
            Build.Box("Plank", pit, new Vector3(0f, 0.005f, 0f), new Vector3(1.1f, 0.04f, 1.1f), wood, false);
            var wind = Sfx.Loop("drone", pit, 0.5f, true, 1.6f);
            bool entered = false;
            float sink = 0f;
            for (float t = 0f; t < 75f && alive; t += Time.deltaTime)
            {
                bool inside = Here == c;
                if (inside) entered = true;
                if (entered && !inside) break;
                if (!entered && t > 40f && !Seen(World(Local(c.x, c.z)))) break;
                if (inside)
                {
                    // On a plank?
                    Vector3 l = c.root.InverseTransformPoint(hPos);
                    bool on = Mathf.Abs(l.x) < 0.75f && Mathf.Abs(l.z) < 0.75f;
                    foreach (var d in planks)
                    {
                        float along = l.x * d.x + l.z * d.z, across = Mathf.Abs(l.x * d.z - l.z * d.x);
                        if (along > -0.3f && across < 0.62f) on = true;
                    }
                    sink = Mathf.MoveTowards(sink, on ? 0f : 1f, Time.deltaTime * (on ? 2.5f : 0.8f));
                    g.player.speedMul = Mathf.Lerp(1f, 0.4f, sink);
                    g.player.eyeHeight = Mathf.Lerp(1.65f, 0.95f, sink);
                    g.player.fovOffset = sink * 10f;
                    if (sink > 0.6f && !on) Look.Kick(0.15f * Time.deltaTime * 6f);
                }
                yield return null;
            }
            if (!alive) yield break;
            g.player.speedMul = 1f; g.player.eyeHeight = 1.65f; g.player.fovOffset = 0f;
            // It's an ordinary floor again the next time you look.
            yield return Wait(8f, () => !Seen(World(Local(c.x, c.z))));
            if (!alive) yield break;
            if (wind != null) Object.Destroy(wind.gameObject);
            Object.Destroy(pit.gameObject);
            c.floor.enabled = true;
            if (c.contents != null) c.contents.gameObject.SetActive(true);
            c.held = false;
        }

        // The ceiling of the room you're in isn't there: the walls go up and up into the dark.
        IEnumerator Tall(Cell c)
        {
            c.held = true;
            var up = new GameObject("Up").transform;
            up.SetParent(c.root, false);
            const float high = 46f;
            float half = Pitch / 2f - 0.05f;
            var wall = WallMat(c.theme, Mathf.Max(1, rot));
            wall = new Material(wall) { mainTextureScale = new Vector2(Pitch / 2.4f, high / 2.4f) };
            Build.Box("Up", up, new Vector3(half, H + high / 2f, 0f), new Vector3(0.1f, high, Pitch), wall, false);
            Build.Box("Up", up, new Vector3(-half, H + high / 2f, 0f), new Vector3(0.1f, high, Pitch), wall, false);
            Build.Box("Up", up, new Vector3(0f, H + high / 2f, half), new Vector3(Pitch, high, 0.1f), wall, false);
            Build.Box("Up", up, new Vector3(0f, H + high / 2f, -half), new Vector3(Pitch, high, 0.1f), wall, false);
            // Something small and lit, very far up.
            Build.Box("Far above", up, new Vector3(0f, H + high - 2f, 0f), new Vector3(0.9f, 0.05f, 0.9f), new Color(0.8f, 0.85f, 1f), false, 2.5f);
            up.gameObject.SetActive(false);
            // It goes when you aren't looking at it.
            yield return Wait(6f, () => !Seen(c.ceiling.transform.position));
            if (!alive) yield break;
            c.ceiling.SetActive(false);
            up.gameObject.SetActive(true);
            foreach (Transform t in c.contents) if (t.name == "Bulb" || t.name == "Room light") t.gameObject.SetActive(false);
            var wind = Sfx.Loop("drone", up, 0.45f, true, 1.9f);
            if (wind != null) wind.transform.localPosition = new Vector3(0f, H + 6f, 0f);
            bool looked = false;
            for (float t = 0f; t < 26f && alive && Here == c; t += Time.deltaTime)
            {
                // The moment you look up, your stomach goes.
                if (!looked && hCam != null && hCam.transform.forward.y > 0.55f) { looked = true; Look.Kick(0.5f); g.player.fovOffset = 9f; }
                g.player.fovOffset = Mathf.MoveTowards(g.player.fovOffset, 0f, Time.deltaTime * 3f);
                yield return null;
            }
            if (!alive) yield break;
            g.player.fovOffset = 0f;
            yield return Wait(10f, () => Here != c || !Seen(c.ceiling.transform.position));
            if (!alive) yield break;
            if (wind != null) Object.Destroy(wind.gameObject);
            Object.Destroy(up.gameObject);
            c.ceiling.SetActive(true);
            if (c.contents != null) foreach (Transform t in c.contents) if (t.name == "Bulb" || t.name == "Room light") t.gameObject.SetActive(true);
            c.held = false;
        }

        // The house leans: the floor isn't level, and it keeps going over.
        IEnumerator Tilt(Cell c)
        {
            float dir = rng.Next(2) == 0 ? -1f : 1f;
            Sfx.At("creak", hPos + Vector3.up * 2.5f, 0.8f, 0.45f);
            for (float t = 0f; t < 13f && alive; t += Time.deltaTime)
            {
                float k = Mathf.Sin(Mathf.Clamp01(t / 13f) * Mathf.PI);
                g.player.Roll = dir * k * (13f + 4f * Mathf.Sin(t * 1.3f));
                g.player.fovOffset = k * 6f;
                yield return null;
            }
            g.player.Roll = 0f; g.player.fovOffset = 0f;
        }

        // ---------------------------------------------------------------- shut in

        // The room gets smaller: four inner walls (with their doorways) and the ceiling come in on you. Never onto you.
        IEnumerator Squeeze(Cell c)
        {
            c.held = true;
            var box = new GameObject("Closing in").transform;
            box.SetParent(c.root, false);
            var wall = WallMat(c.theme, Mathf.Max(1, rot));
            float jamb = (Pitch - Gap) / 2f;
            var walls = new Transform[4];
            for (int s = 0; s < 4; s++)
            {
                bool alongZ = s < 2;
                var w = new GameObject("Inner wall").transform;
                w.SetParent(box, false);
                System.Func<float, Vector3> along = d => alongZ ? new Vector3(0f, 0f, d) : new Vector3(d, 0f, 0f);
                System.Func<float, float, Vector3> size = (len, h) => alongZ ? new Vector3(0.12f, h, len) : new Vector3(len, h, 0.12f);
                Build.Box("Wall", w, along(-(Gap + jamb) / 2f) + Vector3.up * H / 2f, size(jamb, H), wall);
                Build.Box("Wall", w, along((Gap + jamb) / 2f) + Vector3.up * H / 2f, size(jamb, H), wall);
                Build.Box("Wall", w, Vector3.up * (DoorH + H) / 2f, size(Gap, H - DoorH), wall);
                walls[s] = w;
            }
            var lid = Build.Box("Ceiling coming down", box, new Vector3(0f, H, 0f), new Vector3(Pitch, 0.12f, Pitch), new Color(0.16f, 0.15f, 0.14f), false).transform;
            var groan = Sfx.Loop("drone", box, 0.5f, true, 0.6f);
            float d0 = Pitch / 2f - 0.2f, dist = d0, top = H;
            for (float t = 0f; t < 22f && alive && Here == c; t += Time.deltaTime)
            {
                Vector3 l = c.root.InverseTransformPoint(hPos);
                // In to an arm's length of you, on every side; the ceiling to just over your head.
                float limit = Mathf.Max(1.15f, Mathf.Max(Mathf.Abs(l.x), Mathf.Abs(l.z)) + 0.75f);
                dist = Mathf.MoveTowards(dist, limit, Time.deltaTime * (t < 14f ? 0.33f : 0f));
                top = Mathf.MoveTowards(top, 1.95f, Time.deltaTime * (t < 14f ? 0.09f : 0f));
                walls[0].localPosition = new Vector3(dist, 0f, 0f); walls[1].localPosition = new Vector3(-dist, 0f, 0f);
                walls[2].localPosition = new Vector3(0f, 0f, dist); walls[3].localPosition = new Vector3(0f, 0f, -dist);
                lid.localPosition = new Vector3(0f, top, 0f);
                g.player.fovOffset = -Mathf.InverseLerp(d0, 1.15f, dist) * 7f;
                if (rng.NextDouble() < Time.deltaTime * 0.5) Sfx.At("creak", hPos + Random.onUnitSphere * 2f, 0.4f, 0.5f);
                yield return null;
            }
            if (!alive) yield break;
            g.player.fovOffset = 0f;
            // Out again, quickly, the moment you've left or stopped looking.
            for (float t = 0f; t < 1.2f && alive; t += Time.deltaTime)
            {
                dist = Mathf.MoveTowards(dist, d0 + 0.3f, Time.deltaTime * 4f);
                top = Mathf.MoveTowards(top, H, Time.deltaTime * 2f);
                walls[0].localPosition = new Vector3(dist, 0f, 0f); walls[1].localPosition = new Vector3(-dist, 0f, 0f);
                walls[2].localPosition = new Vector3(0f, 0f, dist); walls[3].localPosition = new Vector3(0f, 0f, -dist);
                lid.localPosition = new Vector3(0f, top, 0f);
                yield return null;
            }
            if (!alive) yield break;
            if (groan != null) Object.Destroy(groan.gameObject);
            Object.Destroy(box.gameObject);
            c.held = false;
        }

        // Every doorway of the room slams shut, the light goes, and something breathes. Then one opens.
        IEnumerator Sealed(Cell c)
        {
            c.held = true;
            float until = Time.time + 7.5f;
            foreach (var e in edges) if (e.a == c || e.b == c) { e.sealedUntil = until; Apply(e); }
            Sfx.At("slam", hPos + Vector3.up, 1f, 0.8f);
            Look.Kick(0.5f);
            if (c.lamp != null) c.lamp.enabled = false;
            Blackout = true;
            yield return Wait(1.5f);
            for (int i = 0; i < 3 && alive; i++)
            {
                Vector3 behind = hPos - (hCam != null ? Vis.Level(hCam.transform.forward).normalized : Vector3.forward) * 1.2f + Vector3.up * 1.5f;
                Sfx.At("breath", behind, 0.9f, 0.7f);
                yield return Wait(1.7f);
            }
            yield return Wait(Mathf.Max(0f, until - Time.time));
            if (!alive) yield break;
            Blackout = false;
            if (c.lamp != null) c.lamp.enabled = true;
            foreach (var e in edges) if (e.a == c || e.b == c) { e.sealedUntil = 0f; Apply(e); }
            Sfx.At("creak", hPos + Vector3.up, 0.6f, 0.9f);
            c.held = false;
        }

        // Black water comes up through the floor to your chest, holds, and goes.
        IEnumerator Flood(Cell c)
        {
            c.held = true;
            var water = Build.Box("Rising water", c.root, new Vector3(0f, -0.05f, 0f), new Vector3(Pitch - 0.22f, 0.04f, Pitch - 0.22f), Build.Mat(new Color(0.01f, 0.018f, 0.02f), 0f, 0.62f), false).transform;
            var sound = Sfx.Loop("sink", water, 0.7f, true, 0.7f);
            float level = 0f;
            for (float t = 0f; t < 24f && alive; t += Time.deltaTime)
            {
                float want = t < 16f ? 1.3f : 0f;
                level = Mathf.MoveTowards(level, want, Time.deltaTime * (t < 16f ? 0.14f : 0.5f));
                water.localPosition = new Vector3(0f, level - 0.02f, 0f);
                bool inIt = Here == c && !hHidden;
                if (!hHidden) g.player.speedMul = inIt ? Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(level / 1.2f)) : 1f;
                if (t > 16f && level <= 0.01f) break;
                yield return null;
            }
            if (!alive) yield break;
            if (!hHidden) g.player.speedMul = 1f;
            if (sound != null) Object.Destroy(sound.gameObject);
            Object.Destroy(water.gameObject);
            c.held = false;
        }

        // ---------------------------------------------------------------- the room isn't right

        // The furniture comes off the floor and hangs there, turning. It all drops when you stop looking.
        IEnumerator Float(Cell c)
        {
            c.held = true;
            var things = Loose(c);
            var from = new List<Vector3>();
            var rots = new List<Quaternion>();
            var rise = new List<float>();
            foreach (var t in things) { from.Add(t.localPosition); rots.Add(t.localRotation); rise.Add(0.4f + (float)rng.NextDouble() * 1.3f); }
            for (float t = 0f; t < 15f && alive && Here == c; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 5f);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] == null) continue;
                    things[i].localPosition = from[i] + Vector3.up * (rise[i] * k + 0.05f * Mathf.Sin(t * 1.3f + i));
                    things[i].localRotation = rots[i] * Quaternion.Euler(Mathf.Sin(t * 0.5f + i) * 9f * k, t * (6f + i * 2f) * k, Mathf.Cos(t * 0.4f + i) * 7f * k);
                }
                yield return null;
            }
            if (!alive) yield break;
            // Down, all at once.
            for (int i = 0; i < things.Count; i++) if (things[i] != null) { things[i].localPosition = from[i]; things[i].localRotation = rots[i]; }
            if (things.Count > 0) { Sfx.At("slam", World(Local(c.x, c.z)) + Vector3.up * 0.3f, 1f, 0.6f); Look.Kick(0.35f); }
            c.held = false;
        }

        // Every bulb in the maze goes out, and what you carry gutters. Something whispers, close.
        IEnumerator LightsOut(Cell c)
        {
            Blackout = true;
            foreach (var k in cells) if (k.lamp != null) k.lamp.enabled = false;
            Sfx.At("click", hPos + Vector3.up * 2f, 0.7f, 0.8f);
            yield return Wait(2.5f);
            if (alive) Sfx.At("whisper", hPos + Random.onUnitSphere * 0.8f + Vector3.up * 1.5f, 0.9f, 0.9f);
            yield return Wait(8f);
            if (!alive) yield break;
            Blackout = false;
            foreach (var k in cells) if (k.lamp != null) k.lamp.enabled = k.lit;
        }

        // People standing with their faces to the wall. Every time you look away they have turned, and are nearer.
        IEnumerator Watchers(Cell c)
        {
            c.held = true;
            var group = new GameObject("Watchers").transform;
            group.SetParent(c.root, false);
            var figs = new List<Transform>();
            var turned = new List<bool>();
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = Corner(i, 3.2f);
                if (Blocked(c, p) || Seen(c.root.TransformPoint(p) + Vector3.up * 1.5f)) continue;       // they are only ever there already
                var f = Person("Watcher", group, i);
                f.localPosition = p;
                f.localRotation = Quaternion.LookRotation(new Vector3(p.x, 0f, p.z));
                figs.Add(f);
                turned.Add(false);
            }
            bool touched = false;
            for (float t = 0f; t < 24f && alive && Here == c && figs.Count > 0 && !touched; t += Time.deltaTime)
            {
                for (int i = 0; i < figs.Count; i++)
                {
                    var f = figs[i];
                    Vector3 to = Vis.Level(hPos - f.position);
                    if (Seen(f.position + Vector3.up * 1.6f) || Seen(f.position + Vector3.up * 0.5f)) continue;
                    // Not being looked at: it turns to face you, then comes on.
                    if (!turned[i]) { turned[i] = true; f.rotation = Quaternion.LookRotation(to.normalized); continue; }
                    f.rotation = Quaternion.LookRotation(to.normalized);
                    if (to.magnitude > 1.1f) f.position += to.normalized * Time.deltaTime * 1.1f;
                    else touched = true;
                }
                yield return null;
            }
            if (!alive) yield break;
            if (touched) { Sfx.At("whisper", hPos + Vector3.up * 1.5f, 1f, 0.7f); Look.Kick(0.8f); g.Shake(0.03f, "one of them reached you"); }
            // Gone, the next time you look away.
            yield return Wait(6f, () => { foreach (var f in figs) if (Seen(f.position + Vector3.up * 1.5f)) return false; return true; });
            if (!alive) yield break;
            Object.Destroy(group.gameObject);
            c.held = false;
        }

        // A person, from behind: a nightdress or a suit, pale arms, dark hair. Someone who lived here, maybe. (Not the
        // flat black dummy of the waking house's dreams: these should be people until they turn round.)
        Transform Person(string name, Transform parent, int i)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            Color[] cloth = { new Color(0.62f, 0.6f, 0.55f), new Color(0.12f, 0.12f, 0.14f), new Color(0.36f, 0.3f, 0.36f), new Color(0.5f, 0.44f, 0.36f) };
            var clothes = Build.Mat(cloth[(i + rng.Next(4)) % cloth.Length], 0f, 0.1f);
            var skin = Build.Mat(new Color(0.66f, 0.6f, 0.55f), 0f, 0.25f);
            var hair = Build.Mat(new Color(0.06f, 0.05f, 0.045f), 0f, 0.3f);
            bool dress = rng.Next(2) == 0;
            float tall = 0.95f + (float)rng.NextDouble() * 0.12f;
            root.localScale = Vector3.one * tall;
            if (dress) Build.Prim(PrimitiveType.Cylinder, "Nightdress", root, new Vector3(0f, 0.72f, 0f), new Vector3(0.46f, 0.7f, 0.34f), clothes, false);
            else
            {
                Build.Prim(PrimitiveType.Capsule, "Leg", root, new Vector3(-0.1f, 0.45f, 0f), new Vector3(0.15f, 0.47f, 0.15f), clothes, false);
                Build.Prim(PrimitiveType.Capsule, "Leg", root, new Vector3(0.1f, 0.45f, 0f), new Vector3(0.15f, 0.47f, 0.15f), clothes, false);
            }
            Build.Prim(PrimitiveType.Capsule, "Body", root, new Vector3(0f, 1.18f, 0f), new Vector3(0.42f, 0.36f, 0.25f), clothes, false);
            Build.Prim(PrimitiveType.Capsule, "Arm", root, new Vector3(-0.25f, 1.05f, 0f), new Vector3(0.1f, 0.36f, 0.1f), skin, false);
            Build.Prim(PrimitiveType.Capsule, "Arm", root, new Vector3(0.25f, 1.05f, 0f), new Vector3(0.1f, 0.36f, 0.1f), skin, false);
            Build.Prim(PrimitiveType.Sphere, "Neck", root, new Vector3(0f, 1.5f, 0f), new Vector3(0.1f, 0.12f, 0.1f), skin, false);
            Build.Prim(PrimitiveType.Sphere, "Head", root, new Vector3(0f, 1.66f, 0f), new Vector3(0.2f, 0.25f, 0.22f), skin, false);
            // Hair over the back of the head and down: from behind, no face.
            Build.Prim(PrimitiveType.Sphere, "Hair", root, new Vector3(0f, 1.68f, -0.03f), new Vector3(0.22f, 0.25f, 0.21f), hair, false);
            if (dress) Build.Prim(PrimitiveType.Cube, "Long hair", root, new Vector3(0f, 1.44f, -0.08f), new Vector3(0.2f, 0.42f, 0.05f), hair, false);
            return root;
        }

        // You are small. The room is the same; you aren't.
        IEnumerator Small(Cell c)
        {
            Sfx.At("lullaby", hPos + Vector3.up * 2.5f, 0.5f, 0.8f);
            for (float t = 0f; t < 16f && alive; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Min(t / 3f, (16f - t) / 2f));
                if (!hHidden) { g.player.eyeHeight = Mathf.Lerp(1.65f, 0.72f, k); g.player.speedMul = Mathf.Lerp(1f, 0.75f, k); }
                g.player.fovOffset = -6f * k;
                yield return null;
            }
            if (!hHidden) { g.player.eyeHeight = 1.65f; g.player.speedMul = 1f; }
            g.player.fovOffset = 0f;
        }
    }
}
