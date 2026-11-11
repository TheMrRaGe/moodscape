using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The demo's first part, in the house (day 4, evening): this run's story, the doors opened by hand (with
    /// someone in the gap), a door slammed and the house answering, and the grounded hauntings one by one, each
    /// where it would happen and watched long enough to see it (or, mostly, hear it).
    /// </summary>
    public partial class GameDirector
    {
        // Walk through the house to `dest`, filmed, opening doors by hand on the way.
        IEnumerator DemoGo(Vector3 dest, float speed = 2.2f, float limit = 30f)
        {
            float began = Time.time;
            var legs = house.Path(player.transform.position, dest);
            foreach (var leg in legs)
            {
                List<Vector3> steps = GridPath(new Vector3(player.transform.position.x, 0f, player.transform.position.z), new Vector3(leg.x, 0f, leg.z));
                if (steps == null || steps.Count == 0) steps = new List<Vector3> { leg };
                foreach (var s in steps)
                {
                    for (float t = 0f; t < 8f; t += Time.deltaTime)
                    {
                        if (Time.time - began > limit) yield break;
                        if (Vis.Level(s - player.transform.position).magnitude < 0.25f && demoDoor == null) break;
                        DemoWalk(new Vector3(s.x, player.transform.position.y, s.z), speed);
                        Frame();
                        yield return null;
                    }
                }
            }
        }

        IEnumerator DemoHold(float seconds, System.Action each = null)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) { if (each != null) each(); Frame(); yield return null; }
        }

        IEnumerator DemoLookTurn(Vector3 at, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Vector3 eye = player.cam.transform.position;
                Vector3 want = at - eye;
                float yaw = Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg;
                player.Turn(Mathf.DeltaAngle(player.transform.eulerAngles.y, yaw) * Mathf.Min(1f, Time.deltaTime * 2.5f));
                Frame();
                yield return null;
            }
        }

        // Somewhere grounded to happen, and watched.
        IEnumerator DemoGround(string id, float watch)
        {
            bool ok = haunt.ForceGrounded(id);
            Log("demo: grounded haunting '" + id + "' " + (ok ? "at frame " + filmFrames : "couldn't happen here; skipped"));
            if (ok) yield return DemoHold(watch);
        }

        IEnumerator DemoHouse()
        {
            day = 4;
            phase = Phase.Evening;
            house.SetTime(1, day);
            haunt.demoForce = true;
            house.Wear(day);
            hud.fade = hud.fadeTarget = 0f;
            player.frozen = false;
            Time.captureFramerate = 24;
            Log("demo: the house, day 4, evening, at frame " + filmFrames);
            if (tale != null) Log("demo: this run's story: " + tale.Summary);
            foreach (var d in house.doors) if (d != house.frontDoor) d.Snap(false);
            yield return DemoHold(2.5f);

            // Out of the bedroom: the door by hand, slowly (and someone in the gap, if they're there).
            var bed = house.Find("bedroom");
            if (bed != null && bed.door != null)
            {
                Vector3 inside = bed.door.Middle + Vis.Flat(bed.center - bed.door.Middle).normalized * 0.9f;
                yield return DemoGo(inside, 1.8f, 15f);
                for (float t = 0f; t < 14f; t += Time.deltaTime)
                {
                    DemoWalk(bed.door.Middle + Vis.Flat(bed.door.Middle - bed.center).normalized * 2f, 1.2f);
                    Frame();
                    yield return null;
                    if (demoDoor == null && bed.door.Openness > 0.6f && t > 3f) break;
                }
                yield return DemoHold(1.5f);
            }

            // The bulb in the hall, as you come out under it.
            yield return DemoGo(new Vector3(0f, 0f, 1.5f), 2f, 15f);
            yield return DemoGround("bulb", 5f);

            // A door slammed: the house answers.
            var study = house.Find("study");
            if (study != null && study.door != null)
            {
                study.door.Ajar(0.9f);
                yield return DemoGo(new Vector3(-0.6f, 0f, study.door.Middle.z), 2f, 12f);
                Log("demo: slamming the study door, at frame " + filmFrames);
                yield return DemoSlam(study.door);
                yield return DemoHold(7f);
            }

            // At the far end of the hall, in the dark.
            yield return DemoGo(new Vector3(0f, 0f, 4.5f), 2f, 12f);
            yield return DemoLookTurn(new Vector3(0f, 1.6f, -7.5f), 1.5f);
            yield return DemoGround("bottom", 7f);

            // The living room: the photographs, the presence, someone at the door.
            var living = house.Find("living room");
            if (living != null)
            {
                yield return DemoGo(living.center + Vis.Flat(living.door != null ? living.door.Middle - living.center : Vector3.zero).normalized * 0.8f, 2f, 20f);
                Vector3 mantel = house.InRoom("living room", new Vector3(7.1f, 1.3f, -4.6f));
                // Look away from the mantel; turned round behind your back.
                yield return DemoLookTurn(living.door != null ? living.door.Middle + Vector3.up : living.center, 1.5f);
                yield return DemoGround("facing", 0.5f);
                yield return DemoLookTurn(mantel, 3f);
                yield return DemoHold(2f);
                yield return DemoGround("presence", 7f);
                // Facing the fire, the door at the edge of your eye.
                Vector3 fire = house.InRoom("living room", new Vector3(7.0f, 1.0f, -4f));
                yield return DemoLookTurn(fire, 2f);
                yield return DemoGround("peek", 3.5f);
                if (living.door != null) yield return DemoLookTurn(living.door.Middle + Vector3.up * 0.6f, 1.5f);
                yield return DemoHold(1.5f);
                yield return DemoGround("walk", 13f);
                yield return DemoGround("name", 4f);
                yield return DemoGround("plates", 4f);
                yield return DemoGround("eggs", 3.5f);
                yield return DemoGround("radio", 6f);
                yield return DemoGround("smell", 3.5f);
                yield return DemoGround("cold", 3.5f);
            }

            // The kitchen, afterwards: the plate, the rubbish that isn't yours.
            yield return DemoGround("food", 0.5f);
            yield return DemoGround("moved", 0.5f);
            var kitchen = house.Find("kitchen");
            if (kitchen != null)
            {
                yield return DemoGo(kitchen.center, 2f, 25f);
                yield return DemoLookTurn(house.InRoom("kitchen", new Vector3(-6.3f, 0.2f, -5.4f)), 2.5f);
                yield return DemoHold(2f);
                yield return DemoLookTurn(house.InRoom("kitchen", new Vector3(-2.3f, 0.3f, -7.3f)), 2.5f);
                yield return DemoHold(2f);
            }

            // Lost time, in the hall.
            yield return DemoGo(new Vector3(0f, 0f, -1f), 2f, 20f);
            yield return DemoGround("clock", 8f);
            Log("demo: end of the house, " + demoDoorsOpened + " doors opened by hand, at frame " + filmFrames);
            yield return DemoHold(1f);
        }
    }
}
