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

        // One frame of walking toward a point, stepping round whatever is in the way.
        void DemoWalk(Vector3 goal, float speed)
        {
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

            Log("demo: walking the maze at frame " + filmFrames);
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

            string[] order = { "pit", "tall", "squeeze", "flood", "watchers", "sealed", "float", "lightsout", "tilt", "small" };
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

            // A wave spent hidden, watching.
            if (on())
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
            if (on())
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
