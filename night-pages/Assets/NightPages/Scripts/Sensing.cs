using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;

namespace NightPages
{
    /// <summary>One way of reading the player, and what it currently says.</summary>
    public class Signal
    {
        public string name;
        public float weight;
        public bool live;
        public float value;         // -1 (amused, bored) .. +1 (unsettled), already measured against this player's own normal
        public string note = "";
        public Signal(string name, float weight) { this.name = name; this.weight = weight; }
    }

    /// <summary>
    /// How unsettled the player is right now, from -1 to +1, fused from every signal that is available:
    ///   face        the Moodscape Mirror's expression reading (not in VR: the headset hides the face)
    ///   heart       beats per minute and beat-to-beat variability from a watch or strap, via tools/heart-bridge
    ///   voice       gasps and cries picked up by the microphone, as loudness against the room's normal
    ///   flinch      sudden head movement in VR
    ///   hands       shaking controllers, or hands thrown up near the face, in VR
    ///   behaviour   backing away, freezing, whipping the camera round
    /// Every signal is compared with the player's own baseline, so a calm person and a jumpy one read alike.
    /// Nothing here is recorded or sent anywhere. The Mind overlay (F1) shows each signal live.
    /// </summary>
    public static class Reactions
    {
        public static float Level;
        /// <summary>Set by automated test runs to stand in for a player's reaction.</summary>
        public static float? Override;
        public static bool Watching = true;
        public static string Source = "in-game";
        public static HeartLink heart;
        public static VoiceSense voice;

        public static readonly Signal Face = new Signal("face", 1f), Heart = new Signal("heart", 0.9f), Voice = new Signal("voice", 0.8f),
            Flinch = new Signal("flinch", 0.7f), Hands = new Signal("hands", 0.5f), Behaviour = new Signal("behaviour", 0.6f);
        public static readonly Signal[] All = { Face, Heart, Voice, Flinch, Hands, Behaviour };

        static float flick, still, headFlinch;
        static float bpmBase, hrvBase, heartSeen;

        public static void ResetStatics()
        {
            Level = 0f; flick = still = headFlinch = 0f; bpmBase = hrvBase = heartSeen = 0f;
            heart = null; voice = null;
            foreach (var s in All) { s.live = false; s.value = 0f; s.note = ""; }
        }

