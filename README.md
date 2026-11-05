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

## The Mirror: a painting that evolves toward what each person lights up at

`mirror.html` is a second app, served by the PC painter. Prop your phone up like a small mirror and look into it. It recognizes who is looking. Every few seconds it changes one thing about the painting (the place, a detail, the time of day, the weather, the colours, the style, the framing) and watches the next second and a half of that person's face. What they warm to stays; what they cool on is undone. Each person gets their own taste, and when someone else steps in, the painting changes for them.

Paintings aren't picked from a list. Each one is built from one choice per section of `pc-painter/mirror-space.txt`, which makes about 50 million possible paintings. Prompts are encoded on the GPU as they're needed.

```
phone:   camera → MediaPipe expression (~14×/s) + face-api fingerprint (128 numbers, every 1.3–3 s) ──► laptop
laptop:  who is it?      nearest known fingerprint, or a new "Guest" after 3 unknown reads
         the loop        every ~2.6 s: change one part (picked by Thompson sampling over what it has learned),
                         let it melt in, compare the face 0.7–2.2 s later with just before: keep or undo
                         · lighting up → hold the painting longer · nothing landing → change three parts at once
         what it learns  an affinity per option (e.g. "time: Dusk"), by Bayesian regression from what was on
                         screen 0.7 s ago → how the face responded, plus every judged before/after change.
                         Each reaction teaches it about all seven parts at once
         memory          the strongest moments (exact painting + layout); a known face opens on its best one
         paint it        SDXS one step per frame, melting between prompt embeddings; layouts slerp  ──► JPEG stream
```

**Run it:**

1. On the laptop, double-click **`pc-painter/mirror-windows.bat`** (on Linux/macOS: `./pc-painter/mirror.sh`). The first run sets up Python and downloads the model. At startup it prints the frame rate and how long a new painting takes to encode.
2. Scan the QR code it prints with your Android phone and open it in Chrome. Tap **Look into the mirror** and allow the camera.
3. Hold still for a couple of seconds. It meets you as "Guest 1"; tap the name to rename yourself.

You can also try it on the laptop with its own webcam: `http://localhost:8765/mirror.html`.

**What it learns from:** a growing smile, smile lines around the eyes, wonder (wide eyes, raised brows, open mouth) and leaning in all count as lighting up. Frowning, lowered brows, a wrinkled nose, looking away and closing your eyes count against. Each person's resting face is learned first, including how the phone sees their head. A phone propped at an angle doesn't count as looking away; only turning away from your own resting pose does. **Something else** jumps somewhere new and counts as a no for the current painting.

**What to expect:** in simulation (`python pc-painter/test_mirror_sim.py`), on a scale where 0 is a random painting and 1 is a viewer's perfect one, what it shows climbs from about 0.3 in the first two minutes to about 0.65 after ten. A new viewer reaches about 0.55 within six minutes. Real faces are noisier than simulated ones.

**Change the space:** edit `pc-painter/mirror-space.txt` (sections like `[place]`, lines like `Label | words for the painter`), then restart. Learned scores are kept by section and label, so new options start fresh and everything else is kept.

**Privacy:** the phone sends the laptop only numbers. Names, face fingerprints, tastes and best moments are stored in `pc-painter/profiles.json` on the laptop, which is git-ignored. **Forget** in the People panel deletes a person; deleting the file forgets everyone.

**Notes:**
- The link changes every time the script restarts, so scan the new QR code then.
- Speed is whatever the laptop's GPU manages; under 6 fps it looks like a slow dissolve rather than live. Up to four frames are in flight at once, so the tunnel's round trip doesn't cap the frame rate.
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
