using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The four difficulties (easy, medium, hard, hardcore) and the figure's ladder.
    ///
    /// Difficulty sets how hard the storyteller pushes: its budget, how low and how high adaptation can take it, how
    /// fast calm and shrugged-off frights raise it, how long the rests are, and how quickly the ramp reaches full
    /// strength. A player who isn't shaken gets more (Storyteller.Unfazed); the content settings still cap what can
    /// appear, whatever the difficulty.
    ///
    /// The ladder: the figure doesn't wait for the last day. It climbs, one rung at a time: traces, a glimpse, a
    /// sighting, in the room with you, stalking, and at the top it reaches you. Each rung opens on its story day
    /// (a day later on easy, a day earlier on hard and hardcore), by fixed points in long stays, and is always open to
    /// stalking by the day before the last. It can open one rung early when the player keeps shrugging frights off
    /// (on hardcore, when the storyteller simply has room). It never skips: a rung's haunting only happens once the
    /// one below it has actually been seen.
    /// </summary>
    public partial class GameDirector
    {
        public static readonly string[] DifficultyNames = { "Easy", "Medium", "Hard", "Hardcore" };
        /// <summary>0 easy, 1 medium, 2 hard, 3 hardcore.</summary>
        public static int Level { get { return Mathf.Clamp(Settings.S.difficulty, 0, 3); } }

        /// <summary>One line for the title page: what losing a dream costs at this difficulty.</summary>
        public static string DifficultyLine
        {
            get
            {
                switch (Level)
                {
                    case 0: return "EASY: fewer frights, longer rests. If you don't get out of a dream, you wake; that night is gone.";
                    case 2: return "HARD: it pushes harder and sooner. If you don't get out, you dream it again, with less night.";
                    case 3: return "HARDCORE: it pushes hardest. If you don't get out, you start again from day one.";
                    default: return "If you don't get out, you dream it again, in a different maze.";
                }
            }
        }

        /// <summary>Set on a dream dreamt again after losing it (hard gives that night less time).</summary>
        [System.NonSerialized] public bool dreamRetry;

        static readonly float[] Budget = { 0.7f, 1f, 1.3f, 1.6f };
        static readonly float[] Floor = { 0.4f, 0.4f, 0.6f, 0.7f };
        static readonly float[] Ceil = { 1.15f, 1.5f, 1.8f, 2.1f };
        static readonly float[] Calm = { 0.6f, 1f, 1.5f, 2f };
        static readonly float[] RestMin = { 45f, 30f, 22f, 15f };
        static readonly float[] RestMax = { 60f, 45f, 35f, 28f };
        static readonly int[] RampTo = { 6, 5, 3, 2 };

        /// <summary>Tune the storyteller to the difficulty (every new run, and when it changes).</summary>
        public void ApplyDifficulty()
        {
            if (teller == null) return;
            int d = Level;
            teller.Difficulty = Budget[d];
            teller.AdaptFloor = Floor[d];
            teller.AdaptCeil = Ceil[d];
            teller.CalmRate = Calm[d];
            teller.RelaxMin = RestMin[d];
            teller.RelaxMax = RestMax[d];
            teller.RampEpisodes = RampTo[d];
            teller.OnUnfazed = (inc, streak) => UnfazedEarly(inc.label, streak);
            Log("difficulty: " + DifficultyNames[d] + ". Budget ×" + Budget[d].ToString("0.0#") + ", adaptation can take it from ×" + Floor[d].ToString("0.0#") + " to ×" +
                Ceil[d].ToString("0.0#") + ", calm and shrugged-off frights raise it ×" + Calm[d].ToString("0.0#") + ", rests " + RestMin[d].ToString("0") + "-" +
                RestMax[d].ToString("0") + " s, full strength from day " + RampTo[d] + ", " + CluesNeeded + " clues to open the cellar");
        }

        // ---------------------------------------------------------------- the figure's ladder

        public static readonly string[] RungNames = { "traces", "a glimpse", "a sighting", "in the room with you", "stalking", "it reaches you" };
        /// <summary>The highest rung opened this run, and the highest the player has actually seen.</summary>
        public int rungOpen, rungShown;
        /// <summary>The real day each rung opened (0: not yet).</summary>
        public int[] rungDay = new int[6];
        int waivedLogged = -1;

        /// <summary>A new run: back to traces.</summary>
        public void ResetRungs() { rungOpen = rungShown = 0; rungDay = new int[6]; rungDay[0] = 1; waivedLogged = -1; }
        public int Rung { get { return rungOpen; } }

        /// <summary>The rung this day opens by schedule alone.</summary>
        public int RungScheduled
        {
            get
            {
                int shift = Level == 0 ? -1 : Level >= 2 ? 1 : 0;
                int r = Mathf.Clamp(day - 1 + shift, 0, 5);
                if (RunDays >= 14)
                {
                    // Long stays don't make you wait weeks: a glimpse by day 2, a sighting by an eighth of the way, presence by a quarter.
                    if (calendar >= 2) r = Mathf.Max(r, 1);
                    if (calendar >= Mathf.CeilToInt(RunDays / 8f)) r = Mathf.Max(r, 2);
                    if (calendar >= Mathf.CeilToInt(RunDays / 4f)) r = Mathf.Max(r, 3);
                }
                // The last day is the payoff, not the first meeting: it is stalking you by the day before.
                if (RunDays > 1 && calendar >= RunDays - 1) r = Mathf.Max(r, 4);
                if (calendar <= 1) r = Mathf.Min(r, 4);     // never reaches you on the first day
                return r;
            }
        }

        /// <summary>Morning: open whatever today's schedule opens.</summary>
        public void ConsiderRung()
        {
            int s = RungScheduled;
            if (s <= rungOpen) { Log("the figure: rung " + rungOpen + " (" + RungNames[rungOpen] + ") is as far as it has come; seen up to " + rungShown); return; }
            int was = rungOpen;
            rungOpen = s;
            for (int k = was + 1; k <= s; k++) rungDay[k] = calendar;
            Log("the figure: rung " + rungOpen + " (" + RungNames[rungOpen] + ") opens today by the calendar (" + DifficultyNames[Level] + ", day " + calendar + " of " + RunDays +
                ", story day " + day + "; was " + was + "). It still has to be seen at each rung below first (seen up to " + rungShown + ")");
        }

        /// <summary>
        /// Can a rung's haunting happen now? Open, and the one below it has been seen, or the one below has had a
        /// whole earlier day to be seen and wasn't (the player never looked; it doesn't wait for ever).
        /// </summary>
        public bool RungOk(int k)
        {
            if (rungOpen < k) return false;
            if (rungShown >= k - 1) return true;
            int below = rungDay[k - 1];
            if (below > 0 && below < calendar)
            {
                if (waivedLogged < k) { waivedLogged = k; Log("the figure: rung " + (k - 1) + " (" + RungNames[k - 1] + ") was open since day " + below + " and never seen, so rung " + k + " doesn't wait for it"); }
                return true;
            }
            return false;
        }

        /// <summary>The player has seen the figure at this rung.</summary>
        public void RungSeen(int k, string what)
        {
            if (k <= rungShown) return;
            rungShown = k;
            Log("the figure: seen at rung " + k + " (" + RungNames[k] + "): " + what);
        }

        /// <summary>Open one rung ahead of the calendar (never the top one, never two ahead, never past what's been seen).</summary>
        void OpenEarly(string why)
        {
            int cap = Mathf.Min(4, RungScheduled + 1);
            if (rungOpen >= cap || rungShown < rungOpen) return;
            rungOpen++;
            rungDay[rungOpen] = calendar;
            Log("the figure: rung " + rungOpen + " (" + RungNames[rungOpen] + ") opens early: " + why);
        }

        static readonly int[] StreakFor = { 4, 3, 2, 1 };
        static readonly float[] AdaptFor = { 0.6f, 0.4f, 0.25f, 0f };

        void UnfazedEarly(string label, int streak)
        {
            if (streak >= StreakFor[Level] && teller.Adapt >= AdaptFor[Level])
                OpenEarly("you shrugged off " + streak + " fright" + (streak == 1 ? "" : "s") + " in a row (the last: " + label + "), adaptation " + teller.Adapt.ToString("+0.00;-0.00"));
        }

        /// <summary>Hardcore only: the storyteller having room (adaptation high) is enough to bring it on.</summary>
        public void RoomEarly()
        {
            if (Level == 3 && teller != null && teller.Adapt >= 0.4f)
                OpenEarly("hardcore, and the storyteller has room (adaptation " + teller.Adapt.ToString("+0.00;-0.00") + ")");
        }
    }

    public partial class Hauntings
    {
        bool glimpse;           // the figure out there now is only a glimpse: gone the instant it's seen
        float roomCheck;

        /// <summary>The ladder's own hauntings and gates (called from Init after the others are added).</summary>
        void InitLadder()
        {
            var gl = Add("glimpse", "a glimpse of someone at the edge of things", 1, 1.5f, 1, Glimpse, FearTypes.Hunted, "sight", "far");
            gl.can = () => g.RungOk(1);
            gl.needsCreature = true;
            gl.cooldown = 70f;
            foreach (var inc in teller.incidents)
            {
                if (inc.id == "figure") { inc.minEpisode = 1; inc.can = () => g.RungOk(2) || (bleedFigure && g.RungOk(1)); }
                if (inc.id == "behind") { inc.minEpisode = 1; inc.can = () => g.RungOk(3); }
            }
        }

        /// <summary>Every half-minute in the house: on hardcore, room alone can bring it on.</summary>
        void TickLadder(float now)
        {
            if (now < roomCheck) return;
            roomCheck = now + 30f;
            g.RoomEarly();
        }

        /// <summary>How strong the figure may look at the rung it's on (ambiguous low down, whatever the content settings allow).</summary>
        int FigureCap { get { return Mathf.Min(g.Rung <= 2 ? 1 : 2, Settings.ScareCap); } }

        // At the edge of things: the far end of the hall or a doorway, just out of view, gone the moment it's seen.
        bool Glimpse()
        {
            if (figure != null) return false;
            var h = g.house;
            var spots = new System.Collections.Generic.List<Vector3> { h.hallNorth, h.hallSouth };
            foreach (var door in h.doors) if (door.open && door.gameObject.activeSelf) spots.Add(door.Middle - Vector3.up * door.Middle.y + Vector3.up * PlayerPos.y);
            Vector3 fwd = Vis.Flat(Cam.transform.forward).normalized;
            Vector3 best = Vector3.zero;
            float bestScore = -1f;
            foreach (var s in spots)
            {
                Vector3 to = s - PlayerPos; to.y = 0f;
                float d = to.magnitude;
                if (d < 7f || d > 18f || Vis.Sees(Cam, s + Vector3.up * 1.6f)) continue;
                // Off to the side of where you're looking, so you catch it turning.
                float side = 1f - Mathf.Abs(Vector3.Dot(fwd, to.normalized) - 0.2f);
                float score = side + Random.value * 0.4f;
                if (score > bestScore) { bestScore = score; best = s; }
            }
            if (bestScore < 0f) return false;
            var look = g.fear.Compose(FigureCap);
            houseMonster = Monster.Create(null, look);
            figure = houseMonster.transform;
            figure.position = best;
            stalking = false;
            glimpse = true;
            figureSeen = 0f;
            figureUntil = Time.time + 25f;
            g.Log("haunting: a glimpse; it stands " + (best - PlayerPos).magnitude.ToString("0") + " m away, out of view, and will be gone the moment you see it (" + g.fear.DescribeLook(look) + ")");
            return true;
        }

        /// <summary>The glimpse's frame: true when it handled the figure (seen, too close, or nobody looked).</summary>
        bool UpdateGlimpse(float now, bool seen, float dist)
        {
            if (!glimpse) return false;
            if (seen && figureSeen > 0.12f || dist < 6f)
            {
                Destroy(figure.gameObject);
                figure = null; houseMonster = null; glimpse = false;
                Sfx.Ui("sting", 0.35f);
                g.Shake(0.03f, "a glimpse of someone, gone when you looked");
                g.RungSeen(1, "at the edge of things");
                return true;
            }
            if (now > figureUntil)
            {
                Destroy(figure.gameObject);
                figure = null; houseMonster = null; glimpse = false;
                g.Log("haunting: the glimpse was never caught, so it went");
                return true;
            }
            return true;
        }
    }
}
