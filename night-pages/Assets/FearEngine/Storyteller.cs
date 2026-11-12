using System;
using System.Collections.Generic;
using System.Globalization;

namespace FearCore
{
    /// <summary>One thing the storyteller can do to the player. The game supplies what it does.</summary>
    public class Incident
    {
        public string id;                 // stable name (what the learner remembers it by)
        public string label;              // how the log describes it
        public float cost = 1f;           // fright points it spends
        public int tier;                  // 0 a sign (deniable), 1 an encounter, 2 a confrontation
        public int minEpisode = 1;        // first day/level/chapter it may happen
        public float cooldown = 25f;      // seconds before it may happen again
        public string[] tags = new string[0];   // what it is made of: kinds of fear, the sense it uses, where
        public string domain = "incident";      // which compartment of the instinct learns from it
        public bool needsCreature;        // waits while the monster is backstage
        public Func<bool> can;            // optional: is it possible right now?
        public Func<bool> fire;           // do it; return false if it couldn't happen unseen right now
        internal float lastAt = -1e9f;
    }

    public enum Beat { Quiet, BuildUp, Peak, Fade, Relax }

    /// <summary>
    /// The FEAR Engine's storyteller: decides when the next fright comes, how big, and which.
    ///
    /// Pacing (Left 4 Dead's director): build-up, a short peak, fade, then a real rest of 30-45 s before the
    /// next build-up. It changes how often frights come, not how hard any one of them hits. The rest is where
    /// dread lives: the game may use it for a sign or a cue, and sometimes, just as the player relaxes, one
    /// small second fright (false relief).
    ///
    /// Budget (RimWorld's raid points): each build-up gets points = (base + how well the player is doing)
    /// × difficulty × a ramp over the first episodes × adaptation × how late it is in the episode. A player who
    /// is coping gets more; a player who is shaken gets less.
    ///
    /// Adaptation (RimWorld's adaptation days): calm stretches raise it, being overwhelmed (very frightened
    /// for too long, looking away from a peak, anything the game reports) drops it sharply. It scales the
    /// budget from 0.4× to 1.5× and stretches the rests when it is low.
    ///
    /// Choice: the learner (instinct + record, Thompson sampled, wear-aware) scores each possible fright;
    /// within a build-up it escalates from signs to encounters to a confrontation, and confrontations only
    /// come once the player is already on edge (threat imminence). Every fright is measured (reaction in the
    /// four seconds after against the second and a half before) and learned from.
    ///
    /// Every decision is logged with its reason.
    /// </summary>
    public class Storyteller
    {
        // ---- settings
        public float Difficulty = 1f;
        public int RampEpisodes = 5;                  // the ramp reaches full strength on this episode
        public float BasePoints = 2f, StandingPoints = 4f;
        public float PeakHoldMin = 3f, PeakHoldMax = 5f, RelaxMin = 30f, RelaxMax = 45f;
        public float BuildUpMax = 150f, FadeMax = 20f;
        public float FalseReliefChance = 0.35f;
        /// <summary>Difficulty's say over adaptation: the budget it can fall to and climb to, and how fast calm and
        /// shrugged-off frights raise it (easy 0.4-1.15 ×0.6, medium 0.4-1.5 ×1, hard 0.6-1.8 ×1.5, hardcore 0.7-2.1 ×2).</summary>
        public float AdaptFloor = 0.4f, AdaptCeil = 1.5f, CalmRate = 1f;
        /// <summary>A fright that moved the reading less than this was shrugged off (unfazed).</summary>
        public float UnfazedBelow = 0.05f;
        /// <summary>How much one shrugged-off fright raises adaptation (before CalmRate).</summary>
        public float UnfazedStep = 0.12f;
        /// <summary>Frights shrugged off in a row (a real reaction resets it).</summary>
        public int UnfazedStreak { get; private set; }
        /// <summary>The game hears about each shrugged-off fright: (incident, streak).</summary>
        public Action<Incident, int> OnUnfazed;
        int tierBoost;          // the next build-up may start a tier higher
        bool shortRest;         // the next rest is shorter
        public float HotLevel = 0.9f, HotSeconds = 10f;

        // ---- what the game tells it
        /// <summary>How well the player is doing, 0..1 (health, light, sanity: whatever the game has).</summary>
        public float Standing = 0.8f;
        /// <summary>How far through the episode we are, 0..1 (Night Pages: morning 0.2, afternoon 0.4, evening 0.8).</summary>
        public float Arc = 0f;

