using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    public enum Modal { None, Title, Writing, Reading, Ending, Paused }

    /// <summary>
    /// Everything drawn over the game: subtitles, the task list, the typewriter page, the fade, and the
    /// developer "Mind" overlay (F1) that shows every decision the game makes and why.
    /// Drawn with Unity's immediate-mode GUI so the prototype needs no UI packages. It only draws;
    /// all key presses are read through In.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public static Hud I;

        public Modal modal = Modal.Title;
        public float fade = 1f, fadeTarget = 1f, fadeSpeed = 1.2f;
        public string prompt = "";
        public string header = "", body = "", footer = "";
        public string[] options = new string[0];
        public readonly List<KeyValuePair<string, bool>> objectives = new List<KeyValuePair<string, bool>>();
        public bool mind;
        public string mindStatus = "";
        public readonly List<string> mindLines = new List<string>();
        public float grip = 1f;
        public int openedFrame = -1, closedFrame = -1;

        /// <summary>Close whatever page is open. The keypress that closed it must not also re-open it.</summary>
        public void Close()
        {
            modal = Modal.None;
            closedFrame = Time.frameCount;
        }

        /// <summary>True on the frame a page was closed: that keypress is used up.</summary>
        public bool JustClosed { get { return Time.frameCount == closedFrame; } }

        readonly Queue<KeyValuePair<string, float>> subQueue = new Queue<KeyValuePair<string, float>>();
        string sub = "";
        float subUntil;
        Texture2D vignette;
        GUIStyle sText, sTitle, sSmall, sPage, sMono;

        public bool Fresh { get { return Time.frameCount == openedFrame; } }
        public string Subtitle { get { return sub; } }
        public string vrBody = "";      // title text for VR, where the controls are different
        public string VRBody { get { return modal == Modal.Title && !string.IsNullOrEmpty(vrBody) ? vrBody : body; } }

        void Awake()
        {
            I = this;
            vignette = MakeVignette();
        }

        public void Open(Modal m)
        {
            modal = m;
            openedFrame = Time.frameCount;
        }

        public void Say(string text, float secs = 4.5f)
        {
            if (string.IsNullOrEmpty(text)) return;
            subQueue.Enqueue(new KeyValuePair<string, float>(text, secs));
        }

        public void ClearSubtitles()
        {
            subQueue.Clear();
            sub = "";
        }

        public void Log(string line)
        {
            mindLines.Add(line);
            while (mindLines.Count > 16) mindLines.RemoveAt(0);
        }

        void Update()
        {
            fade = Mathf.MoveTowards(fade, fadeTarget, fadeSpeed * Time.unscaledDeltaTime);
            if (Time.unscaledTime >= subUntil)
            {
                if (subQueue.Count > 0)
                {
                    var next = subQueue.Dequeue();
                    sub = next.Key;
                    subUntil = Time.unscaledTime + next.Value;
                }
                else sub = "";
            }
        }

        static Texture2D MakeVignette()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * n + x] = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.25f, r)));
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        void Styles()
        {
            if (sText != null) return;
            sText = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
            sTitle = new GUIStyle(sText) { fontSize = 76, fontStyle = FontStyle.Bold };
            sSmall = new GUIStyle(sText) { fontSize = 24, alignment = TextAnchor.UpperLeft };
            sPage = new GUIStyle(sText) { fontSize = 28, alignment = TextAnchor.UpperLeft };
            sMono = new GUIStyle(sText) { fontSize = 19, alignment = TextAnchor.UpperLeft };
        }

        void Shadowed(Rect r, string text, GUIStyle style, Color c)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, c.a * 0.85f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            GUI.color = c;
            GUI.Label(r, text, style);
            GUI.color = old;
        }

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || VRRig.Active) return;     // in VR, VRHud draws instead
            Styles();
            float s = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float W = Screen.width / s, H = 1080f;
            var full = new Rect(0f, 0f, W, H);
            var paper = new Color(0.92f, 0.89f, 0.82f, 1f);

            // The edges of the screen close in as Grip drops.
            var oc = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(0.3f + (1f - grip) * 0.75f));
            GUI.DrawTexture(full, vignette, ScaleMode.StretchToFill);
            GUI.color = oc;

            if (modal == Modal.None)
            {
                Fill(new Rect(W / 2f - 2f, H / 2f - 2f, 4f, 4f), new Color(1f, 1f, 1f, 0.5f));
                if (!string.IsNullOrEmpty(prompt)) Shadowed(new Rect(0f, H * 0.58f, W, 50f), prompt, sText, paper);
                float y = 36f;
                foreach (var o in objectives)
                {
                    Shadowed(new Rect(40f, y, 700f, 36f), (o.Value ? "[x]  " : "[  ]  ") + o.Key, sSmall,
                        o.Value ? new Color(0.6f, 0.62f, 0.58f, 0.8f) : paper);
                    y += 36f;
                }
            }

            if (fade > 0.001f) Fill(full, new Color(0f, 0f, 0f, fade));

            if (!string.IsNullOrEmpty(sub) && modal != Modal.Writing && modal != Modal.Reading)
                Shadowed(new Rect(W * 0.15f, H - 190f, W * 0.7f, 120f), sub, sText, paper);

            if (modal == Modal.Title || modal == Modal.Ending)
            {
                Shadowed(new Rect(0f, H * 0.16f, W, 110f), header, sTitle, paper);
                Shadowed(new Rect(W * 0.2f, H * 0.34f, W * 0.6f, H * 0.42f), body, sText, paper);
                Shadowed(new Rect(0f, H * 0.82f, W, 50f), footer, sText, new Color(0.75f, 0.7f, 0.6f, 1f));
            }
            else if (modal == Modal.Paused)
            {
                Fill(full, new Color(0f, 0f, 0f, 0.6f));
                Shadowed(new Rect(0f, H * 0.42f, W, 110f), "Paused", sTitle, paper);
                Shadowed(new Rect(0f, H * 0.56f, W, 50f), "[Esc] to return", sText, paper);
            }
            else if (modal == Modal.Writing || modal == Modal.Reading)
            {
                Fill(full, new Color(0f, 0f, 0f, 0.72f));
                var page = new Rect(W / 2f - 520f, 70f, 1040f, 940f);
                Fill(page, new Color(0.9f, 0.87f, 0.79f, 1f));
                var ink = new Color(0.1f, 0.09f, 0.08f, 1f);
                var oc2 = GUI.color;
                GUI.color = ink;
                GUI.Label(new Rect(page.x + 60f, page.y + 40f, page.width - 120f, 50f), "<b>" + header + "</b>", sPage);
                float bodyH = modal == Modal.Writing ? 430f : 780f;
                GUI.Label(new Rect(page.x + 60f, page.y + 110f, page.width - 120f, bodyH), body, sPage);
                if (modal == Modal.Writing)
                {
                    float y = page.y + 560f;
                    GUI.Label(new Rect(page.x + 60f, y, page.width - 120f, 44f), "<i>" + footer + "</i>", sPage);
                    y += 62f;
                    for (int i = 0; i < options.Length; i++)
                    {
                        GUI.Label(new Rect(page.x + 60f, y, page.width - 120f, 90f), "<b>[" + (i + 1) + "]</b>  " + options[i], sPage);
                        y += 96f;
                    }
                }
                else GUI.Label(new Rect(page.x + 60f, page.y + page.height - 70f, page.width - 120f, 44f), "<i>" + footer + "</i>", sPage);
                GUI.color = oc2;
            }

            if (mind)
            {
                var box = new Rect(W - 760f, 30f, 730f, 560f);
                Fill(box, new Color(0f, 0f, 0f, 0.78f));
                var text = "<b>MIND (dev overlay, F1)</b>\n" + mindStatus + "\n\n" + string.Join("\n", mindLines.ToArray());
                var oc3 = GUI.color;
                GUI.color = new Color(0.75f, 0.95f, 0.8f, 1f);
                GUI.Label(new Rect(box.x + 16f, box.y + 12f, box.width - 32f, box.height - 24f), text, sMono);
                GUI.color = oc3;
            }
        }
    }
}