        public static void Tick(MoodscapeLink link, Player p)
        {
            float dt = Time.deltaTime;

            // Face: only when the Mirror page can see one, and never in VR.
            // The game's own camera reading comes first; the Moodscape Mirror on a phone is the fallback.
            if (FaceReader.Present && !VRRig.Active)
            {
                Face.live = true;
                Face.value = Mathf.Clamp((FaceReader.Fear - 0.25f) * 2f, -1f, 1f);
                Face.note = "camera: " + FaceReader.Status;
            }
            else
            {
                Face.live = link != null && link.Face && !VRRig.Active;
                Face.value = Face.live ? link.Reaction : 0f;
                Face.note = Face.live ? "Moodscape Mirror" : VRRig.Active ? "off in VR" : FaceReader.On ? "camera: " + FaceReader.Status : "no camera reading (C turns it on)";
            }

            // Heart: a rise over this player's resting rate, plus a drop in beat-to-beat variability.
            Heart.live = heart != null && heart.Live && heart.Bpm > 30f;
            if (Heart.live)
            {
                heartSeen += dt;
                if (bpmBase <= 0f) bpmBase = heart.Bpm;
                bpmBase += (heart.Bpm - bpmBase) * (heartSeen < 20f ? 0.05f : 0.004f) * Mathf.Min(1f, dt * 30f);
                float rise = (heart.Bpm - bpmBase) / 12f;
                float tight = 0f;
                if (heart.Rmssd > 1f)
                {
                    if (hrvBase <= 0f) hrvBase = heart.Rmssd;
                    hrvBase += (heart.Rmssd - hrvBase) * 0.003f * Mathf.Min(1f, dt * 30f);
                    tight = Mathf.Clamp01((hrvBase - heart.Rmssd) / Mathf.Max(5f, hrvBase)) * 0.5f;
                }
                Heart.value = Mathf.Clamp(rise + tight, -0.5f, 1f);
                Heart.note = Mathf.RoundToInt(heart.Bpm) + " bpm (resting " + Mathf.RoundToInt(bpmBase) + ") via " + heart.SourceName;
            }
            else { Heart.value = 0f; heartSeen = 0f; bpmBase = 0f; hrvBase = 0f; Heart.note = heart != null ? heart.Status : "off"; }

            // Voice: a gasp or a cry is a spike over the room's usual loudness.
            Voice.live = voice != null && voice.Listening;
            Voice.value = Voice.live ? voice.Spike : 0f;
            Voice.note = voice == null ? "off" : voice.Status;

            // VR body language.
            Flinch.live = Hands.live = VRRig.Active;
            if (VRRig.Active)
            {
                if (VRRig.HeadSpeed > 1.3f || VRRig.HeadAngularSpeed > 260f) headFlinch = 0.8f;
                headFlinch = Mathf.MoveTowards(headFlinch, 0f, dt * 0.6f);
                Flinch.value = headFlinch;
                Flinch.note = "head " + VRRig.HeadSpeed.ToString("0.0") + " m/s";
                Hands.value = Mathf.Clamp01(Mathf.Clamp01((VRRig.HandJitter - 0.25f) / 0.6f) + (VRRig.HandsNearFace ? 0.5f : 0f));
                Hands.note = VRRig.HandsNearFace ? "hands up near the face" : "tremor " + VRRig.HandJitter.ToString("0.00");
            }
            else if (FaceReader.Present)
            {
                // On a flat screen the camera sees the recoil: the head snapping back from the screen.
                Flinch.live = true;
                if (Time.time - FaceReader.FlinchAt < 0.1f) headFlinch = 0.8f;
                headFlinch = Mathf.MoveTowards(headFlinch, 0f, dt * 0.6f);
                Flinch.value = headFlinch;
                Flinch.note = "camera: " + (Time.time - FaceReader.FlinchAt < 3f ? "pulled back from the screen" : "lean " + FaceReader.Lean.ToString("0.0"));
                Hands.value = 0f; Hands.note = "VR only";
            }
            else { Flinch.value = Hands.value = 0f; Flinch.note = Hands.note = "VR, or the camera (C)"; }

            // What the player does in the game. Counts for more when there is nothing better to go on.
            Vector3 v = p.velocity; v.y = 0f;
            float backing = Mathf.Clamp01(-Vector3.Dot(v, p.Forward) / 2.5f);
            still = v.magnitude < 0.15f && p.lookSpeed < 15f ? still + dt : 0f;
            float freeze = still > 0.8f && still < 4f ? 0.35f : 0f;
            if (p.lookSpeed > 260f) flick = 0.6f;
            flick = Mathf.MoveTowards(flick, 0f, dt * 0.5f);
            bool better = Face.live || Heart.live || Voice.live;
            Behaviour.live = true;
            Behaviour.weight = better ? 0.3f : 0.9f;
            Behaviour.value = Mathf.Clamp(0.8f * backing + freeze + flick - 0.05f, -1f, 1f);
            Behaviour.note = backing > 0.2f ? "backing away" : freeze > 0f ? "frozen" : flick > 0.1f ? "looked round fast" : "";

            float target = 0f;
            string lead = "behaviour";
            float leadPart = 0f;
            foreach (var s in All)
            {
                if (!s.live) continue;
                float part = s.weight * s.value;
                target += part;
                if (Mathf.Abs(part) > leadPart) { leadPart = Mathf.Abs(part); lead = s.name; }
            }
            Source = lead;
            if (Override.HasValue) { target = Override.Value; Source = "test run"; }
            else if (In.Held(Btn.Scared)) { target = 1f; Source = "test key (F: unsettled)"; }
            else if (In.Held(Btn.Amused)) { target = -1f; Source = "test key (G: amused)"; }

            Watching = Time.timeScale > 0f;
            Level = Mathf.Lerp(Level, Mathf.Clamp(target, -1f, 1f), 1f - Mathf.Exp(-dt * 6f));
        }

