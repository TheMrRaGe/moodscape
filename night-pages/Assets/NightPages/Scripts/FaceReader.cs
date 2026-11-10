using Unity.InferenceEngine;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Reads the player's face through the webcam, inside the game: no phone, no painter. The same network Moodscape
    /// uses (MediaPipe's 478-point face mesh, Apache-2.0, as ONNX), run by Unity's inference engine on the graphics
    /// card. From the points it measures what fear does to a face (FACS): brows pulled up (AU1+2) and together
    /// (AU4), upper lids raised so the whites show (AU5), lips stretched sideways (AU20), jaw dropped (AU26). And
    /// what fear does to looking: blinks suppressed while something is building, a blink snapped shut at a fright,
    /// the head pulled back (a recoil), the face turned away from the screen.
    ///
    /// Everything is measured against this player's own resting face (the first seconds of play, then a slow
    /// drift), so a stony face and an expressive one read alike. Off unless the player turns it on; the camera
    /// image never leaves this PC and nothing is saved.
    /// </summary>
    public class FaceReader : MonoBehaviour
    {
        public static FaceReader I;

        // What it currently sees. Read these from anywhere.
        public static bool On, Present;
        public static float Fear;               // 0..1: how frightened the face looks, against this player's normal
        public static float EyesOpen = 1f;      // 0..1+ against normal; near 0 = eyes shut
        public static bool Blinking;            // eyes shut right now
        public static int Blinks;               // counted since start
        public static float BlinkRate;          // per minute, recent
        public static bool LookingAway;         // face turned from the screen, eyes covered, or gone
        public static float AwayFor;            // seconds
        public static float FlinchAt = -10f;    // Time.time of the last recoil
        public static float Lean;               // >0 closer to the screen than normal, <0 further back
        // The face itself, for anything that wants to copy it: head turn and tilt (degrees), jaw and brows (0..1+).
        public static float HeadYaw, HeadRoll, Jaw, Brows;
        public static string Status = "off";

        const int Size = 256;
        WebCamTexture cam;
        Texture2D still;                // the self-test feeds a photo instead of the camera
        public static string TestReport = "";
        Worker worker;
        Tensor<float> input;
        float[] pixels;
        Color32[] frame;
        bool pending;
        float lastRun;

        // The crop around the face, in camera pixels.
        float cx, cy, cs;
        bool tracking;
        int searchStep;

        // Baselines (running mean and spread) for each measure.
        readonly Stat brow = new Stat(), knit = new Stat(), eye = new Stat(), stretch = new Stat(), jaw = new Stat(), smile = new Stat(), scale = new Stat();
        float seenFor, closedSince = -1f, lastBlinkAt, awaySince = -1f;
        readonly float[] blinkTimes = new float[40];
        int blinkHead;
        float scaleFast, scaleSlow;

        class Stat
        {
            public float mean, var = 1e-4f;
            public int n;
            public void Add(float x, float slow)
            {
                n++;
                float a = n < 90 ? 1f / n : slow;
                float d = x - mean;
                mean += a * d;
                var += a * (d * d - var);
            }
            public float Z(float x) { return (x - mean) / Mathf.Sqrt(Mathf.Max(var, 1e-6f)); }
        }

        public static void ResetStatics()
        {
            I = null; On = Present = Blinking = LookingAway = false; Fear = Lean = BlinkRate = AwayFor = 0f; EyesOpen = 1f; Blinks = 0; FlinchAt = -10f; Status = "off";
        }

        /// <summary>Turn face reading on (asks Windows for the camera) or off.</summary>
        public static void Enable(bool on)
        {
            PlayerPrefs.SetInt("np_face", on ? 1 : 0);
            if (on && I == null) new GameObject("Face reader").AddComponent<FaceReader>();
            else if (!on && I != null) Destroy(I.gameObject);
        }

        public static bool WantedOn { get { return PlayerPrefs.GetInt("np_face", 0) == 1 || System.Environment.CommandLine.Contains("-npface"); } }

        void Awake()
        {
            I = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            var asset = Resources.Load<ModelAsset>("NPFace/face_landmarks");
            if (asset == null) { Status = "face model not installed (tools/fetch_face_model.py)"; enabled = false; return; }
            if (still == null && WebCamTexture.devices.Length == 0) { Status = "no camera found"; enabled = false; return; }
            var model = ModelLoader.Load(asset);
            worker = new Worker(model, SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU);
            input = new Tensor<float>(new TensorShape(1, Size, Size, 3));
            pixels = new float[Size * Size * 3];
            if (still != null) { On = true; Status = "self-test"; return; }
            // The front camera if there is one, otherwise the first.
            string dev = WebCamTexture.devices[0].name;
            foreach (var d in WebCamTexture.devices) if (d.isFrontFacing) dev = d.name;
            cam = new WebCamTexture(dev, 640, 480, 30);
            cam.Play();
            On = true;
            Status = "starting camera: " + dev;
            if (GameDirector.I != null) GameDirector.I.Log("face reading: on, camera '" + dev + "' (on this PC only; nothing is saved)");
        }

        void OnDestroy()
        {
            if (cam != null) cam.Stop();
            input?.Dispose();
            worker?.Dispose();
            On = Present = false;
            Status = "off";
            if (I == this) I = null;
        }

        void Update()
        {
            if (worker == null || (still == null && (cam == null || !cam.isPlaying))) return;
            float now = Time.unscaledTime;
            if (pending) { Collect(now); pending = false; }
            int w, h;
            if (still != null)
            {
                w = still.width; h = still.height;
                if (frame == null) frame = still.GetPixels32();
            }
            else
            {
                if (!cam.didUpdateThisFrame || now - lastRun < 1f / 24f) return;
                w = cam.width; h = cam.height;
                if (w < 32) return;
                if (frame == null || frame.Length != w * h) frame = new Color32[w * h];
                cam.GetPixels32(frame);
            }
            lastRun = now;
            if (!tracking) Search(w, h);
            Crop(w, h);
            worker.Schedule(input);
            pending = true;             // read the result next frame, so the GPU isn't waited on
        }

        // No face yet: try the middle of the frame at a few sizes, then a little to each side.
        void Search(int w, int h)
        {
            float[] sizes = { 0.75f, 0.55f, 0.95f };
            float[] offs = { 0f, -0.18f, 0.18f };
            int s = searchStep % 3, o = (searchStep / 3) % 3;
            cs = h * sizes[s];
            cx = w * 0.5f + w * offs[o];
            cy = h * 0.5f;
            searchStep++;
        }

        // Copy the square crop into the network's input: 256 x 256, RGB 0..1, top row first.
        void Crop(int w, int h)
        {
            float x0 = cx - cs / 2f, yTop = cy + cs / 2f, step = cs / Size;
            int i = 0;
            for (int y = 0; y < Size; y++)
            {
                int sy = Mathf.Clamp((int)(yTop - (y + 0.5f) * step), 0, h - 1);      // camera rows run bottom-up
                int row = sy * w;
                for (int x = 0; x < Size; x++)
                {
                    int sx = Mathf.Clamp((int)(x0 + (x + 0.5f) * step), 0, w - 1);
                    var c = frame[row + sx];
                    pixels[i++] = c.r / 255f; pixels[i++] = c.g / 255f; pixels[i++] = c.b / 255f;
                }
            }
            input.Upload(pixels);
        }

        Vector2 P(float[] pts, int k)
        {
            // Crop pixels (0..256, y down) -> camera pixels (y up).
            float s = cs / Size;
            return new Vector2(cx - cs / 2f + pts[k * 3] * s, cy + cs / 2f - pts[k * 3 + 1] * s);
        }

        void Collect(float now)
        {
            var outPts = worker.PeekOutput("Identity") as Tensor<float>;
            var outFace = worker.PeekOutput("Identity_1") as Tensor<float>;
            if (outPts == null || outFace == null) return;
            float[] pts, face;
            using (var a = outPts.ReadbackAndClone()) pts = a.DownloadToArray();
            using (var b = outFace.ReadbackAndClone()) face = b.DownloadToArray();
            float presence = 1f / (1f + Mathf.Exp(-face[0]));
            bool was = Present;
            Present = presence > 0.5f && pts.Length >= 478 * 3;
            if (!Present)
            {
                tracking = false;
                Status = "looking for a face";
                Away(now, true);
                return;
            }
            seenFor += Time.unscaledDeltaTime;

            // Landmarks (MediaPipe face mesh indices).
            Vector2 eyeL0 = P(pts, 33), eyeL1 = P(pts, 133), eyeR0 = P(pts, 362), eyeR1 = P(pts, 263);
            float iod = Vector2.Distance(eyeL0, eyeR1);
            if (iod < 4f) return;
            float lidL = Vector2.Distance(P(pts, 159), P(pts, 145)) / Vector2.Distance(eyeL0, eyeL1);
            float lidR = Vector2.Distance(P(pts, 386), P(pts, 374)) / Vector2.Distance(eyeR0, eyeR1);
            float lids = (lidL + lidR) * 0.5f;
            float browUp = (Vector2.Distance(P(pts, 105), P(pts, 159)) + Vector2.Distance(P(pts, 334), P(pts, 386))) * 0.5f / iod;   // AU1+2
            float browGap = Vector2.Distance(P(pts, 55), P(pts, 285)) / iod;                                                          // AU4 shrinks it
            float mouthW = Vector2.Distance(P(pts, 61), P(pts, 291)) / iod;                                                          // AU20 widens it
            float jawOpen = Vector2.Distance(P(pts, 13), P(pts, 14)) / iod;                                                          // AU26
            Vector2 lipMid = (P(pts, 13) + P(pts, 14)) * 0.5f, corners = (P(pts, 61) + P(pts, 291)) * 0.5f;
            float cornerUp = (corners.y - lipMid.y) / iod;                                                                            // a smile lifts the corners
            Vector2 nose = P(pts, 1), eyesMid = (eyeL0 + eyeR1) * 0.5f;
            float yaw = (nose.x - eyesMid.x) / iod;

            // Next frame's crop follows the face.
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int k = 0; k < 468; k += 3)
            {
                var q = P(pts, k);
                minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y);
            }
            cx = Mathf.Lerp(cx, (minX + maxX) * 0.5f, 0.7f);
            cy = Mathf.Lerp(cy, (minY + maxY) * 0.5f, 0.7f);
            cs = Mathf.Lerp(cs, Mathf.Max(maxX - minX, maxY - minY) * 1.55f, 0.6f);
            tracking = true;

            // Learn this face's normal (fast at first, then a slow drift), but not from moments of fright.
            bool calm = Fear < 0.5f;
            float slow = calm ? 0.002f : 0.0002f;
            brow.Add(browUp, slow); knit.Add(browGap, slow); eye.Add(lids, slow); stretch.Add(mouthW, slow);
            jaw.Add(jawOpen, slow); smile.Add(cornerUp, slow); scale.Add(iod, slow * 0.5f);
            bool calibrated = brow.n > 60;

            // Blinks: lids well below this person's normal opening.
            EyesOpen = lids / Mathf.Max(0.05f, eye.mean);
            bool shut = EyesOpen < 0.45f;
            if (shut && closedSince < 0f) closedSince = now;
            if (!shut && closedSince >= 0f)
            {
                float held = now - closedSince;
                if (held < 0.5f && now - lastBlinkAt > 0.12f)
                {
                    Blinks++;
                    lastBlinkAt = now;
                    blinkTimes[blinkHead++ % blinkTimes.Length] = now;
                }
                closedSince = -1f;
            }
            Blinking = shut;
            int recent = 0;
            foreach (var t in blinkTimes) if (t > 0f && now - t < 30f) recent++;
            BlinkRate = recent * 2f;

            // Pulled back from the screen in a fraction of a second: a recoil.
            scaleFast = Mathf.Lerp(scaleFast <= 0f ? iod : scaleFast, iod, 0.6f);
            scaleSlow = Mathf.Lerp(scaleSlow <= 0f ? iod : scaleSlow, iod, 0.05f);
            if (calibrated && scaleFast < scaleSlow * 0.93f && now - FlinchAt > 1.5f) FlinchAt = now;
            Lean = calibrated ? Mathf.Clamp(scale.Z(iod) * 0.3f, -1f, 1f) : 0f;

            // Fear on the face: brows up and together, whites of the eyes, lips stretched, jaw dropped; not a smile.
            if (calibrated)
            {
                float z = 0.32f * brow.Z(browUp) - 0.22f * knit.Z(browGap) + 0.28f * Mathf.Max(0f, eye.Z(lids)) +
                          0.22f * stretch.Z(mouthW) + 0.15f * jaw.Z(jawOpen) - 0.35f * Mathf.Max(0f, smile.Z(cornerUp));
                float target = 1f / (1f + Mathf.Exp(-(z - 1.0f) * 1.6f));
                Fear = Mathf.Lerp(Fear, target, 0.35f);
            }
            Away(now, Mathf.Abs(yaw) > 0.42f || (closedSince >= 0f && now - closedSince > 0.8f));
            Vector2 eyeLine = eyeR1 - eyeL0;
            HeadRoll = Mathf.Atan2(eyeLine.y, eyeLine.x) * Mathf.Rad2Deg;
            HeadYaw = Mathf.Clamp(yaw * 90f, -60f, 60f);
            Jaw = Mathf.Clamp01((jawOpen - jaw.mean) / Mathf.Max(0.05f, jaw.mean * 3f + 0.08f));
            Brows = Mathf.Clamp01(brow.Z(browUp) * 0.35f);
            Status = !calibrated ? "learning your face (" + brow.n + "/60)" :
                     "fear " + Fear.ToString("0.00") + ", blinks " + BlinkRate.ToString("0") + "/min" + (LookingAway ? ", looking away" : "");
        }

        /// <summary>
        /// For the test runs, which have no camera: run the face reader on a photo shipped with the game and report
        /// what it measured, so the whole path (model, inference on the graphics card, landmarks, measures) is checked.
        /// </summary>
        public static System.Collections.IEnumerator SelfTest(System.Action<string> log)
        {
            var photo = Resources.Load<Texture2D>("NPFace/test_face");
            if (photo == null) { log("face test: no test photo"); yield break; }
            var tex = new Texture2D(photo.width, photo.height, TextureFormat.RGBA32, false);
            tex.SetPixels32(photo.GetPixels32());
            tex.Apply();
            var go = new GameObject("Face reader (self-test)");
            go.SetActive(false);
            var fr = go.AddComponent<FaceReader>();
            fr.still = tex;
            go.SetActive(true);
            float t0 = Time.realtimeSinceStartup;
            int frames = 0;
            while (Time.realtimeSinceStartup - t0 < 4f && (!Present || frames < 40)) { frames++; yield return null; }
            float ms = (Time.realtimeSinceStartup - t0) * 1000f / Mathf.Max(1, frames);
            string r = Present
                ? "face test: found the face (crop " + fr.cx.ToString("0") + "," + fr.cy.ToString("0") + " size " + fr.cs.ToString("0") + " in a " + tex.width + "x" + tex.height +
                  " photo), eyes open " + EyesOpen.ToString("0.00") + ", status '" + Status + "', about " + ms.ToString("0") + " ms a frame with the reader running"
                : "face test: PROBLEM: no face found in the test photo (" + Status + ")";
            TestReport = r;
            log(r);
            Destroy(go);
            Destroy(tex);
        }

        void Away(float now, bool away)
        {
            if (away && awaySince < 0f) awaySince = now;
            if (!away) awaySince = -1f;
            LookingAway = away && now - awaySince > 0.35f;
            AwayFor = awaySince < 0f ? 0f : now - awaySince;
        }
    }
}
