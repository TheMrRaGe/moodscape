using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>One line on a menu page: something to do, a setting to step through, a name to type, or a heading.</summary>
    public class MenuEntry
    {
        public string label;
        public System.Func<string> value;           // shown on the right (a setting's current value)
        public System.Action activate;              // Enter / click / trigger
        public System.Action<int> step;             // left / right (-1, +1)
        public System.Func<string> getText;         // a line you type into
        public System.Action<string> setText;
        public int maxLen = 20;
        public bool heading;                        // a section title: not selectable
        public string hint;                         // a line of explanation under the page when this is selected
        public System.Func<bool> enabled;           // greyed out (and not selectable) when false

        public bool Selectable { get { return !heading && (enabled == null || enabled()); } }
        public bool IsText { get { return getText != null; } }

        public static MenuEntry Act(string label, System.Action a, string hint = null) { return new MenuEntry { label = label, activate = a, hint = hint }; }
        public static MenuEntry Head(string label) { return new MenuEntry { label = label, heading = true }; }
        public static MenuEntry Text(string label, System.Func<string> get, System.Action<string> set, int max = 20, string hint = null)
        { return new MenuEntry { label = label, getText = get, setText = set, maxLen = max, hint = hint }; }

        /// <summary>A setting with a list of choices, stepped with left/right (or clicked to go to the next).</summary>
        public static MenuEntry Choice(string label, string[] names, System.Func<int> get, System.Action<int> set, string hint = null)
        {
            var it = new MenuEntry { label = label, hint = hint };
            it.value = () => names[Mathf.Clamp(get(), 0, names.Length - 1)];
            it.step = d => set(((get() + d) % names.Length + names.Length) % names.Length);
            it.activate = () => it.step(1);
            return it;
        }

        public static MenuEntry Toggle(string label, System.Func<bool> get, System.Action<bool> set, string hint = null)
        {
            return Choice(label, new[] { "off", "on" }, () => get() ? 1 : 0, i => set(i == 1), hint);
        }

        /// <summary>A number stepped by `inc` between min and max, shown with `fmt` (e.g. "{0:0}%").</summary>
        public static MenuEntry Slider(string label, float min, float max, float inc, System.Func<float> get, System.Action<float> set, System.Func<float, string> show, string hint = null)
        {
            var it = new MenuEntry { label = label, hint = hint };
            it.value = () => show(get());
            it.step = d => set(Mathf.Clamp(Mathf.Round((get() + d * inc) / inc) * inc, min, max));
            return it;
        }
    }

    public class MenuPage
    {
        public string title, intro, footer;
        public readonly List<MenuEntry> items = new List<MenuEntry>();
        public System.Action back;                  // Esc; null: Esc does nothing (the title page)
        public System.Action onShow;
        /// <summary>A narrow page to one side with little dimming, so the scene behind it shows (the title).</summary>
        public bool compact;
        public MenuPage Add(MenuEntry i) { items.Add(i); return this; }
    }

    /// <summary>
    /// Typed pages you move through with the mouse, the keyboard (arrows or WASD, Enter, Esc) or a VR controller
    /// (stick, trigger, menu button): the title, character creation, options, the pause menu, the box of pages.
    /// </summary>
    public class Menu : MonoBehaviour
    {
        public static Menu I;
        public MenuPage page;
        readonly Stack<MenuPage> stack = new Stack<MenuPage>();
        int sel, top;
        float stickHeld;
        GUIStyle sTitle, sRow, sSmall, sValue;
        readonly List<Rect> rowRects = new List<Rect>();
        int shownFrame;

        void Awake() { I = this; }

        public bool Showing { get { return page != null; } }

        /// <summary>Show a page on top of the current one (Esc comes back).</summary>
        public void Push(MenuPage p)
        {
            if (page != null) stack.Push(page);
            Set(p);
        }

        /// <summary>Replace the whole stack with this page.</summary>
        public void Show(MenuPage p)
        {
            stack.Clear();
            Set(p);
        }

        void Set(MenuPage p)
        {
            page = p;
            sel = 0;
            top = 0;
            while (sel < p.items.Count && !p.items[sel].Selectable) sel++;
            shownFrame = Time.frameCount;
            if (p.onShow != null) p.onShow();
            var hud = Hud.I;
            if (hud != null) hud.Open(Modal.Menu);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = !VRRig.Active;
        }

        public void Back()
        {
            if (stack.Count > 0) { Set(stack.Pop()); Sfx.Ui("pageturn", 0.4f, 1.1f); return; }
            if (page != null && page.back != null) page.back();
        }

        public void Close()
        {
            page = null;
            stack.Clear();
            if (Hud.I != null && Hud.I.modal == Modal.Menu) Hud.I.Close();
        }

        void Move(int d)
        {
            if (page == null || page.items.Count == 0) return;
            int n = page.items.Count, s = sel;
            for (int i = 0; i < n; i++)
            {
                s = (s + d + n) % n;
                if (page.items[s].Selectable) { sel = s; Sfx.Ui("click", 0.15f, 1.6f); return; }
            }
        }

        void Activate()
        {
            if (page == null || sel >= page.items.Count) return;
            var it = page.items[sel];
            if (!it.Selectable || it.IsText) return;
            Sfx.Ui("tap", 0.35f);
            if (it.activate != null) it.activate();
        }

        void Step(int d)
        {
            if (page == null || sel >= page.items.Count) return;
            var it = page.items[sel];
            if (it.step != null && it.Selectable) { it.step(d); Sfx.Ui("click", 0.2f, d > 0 ? 1.2f : 0.9f); Settings.Save(); Settings.Apply(); }
        }

        void Update()
        {
            if (page == null || Hud.I == null || Hud.I.modal != Modal.Menu) return;
            // VR: the stick moves and changes, the trigger chooses, the menu button goes back.
            if (VRRig.Active)
            {
                Vector2 st = VRRig.MoveStick;
                if (st.magnitude < 0.5f) stickHeld = 0f;
                else if (stickHeld <= 0f || stickHeld > 0.45f)
                {
                    if (Mathf.Abs(st.y) > Mathf.Abs(st.x)) Move(st.y > 0 ? -1 : 1); else Step(st.x > 0 ? 1 : -1);
                    if (stickHeld > 0.45f) stickHeld = 0.3f;
                }
                if (st.magnitude >= 0.5f) stickHeld += Time.unscaledDeltaTime;
                if (Time.frameCount != shownFrame && In.Down(Btn.Interact)) Activate();
                if (Time.frameCount != shownFrame && In.Down(Btn.Pause)) Back();
                WriteVR();
            }
        }

        // What the VR page shows (VRHud draws hud.header and hud.body for menus).
        void WriteVR()
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(page.intro)) sb.Append(page.intro).Append("\n\n");
            for (int i = 0; i < page.items.Count; i++)
            {
                var it = page.items[i];
                if (it.heading) { sb.Append("\n<b>").Append(it.label).Append("</b>\n"); continue; }
                bool on = i == sel;
                sb.Append(on ? "<b>> " : "   ").Append(it.label);
                if (it.IsText) sb.Append(":  ").Append(it.getText());
                else if (it.value != null) sb.Append(":  ").Append(it.value());
                sb.Append(on ? "</b>" : "").Append("\n");
            }
            if (sel < page.items.Count && page.items[sel].hint != null) sb.Append("\n<i>").Append(page.items[sel].hint).Append("</i>");
            Hud.I.header = page.title;
            Hud.I.body = sb.ToString();
            Hud.I.footer = "stick: choose and change    trigger: select    menu: back";
        }

        void Styles()
        {
            if (sTitle != null) return;
            sTitle = new GUIStyle(GUI.skin.label) { fontSize = 52, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft, richText = true, wordWrap = true };
            sRow = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleLeft, richText = true, wordWrap = false };
            sValue = new GUIStyle(sRow) { alignment = TextAnchor.MiddleRight };
            sSmall = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.UpperLeft, richText = true, wordWrap = true };
            var mono = Font.CreateDynamicFontFromOSFont(new[] { "Courier New", "Courier", "Liberation Mono", "DejaVu Sans Mono" }, 28);
            if (mono != null) { sTitle.font = mono; sRow.font = mono; sValue.font = mono; sSmall.font = mono; }
        }

        void OnGUI()
        {
            if (page == null || Hud.I == null || Hud.I.modal != Modal.Menu || VRRig.Active) return;
            Styles();
            float s = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float W = Screen.width / s, H = 1080f;
            var e = Event.current;
            bool fresh = Time.frameCount == shownFrame;

            // ---- input
            if (e.type == EventType.KeyDown && !fresh)
            {
                var it = sel < page.items.Count ? page.items[sel] : null;
                bool typing = it != null && it.IsText;
                switch (e.keyCode)
                {
                    case KeyCode.UpArrow: Move(-1); e.Use(); break;
                    case KeyCode.DownArrow: Move(1); e.Use(); break;
                    case KeyCode.LeftArrow: Step(-1); e.Use(); break;
                    case KeyCode.RightArrow: Step(1); e.Use(); break;
                    case KeyCode.Return: case KeyCode.KeypadEnter: if (typing) Move(1); else Activate(); e.Use(); break;
                    case KeyCode.Escape: Back(); e.Use(); break;
                    case KeyCode.Tab: Move(e.shift ? -1 : 1); e.Use(); break;
                    case KeyCode.Backspace:
                        if (typing) { var t = it.getText(); if (t.Length > 0) { it.setText(t.Substring(0, t.Length - 1)); Sfx.Ui("tap", 0.2f, 0.8f); } e.Use(); }
                        break;
                    default:
                        if (typing)
                        {
                            char c = e.character;
                            if (c != 0 && (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '\'' || c == '.'))
                            {
                                var t = it.getText();
                                if (t.Length < it.maxLen) { it.setText(t + c); Sfx.Ui("tap", 0.25f, 0.9f + Random.value * 0.2f); }
                                e.Use();
                            }
                        }
                        else
                        {
                            if (e.keyCode == KeyCode.W) { Move(-1); e.Use(); }
                            else if (e.keyCode == KeyCode.S) { Move(1); e.Use(); }
                            else if (e.keyCode == KeyCode.A) { Step(-1); e.Use(); }
                            else if (e.keyCode == KeyCode.D) { Step(1); e.Use(); }
                            else if (e.keyCode == KeyCode.E || e.keyCode == KeyCode.Space) { Activate(); e.Use(); }
                        }
                        break;
                }
            }
            if (page == null) return;      // an action may have closed the menu

            // ---- the page
            var paper = new Color(0.9f, 0.87f, 0.79f, 1f);
            var ink = new Color(0.1f, 0.09f, 0.08f, 1f);
            var faint = new Color(0.1f, 0.09f, 0.08f, 0.45f);
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, page.compact ? 0.15f : 0.55f));
            var r = page.compact ? new Rect(Mathf.Max(40f, W * 0.07f), 110f, 760f, 860f) : new Rect(W / 2f - 560f, 50f, 1120f, 980f);
            Fill(r, paper);
            var oc = GUI.color;
            GUI.color = ink;
            GUI.Label(new Rect(r.x + 70f, r.y + 46f, r.width - 140f, 70f), page.title, sTitle);
            float y = r.y + 130f;
            if (!string.IsNullOrEmpty(page.intro))
            {
                float h = sSmall.CalcHeight(new GUIContent(page.intro), r.width - 140f);
                GUI.Label(new Rect(r.x + 70f, y, r.width - 140f, h), page.intro, sSmall);
                y += h + 18f;
            }
            // Rows: as many as fit, scrolled to keep the selection in view.
            const float rowH = 46f;
            int fit = Mathf.Max(4, Mathf.FloorToInt((r.yMax - 170f - y) / rowH));
            if (sel < top) top = sel;
            if (sel >= top + fit) top = sel - fit + 1;
            top = Mathf.Clamp(top, 0, Mathf.Max(0, page.items.Count - fit));
            rowRects.Clear();
            for (int i = 0; i < page.items.Count; i++) rowRects.Add(new Rect(0, 0, 0, 0));
            for (int i = top; i < page.items.Count && i < top + fit; i++)
            {
                var it = page.items[i];
                var row = new Rect(r.x + 50f, y, r.width - 100f, rowH);
                rowRects[i] = row;
                bool on = i == sel;
                if (it.heading)
                {
                    GUI.color = faint;
                    GUI.Label(new Rect(row.x + 20f, row.y + 8f, row.width, rowH), it.label.ToUpperInvariant(), sSmall);
                }
                else
                {
                    if (on) Fill(row, new Color(0.1f, 0.09f, 0.08f, 0.1f));
                    GUI.color = it.Selectable ? ink : faint;
                    GUI.Label(new Rect(row.x + 20f, row.y, row.width * 0.6f, rowH), (on ? "> " : "  ") + it.label, sRow);
                    string v = it.IsText ? it.getText() + (on && (int)(Time.unscaledTime * 2f) % 2 == 0 ? "_" : " ") : it.value != null ? it.value() : null;
                    if (v != null) GUI.Label(new Rect(row.x + row.width * 0.42f, row.y, row.width * 0.56f, rowH), (it.step != null && on ? "<  " + v + "  >" : v), sValue);
                }
                y += rowH;
            }
            GUI.color = faint;
            if (top > 0) GUI.Label(new Rect(r.xMax - 120f, r.y + 120f, 80f, 30f), "^ more", sSmall);
            if (top + fit < page.items.Count) GUI.Label(new Rect(r.xMax - 120f, r.yMax - 150f, 80f, 30f), "v more", sSmall);
            // The selected line's explanation, and the keys.
            GUI.color = ink;
            if (sel < page.items.Count && !string.IsNullOrEmpty(page.items[sel].hint))
                GUI.Label(new Rect(r.x + 70f, r.yMax - 140f, r.width - 140f, 80f), "<i>" + page.items[sel].hint + "</i>", sSmall);
            GUI.color = faint;
            GUI.Label(new Rect(r.x + 70f, r.yMax - 52f, r.width - 140f, 40f),
                page.footer ?? "Up/Down or W/S: choose     Left/Right or A/D: change     Enter: select     Esc: back", sSmall);
            GUI.color = oc;

            // ---- the mouse
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
            {
                for (int i = 0; i < rowRects.Count; i++)
                {
                    if (rowRects[i].width <= 0f || !rowRects[i].Contains(e.mousePosition) || !page.items[i].Selectable) continue;
                    if (sel != i) { sel = i; if (e.type == EventType.MouseMove) Sfx.Ui("click", 0.1f, 1.7f); }
                    if (e.type == EventType.MouseDown && !fresh)
                    {
                        var it = page.items[i];
                        // Clicking a setting: the left part of its value steps back, the rest steps on.
                        if (it.step != null && e.mousePosition.x < rowRects[i].x + rowRects[i].width * 0.62f && e.mousePosition.x > rowRects[i].x + rowRects[i].width * 0.42f) Step(-1);
                        else if (it.step != null) Step(1);
                        else Activate();
                        e.Use();
                    }
                    break;
                }
            }
            if (e.type == EventType.ScrollWheel) { Move(e.delta.y > 0 ? 1 : -1); e.Use(); }
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
