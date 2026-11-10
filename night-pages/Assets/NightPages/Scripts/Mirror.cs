using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// A real mirror: a second camera, mirrored through the glass, renders what the glass would show (only while
    /// you're near it and it's on screen). Two things make it a fright. Anything on the OnlyInMirror layer is seen
    /// by the mirror and not by you: something can stand behind you in the reflection and not be there when you
    /// turn. And the reflection can be made to run late, so your own movements come back to you a moment after
    /// you make them.
    /// </summary>
    public class Mirror : MonoBehaviour
    {
        public const int OnlyInMirror = 29;
        static bool rendering;

        Renderer glass;
        Camera reflCam;
        RenderTexture rt;
        /// <summary>How far behind you the reflection runs, in seconds (0 = a normal mirror).</summary>
        public float lag;
        readonly List<KeyValuePair<float, Pose>> history = new List<KeyValuePair<float, Pose>>();

        public static Mirror Attach(Renderer glass)
        {
            var shader = Shader.Find("NightPages/Mirror");
            if (shader == null) { Debug.LogWarning("[NightPages] mirror shader missing; the mirror stays dark"); return null; }
            var m = glass.gameObject.AddComponent<Mirror>();
            m.glass = glass;
            glass.sharedMaterial = new Material(shader);
            return m;
        }

        // You, as the mirror sees you: a dark figure the player's own camera never draws.
        static Transform you;
        readonly List<KeyValuePair<float, Pose>> body = new List<KeyValuePair<float, Pose>>();

        /// <summary>Give the player a body that only mirrors see.</summary>
        public static void AddReflection(Player p)
        {
            if (you != null) return;
            var root = new GameObject("Your reflection").transform;
            root.SetParent(null, false);
            var coat = Build.Mat(new Color(0.12f, 0.12f, 0.14f), 0f, 0.2f);
            var skin = Build.Mat(new Color(0.55f, 0.46f, 0.4f), 0f, 0.3f);
            Build.Prim(PrimitiveType.Capsule, "Body", root, new Vector3(0f, 0.85f, 0f), new Vector3(0.46f, 0.62f, 0.3f), coat, false);
            Build.Prim(PrimitiveType.Sphere, "Head", root, new Vector3(0f, 1.6f, 0.02f), new Vector3(0.2f, 0.25f, 0.22f), skin, false);
            Build.Prim(PrimitiveType.Sphere, "Hair", root, new Vector3(0f, 1.66f, -0.02f), new Vector3(0.21f, 0.2f, 0.22f), Build.Mat(new Color(0.08f, 0.06f, 0.05f)), false);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetLayer(root.gameObject, OnlyInMirror);
            you = root;
        }

        /// <summary>The player's camera never sees what is only in the mirror.</summary>
        public static void HideFrom(Camera cam) { cam.cullingMask &= ~(1 << OnlyInMirror); }

        public static void SetLayer(GameObject g, int layer)
        {
            g.layer = layer;
            foreach (Transform c in g.transform) SetLayer(c.gameObject, layer);
        }

        /// <summary>Is the player looking at the glass, close enough to see themselves in it?</summary>
        public bool Watched(Camera cam, float within = 4.5f)
        {
            Vector3 to = transform.position - cam.transform.position;
            if (to.magnitude > within) return false;
            if (Vector3.Dot(transform.forward, to) < 0f) return false;            // on the room side of the glass
            return Vector3.Angle(cam.transform.forward, to) < 28f;
        }

        void LateUpdate()
        {
            var p = Player.I;
            if (p == null) return;
            history.Add(new KeyValuePair<float, Pose>(Time.time, new Pose(p.cam.transform.position, p.cam.transform.rotation)));
            while (history.Count > 0 && history[0].Key < Time.time - 2f) history.RemoveAt(0);
            body.Add(new KeyValuePair<float, Pose>(Time.time, new Pose(p.transform.position, p.transform.rotation)));
            while (body.Count > 0 && body[0].Key < Time.time - 2f) body.RemoveAt(0);
            if (you != null)
            {
                // Your reflection, as late as the mirror is.
                Pose b = body[body.Count - 1].Value;
                if (lag > 0f) { float want = Time.time - lag; foreach (var h in body) if (h.Key >= want) { b = h.Value; break; } }
                you.SetPositionAndRotation(b.position, b.rotation);
            }
        }

        Pose Source(Camera cam)
        {
            if (lag <= 0f || history.Count == 0) return new Pose(cam.transform.position, cam.transform.rotation);
            float want = Time.time - lag;
            foreach (var h in history) if (h.Key >= want) return h.Value;
            return history[history.Count - 1].Value;
        }

        void OnWillRenderObject()
        {
            var cam = Camera.current;
            if (rendering || cam == null || Player.I == null || cam != Player.I.cam) return;
            if ((cam.transform.position - transform.position).sqrMagnitude > 64f) return;
            rendering = true;
            try { Render(cam); }
            finally { rendering = false; }
        }

        void Render(Camera cam)
        {
            int w = Mathf.Max(64, cam.pixelWidth / 2), h = Mathf.Max(64, cam.pixelHeight / 2);
            if (rt == null || rt.width != w || rt.height != h)
            {
                if (rt != null) { rt.Release(); Destroy(rt); }
                rt = new RenderTexture(w, h, 16) { name = "Mirror" };
                glass.sharedMaterial.mainTexture = rt;
            }
            if (reflCam == null)
            {
                var go = new GameObject("Mirror camera");
                go.hideFlags = HideFlags.HideAndDontSave;
                reflCam = go.AddComponent<Camera>();
                reflCam.enabled = false;
            }
            reflCam.CopyFrom(cam);
            reflCam.cullingMask = cam.cullingMask | (1 << OnlyInMirror);
            reflCam.targetTexture = rt;
            reflCam.allowHDR = false;
            reflCam.allowMSAA = false;

            // The glass is a quad: it faces back along its forward axis, toward the room.
            Vector3 normal = -transform.forward, pos = transform.position;
            float d = -Vector3.Dot(normal, pos) - 0.01f;
            var plane = new Vector4(normal.x, normal.y, normal.z, d);
            Matrix4x4 reflection = Reflection(plane);

            Pose src = Source(cam);
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(src.position, src.rotation, Vector3.one).inverse;
            reflCam.worldToCameraMatrix = view * reflection;
            reflCam.transform.position = reflection.MultiplyPoint(src.position);

            // Clip everything behind the glass (the wall, the room on the other side).
            Matrix4x4 m = reflCam.worldToCameraMatrix;
            Vector3 cpos = m.MultiplyPoint(pos + normal * 0.01f);
            Vector3 cnormal = m.MultiplyVector(normal).normalized;
            reflCam.projectionMatrix = cam.CalculateObliqueMatrix(new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal)));

            bool inv = GL.invertCulling;
            GL.invertCulling = !inv;
            reflCam.Render();
            GL.invertCulling = inv;
        }

        static Matrix4x4 Reflection(Vector4 p)
        {
            var r = Matrix4x4.identity;
            r.m00 = 1f - 2f * p.x * p.x; r.m01 = -2f * p.x * p.y; r.m02 = -2f * p.x * p.z; r.m03 = -2f * p.w * p.x;
            r.m10 = -2f * p.y * p.x; r.m11 = 1f - 2f * p.y * p.y; r.m12 = -2f * p.y * p.z; r.m13 = -2f * p.w * p.y;
            r.m20 = -2f * p.z * p.x; r.m21 = -2f * p.z * p.y; r.m22 = 1f - 2f * p.z * p.z; r.m23 = -2f * p.w * p.z;
            r.m30 = 0f; r.m31 = 0f; r.m32 = 0f; r.m33 = 1f;
            return r;
        }

        public static void ResetStatics() { you = null; }

        void OnDestroy()
        {
            if (reflCam != null) Destroy(reflCam.gameObject);
            if (rt != null) { rt.Release(); Destroy(rt); }
        }
    }
}
