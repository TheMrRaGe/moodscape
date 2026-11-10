using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The link to the Moodscape PC painter running on this machine (pc-painter/mirror-windows.bat).
    /// It reads two local-only endpoints: /api/game/state (is a face in view, and how strongly is it
    /// reacting) and /api/game/frame.jpg (the newest painting). If the painter isn't running, the game
    /// carries on with in-game reaction signals and a stand-in texture.
    /// Polls on a background thread with plain .NET requests, so it needs no Unity player settings.
    /// </summary>
    public class MoodscapeLink : MonoBehaviour
    {
        public string baseUrl = "http://127.0.0.1:8765";
        public Texture2D Tex;
        public bool Live;          // the painter answered recently
        public bool Session;       // someone has the Mirror page open
        public bool Face;          // and their face is in view
        public bool HasFrame;
        public float Reaction;
        public string Showing = "";
        public string Status = "painter: not found (in-game signals only)";

        [System.Serializable]
        class State
        {
            public bool ok = false;
            public bool session = false;
            public bool face = false;
            public float reaction = 0f;
            public float glow = 0f;
            public string edition = "";
            public string[] showing = null;
        }

        Thread thread;
        volatile bool run;
        readonly object gate = new object();
        string json;
        byte[] jpeg;
        bool fresh, failed;
        float lastOk = -99f;
        string lastStatus = "";

        void Awake()
        {
            Tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Tex.SetPixels(new[] { Color.black, Color.black, Color.black, Color.black });
            Tex.Apply();
            run = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "MoodscapeLink" };
            thread.Start();
        }

        void OnDestroy() { run = false; }
        void OnApplicationQuit() { run = false; }

        static byte[] Get(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 1500;
            req.ReadWriteTimeout = 1500;
            req.Proxy = null;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if (resp.StatusCode != HttpStatusCode.OK) return null;
                using (var s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        void Loop()
        {
            int n = 0;
            while (run)
            {
                try
                {
                    var bytes = Get(baseUrl + "/api/game/state");
                    string s = bytes == null ? null : System.Text.Encoding.UTF8.GetString(bytes);
                    byte[] frame = null;
                    if (s != null && n++ % 3 == 0)
                    {
                        try { frame = Get(baseUrl + "/api/game/frame.jpg"); }
                        catch (WebException) { frame = null; }
                    }
                    lock (gate)
                    {
                        json = s;
                        if (frame != null && frame.Length > 0) jpeg = frame;
                        fresh = true;
                        failed = s == null;
                    }
                    Thread.Sleep(200);
                }
                catch (System.Exception)
                {
                    lock (gate) { fresh = true; failed = true; json = null; }
                    Thread.Sleep(3000);
                }
            }
        }

        void Update()
        {
            string s; byte[] frame; bool isFresh, isFailed;
            lock (gate)
            {
                s = json; frame = jpeg; jpeg = null;
                isFresh = fresh; isFailed = failed; fresh = false;
            }
            if (isFresh && !isFailed && s != null)
            {
                State st = null;
                try { st = JsonUtility.FromJson<State>(s); } catch (System.Exception) { st = null; }
                if (st != null && st.ok)
                {
                    lastOk = Time.unscaledTime;
                    Session = st.session;
                    Face = st.session && st.face;
                    Reaction = Mathf.Clamp(st.reaction, -1f, 1f);
                    Showing = st.showing == null ? "" : string.Join(" · ", st.showing);
                }
            }
            if (frame != null && Tex.LoadImage(frame, false)) HasFrame = true;

            Live = Time.unscaledTime - lastOk < 2.5f;
            if (!Live) { Session = false; Face = false; }
            Status = !Live ? "painter: not found (in-game signals only)"
                : !Session ? "painter: running, but nobody has the Mirror page open"
                : !Face ? "painter: Mirror open, no face in view"
                : "painter: reading your face (reaction " + Reaction.ToString("+0.00;-0.00") + ")";
            if (Status.Substring(0, System.Math.Min(Status.Length, 30)) != lastStatus)
            {
                lastStatus = Status.Substring(0, System.Math.Min(Status.Length, 30));
                if (GameDirector.I != null) GameDirector.I.Log(Status);
            }
        }
    }
}
