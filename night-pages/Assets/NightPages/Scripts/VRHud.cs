using System.Text;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The interface in VR. Screen overlays don't exist inside a headset, so the same things the flat Hud
    /// draws (subtitles, tasks, the typewriter page, the fade, the Mind overlay) are drawn here as text
    /// floating in the world, a little in front of you, drifting round to stay in view.
    /// </summary>
    public class VRHud : MonoBehaviour
    {
        Hud hud;
        Player player;
        Transform panel, fadeQuad, page;
        TextMesh subtitle, prompt, tasks, pageText, mindText;
        Material fadeMat;
        Font font;

        public static VRHud Create(Hud hud, Player player)
        {
            var go = new GameObject("VR interface");
            var v = go.AddComponent<VRHud>();
            v.hud = hud;
            v.player = player;
            v.BuildAll();
            return v;
        }

        static Font FindFont()
        {
            foreach (var name in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
            {
                try
                {
                    var f = Resources.GetBuiltinResource<Font>(name);
                    if (f != null) return f;
                }
                catch (System.Exception) { }
            }
            return null;
        }

        TextMesh Text(string name, Transform parent, Vector3 pos, float size, TextAnchor anchor, Color c)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            var t = g.AddComponent<TextMesh>();
            t.font = font;
            t.fontSize = 72;
            t.characterSize = size;
            t.anchor = anchor;
            t.alignment = anchor == TextAnchor.UpperLeft ? TextAlignment.Left : TextAlignment.Center;
            t.richText = true;
            t.color = c;
            if (font != null) g.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return t;
        }

        void BuildAll()
        {
            font = FindFont();
            panel = new GameObject("Panel").transform;
            panel.SetParent(transform, false);
            var paper = new Color(0.92f, 0.89f, 0.82f);
            subtitle = Text("Subtitle", panel, new Vector3(0f, -0.42f, 0f), 0.0085f, TextAnchor.MiddleCenter, paper);
            prompt = Text("Prompt", panel, new Vector3(0f, -0.2f, 0f), 0.0075f, TextAnchor.MiddleCenter, paper);
            tasks = Text("Tasks", panel, new Vector3(-0.75f, 0.5f, 0f), 0.006f, TextAnchor.UpperLeft, paper);
            mindText = Text("Mind", panel, new Vector3(0.3f, 0.62f, 0.05f), 0.0042f, TextAnchor.UpperLeft, new Color(0.7f, 0.95f, 0.78f));

            page = Build.Box("Page", panel, new Vector3(0f, 0.08f, 0.03f), new Vector3(1.25f, 1.05f, 0.005f), new Color(0.86f, 0.83f, 0.74f), false, 0.25f).transform;
            pageText = Text("Page text", panel, new Vector3(-0.57f, 0.56f, 0f), 0.0058f, TextAnchor.UpperLeft, new Color(0.1f, 0.09f, 0.08f));

            // The fade is a black card held right in front of the eyes.
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Fade";
            Destroy(q.GetComponent<Collider>());
            fadeQuad = q.transform;
            fadeQuad.SetParent(player.cam.transform, false);
            fadeQuad.localPosition = new Vector3(0f, 0f, 0.12f);
            fadeQuad.localScale = new Vector3(1.2f, 1.2f, 1f);
            var sh = Shader.Find("Sprites/Default");
            fadeMat = sh != null ? new Material(sh) : Build.Mat(Color.black);
            fadeMat.color = new Color(0f, 0f, 0f, 0f);
            fadeMat.renderQueue = 4000;
            q.GetComponent<Renderer>().sharedMaterial = fadeMat;
            q.SetActive(false);
        }

        /// <summary>TextMesh doesn't wrap lines, so wrap them here. Tags like <b> don't count toward the width.</summary>
        public static string Wrap(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder();
            foreach (var para in text.Split('\n'))
            {
                int col = 0;
                foreach (var word in para.Split(' '))
                {
                    int len = 0;
                    bool tag = false;
                    foreach (char ch in word) { if (ch == '<') tag = true; else if (ch == '>') tag = false; else if (!tag) len++; }
                    if (col > 0 && col + 1 + len > width) { sb.Append('\n'); col = 0; }
                    if (col > 0) { sb.Append(' '); col++; }
                    sb.Append(word);
                    col += len;
                }
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        void LateUpdate()
        {
            if (panel.gameObject.activeSelf != VRRig.Active) panel.gameObject.SetActive(VRRig.Active);
            if (!VRRig.Active)
            {
                if (fadeQuad.gameObject.activeSelf) fadeQuad.gameObject.SetActive(false);
                return;
            }
            var cam = player.cam.transform;

            // Drift round to stay in front of the player, without being glued to the face.
            Vector3 fwd = Vis.Flat(cam.forward);
            Vector3 want = cam.position + fwd * 1.5f + Vector3.up * -0.05f;
            bool modal = hud.modal != Modal.None;
            panel.position = Vector3.Lerp(panel.position, want, 1f - Mathf.Exp(-Time.unscaledDeltaTime * (modal ? 2f : 5f)));
            panel.rotation = Quaternion.LookRotation(Vis.Flat(panel.position - cam.position));

            bool showFade = hud.fade > 0.002f;
            if (fadeQuad.gameObject.activeSelf != showFade) fadeQuad.gameObject.SetActive(showFade);
            fadeMat.color = new Color(0f, 0f, 0f, hud.fade);

            subtitle.text = modal && hud.modal != Modal.Title && hud.modal != Modal.Ending ? "" : Wrap(hud.Subtitle, 52);
            prompt.text = modal ? "" : hud.prompt.Replace("[E]", "[trigger]");

            var sb = new StringBuilder();
            if (!modal) foreach (var o in hud.objectives) sb.Append(o.Value ? "<color=#8a8a80>[x]  " : "[  ]  ").Append(o.Key).Append(o.Value ? "</color>\n" : "\n");
            tasks.text = sb.ToString();

            bool showPage = hud.modal == Modal.Writing || hud.modal == Modal.Reading;
            if (page.gameObject.activeSelf != showPage) page.gameObject.SetActive(showPage);
            pageText.color = showPage ? new Color(0.1f, 0.09f, 0.08f) : new Color(0.92f, 0.89f, 0.82f);
            sb.Length = 0;
            if (hud.modal == Modal.Writing)
            {
                sb.Append("<b>").Append(hud.header).Append("</b>\n\n").Append(Wrap(hud.body, 60)).Append("\n\n<i>").Append(Wrap(hud.footer, 60)).Append("</i>\n\n");
                for (int i = 0; i < hud.options.Length; i++)
                    sb.Append(i == VRRig.Selected ? "<b>>  " : "    ").Append(Wrap(hud.options[i], 56).Replace("\n", "\n    ")).Append(i == VRRig.Selected ? "</b>\n\n" : "\n\n");
                sb.Append("<i>Right stick to choose, trigger to type it.</i>");
            }
            else if (hud.modal == Modal.Reading)
                sb.Append("<b>").Append(hud.header).Append("</b>\n\n").Append(Wrap(hud.body, 60)).Append("\n\n<i>[trigger] put it down</i>");
            else if (hud.modal == Modal.Title || hud.modal == Modal.Ending)
                sb.Append("<size=120><b>").Append(hud.header).Append("</b></size>\n\n").Append(Wrap(hud.VRBody, 60)).Append("\n\n")
                  .Append(hud.modal == Modal.Title ? "[trigger]  Begin" : "[trigger]  Begin again. The dreams will remember you.");
            else if (hud.modal == Modal.Paused) sb.Append("<size=120><b>Paused</b></size>\n\n[menu button] to return");
            pageText.text = sb.ToString();

            mindText.text = hud.mind ? "<b>MIND</b>\n" + hud.mindStatus + "\n\n" + Wrap(string.Join("\n", hud.mindLines.ToArray()), 70) : "";
        }
    }
}