        // ---- hooks
        public Action<string> Log;
        public Func<Incident, float> Bias;            // the game's own preference, added to an incident's score
        public Action<Incident, float> Outcome;       // after a fright has been measured: (incident, reaction change)
        public Func<bool> DreadMoment;                // once per rest: something that isn't a fright (a cue, a sign)

        // ---- parts
        public readonly Learner learner;
        public readonly Menace menace = new Menace();
        public readonly Tactics tactics = new Tactics();
        public readonly List<Incident> incidents = new List<Incident>();

        // ---- state
        public Beat beat { get; private set; }
        public float Intensity { get; private set; }
        public float Adapt { get; private set; }      // -1..1
        public int Episode { get; private set; }
        public Incident LastFired { get; private set; }
        public float LastStimulus { get { return lastStimulus; } }
        float beatAt, beatLen, nextAt, wave, waveLeft, lastStimulus = -1e9f, hotFor, awayFor, lastNow = -1f;
        bool reliefPlanned, dreadDone, awayCounted;
        float reliefAt;
        string last = "";
        readonly System.Random rng = new System.Random();
        readonly Dictionary<string, int> todayUses = new Dictionary<string, int>();
        public float VarietyPenalty = 0.15f;          // per earlier use today: the same fright twice in an evening is a bore
        readonly List<KeyValuePair<float, float>> readings = new List<KeyValuePair<float, float>>();

        class Pending { public Incident inc; public float at, before; public bool unwatched; }
        readonly List<Pending> pending = new List<Pending>();

        public Storyteller(Learner learner)
        {
            this.learner = learner ?? new Learner();
            menace.Log = s => Say(s);
            tactics.Log = s => Say(s);
        }

        void Say(string s) { if (Log != null) Log(s); }

        static string F(float v, string fmt = "0.00") { return v.ToString(fmt, CultureInfo.InvariantCulture); }

        public void Add(Incident i) { incidents.Add(i); }

        // ---------------------------------------------------------------- the numbers

        public float Ramp { get { return RampEpisodes <= 1 ? 1f : Lerp(0.55f, 1f, (Episode - 1f) / (RampEpisodes - 1f)); } }
        public float AdaptFactor { get { return Adapt < 0f ? Lerp(1f, AdaptFloor, -Adapt) : Lerp(1f, AdaptCeil, Adapt); } }
        public float PeakTarget { get { return Lerp(0.5f, 0.85f, Arc); } }

        /// <summary>The fright points the next build-up gets.</summary>
        public float Points()
        {
            return (BasePoints + StandingPoints * Clamp01(Standing)) * Difficulty * Ramp * AdaptFactor * Lerp(1f, 1.4f, Arc);
        }

        static float Lerp(float a, float b, float t) { t = Clamp01(t); return a + (b - a) * t; }
        static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }

        // ---------------------------------------------------------------- what the game calls

        /// <summary>A new day / level / chapter. Wear and habits partly recover; adaptation fades a little.</summary>
        public void BeginEpisode(int n, float now)
        {
            Episode = n;
            Adapt *= 0.85f;
            UnfazedStreak = 0; tierBoost = 0; shortRest = false;
            learner.Overnight();
            tactics.Overnight();
            pending.Clear();
            readings.Clear();
            todayUses.Clear();
            Intensity = 0f;
            last = "";
            Go(Beat.Quiet, now, 12f + (float)rng.NextDouble() * 10f, "a new day starts quiet");
            Say("storyteller: episode " + n + ". Ramp " + F(Ramp) + ", adaptation " + F(Adapt, "+0.00;-0.00") + " (budget ×" + F(AdaptFactor) + ")");
        }

        /// <summary>Something frightening happened (from any source: a fright, a hit, a chase). 0..1.</summary>
        public void Stimulus(float amount, float now)
        {
            Intensity += Clamp01(amount) * (1f - Intensity);
            lastStimulus = now;
        }

        /// <summary>The player is past what's enjoyable: back off now and for a while.</summary>
        public void Overwhelmed(string why, float weight, float now)
        {
            float was = Adapt;
            Adapt = Math.Max(-1f, Adapt - weight);
            UnfazedStreak = 0; tierBoost = 0; shortRest = false;
            Say("storyteller: OVERWHELMED (" + why + "). Adaptation " + F(was, "+0.00;-0.00") + " -> " + F(Adapt, "+0.00;-0.00") +
                ", so frights get smaller (budget ×" + F(AdaptFactor) + ") and rests longer");
            if (beat == Beat.BuildUp || beat == Beat.Peak) Go(Beat.Fade, now, FadeMax, "backing off");
        }

