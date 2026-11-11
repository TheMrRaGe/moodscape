using System.Collections;
using System.Text;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The demo film ("demo" job): one night's dream, played and filmed from the player's eyes, taking its time over
    /// everything the dream maze does. In order:
    ///  - walking the maze (the different rooms and doorways);
    ///  - each dream haunt in turn, asked for when the dreamer is somewhere it can happen, and then WATCHED: looking
    ///    down into the pit while crossing it, looking up the shaft where the ceiling was, standing while the walls
    ///    come in, watching the water rise, turning away from the watchers and back, and so on;
    ///  - the rot creeping over the walls throughout (the night is run on a schedule);
    ///  - a wave spent hidden in a locker, watching it come and go through the slats;
    ///  - a wave spent in the open: seeing it come, running, being caught; what dying is (asleep again, a new maze);
    ///  - the way out.
    /// The log says what each stretch of frames shows ("demo: ... at frame N").
    /// </summary>
    public partial class GameDirector
    {
        float demoStuck, demoBest = float.MaxValue, demoSideUntil;
        Vector3 demoGoal, demoSide;

        // ---------------------------------------------------------------- doors, by hand

        Door demoDoor;
        float demoDoorAt, demoDoorBest;
        int demoDoorPhase, demoDoorsOpened;
        readonly System.Collections.Generic.Dictionary<Door, float> demoDoorDone = new System.Collections.Generic.Dictionary<Door, float>();

        System.Collections.Generic.IEnumerable<Door> DemoDoorsAround()
        {
            if (phase == Phase.Dream && dream.Maze != null) return dream.Maze.DoorsNear(player.transform.position, 2.2f);
            return house.doors;
        }

        // A shut (or nearly shut) door just ahead: the demo stops and opens it by hand, slowly, the way a player
        // should: takes the handle, pushes (or pulls) it a little at a time, looks through the gap, then lets go.
        bool DemoDoorStep(Vector3 goal)
        {
            float now = Time.time, dt = Time.deltaTime;
            Vector3 pp = player.transform.position;
            if (demoDoor == null)
            {
                Vector3 fwd = Vis.Flat(goal - pp).normalized;
                foreach (var d in DemoDoorsAround())
                {
                    if (d == null || !d.gameObject.activeInHierarchy || d.Angle > 45f) continue;
                    Vector3 to = d.Middle - pp; to.y = 0f;
                    float done;
                    if (to.magnitude > 1.45f || Vector3.Dot(fwd, to.normalized) < 0.45f || (demoDoorDone.TryGetValue(d, out done) && now - done < 25f)) continue;
                    demoDoor = d; demoDoorAt = now; demoDoorPhase = 0; demoDoorBest = d.Angle;
                    demoDoorsOpened++;
                    if (demoDoorsOpened <= 8) Log("demo: opening a door by hand, slowly, at frame " + filmFrames);
                    break;
                }
                if (demoDoor == null) return false;
            }
            var door = demoDoor;
            float t = now - demoDoorAt;
            Vector3 eye = player.cam.transform.position;
            switch (demoDoorPhase)
            {
                case 0:     // reach for the handle
                    player.Aim(Vector3.Lerp(door.Middle + Vector3.up * -0.05f, door.HingePos + (door.Middle - door.HingePos) * 1.8f + Vector3.up * 1.0f, 0.5f));
                    if (t > 0.7f) { door.Grab(); demoDoorPhase = 1; demoDoorAt = now; }
                    break;
                case 1:     // push it, slowly; if it won't go, it opens the other way: pull
                case 2:
                {
                    float rate = demoDoorPhase == 1 ? 1f : -1f;
                    door.Drag(rate * (door.Openness < 0.4f ? 12f : 30f) * dt / 3.6f, eye, dt);
                    player.Aim(door.Middle + Vis.Flat(door.Middle - eye).normalized * 1.5f + Vector3.up * 0.1f);      // looking through the gap
                    if (demoDoorPhase == 2) player.WalkToward(pp - Vis.Flat(door.Middle - pp).normalized, 0.4f);
                    demoDoorBest = Mathf.Max(demoDoorBest, door.Angle);
                    if (demoDoorPhase == 1 && t > 1.2f && door.Angle < 4f) { demoDoorPhase = 2; demoDoorAt = now; }
                    if (door.Openness > 0.8f || t > 7.5f) { door.Release(); demoDoorPhase = 3; demoDoorAt = now; }
                    break;
                }
                default:    // a moment, then on
                    if (t > 0.6f) { demoDoorDone[door] = now; demoDoor = null; }
                    break;
            }
            return true;
        }

        // Swing an open door shut, hard: a slam that is yours.
        IEnumerator DemoSlam(Door d)
        {
            for (float t = 0f; t < 1.2f; t += Time.deltaTime) { player.Aim(d.Middle + Vector3.up * 0.2f); Frame(); yield return null; }
            d.Grab();
            d.Fling(420f);
            d.Release();
            for (float t = 0f; t < 1.5f; t += Time.deltaTime) { Frame(); yield return null; }
        }

        // One frame of walking toward a point, stepping round whatever is in the way.
        void DemoWalk(Vector3 goal, float speed)
        {
            if (DemoDoorStep(goal)) return;
            if ((goal - demoGoal).sqrMagnitude > 0.04f) { demoGoal = goal; demoBest = float.MaxValue; demoStuck = 0f; }
            if (Time.time < demoSideUntil) { player.WalkToward(demoSide, speed); return; }
            float d = player.WalkToward(goal, speed);
            if (d < demoBest - 0.03f) { demoBest = d; demoStuck = 0f; } else demoStuck += Time.deltaTime;
            if (demoStuck > 0.5f)
            {
                Vector3 fwd = Vis.Flat(goal - player.transform.position);
                demoSide = player.transform.position + Vector3.Cross(Vector3.up, fwd) * (Random.value < 0.5f ? -1.2f : 1.2f) + fwd * 0.3f;
                demoSideUntil = Time.time + 0.6f;
                demoStuck = 0f; demoBest = float.MaxValue;
            }
        }

        IEnumerator DemoFilm(StringBuilder summary)
        {
            yield return StartRunForTest();
            haunt.paused = true;
            yield return DemoHouse();
            haunt.demoForce = false;
            day = 3;
            phase = Phase.Dream;
            dream.Begin(3);
            RefreshObjectives();
            hud.fade = hud.fadeTarget = 0f;
            player.frozen = false;
            Time.captureFramerate = 24;
            var maze = dream.Maze;
            var st = dream.Stalker;
            float night = dream.TimeLeft;
            int deaths = dreamDeaths;
            System.Func<bool> on = () => dream.active && dream.Maze == maze && dreamDeaths == deaths && phase == Phase.Dream;
            // The night is held to a schedule (so the rot is seen creeping the whole film, and never runs out).
            float planned = 330f, began = Time.time;
            System.Action clock = () =>
            {
                float k = Mathf.Clamp01((Time.time - began) / planned);
                dream.AddTime(night * Mathf.Lerp(0.8f, 0.16f, k) - dream.TimeLeft);
            };
            // One filmed frame of standing and doing `act` (or nothing).
            System.Func<float, System.Action<float>, IEnumerator> stand = null;
            st.Delay(900f);
            maze.HoldHaunts(900f);

            dream.ToggleTorch(true);
            Log("demo: walking the maze, flashlight on, at frame " + filmFrames);
            Vector3 off = maze.FarUnseen(player.transform.position, 3, player.cam);
            for (float t = 0f; t < 14f && on(); t += Time.deltaTime)
            {
                clock();
                Vector3 pp = player.transform.position;
                if (maze.SameRoom(pp, off)) off = maze.FarUnseen(pp, 3, player.cam);
                DemoWalk(maze.GuideTo(pp, off), 2.3f);
                Frame();
                yield return null;
            }

            // Up close to a wall, and a hand comes out of it.
            if (on())
            {
                Log("demo: walking up to a wall at frame " + filmFrames);
                bool done = false;
                for (float t = 0f; t < 14f && on() && !done; t += Time.deltaTime)
                {
                    clock();
                    RaycastHit hit;
                    bool wall = Physics.Raycast(player.cam.transform.position, player.cam.transform.forward, out hit, 6f) && Mathf.Abs(hit.normal.y) < 0.25f && hit.collider.bounds.size.y > 2f;
                    if (!wall) player.Turn(70f * Time.deltaTime);
                    else if (hit.distance > 0.85f) DemoWalk(hit.point + hit.normal * 0.5f, 1.1f);
                    else
                    {
                        for (float s = 0f; s < 1.2f; s += Time.deltaTime) { Frame(); yield return null; }
                        done = WallHands.Force(this);
                        Log("demo: " + (done ? "a hand out of the wall at frame " + filmFrames : "the hand couldn't happen here"));
                    }
                    Frame();
                    yield return null;
                }
                for (float t = 0f; t < 4f && on(); t += Time.deltaTime) { Frame(); yield return null; }
                player.LookPitch(2f);
            }

            // (The rest of the haunts were filmed in the last demo; three, for the new rooms around them.)
            string[] order = { "pit", "watchers", "lightsout" };
            foreach (var id in order)
            {
                if (!on()) break;
                // Walk until somewhere it can happen (filmed: this is also the tour of the rooms and doorways).
                off = maze.FarUnseen(player.transform.position, 2, player.cam);
                bool ready = false;
                for (float t = 0f; t < 40f && on(); t += Time.deltaTime)
                {
                    clock();
                    Vector3 pp = player.transform.position;
                    if (id == "float") { var room = maze.FurnishedRoom(pp); if (room.HasValue) off = room.Value; }
                    bool middle = Vis.Level(maze.RoomCentre(pp) - pp).magnitude < 1.6f;
                    if (t > 2.5f && middle && maze.CanHaunt(id, pp, player.cam)) { ready = true; break; }
                    if (maze.SameRoom(pp, off) && id != "float") { if (middle) off = maze.FarUnseen(pp, 2, player.cam); else { DemoWalk(maze.RoomCentre(pp), 2.3f); Frame(); yield return null; continue; } }
                    else if (maze.SameRoom(pp, off) && !middle) { DemoWalk(maze.RoomCentre(pp), 2.3f); Frame(); yield return null; continue; }
                    DemoWalk(maze.GuideTo(pp, off), 2.5f);
                    Frame();
                    yield return null;
                }
                if (!ready) { Log("demo: haunt '" + id + "': found nowhere it could happen; skipped"); continue; }
                // A breath first, looking about.
                for (float t = 0f; t < 1.5f && on(); t += Time.deltaTime) { clock(); player.Turn(30f * Time.deltaTime); Frame(); yield return null; }
                if (!maze.ForceHaunt(id, player.transform.position, player.cam)) { Log("demo: haunt '" + id + "' refused; skipped"); continue; }
                Log("demo: haunt '" + id + "' at frame " + filmFrames);
                float hold = id == "pit" ? 24f : id == "tall" ? 17f : id == "squeeze" ? 19f : id == "flood" ? 22f : id == "watchers" ? 26f
                           : id == "sealed" ? 11f : id == "float" ? 16f : id == "lightsout" ? 12f : id == "tilt" ? 14f : 17f;
                Vector3 startRoom = maze.RoomCentre(player.transform.position);
                bool crossed = false;
                float facing = 0f;
                for (float t = 0f; t < hold && on(); t += Time.deltaTime)
                {
                    clock();
                    Vector3 pp = player.transform.position;
                    switch (id)
                    {
                        case "pit":
                            // To the doorway, a look down into it, then across on the planks, looking down.
                            if (maze.RoomsToExit(pp) < 1) { player.Turn(20f * Time.deltaTime); break; }
                            bool inPit = !maze.SameRoom(pp, startRoom);
                            if (inPit) crossed = true;
                            if (crossed && !maze.Busy(pp) && t > 6f) { t = Mathf.Max(t, hold - 2f); player.LookPitch(2f); DemoWalk(maze.Guide(pp), 1.6f); break; }
                            DemoWalk(maze.Guide(pp), inPit ? 1.15f : 1.5f);
                            player.LookPitch(inPit ? 38f : 14f);
                            break;
                        case "tall":
                            // Not looking at the ceiling while it goes; then up, slowly, and a long look.
                            player.LookPitch(t < 4f ? 18f : Mathf.Lerp(18f, -78f, Mathf.Clamp01((t - 4f) / 3.5f)));
                            player.Turn((t < 4f ? 25f : 9f) * Time.deltaTime);
                            break;
                        case "flood":
                            player.LookPitch(t < 9f ? 32f : 8f);
                            player.Turn((t < 9f ? 10f : 24f) * Time.deltaTime);
                            break;
                        case "watchers":
                        {
                            // Look at them; turn away; turn back: they've moved.
                            float phase4 = t % 8f;
                            if (phase4 < 3.2f) player.Turn(0f);
                            else if (phase4 < 4.2f) { player.Turn(180f * Time.deltaTime); facing += 180f * Time.deltaTime; }
                            else if (phase4 < 6.8f) player.Turn(0f);
                            else if (phase4 < 7.8f) player.Turn(-180f * Time.deltaTime);
                            if (t < 0.1f) player.Turn(45f);       // toward a corner to begin with
                            break;
                        }
                        case "tilt":
                        case "small":
                            if (maze.SameRoom(pp, off)) off = maze.FarUnseen(pp, 2, player.cam);
                            DemoWalk(maze.GuideTo(pp, off), 1.7f);
                            break;
                        default:
                            player.Turn((id == "lightsout" ? 14f : 24f) * Time.deltaTime);
                            break;
                    }
                    Frame();
                    yield return null;
                }
                player.LookPitch(2f);
            }

            // A door slammed in the quiet: it hears, and comes sooner (then the trap, below).
            if (on())
            {
                Door slamDoor = null;
                for (float t = 0f; t < 40f && on() && slamDoor == null; t += Time.deltaTime)
                {
                    clock();
                    foreach (var d in maze.DoorsNear(player.transform.position, 6f)) if (d.Angle > 30f && Vis.Sees(player.cam, d.Middle)) { slamDoor = d; break; }
                    if (slamDoor == null) DemoWalk(maze.GuideTo(player.transform.position, maze.FarUnseen(player.transform.position, 2, player.cam)), 2.4f);
                    Frame();
                    yield return null;
                }
                if (slamDoor != null)
                {
                    st.Delay(0f);
                    st.Delay(60f);                        // resting; the slam should bring it sooner
                    for (float t = 0f; t < 6f && on() && Vis.Level(slamDoor.Middle - player.transform.position).magnitude > 1.3f; t += Time.deltaTime) { DemoWalk(slamDoor.Middle, 2f); Frame(); yield return null; }
                    Log("demo: slamming a door in the quiet, at frame " + filmFrames);
                    yield return DemoSlam(slamDoor);
                    for (float t = 0f; t < 25f && on() && st.state == MazeStalker.State.Calm; t += Time.deltaTime) { clock(); player.Turn(15f * Time.deltaTime); Frame(); yield return null; }
                    Log("demo: " + (st.state != MazeStalker.State.Calm ? "it heard, and came early: " + st.state + " at frame " + filmFrames : "it didn't come"));
                    for (float t = 0f; t < 35f && on() && dreamDeaths == deaths; t += Time.deltaTime)
                    {
                        clock();
                        if (st.It != null) player.Aim(st.It.transform.position + Vector3.up);
                        else player.Turn(20f * Time.deltaTime);
                        if (st.It != null && Vis.Level(st.It.transform.position - player.transform.position).magnitude < 2.5f) DemoWalk(maze.Guide(player.transform.position), 2.6f);
                        if (st.state == MazeStalker.State.Calm && t > 5f) break;
                        Frame();
                        yield return null;
                    }
                }
                else Log("demo: no open door near enough to slam");
                if (dreamDeaths > deaths)
                {
                    Log("demo: caught (" + lastDreamDeath + ") at frame " + filmFrames);
                    for (float t = 0f; t < 12f; t += Time.deltaTime) { Frame(); yield return null; if (dream.active && dream.Maze != null && dream.Maze != maze && !busy && t > 7f) break; }
                    maze = dream.Maze; st = dream.Stalker; deaths = dreamDeaths;
                }
            }

            // (Filmed in the last demo: the wave sat out in hiding, and the crawling wave in the open.)
            if (false)
            {
                Log("demo: a wave, to be sat out in hiding, at frame " + filmFrames);
                st.Delay(0f);
                st.ForceWave();
                bool hid = false;
                float baseYaw = 0f;
                for (float t = 0f; t < 95f && on(); t += Time.deltaTime)
                {
                    clock();
                    Vector3 pp = player.transform.position;
                    bool hunted = st.state != MazeStalker.State.Calm && st.state != MazeStalker.State.Leave;
                    if (st.Hidden != null)
                    {
                        if (!hunted && t > 8f) { st.Unhide(); Log("demo: it has gone; out of hiding at frame " + filmFrames); break; }
                        // (Show it changing out there, once, while it stands at the locker.)
                        if (st.state == MazeStalker.State.Toy && st.changes == 0 && st.It != null && !st.It.Transforming && Vis.Level(st.It.transform.position - pp).magnitude < 3f)
                            st.ForceChange();
                        // Keeping still, eyes at the gaps; following it a little when it's there.
                        if (st.It != null && Vis.Level(st.It.transform.position - pp).magnitude < 6f)
                        {
                            float want = Mathf.Atan2(st.It.transform.position.x - pp.x, st.It.transform.position.z - pp.z) * Mathf.Rad2Deg;
                            float d = Mathf.Clamp(Mathf.DeltaAngle(baseYaw, want), -16f, 16f);
                            player.Teleport(pp, Mathf.LerpAngle(player.transform.eulerAngles.y, baseYaw + d, Time.deltaTime * 2f));
                        }
                    }
                    else
                    {
                        var hide = maze.NearestHide(pp, 3, true);       // a locker or cabinet for choice: you can watch from it
                        if (hide != null && !hid)
                        {
                            DemoWalk(maze.GuideTo(pp, hide.outside), 3.4f);
                            if (Vis.Level(hide.outside - pp).magnitude < 0.6f) { st.Hide(hide); hid = true; baseYaw = hide.yaw; Log("demo: hid in the " + hide.what + " at frame " + filmFrames); }
                        }
                        else if (hid && !hunted && t > 8f) break;
                        else DemoWalk(maze.Guide(pp), 4.2f);
                    }
                    Frame();
                    yield return null;
                }
            }

            // A wave in the open: see it come, run, and be caught.
            if (false)
            {
                // (First the last one has to have gone.)
                for (float t = 0f; t < 40f && on() && (st.It != null || st.state != MazeStalker.State.Calm); t += Time.deltaTime) { clock(); player.Turn(20f * Time.deltaTime); Frame(); yield return null; }
                for (float t = 0f; t < 4f && on(); t += Time.deltaTime) { clock(); player.Turn(30f * Time.deltaTime); Frame(); yield return null; }
                Log("demo: a wave, in the open (a crawling one, for the walls and ceiling), at frame " + filmFrames);
                var crawl = fear.Compose(2);
                crawl[Monster.Shift] = 0;
                crawl[Monster.Legs] = Monster.SpiderLegs;
                st.forceLook = crawl;
                st.Delay(0f);
                st.ForceWave();
                bool running = false;
                for (float t = 0f; t < 75f && on(); t += Time.deltaTime)
                {
                    clock();
                    Vector3 pp = player.transform.position;
                    if (st.It == null) player.Turn(22f * Time.deltaTime);
                    else
                    {
                        float away = Vis.Level(st.It.transform.position - pp).magnitude;
                        if (!running && away < 2.2f && !st.OffTheFloor) { running = true; Log("demo: it's close; running, at frame " + filmFrames); }
                        if (!running) player.Aim(st.It.transform.position + Vector3.up * (st.OffTheFloor ? 0.2f : 1f));    // standing still, watching it come
                        else if (((int)(t * 0.5f)) % 3 == 2) player.Aim(st.It.HeadPos);    // a look back
                        else DemoWalk(maze.Guide(pp), 2.7f);                              // not fast enough
                    }
                    Frame();
                    yield return null;
                }
            }
            if (dreamDeaths > deaths)
            {
                // What dying is: the dark, then asleep again somewhere else.
                Log("demo: caught (" + lastDreamDeath + ") at frame " + filmFrames);
                for (float t = 0f; t < 12f; t += Time.deltaTime) { Frame(); yield return null; if (dream.active && dream.Maze != null && dream.Maze != maze && !busy && t > 7f) break; }
                maze = dream.Maze; st = dream.Stalker; deaths = dreamDeaths;
            }
            // A trap it learned: by now it knows where you like to hide (the demo makes it a habit), and one of
            // them has it waiting inside. Listen at the door: it breathes. The demo goes in anyway.
            if (maze != null && on())
            {
                for (float t = 0f; t < 30f && on() && (st.It != null || st.state != MazeStalker.State.Calm); t += Time.deltaTime) { clock(); Frame(); yield return null; }
                foreach (var kind in new[] { "locker", "cabinet" }) Habits.Use(this, "hide:" + kind, kind == "locker" ? 6f : 1f);
                Log("demo: habits now: " + Habits.Describe(this));
                st.Delay(0f);
                st.ForceWave();
                // (The trap is set as it arrives, after the three knocks.)
                for (float t = 0f; t < 9f && (st.state == MazeStalker.State.Calm || st.state == MazeStalker.State.Warning); t += Time.deltaTime) { clock(); Frame(); yield return null; }
                var trap = st.TrapSpot;
                Log("demo: " + (trap != null ? "a learned trap: a " + trap.what + " near here has it inside, at frame " + filmFrames : "no trap could be set here"));
                for (float t = 0f; trap != null && t < 60f && on() && dreamDeaths == deaths; t += Time.deltaTime)
                {
                    clock();
                    Vector3 pp = player.transform.position;
                    if (Vis.Level(trap.outside - pp).magnitude > 0.6f) DemoWalk(maze.GuideTo(pp, trap.outside), 3.0f);
                    else if (st.Hidden == null)
                    {
                        // Stand at the door a moment, listening, then go in.
                        player.Aim(trap.inside + Vector3.up * 1.2f);
                        for (float w = 0f; w < 2.5f; w += Time.deltaTime) { Frame(); yield return null; }
                        Log("demo: into the " + trap.what + " at frame " + filmFrames);
                        st.Hide(trap);
                    }
                    Frame();
                    yield return null;
                }
                if (dreamDeaths > deaths)
                {
                    Log("demo: trapped (" + lastDreamDeath + "); traps sprung so far " + Habits.trapsSprung + ", at frame " + filmFrames);
                    for (float t = 0f; t < 12f; t += Time.deltaTime) { Frame(); yield return null; if (dream.active && dream.Maze != null && dream.Maze != maze && !busy && t > 7f) break; }
                    maze = dream.Maze; st = dream.Stalker; deaths = dreamDeaths;
                }
            }
            if (maze != null && on())
            {
                Log("demo: making for the way out at frame " + filmFrames);
                st.Delay(900f);
                maze.HoldHaunts(900f);
                for (float t = 0f; t < 70f && on(); t += Time.deltaTime) { if (dream.TimeLeft < 60f) dream.AddTime(1f); DemoWalk(maze.Guide(player.transform.position), 3.2f); Frame(); yield return null; }
                Log("demo: " + (on() ? "ran out of film before the way out" : "out, at frame " + filmFrames));
                for (float t = 0f; t < 4f; t += Time.deltaTime) { Frame(); yield return null; }
            }
            Time.captureFramerate = 0;
            summary.Append("  \"mode\": \"demo\",\n  \"frames\": ").Append(filmFrames).Append(",\n  \"fps\": 24,\n");
        }
    }
}
