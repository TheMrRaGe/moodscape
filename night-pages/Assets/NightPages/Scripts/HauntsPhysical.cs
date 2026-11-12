using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    public partial class House
    {
        /// <summary>A small thing put down by a haunting at a world position, resting on whatever is under it.</summary>
        public Movable PropAt(string name, PrimitiveType shape, Vector3 world, Vector3 size, Material mat, float mass)
        {
            RaycastHit under;
            if (!Surface(world + Vector3.up * 0.3f, Vector3.down, 1.6f, out under) || under.normal.y < 0.8f) return null;
            Vector3 scale = shape == PrimitiveType.Cylinder || shape == PrimitiveType.Capsule ? new Vector3(size.x, size.y / 2f, size.z) : size;
            var g = Build.Prim(shape, name, transform, Vector3.zero, scale, mat, true);
            g.transform.position = under.point + Vector3.up * (size.y / 2f + 0.002f);
            g.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            if (shape == PrimitiveType.Cylinder) FlatBottom(g);
            var m = Movable.Attach(g, mass);
            m.Rest();
            movables.Add(m);
            return m;
        }

        /// <summary>A cup with something in it, put down by a haunting.</summary>
        public Movable CupAt(Vector3 world, Color china, string kind, float full)
        {
            var cup = PropAt("Teacup", PrimitiveType.Cylinder, world, new Vector3(0.085f, 0.09f, 0.085f), Build.Mat(china, 0f, 0.7f), 0.15f);
            if (cup == null) return null;
            var handle = Build.Box("Handle", cup.transform, new Vector3(0.62f, 0.05f, 0f), Vector3.one, Build.Mat(china, 0f, 0.7f), false);
            handle.transform.localScale = new Vector3(0.2f, 0.6f, 0.14f);
            var c = Liquid.ColorOf(kind);
            var drink = Build.Prim(PrimitiveType.Cylinder, "Tea", cup.transform, new Vector3(0f, 1.0f - (1f - full) * 1.6f, 0f), new Vector3(0.84f, 0.01f, 0.84f), Build.Mat(c, 0f, 0.92f), false);
            Contents.In(cup.gameObject, kind, 0.2f * full, drink);
            return cup;
        }

        /// <summary>A glass of something, put down by a haunting.</summary>
        public Movable GlassAt(Vector3 world, string kind, float full)
        {
            var m = PropAt("Glass", PrimitiveType.Cylinder, world, new Vector3(0.07f, 0.12f, 0.07f), GlassMat(), 0.2f);
            if (m == null) return null;
            var c = Liquid.ColorOf(kind);
            var drink = Build.Prim(PrimitiveType.Cylinder, "Drink", m.transform, new Vector3(0f, -1f + full, 0f), new Vector3(0.86f, full, 0.86f), Build.Mat(new Color(c.r, c.g, c.b), 0f, 0.9f), false);
            var wm = drink.GetComponent<Renderer>().material;
            Work.Fade(wm);
            wm.color = kind == "water" ? new Color(0.7f, 0.8f, 0.85f, 0.35f) : new Color(c.r, c.g, c.b, 0.8f);
            Contents.In(m.gameObject, kind, 0.07f * 0.07f * 0.785f * 0.12f * full * 1000f, drink);
            return m;
        }

        /// <summary>Where the bedroom ceiling stain is (the drip comes from there).</summary>
        public Vector3? CeilingStainPoint { get { return ceilingStain != null ? (Vector3?)ceilingStain.transform.position : null; } }
    }

    /// <summary>
    /// Round 2's physical haunts and the gore haunts (fear notes). Every one leaves something you can touch, and most
    /// leave a job: a ring to wipe, a glass to sweep up, prints to mop, a word in the dust that changes when you clean it.
    /// Gore follows its own rules: none before day 2, at most one strong one a day, and it obeys the gore setting
    /// (shown; implied: the smell, the flies, the stain, never the thing; off: none of it).
    /// </summary>
    public partial class Hauntings
    {
        bool goreToday;
        int giftsGiven, teethFound;
        Movable walkGlass;
        Vector3 walkDir;
        int walkSteps;
        float walkNext;
        Work dustWork;
        TextMesh dustText;
        string dustSecond;
        Vector3? dripAt;
        float dripNext;
        int dripLeft;
        /// <summary>Seconds between the drip's drops this run (a number the dreams can ask for).</summary>
        public int dripSeconds;
        /// <summary>Studio: where the last of these haunts left its trace.</summary>
        public Vector3 lastTrace;
        /// <summary>Studio: where to stand to see it (when it's on one face of something).</summary>
        public Vector3? lastView;
        readonly Dictionary<string, System.Func<bool>> moreActs = new Dictionary<string, System.Func<bool>>();

        static bool GoreAllowed { get { return Settings.S.gore < 2; } }
        static bool GoreShown { get { return Settings.S.gore == 0; } }

        void InitMore()
        {
            dripSeconds = 4 + Random.Range(0, 6);
            More("p:secondcup", "a second cup on the table, still warm", 0, 1f, 1, SecondCup, () => !SeesPoint(TableTop()), FearTypes.Loss, "moved", "home", "someone");
            More("p:ring", "a wet cup ring at their place at the table", 0, 1f, 1, RingAtPlace, () => !SeesPoint(TableTop()), FearTypes.Loss, "sight", "home", "someone");
            More("p:walkglass", "a glass on the bedside table, nearer the edge", 1, 1.5f, 2, WalkingGlass, () => walkGlass == null && Here() != RoomNamed("bedroom"), FearTypes.Uncanny, "moved", "close");
            More("p:dustword", "a word written in the dust", 1, 2f, 3, DustWord, () => dustText == null, FearTypes.Uncanny, FearTypes.Self, "sight", "home");
            More("p:drip", "the bedroom ceiling drips", 1, 1.5f, 3, Drip, () => dripLeft <= 0 && g.house.CeilingStainPoint != null, FearTypes.Nature, "sound", "far");
            More("p:prints", "muddy prints that aren't yours, from the door to your bed", 1, 2f, 4, StrangerPrints, () => Here() != g.house.hall && Here() != RoomNamed("bedroom"), FearTypes.Hunted, "sight", "someone");
            More("p:turned", "everything in a room turned to face the door", 1, 2f, 5, Turned, null, FearTypes.Uncanny, "moved", "far");
            More("p:wetbehind", "wet footprints behind you, stopping at your heels", 2, 3f, 6, WetBehind, () => g.player.velocity.magnitude > 0.5f, FearTypes.Hunted, "behind", "close");
            // Gore.
            More("g:gift", "something dead on the doormat, opened neatly", 1, 2f, 2, Gift, () => GoreAllowed && !goreToday && (DoorMat() - PlayerPos).magnitude > 6f, FearTypes.Disgust, FearTypes.Animal, "sight", "home");
            More("g:hands", "small bloody handprints inside the wardrobe door", 1, 2f, 4, Handprints, () => GoreAllowed && !goreToday && g.house.wardrobe != null && Here() != RoomNamed("bedroom"), FearTypes.Disgust, FearTypes.Loss, "sight", "close");
            More("g:towel", "the bathroom towel, red", 1, 2f, 4, Towel, () => GoreAllowed && !goreToday && g.house.towel != null && Here() == g.house.bath, FearTypes.Disgust, FearTypes.Body, "sight", "close");
            More("g:tooth", "a tooth, with a filling, somewhere it shouldn't be", 0, 1f, 3, Tooth, () => GoreAllowed, FearTypes.Body, FearTypes.Disgust, "sight", "home");
        }

        FearCore.Incident More(string id, string label, int tier, float cost, int minDay, System.Func<bool> act, System.Func<bool> can, params string[] tags)
        {
            var inc = Add(id, label, tier, cost, minDay, act, tags);
            if (can != null) inc.can = can;
            moreActs[id] = act;
            return inc;
        }

        /// <summary>A new day for these: the gore allowance resets; the drip stops.</summary>
        void BeginDayMore()
        {
            goreToday = false;
            dripLeft = 0;
        }

        /// <summary>Studio: make one of these happen now, whatever the day, and say where to look.</summary>
        public bool ForceMore(string id)
        {
            System.Func<bool> act;
            if (!moreActs.TryGetValue(id, out act)) return false;
            goreToday = false;
            return act();
        }
        public IEnumerable<string> MoreIds { get { return moreActs.Keys; } }

        // ---------------------------------------------------------------- helpers

        Room Here() { return g.house.RoomAt(PlayerPos); }
        Room RoomNamed(string name) { return g.house.rooms.Find(r => r.name == name); }
        bool SeesPoint(Vector3 p) { return Vis.Sees(Cam, p); }

        Transform Named(string n)
        {
            foreach (var t in g.house.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
            return null;
        }

        static Bounds BoundsOf(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            var col = t.GetComponent<Collider>();
            // Furniture made from a model is a box collider standing in for it, with the model beside it: use the box.
            if (rs.Length == 0 && col != null) return col.bounds;
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.one * 0.1f);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        Vector3 TableTop()
        {
            var t = Named("Kitchen table");
            if (t == null) return RoomNamed("kitchen") != null ? RoomNamed("kitchen").center : Vector3.zero;
            var b = BoundsOf(t);
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }

        Vector3 DoorMat()
        {
            var t = Named("Doormat");
            return t != null ? t.position : g.house.frontDoor.Middle - Vector3.forward * 0.7f;
        }

        Material Mat(Color c, float smooth = 0.3f) { return Build.Mat(c, 0f, smooth); }

        /// <summary>The face of `t` you see from `from`: where a ray from there meets it, and which way that face looks.</summary>
        static bool FaceOf(Transform t, Vector3 from, out Vector3 point, out Vector3 normal)
        {
            var b = BoundsOf(t);
            point = b.center; normal = Vector3.forward;
            foreach (var aim in new[] { b.center, b.center + Vector3.up * b.extents.y * 0.4f, b.center - Vector3.up * b.extents.y * 0.4f })
                foreach (var h in Physics.RaycastAll(from, (aim - from).normalized, (aim - from).magnitude + 1f, ~0, QueryTriggerInteraction.Ignore))
                    if (h.transform == t || h.transform.IsChildOf(t)) { point = h.point; normal = h.normal; return true; }
            return false;
        }

        // ---------------------------------------------------------------- the physical ones

        // A second cup beside yours, at the place across the table, still warm.
        bool SecondCup()
        {
            Vector3 top = TableTop();
            var cup = g.house.CupAt(top + new Vector3(Random.Range(-0.25f, 0.25f), 0.25f, Random.Range(-0.25f, 0.25f)), new Color(0.9f, 0.88f, 0.82f), "coffee", 0.85f);
            if (cup == null) return false;
            lastTrace = cup.transform.position;
            g.Log("haunting: a second cup of coffee on the kitchen table, still warm. You didn't make it");
            return true;
        }

        // A fresh wet ring where the dead one sat; nobody had a cup there.
        bool RingAtPlace()
        {
            Vector3 top = TableTop();
            Vector3 at = top + new Vector3(Random.Range(-0.3f, 0.3f), 0.001f, Random.Range(-0.3f, 0.3f));
            var r = Stains.Mark(g.house.transform, at, Vector3.up, 0.1f, 0.1f, "ring", Liquid.MarkOf("coffee"), 1f);
            if (r == null) return false;
            Stains.Cleanable(r, "Wipe", "the cup ring", 60f, "rag", "sponge");
            lastTrace = at;
            g.Log("haunting: a wet cup ring at a place nobody sat (wipe it; " + (tale != null ? tale.Var("name", "they") : "they") + " always sat there)");
            return true;
        }

        // A glass of water on the bedside table, nearer the edge each time you're out of the room. It falls when you're in it.
        bool WalkingGlass()
        {
            var t = Named("Bedside table");
            if (t == null) return false;
            var b = BoundsOf(t);
            var bed = RoomNamed("bedroom");
            walkDir = Vis.Flat((bed != null ? bed.center : PlayerPos) - b.center).normalized;
            if (walkDir.sqrMagnitude < 0.01f) walkDir = Vector3.forward;
            walkGlass = g.house.GlassAt(new Vector3(b.center.x, b.max.y + 0.05f, b.center.z) - walkDir * 0.05f, "water", 0.7f);
            if (walkGlass == null) return false;
            walkSteps = 0;
            walkNext = Time.time + 20f;
            lastTrace = walkGlass.transform.position;
            g.Log("haunting: a glass of water on the bedside table. Watch where it is");
            return true;
        }

        void TickWalkingGlass(float now)
        {
            if (walkGlass == null || walkGlass.held) { if (walkGlass != null && walkGlass.held) walkGlass = null; return; }
            var body = walkGlass.Body;
            if (body != null && !body.isKinematic) return;          // it's already moving on its own
            bool inRoom = Here() == RoomNamed("bedroom");
            if (!inRoom && now > walkNext && walkSteps < 4)
            {
                walkGlass.transform.position += walkDir * 0.035f;
                walkSteps++;
                walkNext = now + 25f;
                g.Log("haunting: the glass is " + (walkSteps * 3.5f).ToString("0") + " cm nearer the edge of the bedside table");
            }
            else if (inRoom && walkSteps >= 3 && !Vis.Sees(Cam, walkGlass.transform.position))
            {
                // Over it goes, just out of sight, hard enough to break.
                walkGlass.transform.position += walkDir * 0.06f;
                walkGlass.Wake();
                if (walkGlass.Body != null) walkGlass.Body.velocity = walkDir * 0.6f + Vector3.down * 3.6f;
                Sfx.At("creak", walkGlass.transform.position, 0.2f, 1.6f);
                g.Log("haunting: the glass went over the edge while you were in the room");
                walkGlass = null;
            }
        }

        // Words written by a finger in the dust. Wipe the dust and there's a second word underneath, in the clean wood.
        bool DustWord()
        {
            var house = g.house;
            var choices = new List<Work>();
            foreach (var w in house.dust) if (w != null && !w.Finished && !Vis.Sees(Cam, w.transform.position)) choices.Add(w);
            if (choices.Count == 0) return false;
            dustWork = choices[Random.Range(0, choices.Count)];
            string first = tale != null ? tale.Var("name", "HELLO") : "HELLO";
            dustSecond = g.truth == Truth.Killer ? "YOU" : g.truth == Truth.Keepers ? "DOWNSTAIRS" : "HOME";
            var go = new GameObject("Words in the dust");
            go.transform.SetParent(house.transform, true);
            Vector3 normal = -dustWork.transform.forward;
            go.transform.position = dustWork.transform.position + normal * 0.004f;
            Vector3 toMe = Vis.Flat(dustWork.transform.position - PlayerPos);
            go.transform.rotation = Quaternion.LookRotation(-normal, Mathf.Abs(normal.y) > 0.9f ? (toMe.sqrMagnitude > 0.01f ? toMe.normalized : Vector3.forward) : Vector3.up);
            dustText = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) { dustText.font = font; go.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            dustText.text = first.ToUpperInvariant();
            dustText.anchor = TextAnchor.MiddleCenter;
            dustText.alignment = TextAlignment.Center;
            dustText.fontSize = 64;
            dustText.characterSize = 0.008f;
            dustText.color = new Color(0.22f, 0.17f, 0.12f, 0.85f);
            lastTrace = go.transform.position;
            g.Log("haunting: '" + dustText.text + "' written with a finger in the dust (wipe the dust and see what's under it)");
            return true;
        }

        void TickDustWord()
        {
            if (dustText == null || dustWork == null || dustSecond == null || !dustWork.Finished) return;
            dustText.text = dustSecond;
            dustText.color = new Color(0.12f, 0.08f, 0.05f, 1f);
            g.Shake(0.04f, "under the word in the dust, another one: " + dustSecond);
            g.hud.Say("Where the dust was, written into the wood itself: " + dustSecond + ".", 4f);
            dustSecond = null;
        }

        // The bedroom ceiling stain drips, all day, at a steady count. From day 5, with gore shown, it isn't water.
        bool Drip()
        {
            var at = g.house.CeilingStainPoint;
            if (at == null) return false;
            dripAt = at.Value;
            dripLeft = 60;
            dripNext = Time.time + 1f;
            lastTrace = dripAt.Value + Vector3.down * 2.3f;
            g.Log("haunting: the stain on the bedroom ceiling has started to drip, one drop every " + dripSeconds + " seconds" +
                  (g.day >= 5 && GoreShown ? ". It isn't water" : ""));
            return true;
        }

        void TickDrip(float now)
        {
            if (dripLeft <= 0 || dripAt == null || now < dripNext) return;
            dripNext = now + dripSeconds;
            dripLeft--;
            string kind = g.day >= 5 && GoreShown ? "blood" : g.day >= 5 && GoreAllowed ? "tea" : "water";
            Liquid.Spill(dripAt.Value + Vector3.down * 0.05f, 0.025f, kind, dripLeft == 59 ? "Put something under the drip and mop up (bedroom)" : null, "It drips into the bowl now. You count without meaning to.");
            Sfx.At("drip", dripAt.Value + Vector3.down * 2.2f, 0.45f, 0.95f + Random.value * 0.1f);
        }

        // Muddy prints from the front door to your bed, as if someone came in from the garden and stood there.
        bool StrangerPrints()
        {
            var house = g.house;
            Vector3 from = house.frontDoor.Middle, to = house.bedSpawn;
            from.y = 0f;
            var path = house.Path(from, to);
            if (path == null || path.Count == 0) return false;
            path.Insert(0, from);
            path.Add(to);
            var works = new List<Work>();
            bool left = false;
            Vector3 last = path[0];
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 a = path[i - 1], b = path[i];
                Vector3 d = Vis.Flat(b - a);
                float len = d.magnitude;
                if (len < 0.01f) continue;
                d /= len;
                for (float s = 0f; s < len; s += 0.38f)
                {
                    left = !left;
                    Vector3 feet = a + d * s;
                    feet.y = 0.05f;
                    var w = Liquid.PrintAt(feet, d, left, "mud", 0.85f);
                    if (w != null) works.Add(w);
                }
                last = b;
            }
            if (works.Count == 0) return false;
            lastTrace = works[works.Count / 2].transform.position;
            g.chores.Mess("strangerprints", "Mop up the prints that aren't yours (hall to bedroom)",
                () => { foreach (var w in works) if (w != null && !w.Finished) return false; return true; },
                () => { foreach (var w in works) if (w != null && !w.Finished) return w.transform.position; return to; },
                "Bigger than your feet. Barefoot. They stop at your side of the bed and turn round.");
            g.Log("haunting: " + works.Count + " muddy prints that aren't yours, from the front door to your bed. They stop there");
            return true;
        }

        // Every loose thing in a room you're not in, turned to face the door.
        bool Turned()
        {
            Room here = Here(), pick = null;
            int best = 0;
            foreach (var r in g.house.rooms)
            {
                if (r == here || r == g.house.hall || Vis.Sees(Cam, r.center + Vector3.up)) continue;
                int n = 0;
                foreach (var p in Physical.All) if (p != null && g.house.RoomAt(p.transform.position) == r) n++;
                if (n > best) { best = n; pick = r; }
            }
            if (pick == null || best < 3) return false;
            Vector3 door = pick.door != null ? pick.door.Middle : g.house.hall.center;
            int turned = 0;
            foreach (var p in Physical.All)
            {
                if (p == null || g.house.RoomAt(p.transform.position) != pick) continue;
                var mv = p.GetComponent<Movable>();
                if (mv == null || mv.held) continue;
                Vector3 to = Vis.Flat(door - p.transform.position);
                if (to.sqrMagnitude < 0.01f) continue;
                p.transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
                turned++;
            }
            lastTrace = pick.center;
            g.Log("haunting: " + turned + " things in the " + pick.name + " turned to face the door. Turn them back, or leave them (the storyteller will see which)");
            return turned > 0;
        }

        // Wet prints walking up behind you, a step behind each of yours, stopping at your heels.
        bool WetBehind()
        {
            StartCoroutine(WetSteps());
            return true;
        }

        IEnumerator WetSteps()
        {
            bool left = false;
            for (int i = 0; i < 7; i++)
            {
                Vector3 fwd = Vis.Flat(Cam.transform.forward).normalized;
                Vector3 feet = PlayerPos - fwd * (1.6f - i * 0.15f);
                feet.y = 0.05f;
                left = !left;
                Liquid.PrintAt(feet, fwd, left, "water", 0.9f);
                Sfx.At("squelch", feet + Vector3.up * 0.05f, 0.18f + i * 0.03f, 0.9f);
                lastTrace = feet;
                yield return new WaitForSeconds(0.55f);
            }
            g.Log("haunting: wet footprints followed you a step behind, and stopped at your heels");
        }

        // ---------------------------------------------------------------- gore

        // On the mat inside the front door: something dead, opened neatly. A little bigger each morning.
        bool Gift()
        {
            if (giftsGiven > 4) return false;
            Vector3 mat = DoorMat();
            string[] what = { "a mouse", "a bird", "a rabbit", "a finger" };
            int k = Mathf.Clamp(g.day - 2, 0, 3);
            if (k < giftsGiven) k = giftsGiven;
            k = Mathf.Min(k, 3);
            giftsGiven++;
            goreToday = true;
            var stain = Stains.Mark(g.house.transform, new Vector3(mat.x, 0.02f, mat.z), Vector3.up, 0.28f, 0.22f, "blot", Liquid.MarkOf("blood"), 0.9f);
            var job = stain != null ? Stains.Cleanable(stain, "Scrub", "the stain on the mat", 120f, "sponge", "rag") : null;
            Movable body = null;
            if (GoreShown)
            {
                Vector3 size = k == 0 ? new Vector3(0.05f, 0.04f, 0.1f) : k == 1 ? new Vector3(0.09f, 0.07f, 0.16f) : k == 2 ? new Vector3(0.14f, 0.12f, 0.3f) : new Vector3(0.022f, 0.022f, 0.085f);
                Color c = k == 0 ? new Color(0.35f, 0.33f, 0.31f) : k == 1 ? new Color(0.22f, 0.2f, 0.18f) : k == 2 ? new Color(0.45f, 0.36f, 0.27f) : new Color(0.82f, 0.68f, 0.6f);
                body = g.house.PropAt(what[k] == "a finger" ? "Finger" : "Dead " + what[k].Substring(2), PrimitiveType.Capsule, mat + Vector3.up * 0.1f, size, Mat(c, 0.25f), 0.05f + k * 0.3f);
                if (body != null)
                {
                    body.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
                    // The opening: a dark red line along it.
                    Build.Box("Opened", body.transform, new Vector3(0f, 0f, -0.45f), new Vector3(0.25f, 0.7f, 0.12f), new Color(0.35f, 0.03f, 0.04f), false);
                    if (k == 3) Build.Box("Ring mark", body.transform, new Vector3(0f, -0.25f, 0f), new Vector3(1.04f, 0.1f, 1.04f), new Color(0.68f, 0.55f, 0.5f), false);
                }
                g.Log("haunting (gore): " + what[k] + " on the mat inside the front door, opened neatly" + (k == 3 ? ". It has a ring mark worn into it" : ""));
            }
            else
            {
                g.Log("haunting (gore, implied): something was on the mat inside the front door. Flies, and a stain");
                g.hud.Say("Flies, by the front door. And a smell.", 3.5f);
            }
            var gone = body;
            g.chores.Mess("gift" + giftsGiven, GoreShown ? "Sweep up what's on the mat and bin it, and scrub the mat" : "Scrub the stain on the mat",
                () => (gone == null || (!gone.held && (gone.transform.position - mat).magnitude > 2f)) && (job == null || job.Finished),
                () => mat, "You don't look at it while you do it. Then you do.");
            lastTrace = mat;
            return true;
        }

        // Child-height handprints inside the wardrobe door. One more set each day; they come back fainter from inside the wood.
        bool Handprints()
        {
            var w = g.house.wardrobe;
            var b = BoundsOf(w);
            var bed = RoomNamed("bedroom");
            Vector3 eye = (bed != null ? bed.center : PlayerPos);
            eye.y = 1.1f;
            Vector3 face, dir;
            if (!FaceOf(w, eye, out face, out dir))
            {
                dir = Vis.Flat(eye - b.center).normalized;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
                face = b.center + dir * (Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z);
            }
            face += dir * 0.003f;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            int sets = Mathf.Clamp(g.day - 3, 1, 3);
            var works = new List<Work>();
            for (int i = 0; i < sets * 2; i++)
            {
                Vector3 at = new Vector3(face.x, 0.75f + (i / 2) * 0.16f + Random.Range(-0.04f, 0.04f), face.z) + side * ((i % 2 == 0 ? -0.12f : 0.12f) + Random.Range(-0.05f, 0.05f));
                var r = Stains.Mark(g.house.transform, at, dir, 0.13f, 0.17f, GoreShown ? "smudge" : "grime", GoreShown ? Liquid.MarkOf("blood") : new Color(0.3f, 0.22f, 0.18f), 1f, Random.Range(-20f, 20f));
                if (r == null) continue;
                var job = Stains.Cleanable(r, "Scrub", "the handprint", 120f, "sponge", "rag");
                if (job != null) works.Add(job);
            }
            if (works.Count == 0) return false;
            goreToday = true;
            lastTrace = new Vector3(face.x, 0.95f, face.z);
            lastView = new Vector3(face.x, 0f, face.z) + Vis.Flat(dir).normalized * 1.3f;
            g.chores.Mess("hands", "Scrub the handprints off the wardrobe (bedroom)",
                () => { foreach (var x in works) if (x != null && !x.Finished) return false; return true; },
                () => face, "Small hands. Pressed flat, then dragged. From the inside.");
            g.Log("haunting (gore" + (GoreShown ? "" : ", implied") + "): " + works.Count + " small handprints on the wardrobe door, at a child's height");
            return true;
        }

        // The towel on its rail, soaked red, though there's nothing on your face.
        bool Towel()
        {
            var t = g.house.towel;
            var b = BoundsOf(t);
            Vector3 eye = Cam.transform.position;
            if (g.house.bath != null && g.house.RoomAt(eye) != g.house.bath) { eye = g.house.bath.center; eye.y = 1.5f; }
            Vector3 face, dir;
            if (!FaceOf(t, eye, out face, out dir))
            {
                dir = Vis.Flat(eye - b.center).normalized;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
                face = b.center + dir * (Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z);
            }
            lastView = new Vector3(face.x, 0f, face.z) + Vis.Flat(dir).normalized * 1.2f;
            var r = Stains.Mark(g.house.transform, face + dir * 0.004f, dir, Mathf.Max(b.size.x, b.size.z) * 0.9f, b.size.y * 0.8f, "blot",
                                GoreShown ? Liquid.MarkOf("blood") : new Color(0.45f, 0.3f, 0.22f), 1f);
            if (r == null) return false;
            var job = Stains.Cleanable(r, "Wash", "the towel", 150f, "sponge", "rag");
            goreToday = true;
            lastTrace = b.center;
            g.hud.Say(GoreShown ? "The towel is red, and wet. You touch your face. Nothing." : "The towel is stained brown, and damp.", 4f);
            g.Shake(0.05f, "the towel");
            g.chores.Mess("towel", "Wash the towel (bathroom)", () => job == null || job.Finished, () => b.center, "The water runs pink, then clear, then pink again.");
            g.Log("haunting (gore" + (GoreShown ? "" : ", implied") + "): the bathroom towel is soaked");
            return true;
        }

        // One adult tooth, with a filling, somewhere it shouldn't be. Keep them in a jar, or don't.
        bool Tooth()
        {
            var spots = new List<Vector3>();
            if (g.house.washbasin != null) spots.Add(g.house.washbasin.transform.position + Vector3.up * 0.2f + Vis.Flat(PlayerPos - g.house.washbasin.transform.position).normalized * 0.25f);
            var cab = Named("Cabinets");
            if (cab != null) { var b = BoundsOf(cab); spots.Add(new Vector3(b.center.x, b.max.y + 0.1f, b.center.z)); }
            spots.Add(g.house.bedSpawn + new Vector3(0.3f, 0.1f, 0.2f));
            var bed = Named("Bedside table");
            if (bed != null) { var b = BoundsOf(bed); spots.Add(new Vector3(b.center.x, b.max.y + 0.1f, b.center.z)); }
            for (int tries = 0; tries < spots.Count; tries++)
            {
                var at = spots[Random.Range(0, spots.Count)];
                if (Vis.Sees(Cam, at)) continue;
                var tooth = g.house.PropAt("Tooth", PrimitiveType.Capsule, at, new Vector3(0.009f, 0.02f, 0.009f), Mat(new Color(0.9f, 0.87f, 0.76f), 0.6f), 0.01f);
                if (tooth == null) continue;
                Build.Box("Filling", tooth.transform, new Vector3(0f, 0.7f, 0f), new Vector3(0.6f, 0.15f, 0.6f), new Color(0.45f, 0.45f, 0.47f), false);
                teethFound++;
                lastTrace = tooth.transform.position;
                g.Log("haunting (gore): a tooth with a filling, the " + Ordinal(teethFound) + " this run");
                return true;
            }
            return false;
        }

        static string Ordinal(int n) { return n == 1 ? "first" : n == 2 ? "second" : n == 3 ? "third" : n + "th"; }

        /// <summary>Studio (the storyteller is held there): run the carrying-on part as if `ahead` seconds had passed.</summary>
        public void StudioTick(float ahead)
        {
            dripNext = 0f;
            TickMore(Time.time + ahead);
        }

        /// <summary>Every frame (from Update): the haunts that carry on after they start.</summary>
        void TickMore(float now)
        {
            TickWalkingGlass(now);
            TickDustWord();
            TickDrip(now);
        }
    }
}