        /// <summary>
        /// Call every frame. fear: how frightened the player looks right now, 0..1, or a negative number if the
        /// game can't tell. watching: is the player actually there and looking.
        /// </summary>
        public void Tick(float now, float fear, bool watching)
        {
            float dt = lastNow < 0f ? 0f : Math.Min(0.5f, Math.Max(0f, now - lastNow));
            lastNow = now;

            // Intensity: frights push it up; it decays once nothing has happened for a few seconds.
            if (now - lastStimulus > 3f) Intensity = Math.Max(0f, Intensity - 0.035f * dt);
            if (fear >= 0f)
            {
                Intensity = Math.Max(Intensity, fear * 0.9f);
                if (watching) readings.Add(new KeyValuePair<float, float>(now, fear));
            }
            while (readings.Count > 0 && readings[0].Key < now - 12f) readings.RemoveAt(0);

            Measure(now, watching);
            Adaptation(now, dt, fear, watching);

            switch (beat)
            {
                case Beat.Quiet:
                    if (now - beatAt >= beatLen) StartBuildUp(now);
                    break;
                case Beat.BuildUp:
                    BuildUp(now);
                    break;
                case Beat.Peak:
                    if (now - beatAt >= beatLen) Go(Beat.Fade, now, FadeMax, "the peak has had its moment");
                    break;
                case Beat.Fade:
                    if (Intensity < 0.35f || now - beatAt >= beatLen) StartRelax(now);
                    break;
                case Beat.Relax:
                    Relax(now);
                    break;
            }
        }

        /// <summary>
        /// Call instead of Tick while the storyteller has nothing to work with (a menu, a cutscene, the player out
        /// of reach): time stops for its clocks, so a build-up doesn't expire behind a pause screen.
        /// </summary>
        public void Hold(float now)
        {
            float dt = lastNow < 0f ? 0f : Math.Max(0f, now - lastNow);
            lastNow = now;
            beatAt += dt; nextAt += dt; reliefAt += dt; lastStimulus += dt;
            foreach (var p in pending) { p.at += dt; p.unwatched = true; }
        }

        // ---------------------------------------------------------------- pacing

        void Go(Beat b, float now, float len, string why)
        {
            beat = b;
            beatAt = now;
            beatLen = len;
            Say("storyteller: " + b.ToString().ToUpperInvariant() + " (" + why + "; intensity " + F(Intensity) + ")");
        }

        void StartBuildUp(float now)
        {
            wave = waveLeft = Points();
            if (tierBoost > 0) Say("storyteller: you shrugged off the last frights, so this wave may start a tier higher");
            Go(Beat.BuildUp, now, BuildUpMax, "budget " + F(wave, "0.0") + " points = (" + F(BasePoints, "0.#") + " + " + F(StandingPoints, "0.#") + " × standing " + F(Standing) +
               ") × difficulty " + F(Difficulty) + " × ramp " + F(Ramp) + " × adaptation " + F(AdaptFactor) + " × lateness " + F(Lerp(1f, 1.4f, Arc)) +
               "; peak at intensity " + F(PeakTarget));
            nextAt = now + Gap(0f);
        }

        // Memoryless gaps (unpredictable threat keeps anxiety up; predictable threat only frightens at the cue).
        // They shorten as the build-up goes on, and later in the episode.
        float Gap(float progress)
        {
            float mean = Lerp(24f, 9f, progress) * Lerp(1.2f, 0.6f, Arc);
            float g = -mean * (float)Math.Log(1.0 - rng.NextDouble() * 0.98);
            return Math.Max(4f, Math.Min(mean * 3f, g));
        }

