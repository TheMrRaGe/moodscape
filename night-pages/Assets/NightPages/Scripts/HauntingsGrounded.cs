using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The grounded hauntings: the kind people actually report about their own homes, done the way they report
    /// them. Mostly sounds and small wrongnesses, almost never a clear sight of anything, always deniable, always
    /// while you aren't looking. (What people report: a presence felt 42%, footsteps and voices 37%, a figure 19%;
    /// in poltergeist cases small objects moved 64%, raps 48%, raps that answer 16%.)
    ///
    /// Which of them a run leans on comes from the Tale: its signs are this house's way of showing itself (a
    /// grandfather's stick on the hall boards, a grandmother's plates, a lodger's rubbish), and the storyteller
    /// prefers them; the rest can still happen, rarely, the way a house just creaks. They unlock in the order a
    /// good ghost story escalates: first a wrongness you can explain (a smell, a sound), then a pattern (things
    /// moved, knocks that answer), then confirmation (someone in the gap of a door, at the end of the hall).
    /// </summary>
    public partial class Hauntings
    {
        Tale tale;
        int slamsRecent;
        float slamWindow, lastGap = -100f, lastAnswer = -100f;

        readonly Dictionary<string, FearCore.Incident> grounded = new Dictionary<string, FearCore.Incident>();
        readonly Dictionary<string, System.Func<bool>> groundedAct = new Dictionary<string, System.Func<bool>>();
        /// <summary>Demos: the door hauntings happen every time, even while the storyteller is held.</summary>
        public bool demoForce;

        /// <summary>Demos and tests: do one of the grounded hauntings now, if it can happen here. Returns whether it did.</summary>
        public bool ForceGrounded(string id)
        {
            System.Func<bool> act;
            if (!groundedAct.TryGetValue(id, out act)) return false;
            bool ok = act();
            g.Log("haunting (forced for the demo): " + id + (ok ? " happened" : " couldn't happen here"));
            return ok;
        }
        readonly Dictionary<string, KeyValuePair<string, int>> groundedBase = new Dictionary<string, KeyValuePair<string, int>>();

        void InitGrounded()
        {
            // id, label, tier, cost, first day, what it does, tags
            Ground("walk", "footsteps in the hall, the way someone used to walk", 0, 1f, 1, Walk, FearTypes.Loss, "sound", "far");
            Ground("smell", "a smell that belongs to someone who's gone", 0, 0.6f, 1, Smell, FearTypes.Loss, "smell", "close");
            Ground("presence", "someone in the room with you", 0, 1f, 1, Presence, FearTypes.Unknown, "presence", "behind");
            Ground("eggs", "something rolling off the worktop and breaking", 0, 0.8f, 1, Eggs, FearTypes.Unknown, "sound", "far");
            Ground("name", "a voice from another room", 1, 1.5f, 2, Name, FearTypes.Loss, "voice", "far");
            Ground("radio", "the radio tuning itself", 0, 1f, 2, Radio, FearTypes.Unknown, "sound", "far");
            Ground("moved", "something of theirs moved while you weren't looking", 0, 1f, 2, Moved, FearTypes.Uncanny, "moved", "far");
            Ground("facing", "the photographs turned to face you", 1, 1.5f, 3, Facing, FearTypes.Uncanny, "moved", "close");
            Ground("rap", "raps from inside the wardrobe", 0, 1f, 2, Rap, FearTypes.Trapped, "sound", "close");
            Ground("answer", "knocking that answers", 1, 1.5f, 2, AnswerLater, FearTypes.Unknown, "sound", "far");
            Ground("bulb", "the bulb went as you came near", 0, 1f, 2, Bulb, FearTypes.Unknown, "light", "close");
            Ground("food", "food gone, and rubbish that isn't yours", 0, 1f, 2, Food, FearTypes.Disgust, "moved", "far");
            Ground("cold", "a patch of cold", 0, 0.8f, 2, Cold, FearTypes.Unknown, "presence", "close");
            Ground("plates", "plates breaking in the kitchen", 1, 2f, 3, Plates, FearTypes.Loss, "sound", "far");
            Ground("peek", "someone looking in at you round the door", 1, 2f, 3, Peek, FearTypes.Hunted, "sight", "peripheral");
            Ground("clock", "twenty minutes you can't account for", 1, 2f, 4, LostTime, FearTypes.Self, "time");
            Ground("bottom", "a figure at the far end of the hall", 2, 3f, 4, Bottom, FearTypes.Hunted, "sight", "far");
            Ground("prints", "wet footprints across the hall", 1, 1.5f, 2, Prints, FearTypes.Nature, "sight", "close");

            // Doors: the gap you open slowly, and the slams you make.
            Door.OpeningSlowly += OnOpeningSlowly;
            Door.Slammed += OnSlammed;
        }

        /// <summary>A new run's story: its signs become this house's ways, in its own words; the others get rarer and later.</summary>
        public void SetTale(Tale t)
        {
            tale = t;
            foreach (var kv in grounded)
            {
                var b = groundedBase[kv.Key];
                string own = tale != null ? tale.SignLine(kv.Key) : null;
                kv.Value.label = own ?? b.Key;
                kv.Value.minEpisode = tale != null && !tale.HasSign(kv.Key) && kv.Key != "presence" && kv.Key != "walk" ? Mathf.Max(b.Value, 3) : b.Value;
            }
            if (tale != null) g.Log("haunting: this house shows itself through: " + SignList());
        }

        string SignList()
        {
            var l = new List<string>();
            foreach (var s in tale.signs) l.Add(s.sign + " (" + tale.Fill(s.lines.Length > 0 ? s.lines[0] : s.sign) + ")");
            return string.Join("; ", l.ToArray());
        }

        void Ground(string id, string label, int tier, float cost, int minDay, System.Func<bool> act, params string[] tags)
        {
            var inc = Add("ph:" + id, label, tier, cost, minDay, act, tags);
            grounded[id] = inc;
            groundedAct[id] = act;
            groundedBase[id] = new KeyValuePair<string, int>(label, minDay);
        }

        /// <summary>The storyteller's extra preference for the story's own signs (added to its score).</summary>
        float GroundBias(FearCore.Incident i)
        {
            if (tale == null || !i.id.StartsWith("ph:")) return 0f;
            return tale.HasSign(i.id.Substring(3)) ? 0.35f : -0.2f;
        }

        public void ShutGrounded()
        {
            Door.OpeningSlowly -= OnOpeningSlowly;
            Door.Slammed -= OnSlammed;
        }

        bool InHouse { get { return g.phase == Phase.Morning || g.phase == Phase.Afternoon || g.phase == Phase.Evening; } }
        string Who(string key, string fallback) { return tale != null ? tale.Var(key, fallback) : fallback; }
        string Fill(string s) { return tale != null ? tale.Fill(s) : s; }

        // ---------------------------------------------------------------- the walk

        // Their walk: in another part of the house, coming along the hall, stopping outside the room you're in. Then
        // nothing, for long enough that you have to decide whether to open the door.
        bool Walk()
        {
            Room here = g.house.RoomAt(PlayerPos);
            if (here == null || here == g.house.hall) return false;          // you have to be in a room for it to stop outside
            StartCoroutine(WalkTo(here));
            return true;
        }

        IEnumerator WalkTo(Room here)
        {
            string style = Who("walk", "shoes");
            Vector3 door = here.door != null ? here.door.Middle : here.center;
            Vector3 hallEnd = new Vector3(0f, 0f, door.z > 0f ? -7f : 7f);
            Vector3 stop = new Vector3(0f, 0f, door.z);
            int n = 9;
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = Vector3.Lerp(hallEnd, stop, i / (float)n) + Vector3.up * 0.1f;
                yield return StepLike(style, p);
            }
            // Outside your door. Waiting.
            yield return new WaitForSeconds(2.5f + Random.value * 2f);
            if (here.door != null && !here.door.open && Random.value < 0.5f)
            {
                Sfx.At("tap", here.door.Middle, 0.25f, 0.8f);          // the handle, tried once, very gently
                g.Log("haunting: whoever it is tried the handle of the " + here.name + " door, once");
            }
        }

        IEnumerator StepLike(string style, Vector3 p)
        {
            switch (style)
            {
                case "stick": Sfx.At("shuffle", p, 0.75f, 0.95f + Random.value * 0.05f); yield return new WaitForSeconds(1.15f); break;
                case "slippers": Sfx.At("shuffle", p, 0.45f, 1.3f); yield return new WaitForSeconds(0.85f); break;
                case "heels": Sfx.At("tap", p, 0.55f, 1.35f + Random.value * 0.05f); yield return new WaitForSeconds(0.42f); break;
                case "boots": Sfx.At("step", p, 0.95f, 0.6f); yield return new WaitForSeconds(0.75f); break;
                case "small": Sfx.At("step", p, 0.5f, 1.6f); yield return new WaitForSeconds(0.26f); break;
                case "bare": Sfx.At("step", p, 0.35f, 1.2f); Sfx.At("drip", p, 0.15f, 1.3f); yield return new WaitForSeconds(0.55f); break;
                case "socks": Sfx.At("step", p, 0.25f, 0.9f); yield return new WaitForSeconds(0.65f); break;
                case "claws": Sfx.At("tap", p, 0.25f, 2.2f); yield return new WaitForSeconds(0.12f); Sfx.At("tap", p, 0.2f, 2.5f); yield return new WaitForSeconds(0.22f); break;
                default: Sfx.At("tap", p, 0.45f, 1.0f); yield return new WaitForSeconds(0.5f); break;
            }
        }

        // ---------------------------------------------------------------- small, deniable things

        bool Smell()
        {
            g.hud.Say(Fill(Random.value < 0.5f ? "{Smell}. Just for a breath, and gone." : "Out of nowhere: {smell}."), 3.2f);
            return true;
        }

        // Someone is in the room. A breath behind you, close, and the air goes heavy. Nothing else.
        bool Presence()
        {
            if (g.player.velocity.magnitude > 1.5f) return false;                    // it comes when you've settled
            Vector3 behind = PlayerPos - g.player.Forward * 0.9f + Vector3.up * 1.6f;
            Sfx.At("breath", behind, 0.22f, 0.85f);
            LoopAt("drone", PlayerPos + Vector3.up, 0.12f, 7f, 0.6f);
            StartCoroutine(Heavy());
            return true;
        }

        IEnumerator Heavy()
        {
            for (float t = 0f; t < 6f; t += Time.deltaTime) { Look.Fear(Mathf.Sin(Mathf.PI * t / 6f) * 0.35f); yield return null; }
            Look.Fear(0f);
        }

        bool Eggs()
        {
            if (g.house.RoomAt(PlayerPos) == g.house.Find("kitchen")) return false;
            Sfx.At("roll", g.house.InRoom("kitchen", new Vector3(-6.95f, 0.95f, -4.6f)), 0.7f);
            return true;
        }

        bool Plates()
        {
            Room k = g.house.Find("kitchen");
            if (k == null || g.house.RoomAt(PlayerPos) == k) return false;
            Vector3 cup = g.house.InRoom("kitchen", new Vector3(-7.1f, 1.9f, -5.4f));
            Sfx.At("smash", cup, 1f, 0.95f);
            // One plate, on the floor below the cupboard, in pieces. The rest of the stack untouched.
            var mat = Build.Mat(new Color(0.92f, 0.9f, 0.84f), 0f, 0.6f);
            // The pieces lie in a patch to sweep up: they go when it's swept, and until then it's on the list.
            Vector3 mid = g.house.InRoom("kitchen", new Vector3(-6.3f, 0f, -5.4f));
            var sweep = g.house.Grit(g.house.transform.InverseTransformPoint(mid), 1.3f, "the broken plate", new Color(0.85f, 0.83f, 0.78f, 0.9f), new[] { "broom" }, 500f);
            for (int i = 0; i < 7; i++)
            {
                Vector3 at = g.house.InRoom("kitchen", new Vector3(-6.3f + Random.Range(-0.4f, 0.4f), 0.008f, -5.4f + Random.Range(-0.6f, 0.6f)));
                var s = Build.Box("Piece of plate", sweep.transform, Vector3.zero, new Vector3(Random.Range(0.03f, 0.09f), 0.008f, Random.Range(0.03f, 0.07f)), mat, false);
                s.transform.position = at;
                s.transform.rotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 360f), 0f);
            }
            g.chores.Mess("shards", "Sweep up the broken plate (kitchen)", () => sweep == null || sweep.Finished, () => sweep != null ? sweep.transform.position : mid,
                "You sweep up the pieces. The pattern is hers. You'd have sworn you put that set away.");
            g.Log("haunting: one of " + Who("rel", "their") + "'s plates broke in the kitchen" + (tale != null && tale.HasSign("plates") ? " (this house's sign)" : ""));
            return true;
        }

        bool Name()
        {
            Room far = FarRoom();
            Sfx.At("whisper", far.center + Vector3.up * 1.5f, 0.5f, 0.85f);
            g.hud.Say(Fill("From the " + far.name + ": {voice}."), 3.5f);
            return true;
        }

        bool Radio()
        {
            Vector3 radio = g.house.InRoom("study", new Vector3(-2.2f, 1.0f, 7.5f));
            if ((radio - PlayerPos).magnitude < 2.5f && Vis.Sees(Cam, radio)) return false;
            LoopAt("static", radio, 0.45f, 3.5f, 0.9f);
            StartCoroutine(Later(3.6f, () => { LoopAt("whisper", radio, 0.35f, 3f, 0.7f); }));
            return true;
        }

        static string Lower(string n) { return n.Length > 0 && char.IsUpper(n[0]) && !n.StartsWith("Grandma") && !n.StartsWith("Grandpa") ? char.ToLowerInvariant(n[0]) + n.Substring(1) : n; }

        IEnumerator Later(float s, System.Action a) { yield return new WaitForSeconds(s); a(); }

        // Something of theirs (or anything small) is somewhere it wasn't: the kitchen table, the desk, by the bed.
        bool Moved()
        {
            var pool = new List<Movable>();
            foreach (var m in g.house.movables)
            {
                if (m == null || m.InUse || m.Heavy || m.FromHome > 0.3f) continue;
                if (Vis.Sees(Cam, m.transform.position, m.transform) || (m.transform.position - PlayerPos).magnitude < 3f) continue;
                pool.Add(m);
            }
            if (pool.Count == 0) return false;
            var pick = pool[Random.Range(0, pool.Count)];
            // Where they'd have wanted it: their place at a table, a pillow, the arm of a chair.
            Vector3[] places =
            {
                g.house.InRoom("kitchen", new Vector3(-3.75f, 0.78f, -4.15f)),
                g.house.InRoom("study", new Vector3(-6.1f, 0.83f, 3.3f)),
                g.house.InRoom("bedroom", new Vector3(6.3f, 0.72f, 6.2f)),
                new Vector3(-1.15f, 0.93f, -2.3f),
            };
            var ok = new List<Vector3>();
            foreach (var p in places) if (!Vis.Sees(Cam, p) && (p - PlayerPos).magnitude > 3f && (p - pick.transform.position).magnitude > 1f) ok.Add(p);
            if (ok.Count == 0) return false;
            Vector3 to = ok[Random.Range(0, ok.Count)];
            pick.Teleport(to + Vector3.up * 0.02f, Quaternion.Euler(0f, Models.Face(PlayerPos.x - to.x, PlayerPos.z - to.z), 0f));
            g.Log("haunting: moved '" + pick.name + "' to somewhere it would have been kept");
            var moved = pick;
            g.chores.Mess("moved:" + pick.GetInstanceID(), "Put " + Lower(pick.name) + " back where it goes", () => moved == null || (!moved.held && moved.FromHome < 0.45f),
                () => moved != null ? moved.transform.position : g.house.hallNorth, "Back where it lives. You leave your hand on it a moment.");
            return true;
        }

        // The photographs on the mantel and the hall table, turned (only the ones you can't see) to face you.
        bool Facing()
        {
            int turned = 0;
            foreach (var m in g.house.standingPhotos)
            {
                if (m == null || m.held || Vis.Sees(Cam, m.transform.position, m.transform)) continue;
                Vector3 to = PlayerPos - m.transform.position;
                m.Teleport(m.transform.position, Quaternion.Euler(0f, Models.Face(to.x, to.z) + 180f, 0f));
                turned++;
            }
            return turned > 0;
        }

        // Knuckles on wood, from inside the wardrobe: the way you'd knock if you had to be let out but didn't want to wake anyone.
        bool Rap()
        {
            if (g.house.wardrobe == null) return false;
            Vector3 w = g.house.wardrobe.position + Vector3.up * 1.1f;
            if ((w - PlayerPos).magnitude > 9f) return false;
            StartCoroutine(Raps(w, 3));
            return true;
        }

        IEnumerator Raps(Vector3 at, int n)
        {
            for (int i = 0; i < n; i++) { Sfx.At("tap", at, 0.6f, 0.95f + Random.value * 0.1f); yield return new WaitForSeconds(0.38f + Random.value * 0.1f); }
        }

        bool AnswerLater()
        {
            // Unprompted, it just knocks: three, from a room you aren't in. (When you slam a door, it answers in kind; see OnSlammed.)
            Room far = FarRoom();
            StartCoroutine(Raps(WallNear(far.center), 3));
            return true;
        }

        // The bulb in the room you're walking into: a click, a tick of hot glass, dark.
        bool Bulb()
        {
            Room here = g.house.RoomAt(PlayerPos);
            if (here == null || here.light == null || Time.time < here.darkUntil) return false;
            here.darkUntil = Time.time + 25f;
            Sfx.At("click", here.light.transform.position, 0.6f, 1.4f);
            Sfx.At("crack", here.light.transform.position, 0.12f, 2.2f);
            g.Log("haunting: the bulb in the " + here.name + " went, as you came in");
            return true;
        }

        // Somebody has been eating here: bread gone, a tin opened with a knife, rubbish tied with a knot you don't tie.
        bool Food()
        {
            Room k = g.house.Find("kitchen");
            if (k == null || g.house.RoomAt(PlayerPos) == k) return false;
            int took = 0;
            foreach (var m in g.house.food)
                if (m != null && m.gameObject.activeSelf && !m.held && took < 2) { m.gameObject.SetActive(false); took++; }
            // A bag of rubbish by the back door that isn't yours. It goes on the list: out to the dustbin.
            g.house.PutOutTheRubbish();
            g.chores.Mess("strange rubbish", "Take out the rubbish that isn't yours (kitchen)", () => g.house.RubbishOut, () => g.house.trashBag != null ? g.house.trashBag.transform.position : g.house.outsideBin.transform.position,
                "It clinks. Bottles, and something soft. You don't look.");
            g.Log("haunting: " + took + " things gone from the kitchen, and a bag of rubbish by the back door that isn't yours");
            return true;
        }

        bool Cold()
        {
            g.hud.Say("Your breath shows. Only here, for a step or two.", 3f);
            LoopAt("drone", PlayerPos + Vector3.up, 0.1f, 5f, 0.45f);
            return true;
        }

        // Someone looks in at you round the door frame while you're busy. Only ever at the edge of your eye.
        bool Peek()
        {
            Room here = g.house.RoomAt(PlayerPos);
            if (here == null || here == g.house.hall || here.door == null) return false;
            Vector3 d = here.door.Middle;
            Vector3 toDoor = d - Cam.transform.position; toDoor.y = 0f;
            float ang = Vector3.Angle(Vis.Flat(Cam.transform.forward), toDoor);
            if (ang < 55f || toDoor.magnitude < 2f) return false;               // you'd see it straight on: not now
            StartCoroutine(Peeking(here, d));
            return true;
        }

        IEnumerator Peeking(Room here, Vector3 door)
        {
            // Just outside the doorway, mostly behind the frame, leaning to look in.
            Vector3 outward = door - here.center; outward.y = 0f; outward.Normalize();
            var f = Build.Figure("Looking in", null);
            f.position = new Vector3(door.x, 0f, door.z) + outward * 0.45f;
            Vector3 look = PlayerPos - f.position; look.y = 0f;
            f.rotation = Quaternion.LookRotation(look.normalized) * Quaternion.Euler(0f, 0f, 9f);
            bool seenSide = false;
            for (float t = 0f; t < 3.5f; t += Time.deltaTime)
            {
                Vector3 to = f.position + Vector3.up * 1.6f - Cam.transform.position;
                float a = Vector3.Angle(Cam.transform.forward, to);
                if (a < 32f) break;                                           // you turned to it: gone
                if (a < 80f) seenSide = true;
                yield return null;
            }
            Destroy(f.gameObject);
            StartCoroutine(StepsAway(new Vector3(door.x, 0f, door.z) + outward * 1.2f));
            g.Log("haunting: someone looked in at you round the " + here.name + " door" + (seenSide ? " (at the edge of your eye)" : " (you didn't see)"));
        }

        IEnumerator StepsAway(Vector3 from)
        {
            Vector3 dir = new Vector3(0f, 0f, from.z > 0f ? 1f : -1f);
            for (int i = 0; i < 4; i++) { Sfx.At("step", from + dir * (i * 0.9f), 0.45f, 1.1f); yield return new WaitForSeconds(0.3f); }
        }

        // The hall clock stops; the light goes thin; and then it's later than it was, and you were somewhere else.
        bool LostTime()
        {
            if (g.house.RoomAt(PlayerPos) != g.house.hall) return false;
            StartCoroutine(Gone());
            return true;
        }

        IEnumerator Gone()
        {
            g.house.Clock(false);
            g.hud.fadeSpeed = 3f;
            g.hud.fadeTarget = 0.97f;
            yield return new WaitForSeconds(1.1f);
            g.player.Teleport(g.player.transform.position + g.player.Forward * 1.5f, g.player.transform.eulerAngles.y + 160f);
            g.hud.fadeTarget = 0f;
            g.hud.fadeSpeed = 1.2f;
            yield return new WaitForSeconds(1f);
            g.hud.Say("The hall clock has stopped. When it starts again it's twenty minutes on, and you're facing the other way.", 5f);
            yield return new WaitForSeconds(4f);
            g.house.Clock(true);
        }

        // At the far end of the hall, where the light doesn't reach: a shape the right height. When the light comes back, nothing.
        bool Bottom()
        {
            if (g.house.RoomAt(PlayerPos) != g.house.hall || figure != null) return false;
            float dir = PlayerPos.z > 0f ? -1f : 1f;
            Vector3 at = new Vector3(0f, 0f, Mathf.Clamp(PlayerPos.z + dir * 7f, -7.2f, 7.2f));
            if ((at - PlayerPos).magnitude < 5.5f) return false;
            StartCoroutine(AtTheEnd(at));
            return true;
        }

        IEnumerator AtTheEnd(Vector3 at)
        {
            var hall = g.house.hall;
            hall.darkUntil = Time.time + 6f;
            var f = Build.Figure("At the end of the hall", null);
            f.position = at;
            Vector3 look = PlayerPos - at; look.y = 0f;
            f.rotation = Quaternion.LookRotation(look.normalized);
            // Light from behind it (a door left open at the end of the hall): a shape against it, nothing more.
            var back = Build.Lamp("Behind it", f, -Vector3.forward * 0.9f + Vector3.up * 1.4f, new Color(0.7f, 0.75f, 0.9f), 2.2f, 4.5f);
            back.transform.SetParent(f, true);
            float seen = 0f;
            for (float t = 0f; t < 6f; t += Time.deltaTime)
            {
                if (Vis.Sees(Cam, at + Vector3.up * 1.5f, f)) seen += Time.deltaTime;
                if ((PlayerPos - at).magnitude < 5f) break;
                yield return null;
            }
            Destroy(f.gameObject);
            hall.darkUntil = Time.time + 0.3f;
            g.Log("haunting: a figure at the end of the hall in the dark" + (seen > 0.3f ? " (you saw it for " + seen.ToString("0.0") + " s)" : " (you didn't look)"));
        }

        // Bare wet footprints from the back of the hall to the room you're in (or past you), drying as you look.
        bool Prints()
        {
            Room here = g.house.RoomAt(PlayerPos);
            Vector3 to = here != null && here != g.house.hall && here.door != null ? here.door.Middle : PlayerPos;
            Vector3 from = new Vector3(0f, 0f, to.z > 0f ? -7.4f : 7.4f);
            if (Vis.Sees(Cam, from + Vector3.up * 0.2f) && (from - PlayerPos).magnitude < 4f) return false;
            StartCoroutine(Drying(from, new Vector3(to.x, 0f, to.z)));
            return true;
        }

        IEnumerator Drying(Vector3 from, Vector3 to)
        {
            var wet = Build.Mat(new Color(0.05f, 0.04f, 0.035f), 0f, 0.92f);
            var steps = new List<Transform>();
            float len = Vis.Level(to - from).magnitude;
            Vector3 dir = Vis.Level(to - from).normalized, side = Vector3.Cross(Vector3.up, dir);
            for (float d = 0.3f; d < len - 0.4f; d += 0.62f)
            {
                int k = steps.Count;
                Vector3 p = from + dir * d + side * (k % 2 == 0 ? 0.11f : -0.11f) + Vector3.up * 0.004f;
                var foot = Build.Prim(PrimitiveType.Sphere, "Wet footprint", null, p, new Vector3(0.1f, 0.003f, 0.25f), wet, false).transform;
                foot.rotation = Quaternion.LookRotation(dir);
                var toes = Build.Prim(PrimitiveType.Sphere, "Wet footprint", foot, new Vector3(0f, 0f, 0.58f), new Vector3(1.1f, 1f, 0.35f), wet, false);
                steps.Add(foot);
            }
            g.Log("haunting: " + steps.Count + " wet footprints across the hall, toward " + (g.house.RoomAt(to) != null ? "the " + g.house.RoomAt(to).name : "you"));
            // They dry from the far end, the way they were made, but the floor under the last few stays wet.
            if (steps.Count > 0)
            {
                Vector3 last = steps[steps.Count - 1].position;
                // A real puddle where they stop: it spreads, shines, leaves a green-brown tide mark if it's left to dry.
                Liquid.Spill(new Vector3(last.x, 0.4f, last.z), 0.5f, "pond water", "Wipe up the wet floor (a cloth from the kitchen)",
                    "The water is cold and smells of the pond. You wring the cloth out twice.");
            }
            yield return new WaitForSeconds(9f);
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] != null) Destroy(steps[i].gameObject);
                yield return new WaitForSeconds(1.4f);
            }
        }

        // ---------------------------------------------------------------- doors

        // You're opening a door slowly. Through the first few inches of gap: someone standing in the room, facing the
        // door. By the time it's open, no one. (This house does it more if it's one of its signs; it never does it twice running.)
        void OnOpeningSlowly(Door d, float openness)
        {
            if (demoForce) { if (openness > 0.12f || Time.time - lastGap < 8f) return; lastGap = Time.time; StartCoroutine(InTheGap(d)); return; }
            if (paused || openness > 0.12f || Time.time - lastGap < 70f) return;
            bool dream = g.phase == Phase.Dream;
            if (!dream && (!InHouse || g.day < 2)) return;
            float chance = tale != null && tale.HasSign("gap") ? 0.55f : 0.15f;
            if (dream) chance *= 0.6f;
            lastGap = Time.time;                                    // decided once per opening, either way
            if (Random.value > chance) return;
            StartCoroutine(InTheGap(d));
        }

        IEnumerator InTheGap(Door d)
        {
            Vector3 eye = Cam.transform.position;
            // The gap opens at the latch edge: look through there.
            Vector3 latch = d.HingePos + Vis.Flat(d.Middle - d.HingePos) * 1.85f + Vector3.up * 1.4f;
            Vector3 through = latch - eye; through.y = 0f;
            if (through.sqrMagnitude < 1e-4f) yield break;
            through.Normalize();
            Vector3 at = latch + through * 2.2f;
            at.y = g.player.transform.position.y;
            // Only if there's floor there and nothing in the way.
            if (Physics.CheckCapsule(at + Vector3.up * 0.4f, at + Vector3.up * 1.6f, 0.25f, ~0, QueryTriggerInteraction.Ignore)) yield break;
            var f = Build.Figure("In the gap", null);
            f.position = at;
            f.rotation = Quaternion.LookRotation(-through);
            g.Log("haunting: someone standing in the room, facing the door, as you open it slowly");
            for (float t = 0f; t < 8f; t += Time.deltaTime)
            {
                if (d == null || d.Openness > 0.62f || (!d.Held && t > 3f)) break;
                yield return null;
            }
            if (f != null) Destroy(f.gameObject);
            if (d != null && d.Openness > 0.62f) g.Log("haunting: and by the time the door was open, nobody");
        }

        // You slammed a door. It answers in kind: the same number of knocks, from somewhere you aren't; or a slam of its own.
        void OnSlammed(Door d, float loud)
        {
            if ((paused && !demoForce) || !InHouse) return;
            float now = Time.time;
            if (now > slamWindow) slamsRecent = 0;
            slamsRecent++;
            slamWindow = now + 6f;
            if (now - lastAnswer < 4f) return;
            bool answers = tale != null && tale.HasSign("answer"), slamsBack = tale != null && tale.HasSign("slamback");
            if (!answers && !slamsBack && Random.value > 0.25f && !demoForce) return;
            lastAnswer = now;
            StartCoroutine(Answer(slamsBack && (!answers || Random.value < 0.5f)));
        }

        IEnumerator Answer(bool slam)
        {
            yield return new WaitForSeconds(1.6f + Random.value * 1.4f);
            if (slam)
            {
                foreach (var other in g.house.doors)
                    if (other.gameObject.activeSelf && !Vis.Sees(Cam, other.Middle, other.transform) && (other.Middle - PlayerPos).magnitude > 4f)
                    {
                        other.Set(!other.open, true);
                        g.Log("haunting: you slammed a door, and somewhere else in the house a door slammed back");
                        yield break;
                    }
            }
            int n = Mathf.Clamp(slamsRecent, 1, 5);
            StartCoroutine(Raps(WallNear(FarRoom().center), n));
            g.Log("haunting: you slammed " + (n == 1 ? "a door" : n + " doors") + ", and something knocked back " + n + (n == 1 ? " time" : " times"));
        }
    }
}
