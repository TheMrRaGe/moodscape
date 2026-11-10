# FEAR Engine core (FearCore)

A storyteller for horror games: it decides **when** the next fright comes, **how big**, and **which**, and it learns
what frightens this particular player. Pure C#, no engine references (`FearCore.asmdef` has
`noEngineReferences`), so it drops into any Unity project, or any .NET game, as it is. Night Pages is its first game.

## What it does

| Part | Where it comes from | What it does |
|---|---|---|
| Pacing | Left 4 Dead's AI Director | build-up → peak (3-5 s) → fade → a real rest (30-45 s) → build-up. Changes how often frights come, not how hard any one hits. |
| Budget | RimWorld's raid points | each build-up gets `(base + standing) × difficulty × ramp × adaptation × lateness` points to spend. |
| Adaptation | RimWorld's adaptation days | calm stretches raise it; being overwhelmed drops it sharply. Scales the budget 0.4×-1.5× and stretches rests when low. |
| Escalation | threat imminence (Fanselow, Mobbs) | signs first, encounters next, a confrontation last, and only once the player is already on edge. |
| False relief | the double tap | sometimes one small fright just as the rest begins. |
| Dread moments | Grillon (unpredictable threat) | gaps are memoryless; once per rest the game may play a cue or a sign that is not a fright. |
| Menace | Alien: Isolation | a gauge that fills while the creature is close (faster when seen); when full it goes backstage for a while. |
| Tactics | Alien: Isolation | the game reports what the player keeps doing; repeated enough, it unlocks the house's answer. |
| Learner | Thompson sampling + a fruit-fly mushroom body | see below. |

Every decision is logged with its reason (`Storyteller.Log`).

## The learner

Two memories, the way an animal has them:

- **Instinct** (`Instinct`): a mushroom body. A fright is described by tags (kinds of fear, the sense it uses,
  where it happens). Tags drive glomeruli, glomeruli drive Kenyon cells, and only the top 5% fire (k-winners-take-all).
  A readout per domain (one compartment each: learning about dreams never overwrites learning about the house) is moved
  by the **prediction error**, what happened minus what was expected (Bennett, Philippides & Nowotny 2021). It never
  sees a fright's name, only what it is made of, so it has an opinion about frights it has never tried.
- **Record**: a score per fright from what it actually did. Draws are Thompson sampled
  (`(n·mean + 1.5·instinct) / (n + 1.5)` plus noise shrinking with evidence), so the instinct is the first impression
  and the record takes over.
- **Wear**: frights wear out with repetition and recover with rest; it learns what each would do fresh and applies
  the wear when choosing. Plus a variety penalty for repeats within one episode.

### How well it learns (tools/fear/teller_sim.py, 60 simulated players)

Mean effect of the chosen fright (higher is better; an all-knowing oracle sets the ceiling):

| | first 30 | 30-120 | **new game, first 20** | new game 20-40 |
|---|---|---|---|---|
| random | -0.03 | -0.04 | -0.00 | -0.03 |
| Thompson sampling alone | +0.06 | +0.19 | +0.04 | +0.12 |
| Thompson + hand-made tag prior | +0.06 | +0.16 | +0.05 | +0.09 |
| **Thompson + instinct (this)** | +0.06 | +0.19 | **+0.10** | +0.13 |
| same, FlyHash control wiring | +0.06 | +0.19 | +0.11 | +0.15 |
| oracle | +0.38 | +0.33 | +0.40 | +0.33 |

(players whose fears combine; ±0.011-0.016 standard error. Players whose fears simply add up show the same pattern:
new game first 20, +0.07 vs +0.01.)

Honest reading: inside one game it **ties** plain Thompson sampling. Its gain is **transfer**: in a new game whose
frights are all new, it starts about 2.4 times stronger, because it carries what kinds of thing frighten this
person. The real connectome wiring and a random control with the same fan-in learn equally well, so the gain comes
from the fly's **architecture** (a wide sparse expansion read out by prediction error), not the particular wiring.
An earlier version (depression-only plasticity with a floor, closer to the biology) did no better than random; the
signed prediction-error readout is what works.

## Using it in a game

```csharp
var teller = new FearCore.Storyteller(new FearCore.Learner(wiring));   // wiring: null for the random control
teller.Log = Debug.Log;
teller.Add(new FearCore.Incident {
    id = "knock", label = "knocking in another room", tier = 0, cost = 1f, minEpisode = 1,
    tags = new[] { "unknown", "sound", "far" },
    fire = () => { PlayKnock(); return true; }      // return false if it can't happen unseen right now
});

// each day / level:   teller.BeginEpisode(day, Time.time);
// every frame:        teller.Standing = health01; teller.Arc = howLateInTheLevel01;
//                     teller.Tick(Time.time, fear01OrMinusOne, playerIsWatching);   (or teller.Hold(t) while paused)
// when anything frightening happens:  teller.Stimulus(0..1, Time.time);
// when the player is past enjoyable:  teller.Overwhelmed("why", 0.2f, Time.time);
// the creature:       teller.menace.Tick(t, dt, distance, seen);  if (teller.menace.Backstage) ...
// coping habits:      teller.tactics.Use("hide in the kitchen");
// saving:             File.WriteAllText(path, teller.learner.Save());   learner.Load(text)
```

`fear01` is whatever the game can read (face, heart rate, voice, how the player moves). Pass a negative number when it
can't tell; nothing is learned from frights it couldn't measure.

## Credits

Mushroom-body wiring: MaleCNS connectome v1.0 (HHMI Janelia FlyEM + Google Research), CC-BY 4.0.
