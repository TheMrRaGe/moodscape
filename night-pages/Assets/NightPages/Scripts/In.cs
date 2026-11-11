using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace NightPages
{
    public enum Btn { Interact, One, Two, Three, Pause, Mind, Scared, Amused, SkipAhead, Restart, Sprint, Voice, Face, Flashlight, Work, Watch, Mark, Throw, Turn }

    /// <summary>
    /// All input goes through here, so the game runs whether the project uses Unity's old Input Manager,
    /// the new Input System package, or both.
    /// </summary>
    public static class In
    {
        /// <summary>Which typewriter line was just chosen (0-2), or -1. Keys 1-3, or the VR stick and trigger.</summary>
        public static int Choice()
        {
            if (VRRig.ChoicePressed >= 0) return VRRig.ChoicePressed;
            if (VRRig.Active) return -1;        // in VR the trigger means "the highlighted line", never a number key
            return Down(Btn.One) ? 0 : Down(Btn.Two) ? 1 : Down(Btn.Three) ? 2 : -1;
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        static KeyControl K(Btn b)
        {
            var k = Keyboard.current;
            if (k == null) return null;
            switch (b)
            {
                case Btn.Interact: return k.eKey;
                case Btn.One: return k.digit1Key;
                case Btn.Two: return k.digit2Key;
                case Btn.Three: return k.digit3Key;
                case Btn.Pause: return k.escapeKey;
                case Btn.Mind: return k.f1Key;
                case Btn.Scared: return k.fKey;
                case Btn.Amused: return k.gKey;
                case Btn.SkipAhead: return k.f2Key;
                case Btn.Restart: return k.rKey;
                case Btn.Sprint: return k.leftShiftKey;
                case Btn.Voice: return k.vKey;
                case Btn.Face: return k.cKey;
                case Btn.Flashlight: return k.qKey;
                case Btn.Watch: return k.tabKey;
                case Btn.Mark: return k.f8Key;
            }
            return null;
        }

        public static bool Down(Btn b)
        {
            if (b == Btn.Work) { var m = Mouse.current; return m != null && m.leftButton.wasPressedThisFrame; }
            if (b == Btn.Throw) { var m = Mouse.current; return m != null && m.rightButton.wasPressedThisFrame; }
            if (b == Btn.Turn) { var m = Mouse.current; return m != null && m.middleButton.wasPressedThisFrame; }
            var k = K(b); return (k != null && k.wasPressedThisFrame) || VRRig.Down(b);
        }
        public static bool Held(Btn b)
        {
            if (b == Btn.Work) { var m = Mouse.current; return m != null && m.leftButton.isPressed; }
            if (b == Btn.Throw) { var m = Mouse.current; return m != null && m.rightButton.isPressed; }
            if (b == Btn.Turn) { var m = Mouse.current; return m != null && m.middleButton.isPressed; }
            var k = K(b); return (k != null && k.isPressed) || VRRig.Held(b);
        }

        public static Vector2 Move
        {
            get
            {
                var k = Keyboard.current;
                if (k == null) return Vector2.zero;
                float x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
                float y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
                return new Vector2(x, y);
            }
        }

        public static Vector2 Look
        {
            get { var m = Mouse.current; return m == null ? Vector2.zero : m.delta.ReadValue() * 0.1f; }
        }

        /// <summary>The mouse wheel this frame, in notches (forward is positive).</summary>
        public static float Scroll
        {
            get { var m = Mouse.current; return m == null ? 0f : m.scroll.ReadValue().y / 120f; }
        }
#else
        static KeyCode K(Btn b)
        {
            switch (b)
            {
                case Btn.Interact: return KeyCode.E;
                case Btn.One: return KeyCode.Alpha1;
                case Btn.Two: return KeyCode.Alpha2;
                case Btn.Three: return KeyCode.Alpha3;
                case Btn.Pause: return KeyCode.Escape;
                case Btn.Mind: return KeyCode.F1;
                case Btn.Scared: return KeyCode.F;
                case Btn.Amused: return KeyCode.G;
                case Btn.SkipAhead: return KeyCode.F2;
                case Btn.Restart: return KeyCode.R;
                case Btn.Sprint: return KeyCode.LeftShift;
                case Btn.Voice: return KeyCode.V;
                case Btn.Face: return KeyCode.C;
                case Btn.Flashlight: return KeyCode.Q;
                case Btn.Work: return KeyCode.Mouse0;
                case Btn.Throw: return KeyCode.Mouse1;
                case Btn.Turn: return KeyCode.Mouse2;
                case Btn.Watch: return KeyCode.Tab;
                case Btn.Mark: return KeyCode.F8;
            }
            return KeyCode.None;
        }

        public static bool Down(Btn b) { return Input.GetKeyDown(K(b)) || VRRig.Down(b); }
        public static bool Held(Btn b) { return Input.GetKey(K(b)) || VRRig.Held(b); }

        public static Vector2 Move
        {
            get
            {
                float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
                float y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
                return new Vector2(x, y);
            }
        }

        /// <summary>The mouse wheel this frame, in notches (forward is positive).</summary>
        public static float Scroll { get { return Input.mouseScrollDelta.y; } }

        static bool mouseAxesMissing;

        public static Vector2 Look
        {
            get
            {
                if (mouseAxesMissing) return Vector2.zero;
                try { return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")); }
                catch (System.ArgumentException)
                {
                    mouseAxesMissing = true;
                    Debug.LogWarning("[NightPages] The project's Input Manager has no 'Mouse X' / 'Mouse Y' axes, so mouse look is off.");
                    return Vector2.zero;
                }
            }
        }
#endif
    }
}
