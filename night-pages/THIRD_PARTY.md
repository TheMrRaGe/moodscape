# Third-party assets

Everything here is CC0 (public domain dedication): free for any use, including commercial, no credit required.
Credits are listed anyway, with where each came from, so the licences can be checked.

## 3D models and textures: Poly Haven

All models in `Assets/NightPages/Resources/NPModels` and textures in `Assets/NightPages/Resources/NPTex`
come from [Poly Haven](https://polyhaven.com) (CC0). They aren't stored in git; `tools/fetch_assets.py`
downloads them. The list of ids is in that script.

## Sounds: OpenGameArt.org (all CC0)

Converted to mono or stereo Ogg in `Assets/NightPages/Resources/NPSounds`.

| Clips | Source |
| --- | --- |
| step_*, dooropen_*, doorclose_*, settle_6-8, pageturn_*, bookopen_0, bookclose_0, chop_0, putdown_0, pickup_* | [50 RPG sound effects](https://opengameart.org/content/50-rpg-sound-effects) by Kenney (CC0) |
| slam_*, settle_0-5, knock_* (built from wood_hit), unlock_0, keys_0 | [100 CC0 metal and wood SFX](https://opengameart.org/content/100-cc0-metal-and-wood-sfx) |
| creak_0 | [Creaky door hinge (spooky)](https://opengameart.org/content/creaky-door-hinge-spooky) |
| clocktick_loop | [Ticking clock](https://opengameart.org/content/ticking-clock) |
| fire_loop | [Fireplace sound loop](https://opengameart.org/content/fireplace-sound-loop) |
| crickets_loop | [Crickets ambient noise (loopable)](https://opengameart.org/content/crickets-ambient-noise-loopable) |
| forestday_loop | [Birds and Wind - Ambient](https://opengameart.org/content/birds-and-wind-ambient-birds-wind-and-synth) |
| chimes_loop | [Wind Chimes](https://opengameart.org/node/97378) |

Every other sound (the drone, heartbeat, whispering, lullaby, scratching, heart monitor, footsteps on grass)
is generated in code in `Sfx.cs`.

## Face reading model

`Assets/NightPages/Resources/NPFace/face_landmarks.onnx`: Google's MediaPipe Face Landmarker face-mesh network
(478 landmarks), Apache License 2.0, (c) Google LLC. Format conversion to ONNX by
[senty-au/face_landmarks_detector-ONNX](https://huggingface.co/senty-au/face_landmarks_detector-ONNX) (weights
unchanged; sha256 7d6e82dee82a1dca5fbddb282b3cc74571833a530de317fc22ae325c3358beeb). Runs on the player's PC through
Unity's Inference Engine (com.unity.ai.inference). Camera images are never stored or sent anywhere.

## Post-processing

Unity Post Processing Stack v2 (com.unity.postprocessing), Unity Companion License.

## Fruit fly wiring (the fear engine's instinct)

`Assets/NightPages/Resources/NPFly/mushroom_body.json`: the glomerulus → Kenyon cell → mushroom-body output
wiring of the male fruit fly, extracted by `tools/fly/extract_mb.py` from the MaleCNS connectome v1.0
(HHMI Janelia FlyEM + Google Research), licensed CC-BY 4.0. The game uses the glomerulus → Kenyon cell part to
build the FEAR Engine's instinct (`Assets/FearEngine`).
