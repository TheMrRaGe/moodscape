using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The time of day. A day runs from waking at 7:00 to bed, at one game hour every two minutes (16 hours in about
    /// half an hour: the chores are done by hand and take real time). Morning is until noon, the afternoon until six, the evening after that. The light through the
    /// windows follows the sun; the lamps are whatever you've switched on. The hall clock strikes the hours.
    /// Time stops while the game is paused or a page is open at the desk (work moves it on by itself, in jumps).
    /// You can't see the time on the screen: look at a clock, or at your watch (hold Tab; in VR, look at your left wrist).
    /// </summary>
    public class DayClock : MonoBehaviour
    {
        public static DayClock I;
        public const float Wake = 7f * 60f, Noon = 12f * 60f, Dusk = 18f * 60f, BedFrom = 21f * 60f, PassOut = 25f * 60f;
        /// <summary>Game minutes per real second: one hour in 75 s.</summary>
        /// <summary>Game minutes per real second (the day-length setting: 24, 32 or 48 minutes).</summary>
        public static float Rate { get { return Settings.ClockRate; } }

        /// <summary>Minutes since midnight this morning (past 24*60 after midnight).</summary>
        public float minutes = Wake;
        public bool running;
        /// <summary>Test runs speed the day up.</summary>
        public float speed = 1f;
        public System.Action<int> onHour;
        GameDirector g;
        int lastHour = -1;
        float lightAt;

        public float Hour { get { return minutes / 60f; } }

        public static DayClock Attach(GameDirector director)
        {
            var c = director.gameObject.AddComponent<DayClock>();
            c.g = director;
            I = c;
            return c;
        }

        /// <summary>"7:40 in the morning", "a quarter past three".</summary>
        public static string Say(float minutes)
        {
            int m = Mathf.FloorToInt(minutes) % (24 * 60);
            int h = m / 60, mm = m % 60;
            int h12 = h % 12 == 0 ? 12 : h % 12;
            string part = h < 12 ? "am" : "pm";
            return h12 + ":" + mm.ToString("00") + " " + part;
        }

        public string Now { get { return Say(minutes); } }

        public void StartDay()
        {
            minutes = Wake;
            lastHour = Mathf.FloorToInt(Hour);
            running = true;
            Relight(true);
        }

        /// <summary>Jump forward (an hour at the typewriter passes in a moment).</summary>
        public void Advance(float gameMinutes)
        {
            minutes += Mathf.Max(0f, gameMinutes);
            Relight(true);
            g.Log("clock: " + Mathf.RoundToInt(gameMinutes) + " minutes pass. It's " + Now);
        }

        /// <summary>Set the time outright (never backwards except for a new day).</summary>
        public void JumpTo(float m)
        {
            if (m > minutes) minutes = m;
            Relight(true);
        }

        void Relight(bool now)
        {
            if (g.house == null) return;
            if (!now && Time.time < lightAt) return;
            lightAt = Time.time + 1.5f;
            if (g.phase == Phase.Dream || g.phase == Phase.Title || g.phase == Phase.Ending) return;
            if (g.house.InCellar(g.player.transform.position)) return;
            g.house.SetHour(Hour, g.day);
        }

        void Update()
        {
            ClockFace.ShowAll(minutes);
            if (!running || Time.timeScale <= 0f || g == null) return;
            var hud = g.hud;
            bool stopped = hud == null || hud.modal == Modal.Title || hud.modal == Modal.Ending || hud.modal == Modal.Paused || hud.modal == Modal.Writing || hud.modal == Modal.Menu;
            if (g.phase == Phase.Dream || g.phase == Phase.Title || g.phase == Phase.Ending) stopped = true;
            if (stopped) return;
            minutes += Time.deltaTime * Rate * speed;
            int h = Mathf.FloorToInt(Hour);
            if (h != lastHour)
            {
                lastHour = h;
                Strike(h);
                if (onHour != null) onHour(h);
            }
            Relight(false);
        }

        // The hall clock strikes the hour (unless it has run down: the clock chore winds it).
        void Strike(int hour)
        {
            if (g.house == null || g.house.hallClock == null || !g.house.clockWound) return;
            int n = hour % 12 == 0 ? 12 : hour % 12;
            // From day 2, at bedtime, it strikes the hour it happened instead (the dream's clock asks for it).
            if (hour >= 21 && g.day >= 2) { n = g.DeathHour; g.Log("house: the hall clock struck " + n + " at " + hour + ":00"); }
            g.StartCoroutine(Bongs(n, g.house.hallClock.position + Vector3.up * 1.6f));
        }

        System.Collections.IEnumerator Bongs(int n, Vector3 at)
        {
            for (int i = 0; i < n; i++)
            {
                Sfx.At("chime", at, 0.55f, 1f);
                yield return new WaitForSeconds(1.7f);
            }
        }
    }

    /// <summary>A plain round clock face with two hands. The wall clocks, the hall clock's dial and your watch are these.</summary>
    public class ClockFace : MonoBehaviour
    {
        static readonly List<ClockFace> all = new List<ClockFace>();
        Transform hourHand, minuteHand;
        public float offset;            // minutes this clock is out (a haunting can set it)
        public bool stopped;
        public float stoppedAt;

        public static void ResetStatics() { all.Clear(); }

        /// <summary>A face at `pos` looking along `normal` (out of the wall, toward whoever reads it).</summary>
        public static ClockFace Make(Transform parent, Vector3 pos, Vector3 normal, float radius, Color face, Color rim, bool withCase = true)
        {
            var root = new GameObject("Clock face").transform;
            root.SetParent(parent, false);
            root.position = pos;
            root.rotation = Quaternion.LookRotation(normal);
            var f = root.gameObject.AddComponent<ClockFace>();
            var faceMat = Build.Mat(face, 0.05f, 0.35f);
            var ink = Build.Mat(new Color(0.06f, 0.05f, 0.05f), 0f, 0.4f);
            if (withCase)
            {
                var rimGo = Build.Prim(PrimitiveType.Cylinder, "Clock rim", root, new Vector3(0f, 0f, -radius * 0.08f), new Vector3(radius * 2.18f, radius * 0.09f, radius * 2.18f), Build.Mat(rim, 0f, 0.5f), false);
                rimGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            var disc = Build.Prim(PrimitiveType.Cylinder, "Dial", root, new Vector3(0f, 0f, 0f), new Vector3(radius * 2f, radius * 0.02f, radius * 2f), faceMat, false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            float z = radius * 0.03f;
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                bool big = i % 3 == 0;
                var tick = Build.Box("Tick", root, new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f) * radius * 0.82f + new Vector3(0f, 0f, z), new Vector3(radius * (big ? 0.07f : 0.04f), radius * (big ? 0.2f : 0.12f), radius * 0.01f), ink, false);
                tick.transform.localRotation = Quaternion.Euler(0f, 0f, -i * 30f);
            }
            f.hourHand = Hand(root, radius * 0.5f, radius * 0.075f, z * 1.4f, ink);
            f.minuteHand = Hand(root, radius * 0.78f, radius * 0.05f, z * 1.8f, ink);
            Build.Prim(PrimitiveType.Cylinder, "Pin", root, new Vector3(0f, 0f, z * 2.2f), new Vector3(radius * 0.1f, radius * 0.01f, radius * 0.1f), ink, false).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            all.Add(f);
            return f;
        }

        static Transform Hand(Transform root, float len, float width, float z, Material m)
        {
            var pivot = new GameObject("Hand").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(0f, 0f, z);
            Build.Box("Hand", pivot, new Vector3(0f, len * 0.42f, 0f), new Vector3(width, len, width * 0.25f), m, false);
            return pivot;
        }

        public static void ShowAll(float minutes)
        {
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var f = all[i];
                if (f == null) { all.RemoveAt(i); continue; }
                f.Show(f.stopped ? f.stoppedAt : minutes + f.offset);
            }
        }

        public void Show(float minutes)
        {
            float m = minutes % 60f, h = (minutes / 60f) % 12f;
            minuteHand.localRotation = Quaternion.Euler(0f, 0f, -m * 6f);
            hourHand.localRotation = Quaternion.Euler(0f, 0f, -h * 30f);
        }
    }

    /// <summary>
    /// Your watch. On a flat screen it's on your left wrist just out of sight; hold Tab and you raise it to look.
    /// In VR it's on your left controller's wrist: turn your hand over and it's there.
    /// </summary>
    public class Watch : MonoBehaviour
    {
        public static Watch I;
        /// <summary>Test photographs: hold the watch up as if Tab were held.</summary>
        public static bool ForceRaise;
        Transform root, arm;
        float raise;
        bool inVR;

        public static Watch Attach(Player p)
        {
            var w = p.gameObject.AddComponent<Watch>();
            I = w;
            w.root = new GameObject("Watch").transform;
            w.root.SetParent(p.cam.transform, false);
            var sleeve = Build.Mat(new Color(0.16f, 0.15f, 0.14f), 0f, 0.15f);
            var skin = Build.Mat(new Color(0.62f, 0.48f, 0.4f), 0f, 0.3f);
            // A forearm in a jumper sleeve, a hand, the watch on the wrist.
            w.arm = new GameObject("Forearm").transform;
            w.arm.SetParent(w.root, false);
            Build.Box("Sleeve", w.arm, new Vector3(0f, -0.2f, -0.004f), new Vector3(0.078f, 0.3f, 0.07f), sleeve, false);
            Build.Box("Cuff", w.arm, new Vector3(0f, -0.052f, -0.004f), new Vector3(0.082f, 0.012f, 0.074f), Build.Mat(new Color(0.22f, 0.2f, 0.18f), 0f, 0.15f), false);
            Build.Box("Wrist", w.arm, new Vector3(0f, 0.0f, -0.004f), new Vector3(0.05f, 0.1f, 0.042f), skin, false);
            Build.Box("Hand", w.arm, new Vector3(0f, 0.075f, -0.012f), new Vector3(0.068f, 0.05f, 0.022f), skin, false);
            var strap = Build.Mat(new Color(0.18f, 0.1f, 0.06f), 0f, 0.3f);
            Build.Box("Strap", w.arm, new Vector3(0f, 0.005f, 0f), new Vector3(0.062f, 0.022f, 0.052f), strap, false);
            var c = Build.Prim(PrimitiveType.Cylinder, "Watch case", w.arm, new Vector3(0f, 0.005f, 0.027f), new Vector3(0.036f, 0.004f, 0.036f), Build.Mat(new Color(0.75f, 0.62f, 0.35f), 0f, 0.7f), false);
            c.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ClockFace.Make(w.arm, Vector3.zero, Vector3.forward, 0.016f, new Color(0.93f, 0.9f, 0.82f), new Color(0.75f, 0.62f, 0.35f), false)
                .transform.localPosition = new Vector3(0f, 0.005f, 0.0315f);
            w.Lower(true);
            return w;
        }

        void Lower(bool instant)
        {
            if (instant) raise = 0f;
        }

        void LateUpdate()
        {
            bool vr = VRRig.Active && VRRig.I != null;
            if (vr != inVR)
            {
                inVR = vr;
                // In VR the watch rides on the left hand; on a flat screen it hangs off the camera.
                root.SetParent(vr ? VRRig.I.leftHand : Player.I.cam.transform, false);
            }
            if (vr)
            {
                // The face on top of the wrist, just behind the controller, facing up off the back of the hand.
                root.localPosition = new Vector3(0f, 0.02f, -0.09f);
                root.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                root.localScale = Vector3.one * 0.5f;
                arm.gameObject.SetActive(true);
                foreach (var r in arm.GetComponentsInChildren<Renderer>()) r.enabled = r.name != "Sleeve" && r.name != "Hand" && r.name != "Wrist";
                return;
            }
            root.localScale = Vector3.one;
            var hud = Hud.I;
            bool want = (In.Held(Btn.Watch) || ForceRaise) && (hud == null || hud.modal == Modal.None);
            raise = Mathf.MoveTowards(raise, want ? 1f : 0f, Time.unscaledDeltaTime * 4.5f);
            arm.gameObject.SetActive(raise > 0.01f);
            float e = raise * raise * (3f - 2f * raise);
            // From below the bottom of the view to just under its middle, turned to face you.
            // The forearm comes in from the bottom left, across the body, with the face of the watch turned to you.
            root.localPosition = Vector3.Lerp(new Vector3(-0.2f, -0.45f, 0.3f), new Vector3(-0.02f, -0.075f, 0.25f), e);
            root.localRotation = Quaternion.Slerp(Quaternion.Euler(60f, 20f, 70f), Quaternion.Euler(-12f, 180f, 58f), e);
            if (want && raise > 0.95f && !told && GameDirector.I != null && DayClock.I != null)
            {
                told = true;
                GameDirector.I.Log("looked at your watch: " + DayClock.I.Now);
            }
            if (!want) told = false;
        }

        bool told;
    }
}
