# Night Pages (prototype)

A first-person roguelite horror game where the dreams learn what scares you. You are a writer in the
house of the grandparents who raised you. By day: chores, then a chapter of your novel. By night: a
dream that changes one thing at a time, watches how you react, and keeps what unsettles you.

This folder is a Unity vertical slice of the full loop: seven days, six endings, three hidden truths,
escalating hauntings, and the adaptive dream. Everything is greybox and built from code at startup.
The design doc lives outside the repo; this README covers running and changing the prototype.

## Run it

This folder is a Unity project, set for Unity 6000.3.28f1 (already installed on the PC it was built for).

1. In Unity Hub: **Add > Add project from disk**, and pick this `night-pages` folder.
2. Open it. The first open takes a few minutes while Unity imports the models.
3. Open any scene (a new empty one is fine) and press **Play**. Click inside the Game view so it takes the mouse.

There is no scene to set up. `Bootstrap.cs` starts the game in a fresh empty scene whenever you press Play.
To switch that off later, put an object named `NoNightPagesBootstrap` in your scene.

The furniture comes from Poly Haven (CC0: free for any use, no credit required). The models aren't kept in
git. If `Assets/NightPages/Resources/NPModels` is empty, run `python tools/fetch_models.py` (about 50 MB).
Without them the game still runs, with plain boxes for furniture. If the furniture all faces the walls,
flip `Models.FrontIsPlusZ` in `Models.cs`.

## Controls

| Key | Does |
| --- | --- |
| WASD, mouse, Shift | Move, look, hurry |
| E | Use what you're looking at. On loose things: pick up, and E again to put down |
| 1 / 2 / 3 | Choose a line at the typewriter |
| Esc | Pause |
| F1 | **Mind overlay**: every decision the game makes and why (truth, Grip, hauntings, what the dream is trying) |
| F2 | Skip ahead one phase (finish chores, auto-write the chapter, go to bed, wake up) |
| Hold F / G | In a dream, fake an unsettled / amused face, to watch the engine learn without a camera |

## With the Moodscape painter (real face reading)

The game runs on its own. With the painter running on the same PC, the dream uses your real face and
hangs the live painting on its walls, and on the paintings and mirror in the house.

1. Start the painter: `pc-painter/mirror-windows.bat` (needs this branch's `painter_server.py`, which adds
   the two game endpoints).
2. On the same PC, open `http://localhost:8765/mirror.html` in a browser, choose the **Unease** edition,
   and start the camera. Keep that window visible on screen (a second monitor, or beside Unity):
   browsers slow the camera down in hidden tabs.
3. Press Play in Unity. F1 shows `painter: reading your face` when the link is live.

Without the painter, the reaction comes from what you do in the game (backing away, freezing, flinching
the camera), which is a rough stand-in, and the paintings show a generated placeholder.

## The house and the woods

**The rooms move every playthrough.** The study, kitchen, bedroom and living room go into the four corners off the
hall in a different order each run (24 layouts; `-nplayout N` forces one). Each room is drawn once and mirrored into
its corner, so everything in it keeps its place relative to its own door, walls and windows; the windows outside,
the chimney and the bins in the yard follow. Every build checks the layout makes sense (each door swings open
without hitting anything, a clear way in through every doorway, nothing tall in front of a window, somewhere to
stand beside everything you use, no furniture inside other furniture or the walls) and logs any problem to the
Mind overlay; the tour checks and photographs four layouts that between them put every room in every corner.

The house sits alone in a clearing deep in a conifer forest, at the end of a dirt drive that disappears into the
trees. Outside: weathered siding, a grey gable roof, a brick chimney, shuttered windows that glow when the lamps
are on, and a front porch with a rocking chair and wind chimes. The yard is your grandparents', left as it was:
the covered car on the drive, the mailbox (the mail chore is out there now), firewood stacked against the wall
with the axe in the chopping block, sheets still on the clothesline, the picnic table and fire pit, Grandma's
planter boxes and her gnome, the rain barrel, the padlocked shed. The woods (about 1,700 generated pines) close
in past the yard; the trees get too dense to go further.

Inside, rooms are furnished at true scale from real-world model sizes and cluttered the way a lived-in house is:
boots and a hat by the door, a tea set and fruit on the kitchen table, glasses and postcards on the side table,
a chess game left half-played, boxes nobody unpacked. Most small things can be picked up.