        public static string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in All)
                sb.Append("\n  ").Append(s.name).Append(": ").Append(s.live ? s.value.ToString("+0.00;-0.00") + " x" + s.weight.ToString("0.0") : "--")
                  .Append(string.IsNullOrEmpty(s.note) ? "" : "  (" + s.note + ")");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Reads heart rate from tools/heart-bridge running on this PC (http://127.0.0.1:8766/heart).
    /// The bridge takes it from a Bluetooth heart-rate device or a relay app; the game doesn't care which.
    /// </summary>
    public class HeartLink : MonoBehaviour
    {
        public string url = "http://127.0.0.1:8766/heart";
        public bool Live;
        public float Bpm, Rmssd;
        public string SourceName = "", Status = "heart bridge not running";

        [System.Serializable]
        class Reading
        {
            public bool ok = false;
            public float bpm = 0f;
            public float rmssd = 0f;
            public float age_s = 999f;
            public string source = "";
        }

        Thread thread;
        volatile bool run;
        readonly object gate = new object();
        string json;
        bool fresh;
        float lastOk = -99f;
        bool announced;

        void Awake()
        {
            run = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "HeartLink" };
            thread.Start();
        }

        void OnDestroy() { run = false; }
        void OnApplicationQuit() { run = false; }

        void Loop()
        {
            while (run)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 1500;
                    req.ReadWriteTimeout = 1500;
                    req.Proxy = null;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var reader = new StreamReader(resp.GetResponseStream()))
                    {
                        string s = reader.ReadToEnd();
                        lock (gate) { json = s; fresh = true; }
                    }
                    Thread.Sleep(500);
                }
                catch (System.Exception)
                {
                    lock (gate) { json = null; fresh = true; }
                    Thread.Sleep(3000);
                }
            }
        }

        void Update()
        {
            string s; bool isFresh;
            lock (gate) { s = json; isFresh = fresh; fresh = false; }
            if (isFresh && s != null)
            {
                Reading r = null;
                try { r = JsonUtility.FromJson<Reading>(s); } catch (System.Exception) { r = null; }
                if (r != null)
                {
                    if (r.ok && r.age_s < 6f)
                    {
                        lastOk = Time.unscaledTime;
                        Bpm = r.bpm; Rmssd = r.rmssd; SourceName = r.source;
                    }
                    else Status = "heart bridge running, waiting for a device";
                }
            }
            else if (isFresh) Status = "heart bridge not running";
            Live = Time.unscaledTime - lastOk < 6f;
            if (Live && !announced)
            {
                announced = true;
                if (GameDirector.I != null) GameDirector.I.Log("heart rate is live from " + SourceName + "; it now counts toward the reaction");
            }
            if (!Live) announced = false;
        }
    }

    /// <summary>
    /// Listens to the microphone for gasps and cries. It only measures loudness against the room's usual
    /// level; no audio is kept, saved or sent. Off until the player turns it on (V).
    /// </summary>
    public class VoiceSense : MonoBehaviour
    {
        public bool Listening;
        public float Spike;
        public string Status = "off (press V to turn on)";

        AudioClip clip;
        string device;
        readonly float[] buf = new float[1024];
        float baseline = 0.01f, heard;

        public void Toggle()
        {
            if (Listening) { Stop(); Status = "off (press V to turn on)"; return; }
            if (Microphone.devices == null || Microphone.devices.Length == 0) { Status = "no microphone found"; return; }
            device = Microphone.devices[0];
            clip = Microphone.Start(device, true, 1, 16000);
            if (clip == null) { Status = "couldn't open the microphone"; return; }
            Listening = true;
            heard = 0f;
            Status = "listening for gasps (" + device + ")";
        }

        void Stop()
        {
            if (Listening) Microphone.End(device);
            Listening = false;
            Spike = 0f;
        }

        void OnDestroy() { Stop(); }

        void Update()
        {
            if (!Listening || clip == null) return;
            int pos = Microphone.GetPosition(device);
            if (pos <= 0) return;
            int start = pos - buf.Length;
            if (start < 0) start = 0;
            clip.GetData(buf, start);
            float sum = 0f;
            for (int i = 0; i < buf.Length; i++) sum += buf[i] * buf[i];
            float rms = Mathf.Sqrt(sum / buf.Length);
            heard += Time.deltaTime;
            // The room's normal level drifts slowly; a sudden sound stands out against it.
            baseline += (rms - baseline) * (heard < 3f ? 0.05f : 0.002f);
            float ratio = rms / Mathf.Max(0.004f, baseline);
            float hit = heard < 3f ? 0f : Mathf.Clamp01((ratio - 3f) / 5f);
            Spike = Mathf.Max(hit, Mathf.MoveTowards(Spike, 0f, Time.deltaTime * 0.5f));
        }
    }
}