        void BuildUp(float now)
        {
            float progress = wave <= 0f ? 1f : 1f - waveLeft / wave;
            if (Intensity >= PeakTarget) { Go(Beat.Peak, now, PeakHoldMin + (float)rng.NextDouble() * (PeakHoldMax - PeakHoldMin), "it reached the peak"); return; }
            if (now - beatAt > BuildUpMax)
            {
                if (TryFire(now, 9f, 2, true)) Go(Beat.Peak, now, PeakHoldMax, "the build-up ran long, so it ended on its biggest fright");
                else StartRelax(now);
                return;
            }
            if (now < nextAt) return;
            float cheapest = float.MaxValue;
            foreach (var i in incidents) if (Eligible(i, now, 2)) cheapest = Math.Min(cheapest, i.cost);
            if (cheapest > waveLeft)
            {
                if (Intensity >= 0.4f) Go(Beat.Peak, now, PeakHoldMin, "the budget is spent and the player is on edge");
                else { Say("storyteller: the budget is spent and it never landed (intensity " + F(Intensity) + ")"); StartRelax(now, 0.5f); }
                return;
            }
            // Escalate: signs first, encounters next, a confrontation last, and only once the player is on edge.
            int maxTier = Intensity >= 0.3f && progress >= 0.5f ? 2 : progress >= 0.25f ? 1 : 0;
            maxTier = Math.Min(2, maxTier + tierBoost);     // a player who shrugged the last ones off gets the bigger ones sooner
            int want = (int)Math.Round(progress * 2f);
            if (TryFire(now, waveLeft, maxTier, false, want)) nextAt = now + Gap(progress);
            else nextAt = now + 3f;
        }

        void StartRelax(float now, float scale = 1f)
        {
            float len = (RelaxMin + (float)rng.NextDouble() * (RelaxMax - RelaxMin)) * Lerp(1.3f, 0.7f, Arc) * (Adapt < 0f ? 1f - Adapt * 0.6f : 1f) * scale;
            if (shortRest)
            {
                float k = Lerp(1f, 0.6f, Math.Min(UnfazedStreak, 3) / 3f);
                len *= k;
                shortRest = false;
                Say("storyteller: a shorter rest (×" + F(k) + ") because the last " + UnfazedStreak + " fright" + (UnfazedStreak == 1 ? "" : "s") + " didn't move you");
            }
            else tierBoost = 0;
            Go(Beat.Relax, now, len, "a real rest of " + F(len, "0") + " s");
            dreadDone = false;
            reliefPlanned = Episode >= 2 && scale >= 1f && rng.NextDouble() < FalseReliefChance;
            reliefAt = now + len * (0.25f + 0.2f * (float)rng.NextDouble());
        }

        void Relax(float now)
        {
            float t = (now - beatAt) / Math.Max(1f, beatLen);
            if (reliefPlanned && now >= reliefAt)
            {
                reliefPlanned = false;
                if (TryFire(now, 1.5f, 1, false, 0, "false relief: just as you started to breathe again"))
                    Say("storyteller: the double tap (a small second fright inside the rest)");
            }
            if (!dreadDone && t > 0.55f)
            {
                dreadDone = true;
                if (DreadMoment != null && DreadMoment()) Say("storyteller: in the quiet, a sign that isn't a fright");
            }
            if (now - beatAt >= beatLen) StartBuildUp(now);
        }

        // ---------------------------------------------------------------- choosing

        bool Eligible(Incident i, float now, int maxTier)
        {
            if (i.minEpisode > Episode || i.tier > maxTier) return false;
            if (now - i.lastAt < i.cooldown) return false;
            if (i.needsCreature && menace.Backstage) return false;
            return true;
        }

