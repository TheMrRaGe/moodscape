using System;
using System.Collections.Generic;
using System.Globalization;

namespace FearCore
{
    /// <summary>
    /// The monster's leash (after Alien: Isolation). The storyteller and the creature are two brains: the
    /// creature hunts with its own senses; the storyteller watches how long it has been on top of the player.
    /// The gauge fills while it is close, faster while it is seen, and drains while it is away. When it is full
    /// the creature is sent backstage for a while (into the walls, out of the house) so the player can breathe,
    /// and the frights that need it wait.
    /// </summary>
    public class Menace
    {
        public float Gauge;                      // 0..1
        public float Near = 9f;                  // metres within which it counts as close
        public float FillPerSecond = 0.045f, SeenFactor = 2.5f, DrainPerSecond = 0.03f;
        public float BackstageMin = 25f, BackstageMax = 45f;
        public bool Backstage { get; private set; }
        public Action<string> Log;
        float backUntil;
        readonly System.Random rng = new System.Random();

        /// <summary>Call every frame while the creature exists (distance = float.PositiveInfinity when it doesn't).</summary>
        public void Tick(float now, float dt, float distance, bool seen)
        {
            if (Backstage)
            {
                Gauge = Math.Max(0f, Gauge - DrainPerSecond * 2f * dt);
                if (now >= backUntil)
                {
                    Backstage = false;
                    if (Log != null) Log("menace: it may come back now (gauge " + Gauge.ToString("0.00", CultureInfo.InvariantCulture) + ")");
                }
                return;
            }
            if (distance < Near)
            {
                float closeness = 1f - distance / Near;
                Gauge += FillPerSecond * (0.4f + closeness) * (seen ? SeenFactor : 1f) * dt;
            }
            else Gauge -= DrainPerSecond * dt;
            Gauge = Math.Max(0f, Math.Min(1f, Gauge));
            if (Gauge >= 1f) SendBack(now, "it has been on top of you too long");
        }

        public void SendBack(float now, string why)
        {
            Backstage = true;
            backUntil = now + BackstageMin + (float)rng.NextDouble() * (BackstageMax - BackstageMin);
            if (Log != null) Log("menace: " + why + "; it withdraws for " + (backUntil - now).ToString("0", CultureInfo.InvariantCulture) + " s so you can breathe");
        }
    }

    /// <summary>
    /// What the player keeps doing to cope (hiding in one room, closing doors, never turning round). The game
    /// reports a tactic each time it happens; repeated enough, it unlocks the house's answer to it. Players read
    /// this as the monster learning, which is the point.
    /// </summary>
    public class Tactics
    {
        public static readonly float[] Thresholds = { 2f, 4f, 7f };
        public Action<string> Log;
        public Action<string, int> Unlocked;     // (tactic, new level)
        readonly Dictionary<string, float> uses = new Dictionary<string, float>();
        readonly Dictionary<string, int> level = new Dictionary<string, int>();

        public void Use(string tactic, float amount = 1f)
        {
            float u;
            uses.TryGetValue(tactic, out u);
            u += amount;
            uses[tactic] = u;
            int lv = 0;
            while (lv < Thresholds.Length && u >= Thresholds[lv]) lv++;
            int was;
            level.TryGetValue(tactic, out was);
            if (lv > was)
            {
                level[tactic] = lv;
                if (Log != null) Log("tactics: you keep doing it (" + tactic + ", " + u.ToString("0.0", CultureInfo.InvariantCulture) + " times); the house has an answer for it now (level " + lv + ")");
                if (Unlocked != null) Unlocked(tactic, lv);
            }
        }

        public float Count(string tactic) { float u; uses.TryGetValue(tactic, out u); return u; }
        public int Level(string tactic) { int l; level.TryGetValue(tactic, out l); return l; }

        /// <summary>The most-used tactic starting with prefix, if it has been used at least min times.</summary>
        public string Most(string prefix, float min)
        {
            string best = null;
            float bestU = min;
            foreach (var kv in uses)
                if (kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Value >= bestU) { bestU = kv.Value; best = kv.Key; }
            return best;
        }

        /// <summary>Overnight, habits loosen a little (levels already earned stay).</summary>
        public void Overnight()
        {
            var keys = new List<string>(uses.Keys);
            foreach (var k in keys) uses[k] *= 0.8f;
        }

        public void Clear() { uses.Clear(); level.Clear(); }
    }
}
