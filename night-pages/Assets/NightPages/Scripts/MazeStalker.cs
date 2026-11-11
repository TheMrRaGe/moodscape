using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The thing in the dream maze, and how it hunts. It comes in waves, the way a Left 4 Dead horde does: quiet,
    /// then three knocks from somewhere in the walls, then it is in the maze and coming straight at you. A wave lasts
    /// about half a minute; then it goes, and there is quiet again (less of it as the night runs down).
    ///
    /// It notices by sight: it has to be facing you, with nothing in between, and near enough: across a whole lit
    /// room, but only a few steps in a dark one. If it loses you it goes to where it last saw you and looks about.
    ///
    /// You can hide: shut yourself in a locker or a cabinet, or get under a table (which only works in a dark room).
    /// If it didn't see you go in, it doesn't know. But if it lost you in that room it knows you might be there, and
    /// it toys with you: goes from one hiding place to the next, stands outside each, breathing, scratching at it,
    /// and now and then tears one open. If it watched you get in, it simply comes and gets you.
    /// </summary>
    public class MazeStalker
    {
        public enum State { Calm, Warning, Hunt, Search, Toy, Found, Leave }
        public State state = State.Calm;
        public Monster It { get; private set; }
        public DreamMaze.HideSpot Hidden { get; private set; }
        public int waves, timesHidden, timesSeen, toyed;
        public System.Action<string> onCatch;

        readonly GameDirector g;
        readonly DreamMaze maze;
        readonly Transform dyn;
        readonly int day;
        readonly float floorY;
        float until, huntEnds, knowsUntil, lastSeenAt = -99f, hidAt, stepAt, waitUntil, breathAt;
        Vector3 lastKnown, goal;
        bool sawEnter, saidHide, saidOut, wasSeeing;
        readonly List<DreamMaze.HideSpot> toCheck = new List<DreamMaze.HideSpot>();
        DreamMaze.HideSpot checking;

        public MazeStalker(GameDirector director, DreamMaze maze, Transform dyn, int day, float floorY)
        {
            g = director;
            this.maze = maze;
            this.dyn = dyn;
            this.day = day;
            this.floorY = floorY;
            until = Time.time + 22f;                     // the first wave never comes at once
        }

        Vector3 Pos { get { return It.transform.position; } }

        /// <summary>The next wave starts now (for demos and tests).</summary>
        public void ForceWave() { if (state == State.Calm) until = Time.time; }

        /// <summary>No wave for at least this long.</summary>
        public void Delay(float seconds) { if (state == State.Calm) until = Mathf.Max(until, Time.time + seconds); }

        // ---------------------------------------------------------------- hiding

        public void Hide(DreamMaze.HideSpot spot)
        {
            if (Hidden != null || spot == null) return;
            Vector3 pp = g.player.transform.position;
            if (spot.under && !maze.Dark(spot.inside) && !saidOut) { saidOut = true; g.hud.Say("Under a table, with the light on. It would see you.", 3.5f); }
            // Did it watch you get in?
            sawEnter = It != null && (Sees(pp, true) || Time.time - lastSeenAt < 0.7f);
            Hidden = spot;
            hidAt = Time.time;
            timesHidden++;
            spot.solid.enabled = false;
            g.player.Teleport(spot.inside, spot.yaw);
            g.player.LookPitch(spot.under ? -4f : 2f);
            g.player.speedMul = 0f;
            if (spot.under) g.player.Crouch(true);
            if (!saidHide) { saidHide = true; g.hud.Say("[E] to come out. It can't see you in here, unless it saw you get in.", 4.5f); }
            Sfx.At(spot.under ? "step" : "creak", spot.inside + Vector3.up, 0.5f, 1.1f);
            g.Log("dream maze: hid " + (spot.under ? "under the " : "in the ") + spot.what + (sawEnter ? " (and it saw)" : "") + (spot.under && !maze.Dark(spot.inside) ? " in a lit room" : ""));
        }

        public void Unhide()
        {
            if (Hidden == null) return;
            var spot = Hidden;
            Hidden = null;
            g.player.Crouch(false);
            g.player.speedMul = 1f;
            g.player.Teleport(spot.outside, spot.yaw);
            if (spot.solid != null) spot.solid.enabled = true;
            Sfx.At("creak", spot.outside + Vector3.up, 0.4f, 1.2f);
        }

        public void Cleanup()
        {
            if (Hidden != null) { g.player.Crouch(false); g.player.speedMul = 1f; Hidden = null; }
            if (It != null) Object.Destroy(It.gameObject);
            It = null;
        }

        // ---------------------------------------------------------------- sight

        // Can it see the dreamer at `pp`? Facing them (or very close), near enough for the light, nothing in the way.
        bool Sees(Vector3 pp, bool ignoreHiding = false)
        {
            if (It == null) return false;
            bool dark = maze.Dark(pp);
            float range = dark ? 5.5f : 17f;
            float height = 1.4f;
            if (Hidden != null && !ignoreHiding)
            {
                if (!Hidden.under || dark) return false;       // shut in a locker, or under a table in the dark: not there
                range = 6f; height = 0.4f;                     // under a table with the light on: it only has to look
            }
            Vector3 eye = It.HeadPos, target = pp + Vector3.up * height;
            Vector3 to = target - eye;
            float dist = to.magnitude;
            if (dist > range) return false;
            Vector3 flat = Vis.Level(to);
            if (dist > 1.8f && Vector3.Angle(It.transform.forward, flat) > 65f) return false;
            RaycastHit hit;
            if (Physics.Linecast(eye, target, out hit, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Player>() == null) return false;
            return true;
        }

        // ---------------------------------------------------------------- moving

        // Walk toward `target` through the doorways as they are now. Returns the distance still to go (as the crow flies).
        float Move(Vector3 target, float speed, float dt)
        {
            if (It.Transforming) return (target - Pos).magnitude;          // it stops to become something else
            Vector3 next = maze.GuideTo(Pos, target);
            Vector3 d = Vis.Level(next - Pos);
            float step = Mathf.Min(speed * dt, d.magnitude);
            if (d.sqrMagnitude > 1e-6f)
            {
                Vector3 p = Pos + d.normalized * step;
                p.y = floorY;
                It.transform.position = p;
                It.transform.rotation = Quaternion.Slerp(It.transform.rotation, Quaternion.LookRotation(d.normalized), dt * 7f);
                if (Time.time > stepAt)
                {
                    stepAt = Time.time + Mathf.Clamp(1.5f / Mathf.Max(0.5f, speed), 0.32f, 0.9f);
                    Sfx.At("step", Pos, 0.75f, 0.7f);
                }
            }
            return Vis.Level(target - Pos).magnitude;
        }

        void Face(Vector3 at, float dt)
        {
            Vector3 d = Vis.Level(at - Pos);
            if (d.sqrMagnitude > 1e-4f) It.transform.rotation = Quaternion.Slerp(It.transform.rotation, Quaternion.LookRotation(d.normalized), dt * 5f);
        }

        void Go(State s, string why)
        {
            if (s == state) return;
            g.Log("dream maze: it " + why);
            state = s;
        }

        // ---------------------------------------------------------------- every frame

        /// <summary>`left01`: how much of the night is left (1 at the start, 0 at the end).</summary>
        public void Tick(float now, float dt, Vector3 pp, Camera cam, float left01)
        {
            if (Hidden != null && now > hidAt + 0.35f && In.Down(Btn.Interact)) Unhide();

            if (state == State.Calm)
            {
                if (now < until) return;
                // Three knocks from somewhere in the walls: it's coming.
                state = State.Warning;
                until = now + 4.5f;
                Vector3 from = maze.FarUnseen(pp, 3, cam);
                for (int i = 0; i < 3; i++) g.StartCoroutine(Knock(from, i * 0.55f));
                if (waves == 0) g.hud.Say("Something is coming. Run, or hide.", 4f);
                g.Log("dream maze: a wave is coming (three knocks)");
                return;
            }
            if (state == State.Warning)
            {
                if (now < until) return;
                waves++;
                It = Monster.Create(dyn, g.fear.Look());
                Vector3 at = maze.FarUnseen(pp, 3, cam);
                at.y = floorY;
                It.transform.position = at;
                It.FaceToward(pp);
                // A little light of its own, low and red: in a dark room you see that it's there before you see what it is.
                var glow = new GameObject("Its light").AddComponent<Light>();
                glow.transform.SetParent(It.transform, false);
                glow.transform.localPosition = new Vector3(0f, 1.5f, 0.35f);
                glow.type = LightType.Point; glow.color = new Color(1f, 0.25f, 0.12f); glow.intensity = 1.1f; glow.range = 4.5f;
                huntEnds = now + Mathf.Min(40f, 22f + 2.5f * day);
                knowsUntil = now + 7f;                    // at first it knows exactly where you are
                lastKnown = pp;
                lastSeenAt = -99f;
                state = State.Hunt;
                g.Log("dream maze: wave " + waves + ": it is in the maze, " + maze.Rooms(at, pp) + " rooms away, for " + (huntEnds - now).ToString("0") + " s");
                return;
            }
            if (It == null) { state = State.Calm; until = now + 10f; return; }

            bool sees = Sees(pp);
            if (sees)
            {
                if (!wasSeeing) { timesSeen++; Sfx.At("gasp", It.HeadPos, 0.5f, 0.6f); }
                lastKnown = pp;
                lastSeenAt = now;
            }
            wasSeeing = sees;
            float dist = Vis.Level(pp - Pos).magnitude;

            switch (state)
            {
                case State.Hunt:
                {
                    if (!sees && now < knowsUntil && Hidden == null) lastKnown = pp;
                    float left = Move(lastKnown, sees || now - lastSeenAt < 2f ? 3.9f : 3.1f, dt);
                    if (sees && dist < 1.15f) { Catch("it reached you"); return; }
                    if (Hidden != null && sawEnter && maze.Rooms(Pos, Hidden.inside) <= 1) { Go(State.Found, "watched you hide, and is coming to get you"); return; }
                    if (!sees && left < 1.6f)
                    {
                        // It has got to where it last knew you were, and you aren't there.
                        // (If there's anywhere to hide in this room, it goes through them; if not, it looks elsewhere.)
                        BeginToy(pp);
                        return;
                    }
                    if (now > huntEnds && !sees && now - lastSeenAt > 4f) BeginLeave(pp, cam);
                    break;
                }
                case State.Search:
                {
                    if (sees) { Go(State.Hunt, "has seen you"); return; }
                    if (Move(goal, 2.2f, dt) < 1.2f)
                    {
                        if (now > huntEnds) BeginLeave(pp, cam);
                        else goal = maze.RandomPoint(Pos, 5f, 14f, false, cam);
                    }
                    break;
                }
                case State.Toy:
                {
                    if (sees) { checking = null; Go(State.Hunt, "has seen you"); return; }
                    if (checking == null)
                    {
                        if (toCheck.Count == 0) { if (now > huntEnds) BeginLeave(pp, cam); else { goal = maze.RandomPoint(Pos, 5f, 14f, false, cam); Go(State.Search, "has finished with this room and is looking elsewhere"); } return; }
                        checking = toCheck[0];
                        toCheck.RemoveAt(0);
                        waitUntil = 0f;
                    }
                    if (checking.root == null) { checking = null; return; }
                    if (waitUntil == 0f)
                    {
                        // Unhurried. It knows you can hear it.
                        if (Move(checking.outside, 1.3f, dt) < 0.5f)
                        {
                            waitUntil = now + Random.Range(2.6f, 4.8f);
                            breathAt = 0f;
                            Sfx.At("scratch", checking.inside + Vector3.up * 1.2f, 0.7f, 0.8f);
                        }
                    }
                    else
                    {
                        Face(checking.inside, dt);
                        if (now > breathAt) { breathAt = now + 1.5f; Sfx.At("breath", It.HeadPos, 0.75f, 0.75f); }
                        if (now > waitUntil)
                        {
                            bool yours = checking == Hidden;
                            // It hits the door. Usually that's all. Sometimes it opens it.
                            Sfx.At("slam", checking.inside + Vector3.up * 1.2f, 0.9f, 0.8f);
                            if (yours) Look.Kick(0.7f);
                            float open = Mathf.Min(0.42f, 0.16f + 0.04f * day);
                            if (yours && Random.value < open) { Catch("it found where you were hiding"); return; }
                            if (yours) { g.Shake(0.02f, "it stood outside your hiding place"); g.Log("dream maze: it stood at your " + checking.what + " and didn't open it"); }
                            checking = null;
                        }
                    }
                    break;
                }
                case State.Found:
                {
                    if (Hidden == null) { Go(State.Hunt, "saw you come out"); lastKnown = pp; return; }
                    if (waitUntil == 0f)
                    {
                        if (Move(Hidden.outside, 2.4f, dt) < 0.5f) { waitUntil = now + 1.5f; Sfx.At("scratch", Hidden.inside + Vector3.up * 1.2f, 0.8f, 0.7f); }
                    }
                    else
                    {
                        Face(Hidden.inside, dt);
                        if (now > waitUntil) { Sfx.At("slam", Hidden.inside + Vector3.up, 1f, 0.7f); Catch("it watched you hide"); return; }
                    }
                    break;
                }
                case State.Leave:
                {
                    if (sees && dist < 6f) { huntEnds = now + 8f; Go(State.Hunt, "turned back: it saw you"); return; }
                    float left = Move(goal, 2.6f, dt);
                    bool unseen = !Vis.Sees(cam, It.HeadPos, It.transform) && !Vis.Sees(cam, Pos + Vector3.up * 0.6f, It.transform);
                    if ((unseen && (dist > 9f || left < 1.5f)) || now > until)
                    {
                        Object.Destroy(It.gameObject);
                        It = null;
                        state = State.Calm;
                        until = now + Mathf.Lerp(14f, 32f, left01) - Mathf.Min(6f, day);
                        g.Log("dream maze: it has gone. Quiet for " + (until - now).ToString("0") + " s");
                    }
                    break;
                }
            }
        }

        void BeginToy(Vector3 pp)
        {
            // Every hiding place in the room it lost you in. If you're in one of them, that one is usually on its list.
            toCheck.Clear();
            var here = new List<DreamMaze.HideSpot>(maze.HidesIn(lastKnown));
            for (int i = here.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var t = here[i]; here[i] = here[j]; here[j] = t; }
            foreach (var h in here) if (h != Hidden || Random.value < 0.8f) toCheck.Add(h);
            if (toCheck.Count == 0) { goal = maze.RandomPoint(Pos, 5f, 14f, false, null); Go(State.Search, "has lost you and is looking"); return; }
            checking = null;
            toyed++;
            Go(State.Toy, "knows you might be hiding here, and is taking its time (" + toCheck.Count + " places to look)");
        }

        void BeginLeave(Vector3 pp, Camera cam)
        {
            goal = maze.FarUnseen(pp, 4, cam);
            until = Time.time + 14f;
            waitUntil = 0f;
            Go(State.Leave, "is leaving: the wave is over");
        }

        void Catch(string how)
        {
            g.Log("dream maze: " + how);
            Sfx.Ui("sting", 0.9f);
            if (onCatch != null) onCatch(how);
        }

        System.Collections.IEnumerator Knock(Vector3 at, float delay)
        {
            yield return new WaitForSeconds(delay);
            Sfx.At("knock", at + Vector3.up * 1.5f, 1f, 0.7f);
            Sfx.Ui("knock", 0.25f, 0.6f);                 // (and faintly wherever you are: it's in the walls)
        }
    }
}
