using UnityEngine;

namespace NightPages
{
    /// <summary>A door on a hinge. The player can open and close it; so can the house.</summary>
    public class Door : MonoBehaviour
    {
        public bool open = true;
        public float openAngle = 95f;
        public float baseYaw;           // which way the closed door faces (90 for a door in an east-west wall)
        public Transform slab;
        public string roomName = "";
        float cur;

        public Vector3 Middle { get { return slab.position; } }

        public static Door Create(Transform parent, Vector3 hinge, float openAngle, string roomName, Color c, float baseYaw = 0f, Material mat = null, float width = 1.16f)
        {
            var pivot = new GameObject("Door " + roomName);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = hinge;
            var d = pivot.AddComponent<Door>();
            d.openAngle = openAngle;
            d.roomName = roomName;
            d.baseYaw = baseYaw;
            var s = mat != null
                ? Build.Box("Slab", pivot.transform, new Vector3(0f, 1.05f, width / 2f + 0.02f), new Vector3(0.05f, 2.08f, width), mat)
                : Build.Box("Slab", pivot.transform, new Vector3(0f, 1.05f, width / 2f + 0.02f), new Vector3(0.05f, 2.08f, width), c);
            d.slab = s.transform;
            var brass = Build.Mat(new Color(0.75f, 0.6f, 0.3f), 0f, 0.8f);
            Build.Prim(PrimitiveType.Sphere, "Knob", pivot.transform, new Vector3(0.05f, 1.0f, width - 0.08f), Vector3.one * 0.06f, brass, false);
            Build.Prim(PrimitiveType.Sphere, "Knob", pivot.transform, new Vector3(-0.05f, 1.0f, width - 0.08f), Vector3.one * 0.06f, brass, false);
            var it = Build.Usable(s, "");
            it.promptFn = () => d.open ? "Close the door" : "Open the door";
            it.use = () => d.Set(!d.open, false);
            d.cur = d.open ? d.openAngle : 0f;
            pivot.transform.localRotation = Quaternion.Euler(0f, d.baseYaw + d.cur, 0f);
            return d;
        }

        /// <summary>Set open or closed instantly and silently (building the house).</summary>
        public void Snap(bool makeOpen)
        {
            open = makeOpen;
            cur = open ? openAngle : 0f;
            transform.localRotation = Quaternion.Euler(0f, baseYaw + cur, 0f);
        }

        public void Set(bool makeOpen, bool slam)
        {
            if (open == makeOpen) return;
            open = makeOpen;
            if (slam) Sfx.At("slam", Middle, 0.95f);
            else
            {
                // Old doors: the latch, and now and then the hinge complains.
                Sfx.At(open ? "dooropen" : "doorclose", Middle, 0.7f, 0.95f + Random.value * 0.1f);
                if (open && Random.value < 0.35f) Sfx.At("creak", Middle, 0.35f, 0.9f + Random.value * 0.25f);
            }
            if (slam) { cur = open ? openAngle : 0f; transform.localRotation = Quaternion.Euler(0f, baseYaw + cur, 0f); }
        }

        void Update()
        {
            float target = open ? openAngle : 0f;
            if (Mathf.Approximately(cur, target)) return;
            cur = Mathf.MoveTowards(cur, target, 160f * Time.deltaTime);
            transform.localRotation = Quaternion.Euler(0f, baseYaw + cur, 0f);
        }
    }
}
