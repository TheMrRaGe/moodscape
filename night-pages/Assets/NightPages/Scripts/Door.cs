using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// A door on a hinge, handled by hand (the Layers of Fear / Amnesia way).
    ///
    /// Hold [E] on a door and move the mouse: forward pushes it away from you, back pulls it toward you, and it
    /// swings as fast as your hand moves. Let go mid-swing and it carries on, slowing on its hinge. A tap of [E]
    /// gives it a small push, enough to open a crack and look through. Walking into a door shoves it open at
    /// the speed you're walking; running into one bangs it against the wall.
    ///
    /// Doors are old: they creak while they move (slow movement creaks the most), and a door that hits its frame
    /// or the wall fast enough slams. Slams are noise: whatever is listening hears it (see Noise). Opening a door
    /// slowly is quiet, and it gives you time to see what's on the other side before it sees you; and it gives
    /// the house time to put something in the gap.
    ///
    /// The house and the dreams can still move a door themselves with Set (and Snap while building); those moves
    /// are scripted and aren't your noise.
    /// </summary>
    public class Door : MonoBehaviour
    {
        public bool open = true;
        public float openAngle = 95f;
        public float baseYaw;           // which way the closed door faces (90 for a door in an east-west wall)
        public Transform slab;
        public string roomName = "";
        public float width = 1.16f;
        /// <summary>How much this door complains when it moves (0 a well-oiled door, 1 the worst in the house).</summary>
        public float creakiness = 0.6f;
        float cur, vel;
        bool scripted;
        float scriptedTarget;
        AudioSource hinge;
        float touchedAt = -100f;        // when the player last moved it (its noise is theirs for a few seconds)
        float slowSince = -1f;

        /// <summary>The player's hand is on it.</summary>
        public bool Held { get; private set; }
        /// <summary>The angle, in degrees from shut (always 0..|openAngle|).</summary>
        public float Angle { get { return Mathf.Abs(cur); } }
        /// <summary>How open, 0 shut .. 1 against its stop.</summary>
        public float Openness { get { return Mathf.Abs(cur) / Mathf.Max(1f, Mathf.Abs(openAngle)); } }
        public float Speed { get { return Mathf.Abs(vel); } }

        /// <summary>Every door that is being opened slowly by hand (the house can put something in the gap): (door, how far open 0..1).</summary>
        public static event System.Action<Door, float> OpeningSlowly;
        /// <summary>A door the player slammed or banged: (door, loudness).</summary>
        public static event System.Action<Door, float> Slammed;
        public static void ResetStatics() { OpeningSlowly = null; Slammed = null; }

        public Vector3 Middle { get { return slab.position; } }
        public Vector3 HingePos { get { return transform.position; } }

        float Lo { get { return Mathf.Min(0f, openAngle); } }
        float Hi { get { return Mathf.Max(0f, openAngle); } }

        public static Door Create(Transform parent, Vector3 hinge, float openAngle, string roomName, Color c, float baseYaw = 0f, Material mat = null, float width = 1.16f, float height = 2.08f)
        {
            var pivot = new GameObject("Door " + roomName);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = hinge;
            var d = pivot.AddComponent<Door>();
            d.openAngle = openAngle;
            d.roomName = roomName;
            d.baseYaw = baseYaw;
            d.width = width;
            d.creakiness = 0.35f + Random.value * 0.65f;
            var s = mat != null
                ? Build.Box("Slab", pivot.transform, new Vector3(0f, height / 2f + 0.01f, width / 2f + 0.02f), new Vector3(0.05f, height, width), mat)
                : Build.Box("Slab", pivot.transform, new Vector3(0f, height / 2f + 0.01f, width / 2f + 0.02f), new Vector3(0.05f, height, width), c);
            d.slab = s.transform;
            var brass = Build.Mat(new Color(0.75f, 0.6f, 0.3f), 0f, 0.8f);
            Build.Prim(PrimitiveType.Sphere, "Knob", pivot.transform, new Vector3(0.05f, 1.0f, width - 0.08f), Vector3.one * 0.06f, brass, false);
            Build.Prim(PrimitiveType.Sphere, "Knob", pivot.transform, new Vector3(-0.05f, 1.0f, width - 0.08f), Vector3.one * 0.06f, brass, false);
            var it = Build.Usable(s, "");
            it.promptFn = () => d.Held ? "Move the mouse to swing it. Slow is quiet." : "Hold to take the handle (tap: a push)";
            it.use = () => d.Set(!d.open, false);          // VR and test runs; the mouse player takes the handle instead (Player)
            d.cur = d.open ? d.openAngle : 0f;
            pivot.transform.localRotation = Quaternion.Euler(0f, d.baseYaw + d.cur, 0f);
            return d;
        }

        /// <summary>Set open or closed instantly and silently (building the house).</summary>
        public void Snap(bool makeOpen)
        {
            open = makeOpen;
            cur = open ? openAngle : 0f;
            vel = 0f;
            scripted = false;
            Apply();
        }

        /// <summary>Stand ajar by this fraction of the way open (0..1), silently.</summary>
        public void Ajar(float fraction)
        {
            cur = openAngle * Mathf.Clamp01(fraction);
            vel = 0f;
            scripted = false;
            open = Mathf.Abs(cur) > 10f;
            Apply();
        }

        /// <summary>The house (or a test) opens or shuts it. A slam is instant and loud, but it isn't the player's noise.</summary>
        public void Set(bool makeOpen, bool slam)
        {
            if (open == makeOpen && !scripted) return;
            open = makeOpen;
            if (slam) Sfx.At("slam", Middle, 0.95f);
            else
            {
                // Old doors: the latch, and now and then the hinge complains.
                Sfx.At(open ? "dooropen" : "doorclose", Middle, 0.7f, 0.95f + Random.value * 0.1f);
                if (open && Random.value < 0.35f) Sfx.At("creak", Middle, 0.35f, 0.9f + Random.value * 0.25f);
            }
            if (slam) { cur = open ? openAngle : 0f; vel = 0f; scripted = false; Apply(); return; }
            scripted = true;
            scriptedTarget = open ? openAngle : 0f;
            vel = 0f;
        }

        // ---------------------------------------------------------------- the player's hand

        // Which way an increase in angle moves the slab, seen from `from`: +1 away from it, -1 toward it.
        float AwaySign(Vector3 from)
        {
            Vector3 hingeP = transform.position;
            Vector3 mid = slab.position;
            Vector3 radius = mid - hingeP; radius.y = 0f;
            Vector3 tangent = Vector3.Cross(Vector3.up, radius);         // where the slab goes as the yaw increases
            Vector3 toSlab = mid - from; toSlab.y = 0f;
            return Vector3.Dot(tangent, toSlab) >= 0f ? 1f : -1f;
        }

        public void Grab() { Held = true; scripted = false; touchedAt = Time.time; }

        public void Release() { Held = false; slowSince = -1f; }

        /// <summary>The hand on the handle moved: `push` > 0 pushes away from `from` (mouse forward), < 0 pulls.</summary>
        public void Drag(float push, Vector3 from, float dt)
        {
            touchedAt = Time.time;
            float sign = AwaySign(from);
            // Degrees of swing for this much hand movement; the door follows the hand, with a little lag.
            // (An ordinary sweep of the mouse swings it at a walking pace; it takes a deliberate flick to bang it.)
            float wantVel = Mathf.Clamp(sign * push * 0.7f / Mathf.Max(dt, 1e-3f), -540f, 540f);
            vel = Mathf.Lerp(vel, wantVel, 1f - Mathf.Exp(-dt * 18f));
        }

        /// <summary>A tap on the handle: a small push away from `from`, enough to open a crack.</summary>
        public void Nudge(Vector3 from)
        {
            touchedAt = Time.time;
            scripted = false;
            vel = AwaySign(from) * (60f + Random.value * 20f);
            Sfx.At("dooropen", Middle, 0.35f, 1.05f + Random.value * 0.1f);
        }

        /// <summary>A body walked into it at `speed` m/s, touching it at `point`.</summary>
        public void Shove(Vector3 point, Vector3 from, float speed)
        {
            if (speed < 0.3f) return;
            touchedAt = Time.time;
            scripted = false;
            Vector3 r = point - transform.position; r.y = 0f;
            float radius = Mathf.Max(0.35f, r.magnitude);
            // The door takes about half your speed at the point you hit it (it's heavy, and you lean, you don't fly).
            // Walking into it is a firm push that swings it open quietly; only running into it slams it.
            float cap = speed > 3.2f ? 200f : 70f;
            float w = AwaySign(from) * Mathf.Min(speed / radius * Mathf.Rad2Deg * 0.5f, cap);
            if (Mathf.Abs(w) > Mathf.Abs(vel) || Mathf.Sign(w) != Mathf.Sign(vel)) vel = w;
        }

        /// <summary>The player swings it shut hard (tests and demos: a slam that's theirs).</summary>
        public void Fling(float speed)
        {
            touchedAt = Time.time;
            scripted = false;
            vel = -Mathf.Sign(cur == 0f ? openAngle : cur) * speed;
        }

        /// <summary>Something that isn't the player shoved it hard (the thing in the dream coming through).</summary>
        public void Burst(Vector3 from, float speed)
        {
            scripted = false;
            vel = AwaySign(from) * speed;
        }

        // ---------------------------------------------------------------- swinging

        void Apply() { transform.localRotation = Quaternion.Euler(0f, baseYaw + cur, 0f); }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (scripted)
            {
                cur = Mathf.MoveTowards(cur, scriptedTarget, 160f * dt);
                if (Mathf.Approximately(cur, scriptedTarget)) scripted = false;
                Apply();
                Creak(0f);
                return;
            }
            if (vel == 0f) { Creak(0f); return; }
            float before = cur;
            cur += vel * dt;
            // The hinge drags: a door let go keeps swinging, slower and slower. (Held, the hand sets the speed.)
            if (!Held) vel *= Mathf.Exp(-dt * 2.4f);
            if (Mathf.Abs(vel) < 4f && !Held) vel = 0f;
            float hit = 0f;
            bool shutStop = false;
            if (cur > Hi) { hit = Mathf.Abs(vel); shutStop = Hi == 0f; cur = Hi; vel = -vel * 0.18f; }
            else if (cur < Lo) { hit = Mathf.Abs(vel); shutStop = Lo == 0f; cur = Lo; vel = -vel * 0.18f; }
            Apply();
            bool wasOpen = open;
            open = Mathf.Abs(cur) > 10f;
            if (hit > 0f) Impact(hit, shutStop);
            Creak(Mathf.Abs(cur - before) / dt);

            // Being opened slowly by hand, the first part of the way: the gap is opening.
            bool slow = Held && Mathf.Abs(cur) > Mathf.Abs(before) && Mathf.Abs(vel) < 70f;
            if (slow && Openness < 0.5f)
            {
                if (slowSince < 0f) slowSince = Time.time;
                if (OpeningSlowly != null) OpeningSlowly(this, Openness);
            }
            if (!wasOpen && open && Time.time - touchedAt < 3f && GameDirector.I != null)
            {
                // How a door gets opened is a habit like any other.
                if (Held && Mathf.Abs(vel) < 80f) Habits.Use(GameDirector.I, "careful with doors", 0.5f);
            }
        }

        // It hit the frame (shut) or its stop (the wall). Hard enough, and that's a slam.
        void Impact(float speed, bool shut)
        {
            bool players = Time.time - touchedAt < 3f;
            if (speed >= 200f)
            {
                Sfx.At("slam", Middle, Mathf.Clamp01(0.55f + speed / 900f), 0.95f + Random.value * 0.1f);
                // A slam shakes the room: what's on the shelves and tables near it rattles, and can fall.
                Physical.Jostle(Middle, 2.4f, Mathf.Clamp(speed / 900f, 0.2f, 0.6f), -1.5f);
                if (players) Loud(1f, shut ? "you slammed a door" : "you flung a door against the wall");
            }
            else if (speed >= 115f)
            {
                Sfx.At("slam", Middle, 0.42f, 1.15f + Random.value * 0.1f);
                if (players) Loud(0.55f, shut ? "a door banged shut" : "a door banged against the wall");
            }
            else if (speed >= 40f)
            {
                if (shut) Sfx.At("doorclose", Middle, 0.45f, 1f + Random.value * 0.1f);
                else Sfx.At("putdown", Middle, 0.25f, 0.7f);
                if (players && shut) Loud(0.15f, "a door latched");
            }
        }

        void Loud(float loud, string what)
        {
            Noise.Make(Middle, loud, what);
            if (loud >= 0.5f)
            {
                var g = GameDirector.I;
                if (g != null) Habits.Use(g, "slams doors", loud);
                if (Slammed != null) Slammed(this, loud);
            }
        }

        // Old hinges: a creak while it moves, loudest when it moves slowly (that long drawn-out one), quiet when flung.
        void Creak(float degPerSec)
        {
            float want = 0f, pitch = 1f;
            if (degPerSec > 2f)
            {
                float slowness = 1f - Mathf.InverseLerp(20f, 260f, degPerSec);
                want = creakiness * Mathf.Lerp(0.08f, 0.42f, slowness) * Mathf.InverseLerp(2f, 10f, degPerSec);
                pitch = Mathf.Lerp(0.7f, 1.35f, Mathf.InverseLerp(5f, 200f, degPerSec)) * (0.9f + creakiness * 0.2f);
            }
            if (hinge == null)
            {
                if (want <= 0.001f) return;
                hinge = Sfx.Loop("doorcreak", transform, 0f, true);
                hinge.transform.localPosition = new Vector3(0f, 1.9f, 0.05f);
                hinge.maxDistance = 18f;
            }
            hinge.volume = Mathf.MoveTowards(hinge.volume, want, Time.deltaTime * (want > hinge.volume ? 3f : 1.5f));
            hinge.pitch = Mathf.Lerp(hinge.pitch, pitch, Time.deltaTime * 6f);
            if (hinge.volume <= 0.001f && want <= 0f) { Destroy(hinge.gameObject); hinge = null; }
        }
    }
}
