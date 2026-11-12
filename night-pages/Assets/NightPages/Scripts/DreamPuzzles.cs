using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Each night's way out can be locked by a small puzzle that uses something the day taught, and the maze can
    /// offer help that also uses the day's verbs. Rules (Round 2): one fair answer, readable without guessing; hints
    /// climb in three steps (the place itself, a line, then the answer on a page), each about 90 seconds apart (45 on
    /// easy); failing costs time, never progress.
    ///
    /// Locks, by story day: 4, set the clock by the door to the hour the hall clock strikes at bedtime; 5, turn the
    /// dial to the seconds between drops (the drip is heard by the door, so it can always be counted); 6, both.
    /// Help: from day 2, a dusty table near where you wake; wipe it and the way out is drawn underneath. From day 4,
    /// bare wet footprints lead toward the way out, drying as you follow.
    /// </summary>
    public class DreamPuzzles
    {
        readonly GameDirector g;
        readonly DreamMaze maze;
        readonly Transform root;
        readonly int day;
        bool started;

        class Lock
        {
            public string id, name;
            public int value, answer, max;
            public bool solved;
            public Transform obj;
            public ClockFace face;
            public TextMesh label;
            public string[] hints;
        }
        readonly List<Lock> locks = new List<Lock>();
        float hintAt = -1f;
        int hintStep;
        float dripNext;
        Transform dustFilm;
        bool dustWiped;

        public bool Locked { get { foreach (var l in locks) if (!l.solved) return true; return false; } }
        public string Describe { get { return locks.Count == 0 ? "no lock" : string.Join(", ", locks.ConvertAll(l => l.id + (l.solved ? " (solved)" : "")).ToArray()); } }

        public DreamPuzzles(GameDirector director, DreamMaze m, Transform dreamRoot, int storyDay)
        {
            g = director; maze = m; root = dreamRoot; day = storyDay;
        }

        float HintGap { get { return GameDirector.Level == 0 ? 45f : 90f; } }

        // ---------------------------------------------------------------- setting up (the first frame, once you're in the maze)

        void Start(Vector3 pp)
        {
            started = true;
            var exit = maze.ExitTransform;
            if (exit != null)
            {
                if (day == 4 || day == 6) locks.Add(ClockLock(exit));
                if (day == 5 || day == 6) locks.Add(DialLock(exit, day == 6 ? 1 : 0));
            }
            if (day >= 2) DustMap(pp);
            if (day >= 4) WetPrints(pp);
            g.Log("dream puzzles: " + (locks.Count == 0 ? "the way out isn't locked tonight" : "the way out is locked: " + Describe) +
                  (day >= 2 ? "; a dusty table near where you woke" : "") + (day >= 4 ? "; wet prints toward the way out" : ""));
        }

        Lock ClockLock(Transform exit)
        {
            var l = new Lock { id = "clock", name = "the clock by the door", answer = g.DeathHour, max = 12 };
            l.value = 1 + (l.answer + 5) % 12;
            var post = new GameObject("Clock post").transform;
            post.SetParent(exit, false);
            post.localPosition = new Vector3(-1.05f, 0f, 0.15f);
            post.localRotation = Quaternion.identity;
            Build.Box("Post", post, new Vector3(0f, 0.75f, 0f), new Vector3(0.1f, 1.5f, 0.1f), new Color(0.2f, 0.15f, 0.1f), false);
            l.face = ClockFace.Make(post, Vector3.zero, Vector3.forward, 0.16f, new Color(0.92f, 0.88f, 0.78f), new Color(0.5f, 0.38f, 0.2f));
            l.face.transform.localPosition = new Vector3(0f, 1.6f, -0.08f);
            l.face.transform.localRotation = Quaternion.LookRotation(-Vector3.forward);
            l.face.stopped = true;
            l.face.stoppedAt = l.value * 60f;
            l.face.Show(l.face.stoppedAt);
            l.obj = post;
            var col = post.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.2f, 0f); col.size = new Vector3(0.5f, 1.2f, 0.4f);
            var it = Build.Usable(post.gameObject, "");
            it.promptFn = () => l.solved ? "" : "Turn the hands (" + l.value + " o'clock now)";
            it.use = () => Turn(l);
            l.hints = new[] { null, "At home the hall clock strikes the same hour every night, at bedtime. Not ten.", "Set it to " + l.answer + " o'clock." };
            return l;
        }

        Lock DialLock(Transform exit, int slot)
        {
            int answer = g.haunt != null ? g.haunt.dripSeconds : 6;
            var l = new Lock { id = "dial", name = "the dial on the door", answer = answer, max = 12, value = 1 + (answer + 4) % 12 };
            var box = new GameObject("Dial").transform;
            box.SetParent(exit, false);
            box.localPosition = new Vector3(slot == 1 ? 1.05f : -1.05f, 0f, 0.15f);
            Build.Box("Post", box, new Vector3(0f, 0.65f, 0f), new Vector3(0.1f, 1.3f, 0.1f), new Color(0.2f, 0.15f, 0.1f), false);
            Build.Box("Plate", box, new Vector3(0f, 1.35f, -0.06f), new Vector3(0.26f, 0.26f, 0.03f), new Color(0.55f, 0.5f, 0.42f), false);
            var lab = new GameObject("Number").transform;
            lab.SetParent(box, false);
            lab.localPosition = new Vector3(0f, 1.35f, -0.08f);
            lab.localRotation = Quaternion.LookRotation(Vector3.forward);
            l.label = lab.gameObject.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) { l.label.font = font; lab.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            l.label.anchor = TextAnchor.MiddleCenter;
            l.label.fontSize = 80; l.label.characterSize = 0.012f;
            l.label.color = new Color(0.1f, 0.08f, 0.06f);
            l.label.text = l.value.ToString();
            l.obj = box;
            var col = box.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.1f, 0f); col.size = new Vector3(0.45f, 1.0f, 0.4f);
            var it = Build.Usable(box.gameObject, "");
            it.promptFn = () => l.solved ? "" : "Turn the dial (" + l.value + ")";
            it.use = () => Turn(l);
            l.hints = new[] { null, "Listen by the door. Count between the drops.", "Turn it to " + l.answer + "." };
            return l;
        }

        void Turn(Lock l)
        {
            if (l.solved) return;
            l.value = l.value % l.max + 1;
            Sfx.At(l.id == "clock" ? "ratchet" : "click", l.obj.position + Vector3.up * 1.4f, 0.6f, 0.9f + Random.value * 0.2f);
            if (l.face != null) { l.face.stoppedAt = l.value * 60f; l.face.Show(l.face.stoppedAt); }
            if (l.label != null) l.label.text = l.value.ToString();
            if (l.value != l.answer) return;
            l.solved = true;
            Sfx.At(l.id == "clock" ? "chime" : "clunk", l.obj.position + Vector3.up * 1.4f, 0.8f);
            g.Log("dream puzzles: " + l.name + " is right (" + l.answer + ")" + (Locked ? "; another lock still holds" : "; the way out is open"));
            g.hud.Say(Locked ? "Something gives. Not everything." : "The door gives.", 3f);
            if (!Locked) g.Steady(0.02f, "worked out the dream's lock");
        }

        // A table thick with dust near where you woke. Wipe it: the way out is drawn underneath, as it was when you wiped it.
        void DustMap(Vector3 pp)
        {
            Vector3 centre = maze.RoomCentre(pp);
            Vector3 spot = Vector3.zero;
            bool ok = false;
            foreach (var o in new[] { new Vector3(1.4f, 0f, 1.4f), new Vector3(-1.4f, 0f, 1.4f), new Vector3(1.4f, 0f, -1.4f), new Vector3(-1.4f, 0f, -1.4f), new Vector3(0f, 0f, 1.8f), new Vector3(1.8f, 0f, 0f) })
            {
                Vector3 c = centre + o;
                if (Physics.CheckBox(c + Vector3.up * 0.45f, new Vector3(0.55f, 0.42f, 0.4f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                spot = c; ok = true; break;
            }
            if (!ok) { g.Log("dream puzzles: no room for the dusty table near where you woke"); return; }
            var t = new GameObject("Dusty table").transform;
            t.SetParent(root, true);
            t.position = new Vector3(spot.x, centre.y, spot.z);
            var wood = new Color(0.28f, 0.2f, 0.14f);
            var top = Build.Box("Top", t, new Vector3(0f, 0.76f, 0f), new Vector3(1.0f, 0.04f, 0.7f), wood, true);
            foreach (var lx in new[] { -0.45f, 0.45f }) foreach (var lz in new[] { -0.3f, 0.3f })
                Build.Box("Leg", t, new Vector3(lx, 0.37f, lz), new Vector3(0.05f, 0.74f, 0.05f), wood, false);
            var film = Stains.Film(t, t.position + Vector3.up * 0.781f, Vector3.up, 0.98f, 0.68f, "dust", 1f);
            dustFilm = film != null ? film.transform : null;
            var it = Build.Usable(top, "");
            it.promptFn = () => dustWiped ? "" : "Wipe the dust off the table";
            it.use = () => WipeMap(t);
        }

        void WipeMap(Transform t)
        {
            if (dustWiped) return;
            dustWiped = true;
            Sfx.At("scrub", t.position + Vector3.up * 0.8f, 0.6f);
            if (dustFilm != null) Object.Destroy(dustFilm.gameObject);
            // Under the dust: the way from here to the way out, scratched into the wood.
            var pts = Route(t.position);
            if (pts.Count < 2) { g.hud.Say("Under the dust, the wood is scratched. Nothing you can read.", 3f); return; }
            Vector3 o = pts[0];
            float ext = 0.1f;
            foreach (var p in pts) ext = Mathf.Max(ext, Mathf.Abs(p.x - o.x), Mathf.Abs(p.z - o.z));
            float s = 0.3f / ext;
            var ink = new Color(0.08f, 0.05f, 0.04f);
            Vector3 topC = t.position + Vector3.up * 0.785f;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 a = topC + new Vector3(pts[i - 1].x - o.x, 0f, pts[i - 1].z - o.z) * s, b = topC + new Vector3(pts[i].x - o.x, 0f, pts[i].z - o.z) * s;
                var seg = Build.Box("Scratch", t, Vector3.zero, new Vector3(0.012f, 0.002f, (b - a).magnitude + 0.012f), ink, false);
                seg.transform.position = (a + b) * 0.5f;
                seg.transform.rotation = Quaternion.LookRotation((b - a).sqrMagnitude > 0.0001f ? (b - a) : Vector3.forward);
            }
            var x = Build.Box("Cross", t, Vector3.zero, new Vector3(0.05f, 0.002f, 0.012f), ink, false);
            x.transform.position = topC + new Vector3(pts[pts.Count - 1].x - o.x, 0f, pts[pts.Count - 1].z - o.z) * s;
            x.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            var x2 = Object.Instantiate(x, t); x2.transform.rotation = Quaternion.Euler(0f, -45f, 0f);
            var me = Build.Box("You", t, Vector3.zero, new Vector3(0.03f, 0.002f, 0.03f), new Color(0.5f, 0.08f, 0.06f), false);
            me.transform.position = topC;
            g.hud.Say("Under the dust, scratched into the wood: a way through. It was true when you wiped it.", 4f);
            g.Log("dream puzzles: wiped the dusty table; the way out is drawn under it (" + pts.Count + " turns)");
        }

        /// <summary>The way from `from` to the way out, as the maze stands now: doorway, room, doorway, ..., the door.</summary>
        List<Vector3> Route(Vector3 from)
        {
            var pts = new List<Vector3> { from };
            Vector3 p = from, exit = maze.ExitPosition;
            for (int i = 0; i < 40; i++)
            {
                Vector3 q = maze.Guide(p);
                if ((q - p).magnitude < 0.2f) break;
                pts.Add(q);
                if (Vis.Flat(q - exit).magnitude < 1f) break;
                p = q + Vis.Flat(q - p).normalized * 0.6f;
            }
            return pts;
        }

        // Bare wet footprints from where you woke toward the way out. They dry as you follow.
        void WetPrints(Vector3 pp)
        {
            var pts = Route(pp);
            if (pts.Count < 2) return;
            bool left = false;
            float laid = 0f;
            for (int i = 1; i < pts.Count && laid < 26f; i++)
            {
                Vector3 a = pts[i - 1], b = pts[i];
                Vector3 d = Vis.Flat(b - a);
                float len = d.magnitude;
                if (len < 0.05f) continue;
                d /= len;
                for (float s = (i == 1 ? 1.2f : 0f); s < len && laid < 26f; s += 0.42f, laid += 0.42f)
                {
                    left = !left;
                    Liquid.PrintAt(new Vector3(a.x, pp.y + 0.05f, a.z) + d * s, d, left, "water", 0.95f);
                }
            }
        }

        // ---------------------------------------------------------------- every frame

        public void Tick(float now, Vector3 pp)
        {
            if (!started) Start(pp);
            // The drip, heard by the dialled door, at its count.
            foreach (var l in locks)
            {
                if (l.id != "dial" || l.obj == null) continue;
                if (now >= dripNext) { dripNext = now + l.answer; Sfx.At("drip", l.obj.position + Vector3.up * 2f, 0.7f, 0.95f); }
            }
            if (hintAt < 0f || !Locked || now < hintAt) return;
            hintAt = now + HintGap;
            hintStep++;
            foreach (var l in locks)
            {
                if (l.solved) continue;
                if (hintStep == 1)
                {
                    // The place itself: the lock lights up, and on the clock night, a far-off chime strikes the hour.
                    Build.Lamp("Hint", l.obj, new Vector3(0f, 1.6f, -0.4f), new Color(1f, 0.85f, 0.6f), 1.2f, 3f);
                    if (l.id == "clock") g.StartCoroutine(Chimes(l.answer, pp + Vector3.up * 3f + Random.onUnitSphere * 12f));
                    g.Log("dream puzzles: hint 1 for " + l.name + " (it lights up" + (l.id == "clock" ? "; a far-off clock strikes" : "") + ")");
                }
                else if (hintStep == 2) { g.hud.Say(l.hints[1], 5f); g.Log("dream puzzles: hint 2 for " + l.name + ": " + l.hints[1]); }
                else { g.ReadDreamPage(l.hints[2], null); g.Log("dream puzzles: hint 3 for " + l.name + " (the answer, on a page)"); }
                break;
            }
        }

        System.Collections.IEnumerator Chimes(int n, Vector3 at)
        {
            for (int i = 0; i < n; i++) { Sfx.At("chime", at, 0.5f, 0.95f); yield return new WaitForSeconds(1.7f); }
        }

        /// <summary>At the way out, and it's still locked.</summary>
        public void AtLockedDoor(float now)
        {
            if (hintAt < 0f)
            {
                hintAt = now + HintGap * 0.5f;
                Sfx.At("clunk", maze.ExitPosition + Vector3.up, 0.6f, 0.8f);
                Lock first = locks.Find(l => !l.solved);
                g.hud.Say("It won't open. " + (first != null ? "Look at " + first.name + "." : ""), 3.5f);
                g.Log("dream puzzles: reached the way out and it's locked (" + Describe + ")");
            }
        }

        /// <summary>Studio: solve every lock as a player would, one turn at a time.</summary>
        public int SolveAll()
        {
            int turns = 0;
            foreach (var l in locks) for (int i = 0; i < 13 && !l.solved; i++) { Turn(l); turns++; }
            return turns;
        }
        public bool Wipe() { var t = root.Find("Dusty table"); if (t == null) return false; WipeMap(t); return true; }
        public Transform DustTable { get { return root.Find("Dusty table"); } }
    }
}