        bool TryFire(float now, float budget, int maxTier, bool biggest, int want = 0, string why = null)
        {
            var scored = new List<KeyValuePair<float, Incident>>();
            foreach (var i in incidents)
            {
                if (!Eligible(i, now, maxTier) || i.cost > budget || i.id == last) continue;
                if (i.can != null && !i.can()) continue;
                float s = learner.Draw(i.domain, i.id, i.tags);
                s -= 0.2f * Math.Abs(i.tier - want);
                int used;
                if (todayUses.TryGetValue(i.id, out used)) s -= VarietyPenalty * used;
                if (biggest) s += i.tier * 0.5f;
                if (Bias != null) s += Bias(i);
                scored.Add(new KeyValuePair<float, Incident>(s, i));
            }
            scored.Sort((a, b) => b.Key.CompareTo(a.Key));
            for (int k = 0; k < scored.Count && k < 3; k++)
            {
                var i = scored[k].Value;
                if (!i.fire()) continue;
                i.lastAt = now;
                last = i.id;
                LastFired = i;
                int u; todayUses.TryGetValue(i.id, out u); todayUses[i.id] = u + 1;
                waveLeft -= i.cost;
                learner.Rest();
                Stimulus(0.12f + 0.12f * i.tier, now);
                pending.Add(new Pending { inc = i, at = now, before = Avg(now - 1.5f, now) });
                Say("storyteller: " + (why ?? "fright") + ": " + i.label + " (tier " + i.tier + ", cost " + F(i.cost, "0.#") + ", " + F(Math.Max(0f, waveLeft), "0.#") +
                    " left; expected " + F(learner.Expect(i.domain, i.id, i.tags), "+0.00;-0.00") + ", tried " + learner.Tries(i.domain, i.id) + " times" +
                    (k > 0 ? "; first choice couldn't happen unseen" : "") + ")");
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- measuring

        float Avg(float t0, float t1)
        {
            float s = 0f; int n = 0;
            foreach (var r in readings) if (r.Key >= t0 && r.Key <= t1) { s += r.Value; n++; }
            return n == 0 ? -1f : s / n;
        }

        void Measure(float now, bool watching)
        {
            for (int k = pending.Count - 1; k >= 0; k--)
            {
                var p = pending[k];
                if (!watching && now - p.at < 4f) p.unwatched = true;
                if (now - p.at < 4f) continue;
                pending.RemoveAt(k);
                float after = Avg(p.at + 0.6f, p.at + 4f);
                if (p.unwatched || after < 0f || p.before < 0f) { Say("storyteller: couldn't read you during '" + p.inc.label + "'; nothing learned"); continue; }
                float y = after - p.before;
                Say("storyteller: learned from '" + p.inc.label + "': " + learner.Learn(p.inc.domain, p.inc.id, p.inc.tags, y));
                if (Outcome != null) Outcome(p.inc, y);
                if (y < UnfazedBelow && after < 0.45f) Unfazed(p.inc, y);
                else if (y > 0.15f && UnfazedStreak > 0) { Say("storyteller: that one landed (reaction " + F(y, "+0.00;-0.00") + "); the unfazed streak of " + UnfazedStreak + " ends"); UnfazedStreak = 0; }
            }
        }

        /// <summary>
        /// A fright landed and the player didn't react: push harder. Adaptation rises (scaled by difficulty), the next
        /// rest is shorter, the next wave may start a tier higher, and the game is told (it may bring the figure on).
        /// What they shrugged off already scores lower with the learner, so it reaches for other kinds of fear.
        /// </summary>
        void Unfazed(Incident inc, float y)
        {
            UnfazedStreak++;
            float was = Adapt;
            Adapt = Math.Min(1f, Adapt + UnfazedStep * CalmRate);
            shortRest = true;
            tierBoost = 1;
            Say("storyteller: unfazed by " + inc.label + " (reaction " + F(y, "+0.00;-0.00") + ", " + UnfazedStreak + " in a row). Adaptation " + F(was, "+0.00;-0.00") + " -> " +
                F(Adapt, "+0.00;-0.00") + " (budget ×" + F(AdaptFactor) + "), next rest shorter, next wave may start a tier higher");
            if (OnUnfazed != null) OnUnfazed(inc, UnfazedStreak);
        }

        /// <summary>Teach the learner from something the game measured itself (a dream change, a monster gene).</summary>
        public string Teach(string domain, string id, string[] tags, float y) { return learner.Learn(domain, id, tags, y); }

        // ---------------------------------------------------------------- adaptation

        void Adaptation(float now, float dt, float fear, bool watching)
        {
            // Calm stretches: the player is coping, so it may push a little harder (about +1 over fifteen minutes).
            if (watching && Intensity < 0.3f && (beat == Beat.BuildUp || beat == Beat.Relax)) Adapt = Math.Min(1f, Adapt + dt / 900f * CalmRate);
            // The sweet spot: very frightened for too long is past enjoyable.
            if (fear >= HotLevel && watching) hotFor += dt; else hotFor = Math.Max(0f, hotFor - dt * 0.5f);
            if (hotFor > HotSeconds) { hotFor = 0f; Overwhelmed("very frightened for " + F(HotSeconds, "0") + " s", 0.35f, now); }
            // Looking away from a peak, not just glancing: they can't take it.
            if (!watching && (beat == Beat.Peak || (beat == Beat.Fade && now - beatAt < 6f))) awayFor += dt; else { awayFor = 0f; if (beat == Beat.Relax) awayCounted = false; }
            if (awayFor > 4f && !awayCounted) { awayCounted = true; Overwhelmed("looked away from the peak for 4 s", 0.15f, now); }
        }

        public string Status()
        {
            return beat + " · intensity " + F(Intensity) + " · points " + F(Math.Max(0f, waveLeft), "0.0") + "/" + F(wave, "0.0") +
                   " · adapt " + F(Adapt, "+0.00;-0.00") + " (×" + F(AdaptFactor) + ")" + " · menace " + F(menace.Gauge) + (menace.Backstage ? " (backstage)" : "");
        }
    }
}
