using System.Collections;
using System.Text;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The demo film ("demo" job): one dream, played and filmed from the player's eyes, showing everything the dream
    /// maze does. In order: walking the maze; each dream haunt, asked for one after another (the floor gone, no
    /// ceiling, the walls closing in, the flood, the watchers, the doors sealing, furniture floating, lights out, the
    /// house leaning, being small); the rot creeping over the walls throughout (the night is run fast); then a wave:
    /// the knocks, getting into a hiding place, it coming, looking, toying; then out, and the run for the way out.
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
            Time.captureFramerate = 30;
            var maze = dream.Maze;
            var st = dream.Stalker;
            float night = dream.TimeLeft;
            int deaths = dreamDeaths;
            System.Func<bool> on = () => dream.active && dream.Maze == maze && dreamDeaths == deaths && phase == Phase.Dream;
            // The night is held to a schedule (so the rot is seen creeping the whole film, and never runs out).
            float planned = 150f, began = Time.time;
            System.Action clock = () =>
            {
                float k = Mathf.Clamp01((Time.time - began) / planned);
                float want = night * Mathf.Lerp(0.8f, 0.14f, k);
                dream.AddTime(want - dream.TimeLeft);
            };
            st.Delay(600f);
            maze.HoldHaunts(600f);

            Log("demo: walking the maze at frame " + filmFrames);
            for (float t = 0f; t < 7f && on(); t += Time.deltaTime)
            {
                clock();
                if (maze.RoomsToExit(player.transform.position) > 1) DemoWalk(maze.Guide(player.transform.position), 2.4f);
                else player.Turn(40f * Time.deltaTime);
                Frame();
                yield return null;
            }

            // Each haunt in turn. Some happen to the room you're standing in (stay and look); some you walk through.
            string[] order = { "pit", "tall", "squeeze", "flood", "watchers", "sealed", "float", "lightsout", "tilt", "small" };
            foreach (var id in order)
            {
                if (!on()) break;
                // On to a fresh room first: somewhere a few rooms off that isn't the way out and has nothing going on in it.
                Vector3 off = maze.FarUnseen(player.transform.position, 2, player.cam);
                for (float t = 0f; t < 12f && on(); t += Time.deltaTime)
                {
                    clock();
                    Vector3 pp = player.transform.position;
                    if (t > 3.5f && !maze.Busy(pp) && maze.RoomsToExit(pp) > 0 && Vis.Level(maze.RoomCentre(pp) - pp).magnitude < 2.5f) break;
                    if (maze.SameRoom(pp, off)) off = maze.FarUnseen(pp, 2, player.cam);
                    DemoWalk(maze.GuideTo(pp, off), 2.8f);
                    Frame();
                    yield return null;
                }
                for (float t = 0f; t < 1.2f && on(); t += Time.deltaTime) { clock(); DemoWalk(maze.RoomCentre(player.transform.position), 2.0f); Frame(); yield return null; }
                bool ok = maze.ForceHaunt(id, player.transform.position, player.cam);
                Log("demo: haunt '" + id + "' " + (ok ? "at frame " + filmFrames : "couldn't happen in this room; skipped"));
                if (!ok) continue;
                bool walkThrough = id == "pit" || id == "tilt" || id == "small" || id == "lightsout";
                float hold = id == "pit" ? 9f : id == "sealed" ? 9f : id == "watchers" ? 11f : 8.5f;
                for (float t = 0f; t < hold && on(); t += Time.deltaTime)
                {
                    clock();
                    Vector3 pp = player.transform.position;
                    if (id == "pit" && maze.RoomsToExit(pp) > 1) DemoWalk(maze.Guide(pp), 1.8f);        // the pit is the next room toward the way out
                    else if (walkThrough) DemoWalk(maze.GuideTo(pp, off), 2.2f);
                    else if (id == "tall") { player.LookPitch(Mathf.Lerp(0f, -70f, Mathf.Clamp01((t - 2f) / 2f))); player.Turn(14f * Time.deltaTime); }
                    else if (id == "watchers") player.Turn((Mathf.Sin(t * 0.9f) > 0f ? 75f : -75f) * Time.deltaTime);      // look away, look back
                    else player.Turn(32f * Time.deltaTime);
                    Frame();
                    yield return null;
                }
                player.LookPitch(2f);
            }

            // A wave.
            if (on())
            {
                Log("demo: a wave at frame " + filmFrames);
                st.Delay(0f);
                st.ForceWave();
                bool hid = false;
                for (float t = 0f; t < 70f && on(); t += Time.deltaTime)
                {
                    clock();
                    Vector3 pp = player.transform.position;
                    bool hunted = st.state != MazeStalker.State.Calm && st.state != MazeStalker.State.Leave;
                    if (st.Hidden != null)
                    {
                        if (!hunted && t > 6f) { st.Unhide(); Log("demo: it has gone; out of hiding at frame " + filmFrames); break; }
                        player.Turn(Mathf.Sin(t * 0.8f) * 18f * Time.deltaTime);          // watching through the slats
                    }
                    else
                    {
                        var hide = maze.NearestHide(pp, 2, true);       // a locker or cabinet for choice: you can watch from it
                        if (hide != null && !hid)
                        {
                            DemoWalk(maze.GuideTo(pp, hide.outside), 3.6f);
                            if (Vis.Level(hide.outside - pp).magnitude < 0.6f) { st.Hide(hide); hid = true; Log("demo: hid in the " + hide.what + " at frame " + filmFrames); }
                        }
                        else if (hid && !hunted && t > 6f) break;
                        else DemoWalk(maze.Guide(pp), 4.2f);
                    }
                    Frame();
                    yield return null;
                }
            }
            if (!on() && dreamDeaths > deaths)
            {
                // It got in. Film what dying is: the dark, then asleep again somewhere else.
                Log("demo: caught (" + lastDreamDeath + ") at frame " + filmFrames);
                for (float t = 0f; t < 9f; t += Time.deltaTime) { Frame(); yield return null; }
            }
            else if (on())
            {
                Log("demo: making for the way out at frame " + filmFrames);
                st.Delay(600f);
                for (float t = 0f; t < 45f && on(); t += Time.deltaTime) { clock(); DemoWalk(maze.Guide(player.transform.position), 3.4f); Frame(); yield return null; }
                Log("demo: " + (on() ? "ran out of film before the way out" : "out, at frame " + filmFrames));
                for (float t = 0f; t < 3f; t += Time.deltaTime) { Frame(); yield return null; }
            }
            Time.captureFramerate = 0;
            Log("demo: the maze changed " + maze.shifts + " times, " + maze.hauntsFired + " haunts, " + st.waves + " waves, hid " + st.timesHidden + " times");
            summary.Append("  \"mode\": \"demo\",\n  \"frames\": ").Append(filmFrames).Append(",\n  \"fps\": 30,\n");
        }
    }
}