Sound: recorded footsteps on the boards (generated ones on grass), old doors with latches and creaking hinges,
the hall clock ticking, the fire, the house settling, page turns when you read; outside, birds and wind by day
and crickets at night, muffled through the walls when you're in. Sources and licences: THIRD_PARTY.md.
Models and textures: `python tools/fetch_assets.py` (all CC0 from Poly Haven).

The monster's surfaces are painted in code (MonsterSkin.cs): veined dark skin, waterlogged skin, crazed doll
porcelain, greasepaint, a faded harlequin suit, a knitted cardigan, a tweed coat, hospital gown, chitin, scales,
mangy fur. Its body is jointed: knees and feet, elbows, long fingers, and on the bare forms ribs, spine and
collarbones showing through.

## The quieter frights, and the nights

Researched from what really frightens people, then built as incidents the storyteller chooses and learns from:

- **Sleep paralysis** (Cheyne's three kinds of experience: intruder, incubus, floating). Some nights you wake before
  the dream, facing the bedroom doorway, able to move only your eyes. A presence; a shadow in the doorway; steps,
  closer at each blink; the mattress takes its weight; then it's on your chest. The way out is the one people
  really use: move one finger, again and again (E). Guaranteed once by day 4; more often for players it works on.
- **The false awakening** (Green and McCreery's "type 2": an ordinary morning with something wrong). You wake to
  day N: but the windows are dark, the hall clock has stopped, every door is open, the chore list won't read, and
  something stands at the end of the hall. Look at it too long and it's in front of you. Then you wake for real.
- **Someone else lives here** (what people who found a stranger living in their house noticed first): her
  wheelchair somewhere else, facing you; a kitchen chair pulled out and turned to the room; the fruit gone and a
  core on the table; the bed slept in; the television on by itself (and, later, a shape in the snow).
- **The familiar made wrong** (P.T.'s ordinary hallway with small changes): every photograph face down; a stranger
  in the family photos; the telephone ringing, and on the line only your own house: the hall clock and someone
  breathing close to the receiver; your footsteps a beat late behind you, and one more step after you stop;
  knocking from inside the wardrobe; every day on the kitchen calendar crossed off; the 8 mm projector running a
  home movie with someone behind your grandparents, nearer each reel.
- **The hall mirror** is a real mirror. Something can stand behind you in it and not be there when you turn; your
  reflection can run half a second late.

The house is furnished the way a 1960s-70s grandparents' house was: telephone table and rotary phone, a mirror over
the hall table, a humming fridge with a bowl on top, canisters and a wall calendar, a television on its own table,
a china cabinet, doilies, ashtrays and magazines, a day bed in the study, slippers by the bed, crutches in the
corner and her wheelchair by the window. (Models from Poly Haven, CC0.)

**It changes in front of you, gradually.** When the fear engine changes the monster while you're watching, it no
longer swaps: both bodies are melted into one field and it goes through the shapes in between (14 stages computed
on a worker thread), not smoothly: it holds, lurches forward a stage or three, slips back, seizes, lurches again,
with a crack of something giving at each lurch. Halfway, the flesh boils. `-npvideo <dir>` (agent job `video`) films
it in the dark hall by a failing bulb, becoming three other things in turn.

**Everything is its own thing.** Sets come apart into their pieces (the tea set's pot and cups, the chess pieces,
the wine bottles, the books, the pillows, the boots), and the canisters, magazines and ashtrays are separate objects
you can pick up.

**The bot plays it for real.** Each morning it walks to every chore through the doorways (out the front door and
down the drive for the mail), with real collisions, opening doors it bumps into, aims at the thing and uses it with
the same ray the E key uses; it walks to the typewriter in the afternoon and the bed at night. Anything it can't
reach, can't see to use, or that doesn't work when used is logged as a PLAYTHROUGH PROBLEM and counted in the
summary.

## The storyteller (FEAR Engine core)

The house's frights are run by a storyteller (`Assets/FearEngine`, pure C# with no Unity references, so other
games can use it). It decides when the next fright comes, how big, and which:

- **Pacing** (after Left 4 Dead's director): build-up, a 3-5 s peak, fade, then a real rest of 30-45 s. In the rest it
  may play the music box alone (a dread moment), and sometimes a small second fright lands just as you relax.
- **Budget** (after RimWorld's raid points): each build-up gets points = (2 + 4 × Grip) × difficulty × a ramp over the
  first five days × adaptation × how late in the day it is. Signs cost 1, encounters 2, confrontations 3.
- **Adaptation**: calm stretches let it push harder; being very frightened for 10 s (here or in a dream) or looking
  away from a peak makes it back off and rest longer.
- **Escalation**: signs, then encounters, then a confrontation, and only once you're already on edge.
- **The monster's leash** (after Alien: Isolation): while it is close the menace gauge fills; when full it goes
  back into the walls for a while.
- **Your habits**: where you hide after a fright is a tactic it counts; hide there often enough and it stops being safe.
- **Hallucinations** (slot 17): in the corner of your eye, a painting that looks back, the hall stretching, something
  outside the window, a figure in a doorway that is gone when you blink, a whisper at your ear, the walls breathing,
  a line in the manuscript you didn't write. Chosen and learned like any other fright, and the score goes back into
  the dreams.
- **Learning**: every fright is measured (your reaction in the four seconds after against the second and a half
  before) and learned from by two memories: an instinct built on the fruit fly's mushroom body, which judges a fright
  by what it is made of (so it has an opinion about combinations nobody has tried), and a record per fright that takes
  over as evidence comes in. The same instinct gives the dreams and the monster's look their first impressions.
  Saved to `nightpages-teller.txt` beside the fear profile.

F1 shows the storyteller's beat, intensity, points, adaptation and menace. How well the learner does, honestly
measured, is in `Assets/FearEngine/README.md` (bench: `tools/fear/teller_sim.py`).

## The fear engine, part two: dread

On top of the moment-to-moment fear engine (Thompson sampling over the monster's shape, the dream spaces and
the sounds, aimed at each player's "just right" level of fear), `Dread.cs` works on the player over days,
using rules taken from fear research:

- **Conditioning** (Pavlov; Rescorla-Wagner). A music-box phrase from somewhere in the house is played a couple of
  seconds before the big frights. Its associative strength is learned trial by trial. Once learned, the house
  plays it alone and lets the player frighten themselves; played alone too often it extinguishes, and one
  unannounced fright brings it back (reinstatement). The Mind overlay logs every pairing and the player's
  measured reaction to the phrase alone.
- **Safety behaviour** (Salkovskis). Where the player goes in the half-minute after each fright is recorded. The
  room they keep retreating to becomes their refuge, and from day 4 that is where the worst things happen.
- **Unpredictability** (Grillon's threat-of-shock work: unpredictable threat sustains anxiety). The gaps between
  events are memoryless, so waiting never means it's nearly over.
- **Habituation and recovery.** Each kind of event loses force with repetition and recovers overnight; fresh
  frights are preferred.
- **The double tap.** After a big fright, sometimes a second, smaller one lands just as the player relaxes (now the storyteller's false relief).
- **Threat imminence** (Fanselow; Mobbs). It keeps its distance most of the time: signs, sounds, a glimpse. It
  comes within reach rarely: "it was right behind you" stands at the player's back, breathing, and shows itself
  only when they turn round.

## Face reading (in the game)

Press **C** to turn on the webcam. The game runs MediaPipe's 478-point face mesh (the network Moodscape uses) on
the graphics card through Unity's Inference Engine, and measures what fear does to a face (FACS AU1+2, AU4, AU5,
AU20, AU26), blinking, recoiling from the screen and looking away, all against the player's own resting face.
It feeds the fear engine's face signal (and its flinch signal on a flat screen), and it changes the house:

- look away from the screen and the house moves something while you aren't watching;
- late in the run, the thing in the hall comes closer every time you really blink.

The camera image never leaves the PC and nothing is saved. Without a camera, the Moodscape Mirror on a phone still
works as before.

## How it looks

Linear lighting with Unity's post-processing stack: ACES filmic tonemapping, ambient occlusion, bloom, eye
adaptation (stepping into a dark room stays dark for a moment), grain, vignette, SMAA. Fear drains the colour and
tightens the vignette; a scare hits the image (`Look.cs`). The monster's body is generated as one continuous
skinned mesh over its skeleton (`Flesh.cs`): a signed-distance field melted with smooth unions and meshed with
marching tetrahedra, so ribs and spine push up under the skin and it bends as it moves.

## The monster

It has no fixed shape. The fear engine picks each part of it, keeps what you react to, and the version it
settles on is the one that appears in the house from Day 5 and hunts you from Day 6.

| Trait | Options |
| --- | --- |
| What it is like | a person (almost), a spider, a doll, a clown, a snake, a hound, a swarm, covered in holes, drowned, the doctor, Grandma from behind, you, a shadow |
| Body | tall and thin, hunched, too tall for the room, on all fours, child-sized |
| Head | ordinary, tilted too far, long neck, too many heads, none |
| Arms | at its sides, reaching the floor, too many, reaching for you, none, bending the wrong way |
| Face | none, pale eyes, a mouth and nothing else, a smile, too many eyes |
| How it holds itself | still, swaying, twitching, slowly shaking its head |
| What it does | not here, heard but never seen, only stands, mirrors you, moves when you don't look, walks away from you |
| How it changes | only when you look away, or while you watch |

### Kinds of fear

Every option (monster traits, dream spaces, sounds, light, wrongness) is tagged with the kinds of fear it plays
on, and the engine learns a score for each kind as well as for each option. Once a kind works on you, options of
the same kind you haven't seen yet get tried sooner. The kinds come from three ways of sorting fear:

| Kind | Where it comes from |
| --- | --- |
| The unknown | Stephen King's "terror": what you imagine |
| Being hunted | Albrecht's five basic fears: extinction |
| The body made wrong | Albrecht: mutilation; King's "horror" |
| Animals | DSM-5 specific phobia: animal type |
| Disgust | King's "gross-out" (no gore) |
| The uncanny | DSM-5 "other" type (costumed characters); the uncanny valley |
| Being trapped | Albrecht: loss of autonomy; DSM-5 situational type |
| Heights, water and the dark | DSM-5 natural environment type |
| Needles and doctors | DSM-5 blood-injection-injury type (never wounds or blood) |
| Being left | Albrecht: separation |
| Yourself | Albrecht: ego-death |

From Day 3 the house uses the kind that works best on you: scratching in the walls, dripping water, a doll in the
hall, Grandma's chair rocking, typing from the empty study, footsteps coming closer, a door slamming behind you,
a heart monitor in another room, joints cracking behind you.

**The sweet spot.** A haunted-house study (Andersen et al., Aarhus University, Psychological Science 2020) found
enjoyment rises with fear up to a point and then falls. So the engine doesn't hold you at the top: after 10 s of
being very frightened it removes the most intense thing on screen and changes nothing for 20 s. The third time in
one dream, the most intense options are switched off for the rest of it. Every decision shows in the F1 overlay.

To leave a form out (a real phobia is not the kind of fear this is for), set the PlayerPrefs string
`np_banned` to a comma-separated list of option labels, for example `A spider,Covered in holes`. A settings
screen for this is on the roadmap.

## Reading fear

The game blends whatever signals it has into one number, and F1 shows each one's share:

- **Face**: from the Moodscape painter (see above).
- **Heart rate**: from a watch or chest strap, through `tools/heart-bridge` (double-click
  `start-heart-bridge.bat`). Bluetooth heart-rate broadcast works directly (Polar, Garmin with broadcast on,
  most straps). Apple Watch, Galaxy Watch and Wear OS go through the Pulsoid app.
- **Voice**: microphone loudness only (gasps, shouts). Off until you turn it on; nothing is recorded.
- **Body**: what you do in the game: backing away, freezing, flinching the camera.
- **VR**: head flinches and hand tremor, when a headset is running.

## VR

VR mode switches on by itself when a headset is active. To enable a headset in the project: **Edit >
Project Settings > XR Plug-in Management**, install it, and tick **OpenXR** (works with Quest over Link or
Air Link, SteamVR headsets and Windows Mixed Reality). Left stick walks, right stick turns in steps, the
right trigger uses what your right hand points at. VR has not been tested on a headset yet.

## Testing alongside your work (the Unity agent)

Night Pages can be built and play-tested from the command line with no window (the same idea as Verge), so Claude
can test it remotely while you use the PC, without changing your active app.

**Set up once:** double-click **Install Night Pages agent (background).bat**. It registers a Windows scheduled task
(for your user only) that runs the agent with `pythonw` (no console, no window) at log-on, and checks every minute
that it's still running, so it comes back by itself after a crash, a reboot or an update. Nothing it runs opens a
window or takes focus. Remove it with **Uninstall Night Pages agent (background).bat**. (**Start Night Pages
agent.bat** still runs it in a visible console if you ever want to watch its output; only one runs at a time.)

Claude drives it by dropping a one-word file in `agent/requests/` (or `agent/request.txt`): `compile`, `build`,
`tour`, `bot [runs]`, `check`, `watch`, `status`, `restart`. Nothing else is accepted. Unity and the game run
headless at below-normal priority.

- **tour**: the built game takes screenshots of every room by day and evening, the yard and the woods, the cellar,
  every monster form (and close-ups) and every dream space, and checks that the things you can move stay put.
- **bot**: plays whole runs at speed (chores, writing, reading clues or not, dreams with a stand-in reaction) and
  reports each ending and every error.
- **watch**: the same bot run in a normal window with sound, so you can watch it play. It opens a window, so it's
  **refused unless `agent/SHOW-WINDOWS` exists**. Create that file only when a window popping up is fine.
- **restart**: the agent exits and the scheduled task starts a fresh one within a minute. It also does this by
  itself when its own code changes, so a new version takes over with nobody touching the PC.
- Results go to `runs/<date>-<time>-<job>/`: Unity's log, `log.txt`, `errors.txt`, `summary.json` and the PNGs.
  `agent/status.json` says what it's doing; its `alive` time updates every 5 s while the agent runs.

Switches (files in `agent/`, noticed within a second):
- `PAUSED`: take no jobs until it's deleted (requests wait in the queue).
- `SHOW-WINDOWS`: allow `watch`.
- `WAIT-FOR-IDLE`: start a job only after this many minutes with no mouse or keyboard input (write the number in the
  file; empty means 5). For times when even background CPU use would get in the way.

If a test run freezes, the game's watchdog writes `hang.txt` (the last step it reached) and the agent ends it after
90 s. Stopping the agent also stops any game it started.

Close Night Pages in the Unity editor before a `compile`, `build` or `check`: Unity can't open one project twice.

## What's where

All in `Assets/NightPages/Scripts`:

| File | Job |
| --- | --- |
| `GameDirector.cs` | The run: day and phase flow, Grip, the hidden truth, clues, endings |
| `House.cs`, `Door.cs`, `Interactable.cs` | The greybox house and everything you can use in it |
| `Chores.cs` | The morning task pool and how each task goes wrong later in the run |
| `Manuscript.cs` | The novel: prompts, tagged lines, the "knows too much" lines, low-Grip rewrites |
| `Hauntings.cs` | The daily haunting budget, the events, the look-away rule, dream bleed |
| `FearEngine.cs` | The learner: try one change, judge the reaction, keep or undo; saved across runs |
| `Dream.cs` | Builds the dream spaces and makes each of the engine's choices real |
| `Monster.cs` | The shape-changing antagonist, built from its seven traits |
| `Models.cs`, `Movable.cs` | Loading and fitting the downloaded furniture, generated textures, things you can carry |
| `FearTypes.cs` | The eleven kinds of fear and where each comes from |
| `Tour.cs`, `Editor/NightPagesBuild.cs` | Headless screenshot tours, bot playthroughs and the command-line build |
| `Sensing.cs` | Blends face, heart rate, voice, body and VR signals into one fear reading |
| `VRRig.cs`, `VRHud.cs` | Headset tracking, controllers and the in-headset interface |
| `MoodscapeLink.cs` | The link to the PC painter, and the in-game reaction fallback |
| `Player.cs`, `In.cs`, `Hud.cs`, `Sfx.cs`, `Build.cs` | Body and input, interface, generated audio, greybox helpers |

Tuning lives next to the thing it tunes: dream options are the `Slots` table in `FearEngine.cs`, story
text is in `Manuscript.cs` and `GameDirector.cs` (`ClueTexts`, endings), hauntings are the list in
`Hauntings.Init`.

The fear profile is saved to `nightpages-fear.json` in Unity's persistent data folder. Delete it to
make the dreams forget you.

## Test the fear engine without Unity

```
cd Tests/fear-sim
dotnet run
```

It runs the game's real `FearEngine.cs` for seven nights against a simulated player who is unsettled by
three specific things, then against one afraid of a whole kind of fear (animals), and prints PASS/FAIL on
whether it learned each.

## Known limits of this prototype

- Greybox art, generated sound, immediate-mode UI. All placeholders.
- Nobody has run this build in the Unity editor yet: expect placement and scale fixes on first play.
- The house layout is fixed; only chores, hauntings, clues and the truth vary per run.
- Face reading still happens in the browser Mirror page, not inside the game.
- No save mid-run. The only thing kept between sessions is the fear profile and which endings you've seen.
