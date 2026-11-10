using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The part of the fear engine that works on you over days rather than moments. Each piece comes from fear
    /// research, turned into a rule the house can follow:
    ///
    ///  Conditioning (Pavlov; Rescorla-Wagner). One harmless sound from the house, a music-box phrase, is played
    ///    a couple of seconds before the big frights. Its "associative strength" V is learned trial by trial. Once
    ///    V is high the house can play the phrase alone and let you frighten yourself, and the body readings show
    ///    whether it took. Played alone too often it extinguishes; one unannounced fright brings it back
    ///    (reinstatement), the way real conditioned fear returns.
    ///
    ///  Safety behaviour (Salkovskis). After a fright, people retreat somewhere. The house notes where you go in
    ///    the half-minute after each scare. The place you keep retreating to becomes your refuge, and from day 4
    ///    the house stops respecting it.
    ///
    ///  Unpredictability (Grillon's threat-of-shock studies: unpredictable threat sustains anxiety, predictable
    ///    threat only produces fear at the cue). Gaps between events are drawn from a memoryless distribution, so
    ///    the quiet never tells you how long you have.
    ///
    ///  Habituation and recovery. Each kind of event loses force with repetition and gets some of it back
    ///    overnight; novel events are preferred.
    ///
    ///  (The double tap, a small second fright just as you relax, is now the storyteller's false relief.)
    ///
    ///  Threat imminence (Fanselow; Mobbs). Most of the time it stays at a distance: signs, sounds, a glimpse
    ///    (dread). It comes within arm's reach rarely, and only when the readings say there's room for it.
    /// </summary>
    public class Dread : MonoBehaviour
    {
        GameDirector g;

        // ---- conditioning
        public float V;                         // associative strength of the cue, 0..1
        const float Alpha = 0.35f;              // learning rate for a paired trial
        const float Extinguish = 0.18f;         // how much one unpaired cue weakens it
        int pairings, probes;
        float probeAt = -1f, probeBefore;
        readonly List<float> probeResponses = new List<float>();

        // ---- refuge (a tactic: the storyteller's tactics tracker counts the retreats)
        float watchRetreatUntil;
        float lastSample;
        string refugeTactic;
        public Room Refuge { get; private set; }
        /// <summary>From day 4, or sooner if you've hidden there often enough for the house to have an answer.</summary>
        public bool RefugeCorrupt { get { return Refuge != null && g != null && (g.day >= 4 || (refugeTactic != null && g.teller.tactics.Level(refugeTactic) >= 2)); } }

        // ---- habituation
        readonly Dictionary<string, float> uses = new Dictionary<string, float>();

        public void Init(GameDirector director) { g = director; }

        public void BeginDay(int day)
        {
            // Overnight, habituation partly wears off (spontaneous recovery) and so does the cue, a little.
            var keys = new List<string>(uses.Keys);
            foreach (var k in keys) uses[k] *= 0.45f;
            V *= 0.9f;
            // Yesterday's retreats decide today's refuge.
            Refuge = null;
            refugeTactic = g.teller.tactics.Most("hide in the ", 1.5f);
            if (refugeTactic != null)
            {
                string room = refugeTactic.Substring("hide in the ".Length);
                Refuge = g.house.rooms.Find(r => r.name == room);
                if (Refuge != null)
                    g.Log("dread: after a fright you go to the " + Refuge.name + " (" + g.teller.tactics.Count(refugeTactic).ToString("0.0") + " retreats)" +
                          (RefugeCorrupt ? "; from today it is not safe there" : "; the house has noticed"));
            }
        }

        // ---------------------------------------------------------------- timing

        /// <summary>The next gap between events: memoryless, so waiting never makes the next one more likely.</summary>
        public float NextGap(bool evening)
        {
            float mean = evening ? 14f : 30f;
            float gap = -mean * Mathf.Log(1f - Random.value * 0.98f);
            return Mathf.Clamp(gap, evening ? 5f : 9f, evening ? 50f : 80f);
        }

        // ---------------------------------------------------------------- habituation

        /// <summary>How much force an event still has for this player (1 = fresh, falls with each use).</summary>
        public float Novelty(string name)
        {
            float u;
            uses.TryGetValue(name, out u);
            return Mathf.Exp(-0.55f * u);
        }

        public void Used(string name)
        {
            float u;
            uses.TryGetValue(name, out u);
            uses[name] = u + 1f;
        }

        // ---------------------------------------------------------------- conditioning

        /// <summary>Should this fright be announced by the cue? Early on, nearly always (acquisition).</summary>
        public bool AnnounceWithCue()
        {
            return g.day <= 3 ? Random.value < 0.85f : Random.value < 0.5f;
        }

        public void PlayCue(Vector3 near)
        {
            Sfx.At("musicbox", near + Vector3.up * 1.2f, 0.55f, 0.98f + Random.value * 0.04f);
        }

        /// <summary>A fright followed the cue: the association strengthens.</summary>
        public void Paired()
        {
            pairings++;
            V += Alpha * (1f - V);
            g.Log("dread: the music box came before a fright (" + pairings + " pairings); its pull on you is now " + V.ToString("0.00"));
        }

        /// <summary>A fright with no cue: if the cue had been extinguished, its fear partly returns (reinstatement).</summary>
        public void Unannounced()
        {
            if (pairings >= 2 && V < 0.5f)
            {
                V = Mathf.Lerp(V, 0.7f, 0.5f);
                g.Log("dread: an unannounced fright brings back what the music box meant (" + V.ToString("0.00") + ")");
            }
        }

        /// <summary>
        /// Once the cue has been learned, play it alone and do nothing: the body does the rest. The response over the
        /// next few seconds is measured, which is how the game knows the conditioning took.
        /// </summary>
        public bool ProbeCue()
        {
            if (pairings < 2 || V < 0.4f || probeAt > 0f) return false;
            var room = g.house.rooms[Random.Range(0, g.house.rooms.Count)];
            PlayCue(room.center);
            probeAt = Time.time;
            probeBefore = Reactions.Level;
            probes++;
            V += Extinguish * (0f - V);             // unpaired: it weakens a little each time
            return true;
        }

        // ---------------------------------------------------------------- the double tap

        public void BigFright()
        {
            Look.Kick(1f);
            watchRetreatUntil = Time.time + 25f;
            if (g.teller != null) g.teller.Stimulus(0.5f, Time.time);
        }

        void Update()
        {
            if (g == null || g.player == null) return;
            float now = Time.time;
            // Where do you go after a fright?
            if (now < watchRetreatUntil && now - lastSample > 1f)
            {
                lastSample = now;
                var room = g.house.RoomAt(g.player.transform.position);
                string key = g.house.Outside(g.player.transform.position) ? "outside" : room.name;
                g.teller.tactics.Use(key == "outside" ? "run outside" : "hide in the " + key, 1f / 25f);
            }
            // Did the cue alone frighten you?
            if (probeAt > 0f && now - probeAt > 4f)
            {
                float resp = Reactions.Level - probeBefore;
                probeResponses.Add(resp);
                g.Log("dread: the music box played and nothing happened. Your reaction: " + (resp >= 0f ? "+" : "") + resp.ToString("0.00") +
                      (resp > 0.08f ? " (it has you)" : resp < 0.02f ? " (it doesn't, yet)" : ""));
                probeAt = -1f;
            }
        }

        public string Describe()
        {
            return "cue V " + V.ToString("0.00") + ", " + pairings + " pairings, " + probes + " probes" +
                   (Refuge != null ? ", refuge " + Refuge.name : "");
        }
    }
}
