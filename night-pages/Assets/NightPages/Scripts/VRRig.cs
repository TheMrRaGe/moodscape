using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace NightPages
{
    /// <summary>
    /// VR mode. If a headset is running (OpenXR turned on in the project and a Quest connected over Link
    /// or Air Link), the game switches to it by itself: the camera follows your head, the left stick walks,
    /// the right stick turns in steps, the right trigger uses what your right hand points at, and the
    /// interface floats in front of you. With no headset, none of this does anything.
    ///
    /// It reads the headset and controllers through Unity's built-in XR device API, so the game code needs
    /// no VR packages to compile; the project only needs the OpenXR plug-in to actually drive a headset.
    /// It also measures how you move (head jerks, shaking hands, hands raised to the face) for the fear reading.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class VRRig : MonoBehaviour
    {
        public static VRRig I;
        public static bool Active;
        public static float HeadSpeed, HeadAngularSpeed, HandJitter;
        public static bool HandsNearFace;
        public static Vector2 MoveStick;
        public static int Selected;             // highlighted line at the typewriter
        public static int ChoicePressed = -1;   // set for one frame when the trigger confirms it

        public Transform rightHand, leftHand;
        public Vector3 headLocalPos = new Vector3(0f, 1.65f, 0f);
        public Quaternion headLocalRot = Quaternion.identity;
        public float turnStep;                  // degrees to turn this frame (snap turning)

        Player player;
        LineRenderer laser;
        readonly Dictionary<Btn, bool> now = new Dictionary<Btn, bool>(), before = new Dictionary<Btn, bool>();
        readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        bool turnArmed = true, pickArmed = true, originSet, floorOrigin;
        Vector3 lastHead, lastRightVel, lastLeftVel, lastRight, lastLeft;
        Quaternion lastHeadRot = Quaternion.identity;
        float checkAt;

        static readonly Btn[] Mapped = { Btn.Interact, Btn.Pause, Btn.Mind, Btn.Sprint, Btn.Restart };

        public static void ResetStatics()
        {
            I = null; Active = false; HeadSpeed = HeadAngularSpeed = HandJitter = 0f;
            HandsNearFace = false; MoveStick = Vector2.zero; Selected = 0; ChoicePressed = -1;
        }

        public static VRRig Attach(Player p)
        {
            var rig = p.gameObject.AddComponent<VRRig>();
            rig.player = p;
            I = rig;
            var skin = Build.Mat(new Color(0.08f, 0.07f, 0.07f));
            rig.rightHand = Build.Prim(PrimitiveType.Sphere, "Right hand", p.transform, Vector3.zero, Vector3.one * 0.08f, skin, false).transform;
            rig.leftHand = Build.Prim(PrimitiveType.Sphere, "Left hand", p.transform, Vector3.zero, Vector3.one * 0.08f, skin, false).transform;
            rig.rightHand.gameObject.SetActive(false);
            rig.leftHand.gameObject.SetActive(false);

            var lg = new GameObject("Pointer");
            lg.transform.SetParent(rig.rightHand, false);
            rig.laser = lg.AddComponent<LineRenderer>();
            rig.laser.useWorldSpace = true;
            rig.laser.positionCount = 2;
            rig.laser.startWidth = 0.004f;
            rig.laser.endWidth = 0.001f;
            var sh = Shader.Find("Sprites/Default");
            if (sh != null) rig.laser.material = new Material(sh);
            rig.laser.startColor = new Color(1f, 0.9f, 0.7f, 0.35f);
            rig.laser.endColor = new Color(1f, 0.9f, 0.7f, 0f);
            rig.laser.enabled = false;

            foreach (var b in Mapped) { rig.now[b] = false; rig.before[b] = false; }
            Application.onBeforeRender += rig.BeforeRender;
            return rig;
        }

        void OnDestroy() { Application.onBeforeRender -= BeforeRender; if (I == this) I = null; }

        public static bool Down(Btn b)
        {
            if (!Active || I == null || !I.now.ContainsKey(b)) return false;
            return I.now[b] && !I.before[b];
        }

        public static bool Held(Btn b)
        {
            return Active && I != null && I.now.ContainsKey(b) && I.now[b];
        }

        public Vector3 AimOrigin { get { return rightHand.position; } }
        public Vector3 AimDirection { get { return rightHand.forward; } }

        static bool Button(InputDevice d, InputFeatureUsage<bool> usage)
        {
            bool v;
            return d.isValid && d.TryGetFeatureValue(usage, out v) && v;
        }

        static Vector2 Stick(InputDevice d)
        {
            Vector2 v;
            return d.isValid && d.TryGetFeatureValue(CommonUsages.primary2DAxis, out v) ? v : Vector2.zero;
        }

        static bool Pose(InputDevice d, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (!d.isValid) return false;
            bool a = d.TryGetFeatureValue(CommonUsages.devicePosition, out pos);
            bool b = d.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);
            return a && b;
        }

        void ReadHead()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            Vector3 pos; Quaternion rot;
            if (!Pose(head, out pos, out rot)) return;
            // If the headset reports from eye level instead of the floor, stand the player up ourselves.
            if (!floorOrigin) pos.y += 1.65f;
            headLocalPos = pos;
            headLocalRot = rot;
        }

        // Unity calls this just before drawing each frame, so the view uses the freshest head pose.
        void BeforeRender()
        {
            if (!Active || player == null) return;
            ReadHead();
            player.ApplyVRPose(headLocalPos, headLocalRot);
        }

        void Update()
        {
            float t = Time.unscaledTime, dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool on = head.isValid && XRSettings.isDeviceActive;
            if (on != Active)
            {
                Active = on;
                rightHand.gameObject.SetActive(on);
                leftHand.gameObject.SetActive(on);
                laser.enabled = on;
                originSet = false;
                if (GameDirector.I != null)
                    GameDirector.I.Log(on ? "VR headset detected (" + head.name + "): switched to VR controls and the in-world interface"
                                          : "VR headset gone: back to keyboard and mouse");
            }
            ChoicePressed = -1;
            turnStep = 0f;
            if (!Active) { MoveStick = Vector2.zero; return; }

            if (!originSet && t > checkAt)
            {
                checkAt = t + 1f;
                SubsystemManager.GetSubsystems(subsystems);
                foreach (var s in subsystems)
                {
                    if (s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor)) floorOrigin = true;
                    originSet = true;
                }
                if (originSet && GameDirector.I != null)
                    GameDirector.I.Log(floorOrigin ? "VR: tracking from the floor" : "VR: headset reports from eye level; assuming a standing height of 1.65 m");
            }

            ReadHead();
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            Vector3 rp, lp; Quaternion rr, lr;
            if (Pose(right, out rp, out rr)) { if (!floorOrigin) rp.y += 1.65f; rightHand.localPosition = rp; rightHand.localRotation = rr; }
            if (Pose(left, out lp, out lr)) { if (!floorOrigin) lp.y += 1.65f; leftHand.localPosition = lp; leftHand.localRotation = lr; }

            foreach (var b in Mapped) before[b] = now[b];
            now[Btn.Interact] = Button(right, CommonUsages.triggerButton);
            now[Btn.Restart] = now[Btn.Interact];
            now[Btn.Pause] = Button(left, CommonUsages.menuButton);
            now[Btn.Mind] = Button(left, CommonUsages.secondaryButton);
            now[Btn.Sprint] = Button(left, CommonUsages.primary2DAxisClick) || Button(left, CommonUsages.gripButton);

            MoveStick = Stick(left);
            Vector2 rs = Stick(right);
            if (Mathf.Abs(rs.x) > 0.7f && turnArmed) { turnStep = rs.x > 0f ? 30f : -30f; turnArmed = false; }
            if (Mathf.Abs(rs.x) < 0.3f) turnArmed = true;

            // At the typewriter: right stick up and down picks a line, trigger types it.
            var hud = Hud.I;
            if (hud != null && hud.modal == Modal.Writing)
            {
                int n = Mathf.Max(1, hud.options.Length);
                if (Mathf.Abs(rs.y) > 0.7f && pickArmed) { Selected = (Selected + (rs.y < 0f ? 1 : n - 1)) % n; pickArmed = false; }
                if (Mathf.Abs(rs.y) < 0.3f) pickArmed = true;
                if (Down(Btn.Interact) && !hud.Fresh) ChoicePressed = Mathf.Clamp(Selected, 0, n - 1);
            }
            else Selected = 0;

            // Body language for the fear reading.
            HeadSpeed = Mathf.Lerp(HeadSpeed, (headLocalPos - lastHead).magnitude / dt, 0.4f);
            HeadAngularSpeed = Mathf.Lerp(HeadAngularSpeed, Quaternion.Angle(lastHeadRot, headLocalRot) / dt, 0.4f);
            lastHead = headLocalPos; lastHeadRot = headLocalRot;
            Vector3 rv = (rightHand.localPosition - lastRight) / dt, lv = (leftHand.localPosition - lastLeft) / dt;
            float jitter = ((rv - lastRightVel).magnitude + (lv - lastLeftVel).magnitude) * 0.5f;
            HandJitter = Mathf.Lerp(HandJitter, jitter, 0.05f);
            lastRightVel = rv; lastLeftVel = lv; lastRight = rightHand.localPosition; lastLeft = leftHand.localPosition;
            HandsNearFace = (rightHand.localPosition - headLocalPos).magnitude < 0.3f || (leftHand.localPosition - headLocalPos).magnitude < 0.3f;

            laser.SetPosition(0, rightHand.position);
            laser.SetPosition(1, rightHand.position + rightHand.forward * 2.6f);
            bool pointing = hud != null && hud.modal == Modal.None;
            if (laser.enabled != pointing) laser.enabled = pointing;
        }
    }
}
