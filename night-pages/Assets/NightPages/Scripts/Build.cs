using UnityEngine;

namespace NightPages
{
    /// <summary>Greybox building blocks. Everything in the prototype is made from these at startup.</summary>
    public static class Build
    {
        static Material baseMat;

        public static void ResetStatics() { baseMat = null; }

        // Copy the render pipeline's own default material, so this works in Built-in, URP and HDRP alike.
        static Material Base
        {
            get
            {
                if (baseMat == null)
                {
                    var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    baseMat = new Material(g.GetComponent<Renderer>().sharedMaterial);
                    Object.Destroy(g);
                }
                return baseMat;
            }
        }

        public static Material Mat(Color c, float emit = 0f, float smooth = 0.08f)
        {
            var m = new Material(Base);
            m.color = c;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (emit > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * emit);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return m;
        }

        /// <summary>A material that shows a texture at full brightness (paintings, the dream's far walls).</summary>
        public static Material TexMat(Texture t)
        {
            var sh = Shader.Find("Unlit/Texture");
            if (sh != null)
            {
                var u = new Material(sh);
                u.mainTexture = t;
                return u;
            }
            var m = Mat(Color.black);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.white);
            m.SetTexture("_EmissionMap", t);
            return m;
        }

        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, bool solid = true)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            if (!solid)
            {
                var col = g.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
            }
            return g;
        }

        public static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Color c, bool solid = true, float emit = 0f)
        {
            return Prim(PrimitiveType.Cube, name, parent, localPos, size, Mat(c, emit), solid);
        }

        public static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, bool solid = true)
        {
            return Prim(PrimitiveType.Cube, name, parent, localPos, size, mat, solid);
        }

        public static Light Lamp(string name, Transform parent, Vector3 localPos, Color c, float intensity, float range, bool shadows = false)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            var l = g.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return l;
        }

        public static Interactable Usable(GameObject g, string prompt, System.Action use = null)
        {
            var it = g.GetComponent<Interactable>();
            if (it == null) it = g.AddComponent<Interactable>();
            it.prompt = prompt;
            it.use = use;
            return it;
        }

        /// <summary>A tall, thin, dark figure. No colliders: it is never something you can push.</summary>
        public static Transform Figure(string name, Transform parent)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var dark = Mat(new Color(0.015f, 0.015f, 0.02f), 0f, 0f);
            Prim(PrimitiveType.Capsule, "Body", root, new Vector3(0f, 1.0f, 0f), new Vector3(0.42f, 1.0f, 0.42f), dark, false);
            Prim(PrimitiveType.Sphere, "Head", root, new Vector3(0f, 2.15f, 0f), new Vector3(0.34f, 0.4f, 0.34f), dark, false);
            Prim(PrimitiveType.Cube, "ArmL", root, new Vector3(-0.31f, 1.2f, 0f), new Vector3(0.07f, 1.5f, 0.07f), dark, false);
            Prim(PrimitiveType.Cube, "ArmR", root, new Vector3(0.31f, 1.2f, 0f), new Vector3(0.07f, 1.5f, 0.07f), dark, false);
            return root;
        }
    }

    /// <summary>What the player can and can't see right now. The look-away rule is built on this.</summary>
    public static class Vis
    {
        public static bool Sees(Camera cam, Vector3 point, Transform self = null, float margin = 0.06f)
        {
            if (cam == null) return false;
            Vector3 v = cam.WorldToViewportPoint(point);
            if (v.z <= 0.05f || v.x < -margin || v.x > 1f + margin || v.y < -margin || v.y > 1f + margin) return false;
            Vector3 o = cam.transform.position;
            Vector3 d = point - o;
            float dist = d.magnitude;
            if (dist < 0.3f) return true;
            RaycastHit hit;
            if (Physics.Raycast(o, d / dist, out hit, dist - 0.15f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (self != null && hit.transform.IsChildOf(self)) return true;
                return false;
            }
            return true;
        }

        /// <summary>The same vector laid flat on the floor (its length kept: use this for distances along the ground).</summary>
        public static Vector3 Level(Vector3 v) { v.y = 0f; return v; }

        /// <summary>The direction of a vector along the floor (always length 1).</summary>
        public static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
        }
    }
}
