using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NightPages
{
    public partial class GameDirector
    {
        /// <summary>
        /// Studio: make each of the physical and gore haunts happen (gore shown), let it settle, and photograph what
        /// it left. A haunt that can't happen in this house is a problem.
        /// </summary>
        IEnumerator HauntStills(StringBuilder summary)
        {
            int keepGore = Settings.S.gore;
            Settings.S.gore = 0;
            int keepDay = day;
            day = 6;
            house.SetHour(11f, day);
            foreach (var r in house.rooms) r.lit = true;
            int ok = 0, total = 0;
            foreach (var id in new List<string>(haunt.MoreIds))
            {
                if (id == "p:wetbehind") continue;          // happens at your heels as you walk; the bot covers it
                total++;
                bool fired = false;
                try { fired = haunt.ForceMore(id); }
                catch (System.Exception e) { Problem("haunts: '" + id + "' threw: " + e.Message); continue; }
                if (!fired) { Problem("haunts: '" + id + "' couldn't happen in this house"); continue; }
                ok++;
                for (int i = 0; i < 6; i++) haunt.StudioTick(i * 20f);
                yield return Settle(1.2f);
                Vector3 at = haunt.lastTrace;
                var room = house.RoomAt(at);
                Vector3 dir = Vis.Flat((room != null ? room.center : at + Vector3.back) - at);
                if (dir.sqrMagnitude < 0.04f) dir = Vector3.back;
                dir.Normalize();
                Vector3 from = at + dir * 1.25f;
                from.y = 0f;
                if (id == "p:drip") from = at + dir * 1.6f;
                if (id == "p:turned" && room != null && room.door != null) { from = room.door.Middle; from.y = 0f; }
                if (id == "p:prints") { from = house.hall.center; from.y = 0f; }
                float pitch = Mathf.Clamp(Mathf.Atan2(1.6f - at.y, Vis.Flat(at - from).magnitude) * Mathf.Rad2Deg, -30f, 60f);
                yield return Pose(from, at, pitch, "haunt " + id.Substring(2));
            }
            Log("haunts: " + ok + " of " + total + " physical and gore haunts happened and were photographed");
            summary.Append("  \"haunts\": \"").Append(ok).Append("/").Append(total).Append("\",\n");
            Settings.S.gore = keepGore;
            day = keepDay;
            haunt.ClearTransient();
        }
            /// <summary>
        /// Studio: dream night 6 (both locks), photograph the locked way out and the dusty table, wipe it and
        /// photograph the map, then solve both locks a turn at a time and check the way out opens.
        /// </summary>
        IEnumerator DreamPuzzleStills(StringBuilder summary)
        {
            int keepDay = day;
            day = 6;
            phase = Phase.Dream;
            dream.Begin(6);
            yield return Settle(1.0f);
            var pz = dream.puzzles;
            if (pz == null) { Problem("dream puzzles: none made"); yield break; }
            pz.Tick(Time.time, player.transform.position);
            if (!pz.Locked) Problem("dream puzzles: night 6's way out isn't locked");
            var table = pz.DustTable;
            if (table != null)
            {
                yield return Pose(table.position + new Vector3(0f, 0f, -1.2f), table.position + Vector3.up * 0.78f, 40f, "dream: the dusty table near where you woke");
                pz.Wipe();
                yield return Pose(table.position + new Vector3(0f, 0f, -0.9f), table.position + Vector3.up * 0.78f, 55f, "dream: wiped, the way out scratched under the dust");
            }
            else Log("dream puzzles: no dusty table in this maze (no room near the start)");
            var exit = dream.Maze.ExitTransform;
            Vector3 rc = dream.Maze.RoomCentre(exit.position);
            Vector3 away = Vis.Flat(rc - exit.position);
            Vector3 front = rc + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back) * 1.3f;
            front.y = exit.position.y;
            yield return Pose(front, exit.position + Vector3.up * 1.3f, 5f, "dream: the way out, locked (clock and dial)");
            int turns = pz.SolveAll();
            if (pz.Locked) Problem("dream puzzles: solving every lock didn't open the way out");
            Log("dream puzzles: studio solved " + pz.Describe + " in " + turns + " turns");
            summary.Append("  \"dream_locks\": \"").Append(pz.Describe).Append("\",\n");
            dream.Clear();
            day = keepDay;
            phase = Phase.Morning;
        }
    }
}
