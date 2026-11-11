using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// It is always watching what you do, and it remembers it from one run to the next.
    ///
    /// Each habit is a tactic in the storyteller's book (FearCore.Tactics): used often enough, it earns a level, and
    /// the levels are the traps it sets for you. Habits carry over between runs (weaker, but a level once earned
    /// stays one lower at worst), so a player who always hides in lockers will, sooner or later, find one already
    /// occupied.
    ///
    /// Habits it watches for:
    ///   hide:locker / hide:cabinet / hide:table   where you hide in the dream
    ///   hide in the &lt;room&gt; / run outside       where you go when it's bad in the house (Dread)
    ///   reads pages        stopping to read what you find
    ///   flashlight         keeping the light on
    ///   takes batteries    going for the batteries
    ///   looks behind       turning round, fast
    ///   hugs walls         walking pressed close to walls
    ///   runs               sprinting
    ///   freezes            standing still while it's hunting you
    ///   stares at pictures looking long at the faces on the walls
    ///
    /// And what it does about them (see Traps below and where each is used):
    ///   your hiding place  -> it checks your favourite kind first; then one of them has it already inside
    ///   reading pages      -> some pages are bait: reading one brings it, and the lights go
    ///   batteries          -> some batteries are bait: picking them up shuts the room
    ///   looking behind     -> when you turn round, something is where you were just looking
    ///   hugging walls      -> the hands in the walls come more often
    ///   running            -> it cuts you off where you're running to
    ///   freezing           -> it searches the dark rooms first
    ///   staring at faces   -> the faces on the walls are the ones you looked at longest, and sometimes they turn
    /// </summary>
    public static class Habits
    {
        static float lastYaw, yawAt, sinceTurnCheck;
        static Vector3 beforeTurn;
        static bool hasYaw;
        static readonly Queue<KeyValuePair<float, float>> yaws = new Queue<KeyValuePair<float, float>>();
        public static int trapsSprung;
        static float behindNext;

        public static void ResetStatics() { hasYaw = false; yaws.Clear(); trapsSprung = 0; behindNext = 0f; }

        static FearCore.Tactics T(GameDirector g) { return g.teller != null ? g.teller.tactics : null; }

        public static void Use(GameDirector g, string habit, float amount = 1f) { var t = T(g); if (t != null) t.Use(habit, amount); }
        public static int Level(GameDirector g, string habit) { var t = T(g); return t != null ? t.Level(habit) : 0; }
        public static string Most(GameDirector g, string prefix, float min) { var t = T(g); return t != null ? t.Most(prefix, min) : null; }

        /// <summary>Every frame: the habits that are about how you move and look.</summary>
        public static void Tick(GameDirector g)
        {
            var p = g.player;
            if (p == null || p.frozen || g.hud.modal != Modal.None) { hasYaw = false; return; }
            float dt = Time.deltaTime, now = Time.time;
            bool dream = g.phase == Phase.Dream && g.dream.active;

            // Turning round, fast: 140 degrees or more inside 0.7 s.
            float yaw = p.cam.transform.eulerAngles.y;
            yaws.Enqueue(new KeyValuePair<float, float>(now, yaw));
            while (yaws.Count > 0 && yaws.Peek().Key < now - 0.7f) yaws.Dequeue();
            if (now > yawAt && yaws.Count > 2)
            {
                float first = yaws.Peek().Value;
                if (Mathf.Abs(Mathf.DeltaAngle(first, yaw)) > 140f)
                {
                    yawAt = now + 1.5f;
                    Use(g, "looks behind");
                    // The trap: something is now where you were just looking (behind you, since you turned).
                    // (Not every time: it would stop meaning anything. Once in a while, and only once it's a real habit.)
                    if (dream && now > behindNext && Level(g, "looks behind") >= 2 && Random.value < 0.2f + 0.08f * Level(g, "looks behind"))
                    {
                        Vector3 back = Quaternion.Euler(0f, first, 0f) * Vector3.forward;
                        g.StartCoroutine(BehindYou(g, p.transform.position + back * 3.2f));
                        behindNext = now + Random.Range(90f, 160f);
                    }
                }
            }

            // Running.
            Vector3 v = p.velocity; v.y = 0f;
            if (In.Held(Btn.Sprint) && v.magnitude > 3.5f) Use(g, "runs", dt / 20f);
            // Keeping the light on (the dream's flashlight).
            if (dream && g.dream.TorchOn) Use(g, "flashlight", dt / 30f);
            // Standing still while it hunts you.
            var st = dream ? g.dream.Stalker : null;
            if (st != null && st.Hidden == null && (st.state == MazeStalker.State.Hunt || st.state == MazeStalker.State.Search) && v.magnitude < 0.25f)
                Use(g, "freezes", dt / 10f);
            Painted.Watch(g);
        }

        // You turned round. It's in front of you now, where your back was a moment ago. It's gone when you look at it again.
        static System.Collections.IEnumerator BehindYou(GameDirector g, Vector3 at)
        {
            trapsSprung++;
            g.Log("habits: you turn round a lot; this time something stood where you'd just been looking");
            var f = Build.Figure("Behind you", null);
            f.position = new Vector3(at.x, g.player.transform.position.y, at.z);
            Vector3 to = g.player.transform.position - f.position; to.y = 0f;
            if (to.sqrMagnitude > 1e-4f) f.rotation = Quaternion.LookRotation(to.normalized);
            Sfx.At("breath", f.position + Vector3.up * 1.7f, 0.5f, 0.8f);
            float shown = 0f;
            for (float t = 0f; t < 8f; t += Time.deltaTime)
            {
                if (Vis.Sees(g.player.cam, f.position + Vector3.up * 1.4f)) shown += Time.deltaTime;
                if (shown > 0.25f) { Look.Kick(0.6f); g.Shake(0.02f, "it was where you'd just been looking"); break; }
                yield return null;
            }
            Object.Destroy(f.gameObject);
        }

        /// <summary>All the habits, strongest first, for the log.</summary>
        public static string Describe(GameDirector g)
        {
            var t = T(g);
            if (t == null) return "";
            var list = new List<KeyValuePair<string, float>>(t.All());
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            foreach (var kv in list) if (kv.Value >= 0.5f) parts.Add(kv.Key + " " + kv.Value.ToString("0.0") + (t.Level(kv.Key) > 0 ? " (level " + t.Level(kv.Key) + ")" : ""));
            return string.Join(", ", parts.ToArray());
        }
    }
}
