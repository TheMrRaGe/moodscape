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
            house.Redust();             // the mess stills dusted everything; the words need dust to be written in
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
                haunt.lastView = null;
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
                if (id == "p:prints") { from = house.hall.center; from.y = 0f; at = house.hall.center + Vector3.up * 0.02f + Vis.Flat(house.bedSpawn - house.hall.center).normalized * 0.8f; }
                if (haunt.lastView != null) from = haunt.lastView.Value;
                float pitch = Mathf.Clamp(Mathf.Atan2(1.6f - at.y, Vis.Flat(at - from).magnitude) * Mathf.Rad2Deg, -30f, 60f);
                yield return Pose(from, at, pitch, "haunt " + id.Substring(2));
            }
            Log("haunts: " + ok + " of " + total + " physical and gore haunts happened and were photographed");
            // The grammar's inventions: the first few in the pool, done now and photographed.
            int inv = 0;
            for (int i = 0; i < Mathf.Min(5, haunt.GrammarCount); i++)
            {
                string what;
                bool did = false;
                try { did = haunt.ForceGene(i, out what); }
                catch (System.Exception e) { Problem("haunt grammar: invention " + i + " threw: " + e.Message); continue; }
                if (!did) { Log("haunt grammar: studio: nothing to do invention " + i + " to (" + what + ")"); continue; }
                inv++;
                yield return Settle(1.0f);
                Vector3 at = haunt.lastTrace;
                var room = house.RoomAt(at);
                Vector3 from = room != null && room.door != null ? room.door.Middle : at + Vector3.back * 2f;
                from.y = 0f;
                if (Vis.Flat(from - at).magnitude < 1.2f) from = at + Vis.Flat(from - at).normalized * 1.8f;
                yield return Pose(from, at, Mathf.Clamp(Mathf.Atan2(1.6f - at.y, Vis.Flat(at - from).magnitude) * Mathf.Rad2Deg, -20f, 50f), "invented: " + what);
            }
            if (haunt.GrammarCount < 8) Problem("haunt grammar: only " + haunt.GrammarCount + " invented haunts in the pool");
            Log("haunt grammar: studio did " + inv + " inventions");
            summary.Append("  \"inventions\": ").Append(inv).Append(",\n");
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
            // The door stands free in its room facing -z; the locks are either side of it.
            Vector3 front = exit.position - exit.forward * 2.6f;
            front.y = exit.position.y;
            Log("dream puzzles: studio: the way out at " + exit.position.ToString("F1") + ", photographed from " + front.ToString("F1") + "; " + exit.childCount + " parts on it");
            yield return Pose(front, exit.position + Vector3.up * 1.3f, 5f, "dream: the way out, locked (clock and dial)");
            int turns = pz.SolveAll();
            if (pz.Locked) Problem("dream puzzles: solving every lock didn't open the way out");
            Log("dream puzzles: studio solved " + pz.Describe + " in " + turns + " turns");
            summary.Append("  \"dream_locks\": \"").Append(pz.Describe).Append("\",\n");
            dream.Clear();
            day = keepDay;
            phase = Phase.Morning;
        }
            /// <summary>
        /// Studio: does the fly body learn? A pretend player who only jumps when it freezes in full view: after a few
        /// hundred moments, it should freeze in view far more than it did at first.
        /// </summary>
        void FlyBodyCheck(StringBuilder summary)
        {
            var fb = new FlyBody(teller.learner.instinct);
            int early = 0, late = 0;
            float t = 0f, reaction = 0.1f;
            for (int i = 0; i < 600; i++)
            {
                t += 1f;
                var a = fb.Choose(true, true, 4f, false, false, false, reaction, t, null);
                if (i < 20 && a == FlyBody.Act.Freeze) early += 5;      // its first 20 moments, out of 100
                if (i >= 500 && a == FlyBody.Act.Freeze) late++;
                t += 3.1f;
                fb.Observe(a == FlyBody.Act.Freeze ? 0.7f : 0.1f, t);
                fb.hunger = 0.3f; fb.nerve = 0.7f;
            }
            Log("fly body: a pretend player who only jumps when it freezes in view: it froze in " + early + "% of its first 20 moments, " + late + " in its last 100");
            if (late < 80 || late <= early) Problem("fly body: it didn't learn to freeze for a player who only reacts to that (" + early + " -> " + late + ")");
            summary.Append("  \"fly_body\": \"").Append(early).Append(" -> ").Append(late).Append("\",\n");
        }
    }
}
