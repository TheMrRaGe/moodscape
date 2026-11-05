# Moodscape

A phone web app that reads your facial expression through the front camera and paints a landscape to match it, using a small text-to-image model that runs entirely on the phone. It also learns from how your face reacts to each painting and steers toward what you respond to.

## How it works

```
front camera ──► MediaPipe Face Landmarker (52 blendshapes, ~25 fps, on-device)
                   │
                   ▼
             mood vector: joy · surprise · anger · sadness · calm  (+ night when your eyes are closed)
                   │  calibrated to your neutral face, smoothed over ~0.6 s
                   ▼
   conditioning = Σ mood weight × (mood prompt embedding + Σ learned style × style direction)
                   │  all pre-computed CLIP embeddings, baked into index.html (2.7 MB)
                   ▼
   SDXS-512 UNet, ONE denoising step on WebGPU  ──►  TAESD decoder  ──►  512×512 painting
                   │  noise seed = the layout; kept steady so the composition stays put
                   ▼
   crossfade + head parallax, with a live weather layer on top (rain, embers, petals, fireflies)
                   │
                   ▼
   your reaction over the next ~2 s  ──►  reward  ──►  updates style taste and layout scores
```

- **Model:** [skillsafeai/sdxs-512-dreamshaper-onnx-webgpu](https://huggingface.co/skillsafeai/sdxs-512-dreamshaper-onnx-webgpu), an fp16 ONNX export of IDKiro's SDXS-512-DreamShaper. The phone downloads only the UNet (632 MB) and decoder (2.5 MB), once. They are saved in the browser's Cache Storage after that.
- **No text encoder on the phone.** All prompts are encoded ahead of time. At runtime the app only adds up embeddings, which saves 246 MB and makes moods and styles blend smoothly.
- **Repaint rule:** a new painting starts when the prompt mix moves more than `AI.repaintDelta` (L1 distance), at least `AI.minGapMs` after the last one, and only after the current painting's reaction window has closed.
- **Fallback:** while the model downloads, or on a browser without WebGPU, a procedural sketch of the same moods is shown instead.

## How it learns from you

The model's weights never change on the phone; training them needs a desktop GPU. What it learns is how to **steer** the model, per mood and per person.

**The reward.** When a painting appears, the app compares your face over the next 0.5–2.6 s with the second just before:

| Signal | Effect on the score |
|---|---|
| smile grows | up (strongest) |
| eyes widen or brows lift (wonder) | up |
| brows lower or frown deepens | down |
| nose wrinkles (disgust) | down (strongest) |
| face leaves the frame | down |

The score is clipped to −1…+1. A painting is only scored if your face was visible just before it appeared, and only while the camera is on and calibrated. Demo mode uses what was learned but never learns.

**Style taste.** Each mood has six style directions: warm, cool, vivid, pastel, photo and epic. Each one is the difference between the mood's prompt with and without a short style phrase. Every painting shows your current taste plus a small random probe. Every (style shown, reaction) pair is stored, and a small reward model is fitted to them by weighted ridge regression, `reaction ≈ a + b·style − c·|style|²`. Your taste becomes that model's predicted best style. The probe shrinks as reactions pile up, so it explores early and settles later. A blended painting credits each mood that made up at least 25% of it, in proportion to its weight.

**Layouts.** Each noise seed gets a running reaction score. When the leading mood changes (at most every 25 s), it either returns to a layout you liked (upper confidence bound) or tries a new one; it explores more early on. Tapping **New world** counts as a mild thumbs-down for the current layout. On launch it starts from your best-scoring layout.

**What to expect.** Face reactions are a noisy signal. In simulation, the viewer reacted only some of the time and had a preferred style for joy scenes. The app's own learner code found the right direction within about 20 reactions and was close to that viewer's preference by about 80. Expect it to feel noticeably "yours" after a few sessions, not a few minutes.

Style changes are deliberately subtle, such as a more saturated field or a softer watercolor sky. In testing, pushing a style twice as far as the cap produced broken images that lost the mood, so taste stays within 1.0 per style and 1.2 overall. Layout learning makes the bigger visible difference.

**Seeing and controlling it.** The **Taste** panel shows per-mood style bars, reaction counts and averages, your favourite layouts, live face signals, and a log of every decision: what was painted, how the reaction scored and why, and which way each mood moved. You can pause learning or reset it there; the reset needs two taps. Everything is stored in this browser's local storage on the phone, about 10–60 KB, and nothing is sent anywhere.

## Run it

The camera and WebGPU both need HTTPS (or `localhost`).

- **GitHub Pages:** push this folder to a public repo, then go to Settings → Pages → Deploy from branch → `main` / root. It is served at `https://<user>.github.io/<repo>/`.
- **Netlify:** drag the folder onto [Netlify Drop](https://app.netlify.com/drop).
- **Local on desktop:** `npx serve .` and open `http://localhost:3000`. To test on an Android phone over USB, run `adb reverse tcp:3000 tcp:3000` and open `http://localhost:3000` in Chrome on the phone.

### On Android

1. Open the link in **Chrome** (version 121 or later, on Android 12 or later). Check `chrome://gpu` and look for "WebGPU: Hardware accelerated". Most phones with a Snapdragon (Adreno) or recent Mali/Immortalis GPU have it. If it's off, try enabling `chrome://flags/#enable-unsafe-webgpu`.
2. Use **Wi-Fi** for the first launch, because of the 635 MB download. The app checks memory and free storage before starting. It is saved on the phone afterward, so later launches start without downloading.
3. Tap **Start camera** and allow camera access. Then hold a relaxed face for about 2 s while it calibrates.
4. To make it feel like an app, open Chrome's ⋮ menu → **Add to Home screen**. It then launches full-screen from its own icon.

Expect about 1–4 s per painting on a recent flagship, and longer for the first one while shaders compile. 6 GB of RAM or more is recommended. With 4 GB, close other apps first; below 4 GB it won't start the painter and shows the live sketch instead. If anything fails, the status line and the Taste panel's log say what happened.

## PC painter: live stream for phones that can't run the model

Budget phones such as the Galaxy A15 don't have the memory or GPU for the painter. `pc-painter/` runs it on a computer's graphics card instead and **streams a continuously repainted landscape** to the phone. The landscape morphs as your face changes, at whatever frame rate the GPU manages.

```
phone: camera → face → mood mix + learned style taste + layout seed ──(~20×/s, a few hundred bytes)──► PC
PC:    SDXS one-step painting on the GPU, with a slow drift in noise space so the scene breathes ──(JPEG frames)──► phone
```

1. On the PC, open the `pc-painter` folder and run **`start-windows.bat`** (Windows; double-click) or **`./start.sh`** (Linux/macOS). The first run installs Python packages, downloads the model (635 MB) and Cloudflare's tunnel tool, then tests your GPU. It prints the GPU's name and a measured frame rate, for example "about 18 frames per second on NVIDIA GeForce RTX 3060, which will look smooth".
2. It then prints a **QR code**. Scan it with the phone and open the link in Chrome. That link opens Moodscape at its usual address and pairs it with this PC, so your learned taste is kept.
3. Tap **Start camera**. The status line shows "Live from <your PC> · N fps".

**Notes:**
- The tunnel address changes each time the script restarts, so scan the new code then.
- Once paired, the phone never tries to run the painter itself. If the PC is off, it shows the sketch and keeps retrying.
- Only numbers leave the phone, never video or images. The PC only accepts stream connections from the app's own addresses.
- Learning still works in the stream. Every 6 s (`LEARN.streamProbeGapMs`) it sends a style variation and scores your reaction to the first frame that shows it.
- Options: `--max-fps 15` caps the frame rate, `--motion 0` turns off the gentle self-motion, and `--no-tunnel` serves on the local network only.
- If the network blocks WebSockets, the app falls back to single paintings over HTTPS (`/api/paint`).

## The Mirror: a painting that evolves toward how you react

`mirror.html` is a second app, served by the PC painter. Prop your phone up like a small mirror and look into it. Every few seconds it changes one thing about the painting and watches the next second and a half of your face. A change that lands stays; one that doesn't is undone. It comes in two editions:

- **Bliss** keeps what you light up at: a smile that reaches your eyes, wonder, leaning in.
- **Unease** keeps whatever makes you squirm *while you keep watching*. That covers horror (ghosts, creatures, something watching), disgust (insects, rot, clusters of holes), fears (deep water, heights, tight spaces) and the uncanny. Each person's mirror finds their own. It reads fear and discomfort: wide eyes and raised brows, a gasp, pressed or stretched lips, a wrinkled nose, pulling back, a flinch. A smile means it isn't working, and leaving means it lost you. There's no gore or graphic violence. The phone asks before entering it, and **Bliss** at the top takes you back.

Paintings aren't picked from a list. Each one is a choice from every section of `pc-painter/mirror-bliss.txt` or `pc-painter/mirror-unease.txt`. Bliss has subject, place, detail, texture, era, time, weather, lighting, mood, palette, style and framing: about 6.3 quadrillion paintings. Unease has subject, place, detail, texture, era, time, lighting, palette, style and framing: about 1.6 trillion. CLIP reads at most 77 tokens, so when a combination runs long, the painter leaves parts out in each file's `drop:` order until it fits. Prompts are encoded on the GPU as they're needed.

```
phone:   camera → MediaPipe expression (~14×/s): smile, brows, eyes, lips, head pose, distance, gaze,
                  head motion, blink rate
                → face-api emotion model (~4×/s): neutral, happy, sad, angry, fearful, disgusted, surprised
                → heart rate from skin colour (~1×/s, rPPG) with a quality score                      ──► laptop
         (only if the viewer ticks "Recognize faces": a face-api fingerprint every 1.3–3 s)
laptop:  who is it?      a known fingerprint, a new "Guest", or, with recognition off, simply this phone
         reading them    every signal as "how far from this person's normal" (a z-score against their own
                         running range), weighted into one reaction; the weights are learned per person
                         and edition from their Love / Skip / Keep taps
         the loop        every ~2.6 s: change one part (Thompson sampling over what it has learned), let it
                         melt in, compare the face 0.7–2.2 s later with just before: keep or undo
                         · reacting strongly → hold longer · nothing landing → change three parts at once
         what it learns  per person, per edition: an affinity for every option (e.g. "time: 3 am"), from
                         every second of viewing and every judged change, plus their strongest moments
         paint it        SDXS one step per frame, melting between prompt embeddings; layouts slerp  ──► JPEG stream
```

**Run it:**

1. On the laptop, double-click **`pc-painter/mirror-windows.bat`** (on Linux/macOS: `./pc-painter/mirror.sh`). At startup it prints the frame rate and how long a new painting takes to encode.
2. Scan the QR code with your Android phone and open it in Chrome. Choose whether it may recognize faces, tap **Look into the mirror** and allow the camera.
3. Hold still for a couple of seconds while it learns your resting face. With recognition on, it meets you as "Guest 1"; tap the name to rename yourself.

You can also try it on the laptop with its own webcam: `http://localhost:8765/mirror.html`.

**How it reads you:** every signal is measured against your own normal range, so a subtle face and an expressive one read alike. The signals are:
- **Expression:** smile, smile lines, raised brows and wide eyes, open mouth, frown, furrowed brows, wrinkled nose, tight lips, wincing, leaning in or pulling back.
- **Attention:** looking away (by head pose or eyes), going still, blink rate, closing your eyes.
- **The emotion model's seven feelings.**
- **Your pulse:** read from the camera. It's used only when the reading is clean (quality 0.4 or higher); in tests, no reading at that quality was wrong.

Each edition starts from sensible defaults (in Bliss, a smile means yes; in Unease, a smile means it isn't working). Then it learns **your** tells. **Double-tap the mirror** when you love what you see, tap **Skip** when you don't, and **Keep** counts as a yes. Each tap teaches it what your face looked like just before you decided. The People panel shows what it has learned, e.g. "raised brows / wide eyes = yes". A Love also holds the painting, saves it as a best moment and counts strongly for every part of it. Someone new starts from what everyone else reacts to, instead of from scratch.

In `python pc-painter/test_face_calibration.py`, a simulated person raises their eyebrows when they like something and smirks when they don't. The defaults read that backwards, and the mirror drifted to −0.54 (worse than random). With occasional taps it learned "eyebrows up = yes" and climbed to +0.46. `node tools/test_heart_rate.js` checks the pulse reader on synthetic skin colour.

**Let Claude run it (the Mirror agent):** double-click **`pc-painter/mirror-agent.bat`** instead of `mirror-windows.bat`. It starts the painter and keeps it running, and lets anything with access to the folder (such as Claude in the desktop app) start, stop or restart it by writing one line to `pc-painter/control/command.txt`. The accepted commands are `start`, `stop`, `restart` and `status`, plus a few safe painter options such as `--gpu 1` or `--max-fps 15`; anything else is refused. It also lets Claude pick up the QR code and read the log:
- The painter's output goes to `pc-painter/status/painter.log`.
- When its link is up, the painter saves `status/qr.png` and `status/link.json`.
- `mirror-agent.bat autostart` also makes it start, minimized, whenever you log in.

Closing its window stops everything.

**Keep:** tap **Keep** to save what's on screen in `pc-painter/captures/<edition>-<date>-<time>-<place>/`. Each capture holds:
- `still.png`
- `loop.webp`: a seamless 48-frame living loop of the painting breathing
- `metadata.json`: in the common NFT metadata shape (`name`, `description`, `image`, `animation_url`, `attributes` for edition and every part), plus the recipe (prompt and layout seed) so the painting can be re-rendered as proof of origin.

Nothing about the viewer goes into a capture.

**What to expect:** in simulation (`python pc-painter/test_mirror_sim.py`), 0 is a random painting and 1 is a viewer's ideal. With a space this big, it takes a while to find you: Bliss climbs to about 0.5–0.6 after fifteen minutes, and Unease to about 0.3–0.45 after ten. Taste is saved, so each session picks up where the last one left off. Real faces are noisier than simulated ones.

**Change the space:** edit the edition's `.txt` file (sections like `[subject]`, lines like `Label | words for the painter`), then restart. Learned scores are kept by section and label. In Unease, leave unwanted things out rather than writing "no gore": CLIP doesn't understand "no", so naming a thing pulls it in.

**Privacy:** the phone sends the laptop numbers, never video or photos.
- **Face recognition is off unless the viewer ticks the box.** With it off, the mirror belongs to that phone (a random id) and no fingerprint is made.
- **With it on,** names, fingerprints, tastes and best moments are stored in `pc-painter/profiles.json` on the laptop, which is git-ignored.
- **Forget** in the People panel deletes a person; deleting the file forgets everyone.

**Android app:** `android/` builds **Moodscape Mirror**, a small app (about 90 KB) that holds the Mirror page and opens it full screen. It asks for the camera when you start, and keeps the screen awake.
1. Get `moodscape-mirror.apk` onto the phone. The painter serves the copy in `android/` at `<its link>/mirror.apk` and prints that address at startup, so you can open it in the phone's browser.
2. Open it. Android asks you to allow installs from your browser once.
3. In the app, tap **Scan the QR code** and point it at the code `mirror-windows.bat` prints, or paste the link.

The app remembers the laptop. When the painter restarts, its link changes: the app notices and asks you to scan again (**People → Change laptop** also does it). The painting still happens on the laptop; the app is the phone's half.

To rebuild after changing `mirror.html`, run `ANDROID_HOME=… bash android/build.sh <versionCode> <versionName>` (see the top of the script for what it needs). The first build makes a signing key in `android/` (git-ignored). Keep it: Android only installs an update over the old app if it's signed with the same key.

**Notes:**
- The link changes every time the script restarts, so scan the new QR code then.
- Speed is whatever the laptop's GPU manages; under 6 fps it looks like a slow dissolve. Up to four frames are in flight at once, so the tunnel's round trip doesn't cap the frame rate.
- `start-windows.bat` still works exactly as before for the mood app. Both apps share the one painter.

## Tuning

At the top of the script in `index.html`:

| Setting | What it does |
|---|---|
| `CONFIG.gains` | Sensitivity of each expression. Raise `sadness` if frowns barely register. |
| `CONFIG.smoothing` | How fast the mood follows your face. |
| `AI.repaintDelta` | Lower values repaint more often and produce more in-between scenes. |
| `AI.minGapMs` | Floor between paintings, which protects battery and heat. |
| `AI.sharpen` | Values above 1 push blends toward the leading mood. Use 1.0 for pure linear mixing. |
| `LEARN.score` | How much each reaction signal counts toward the reward. |
| `LEARN.reactFromMs` / `reactToMs` | The reaction window after a painting appears. |
| `LEARN.sigma0` / `sigmaMin` | How boldly it experiments with styles at first, and the floor it settles to. |
| `LEARN.ridge` | Lower values learn faster but are jumpier. |
| `LEARN.thetaMin` / `thetaMax` / `maxNorm` | How far styles may be pushed before images degrade. |
| `LEARN.seedHoldMs` | Minimum time before a scene change may switch layout. |

## Changing the scenes or styles

Edit `SCENES`, `STYLES` or `STYLE` in `tools/embed_moods.py`, then run:

```
pip install onnxruntime transformers numpy
python tools/embed_moods.py
```

It encodes the prompts and rewrites the embedded data in `index.html`. Keep each prompt with its longest style under 77 CLIP tokens; the script warns if one gets cut off. Changing the style list resets everyone's learned taste on next launch, because the old directions no longer apply.

## License

SDXS-512-DreamShaper is under CreativeML Open RAIL++-M, which carries use-based restrictions that apply to the outputs too. TAESD is MIT. MediaPipe is Apache 2.0.
