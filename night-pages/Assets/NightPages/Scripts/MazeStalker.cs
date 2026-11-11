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
        /// <summary>For demos and tests: what it looks like next wave (otherwise the fear engine's choice).</summary>
        public int[] forceLook;

        readonly GameDirector g;
        readonly DreamMaze maze;
        readonly Transform dyn;
        readonly int day;
        readonly float floorY;
        float until, huntEnds, knowsUntil, lastSeenAt = -99f, hidAt, stepAt, waitUntil, breathAt, nextChange;
        public int changes;
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

        // Where it is, on the floor plan. (When it's up a wall or across the ceiling, this is the spot on the floor
        // under it, and the body is drawn up there; see Surfaces.)
        Vector3 Pos { get { return surf == 0 ? It.transform.position : virt; } }

        // Crawling things don't keep to the floor. 0 on the floor, 1 going to a wall, 2 climbing it, 3 on the ceiling, 4 dropping.
        int surf;
        Vector3 virt, wallSpot, wallNormal, heading = Vector3.forward;
        float climbY, fallV, ceilingSince, nextClimb;
        public int climbs, drops;
        public bool OffTheFloor { get { return surf >= 2; } }

        /// <summary>It changes the next chance it gets (for demos and tests).</summary>
        public void ForceChange() { nextChange = 0f; }

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
            if (It.Crawler && surf == 0 && state != State.Toy && state != State.Found && Time.time > nextClimb)
            {
                nextClimb = Time.time + 1.5f;
                // Somewhere to go a fair way off in this room: up the wall and over, sometimes.
                if (Vis.Level(target - Pos).magnitude > 4f && maze.SameRoom(Pos, target) == false && Random.value < 0.75f && maze.ClimbSpot(Pos, out wallSpot, out wallNormal))
                {
                    surf = 1; virt = It.transform.position;
                    g.Log("dream maze: it is going up the wall");
                }
                else if (Vis.Level(target - Pos).magnitude > 4f && Random.value < 0.4f && maze.ClimbSpot(Pos, out wallSpot, out wallNormal))
                {
                    surf = 1; virt = It.transform.position;
                    g.Log("dream maze: it is going up the wall");
                }
            }
            if (surf > 0) return Crawl(target, speed, dt);
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

        // Up the wall, across the ceiling, and down again (dropping, if it's over you).
        float Crawl(Vector3 target, float speed, float dt)
        {
            float floorY = maze.FloorY, ceil = maze.CeilingOver(virt);
            Transform tr = It.transform;
            switch (surf)
            {
                case 1:     // to the foot of the wall
                {
                    Vector3 d = Vis.Level(wallSpot - virt);
                    if (d.magnitude < 0.25f || ceil <= 0f) { surf = ceil > 0f ? 2 : 0; climbY = 0f; climbs++; break; }
                    virt += d.normalized * Mathf.Min(speed * dt, d.magnitude);
                    tr.position = new Vector3(virt.x, floorY, virt.z);
                    tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.LookRotation(-wallNormal), dt * 8f);
                    break;
                }
                case 2:     // straight up it, head first, its back to the room
                    if (ceil <= 0f) { surf = 4; fallV = 0f; break; }
                    climbY += speed * 0.9f * dt;
                    tr.position = new Vector3(wallSpot.x, floorY + climbY, wallSpot.z) - wallNormal * 0.05f;
                    tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.LookRotation(Vector3.up, wallNormal), dt * 9f);
                    if (Time.time > stepAt) { stepAt = Time.time + 0.35f; Sfx.At("scratch", tr.position, 0.55f, 1.3f); }
                    if (climbY >= ceil - 0.25f) { surf = 3; ceilingSince = Time.time; virt = wallSpot + wallNormal * 0.5f; heading = wallNormal; }
                    break;
                case 3:     // upside down across the ceiling
                {
                    if (ceil <= 0f) { surf = 4; fallV = 0f; break; }
                    Vector3 next = maze.GuideTo(virt, target);
                    Vector3 d = Vis.Level(next - virt);
                    bool toDoor = (next - target).sqrMagnitude > 0.01f && d.magnitude < 2.2f;        // a doorway's next: it can't go through on the ceiling
                    bool overYou = state == State.Hunt && Vis.Level(target - virt).magnitude < 1.6f;
                    bool mustLand = state == State.Toy || state == State.Found || Time.time - ceilingSince > 14f;
                    if (toDoor || overYou || mustLand)
                    {
                        surf = 4; fallV = 0f; drops++;
                        g.Log("dream maze: it drops from the ceiling" + (overYou ? ", right in front of you" : ""));
                        break;
                    }
                    if (d.sqrMagnitude > 1e-6f)
                    {
                        heading = Vector3.Slerp(heading, d.normalized, dt * 6f);
                        virt += d.normalized * Mathf.Min(speed * 1.1f * dt, d.magnitude);
                    }
                    tr.position = new Vector3(virt.x, floorY + ceil, virt.z);
                    tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.LookRotation(heading, Vector3.down), dt * 8f);
                    if (Time.time > stepAt) { stepAt = Time.time + 0.3f; Sfx.At("scratch", tr.position, 0.6f, 1.2f); }
                    break;
                }
                case 4:     // falling, turning over as it comes
                {
                    fallV += 9.8f * dt;
                    Vector3 p = tr.position;
                    p.y -= fallV * dt;
                    tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.LookRotation(heading, Vector3.up), dt * 10f);
                    if (p.y <= floorY)
                    {
                        p.y = floorY;
                        surf = 0;
                        tr.rotation = Quaternion.LookRotation(heading, Vector3.up);
                        nextClimb = Time.time + Random.Range(6f, 12f);
                        Sfx.At("slam", p, 1f, 0.45f);
                        if (Vis.Level(g.player.transform.position - p).magnitude < 5f) { Look.Kick(0.6f); g.Shake(0.02f, "it dropped from the ceiling near you"); }
                    }
                    tr.position = p;
                    break;
                }
            }
            return Vis.Level(target - Pos).magnitude;
        }

        void Face(Vector3 at, float dt)
        {
            if (It.Transforming || surf != 0) return;
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
                It = Monster.Create(dyn, forceLook ?? g.fear.Look());
                forceLook = null;
                Vector3 at = maze.FarUnseen(pp, 3, cam);
                at.y = floorY;
                It.transform.position = at;
                It.FaceToward(pp);
                surf = 0;
                nextClimb = now + Random.Range(0.5f, 2f);
                g.Log("dream maze: this one " + (It.Crawler ? "crawls: it can take the walls and the ceiling" : "walks"));
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
                nextChange = now + Random.Range(6f, 12f);
                g.Log("dream maze: wave " + waves + ": it is in the maze, " + maze.Rooms(at, pp) + " rooms away, for " + (huntEnds - now).ToString("0") + " s");
                return;
            }
            if (It == null) { state = State.Calm; until = now + 10f; return; }

            // It doesn't stay one thing. Now and then it stops, and becomes something else, where it stands: more
            // often when it knows you're watching it (in sight, or outside your hiding place).
            if (now > nextChange && !It.Transforming && state != State.Leave)
            {
                bool watched = Vis.Sees(cam, It.HeadPos, It.transform) || Vis.Sees(cam, Pos + Vector3.up * 0.7f, It.transform);
                bool atYou = state == State.Toy && checking != null && checking == Hidden && waitUntil > 0f;
                if (watched || atYou || Random.value < 0.35f)
                {
                    var next = g.fear.Compose(2);
                    next[Monster.Shift] = 0;
                    changes++;
                    g.Log("dream maze: it is becoming something else" + (watched ? ", where you can see it" : "") + (atYou ? ", outside your hiding place" : "") + ": " + g.fear.DescribeLook(next));
                    g.StartCoroutine(It.TransformInto(next, watched ? 1.15f : 0.85f));
                    if (atYou) waitUntil = Mathf.Max(waitUntil, now + 7f);          // it takes its time over it, right there
                }
                nextChange = now + Random.Range(14f, 24f) - Mathf.Min(6f, day);
            }

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
                    if (sees && dist < 1.15f && surf == 0) { Catch("it reached you"); return; }
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
            // The last thing you see: it lunges, right into your face.
            if (It != null)
            {
                Vector3 to = Vis.Level(g.player.transform.position - It.transform.position);
                if (to.sqrMagnitude > 1e-4f)
                {
                    It.transform.rotation = Quaternion.LookRotation(to.normalized);
                    It.transform.position += to.normalized * Mathf.Max(0f, to.magnitude - 0.45f);
                }
                Look.Kick(1f);
            }
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
