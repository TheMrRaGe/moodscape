using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Something in the house you can pick up, carry and put down somewhere else. Each one remembers where
    /// it belongs, which is how the house knows what you've moved (and can quietly put it back).
    /// </summary>
    public class Movable : MonoBehaviour
    {
        public float mass = 1f;
        public Vector3 homePos;
        public Quaternion homeRot;
        public bool held;
        Rigidbody body;

        public Rigidbody Body { get { return body; } }
        public bool Heavy { get { return mass > 3f; } }
        public float FromHome { get { return (transform.position - homePos).magnitude; } }

        public static Movable Attach(GameObject g, float mass)
        {
            var m = g.AddComponent<Movable>();
            m.mass = mass;
            m.body = g.AddComponent<Rigidbody>();
            m.body.mass = mass;
            m.body.interpolation = RigidbodyInterpolation.Interpolate;
            m.body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            m.homePos = g.transform.position;
            m.homeRot = g.transform.rotation;
            Physical.On(m);
            var it = Build.Usable(g, "");
            it.promptFn = () => m.Heavy ? "Drag this" : "Pick this up";
            it.use = () => { if (Player.I != null) Player.I.PickUp(m); };
            return m;
        }

        bool resting;

        /// <summary>
        /// Stay exactly as arranged (a row of books, chess pieces on their squares) until someone picks it up; after
        /// that it is an ordinary physical thing.
        /// </summary>
        public void Rest() { resting = true; if (body != null) body.isKinematic = true; }

        /// <summary>Knocked, bumped or shaken: an ordinary physical thing again, even if it was resting as arranged.</summary>
        public void Wake()
        {
            if (held || body == null) return;
            resting = false;
            body.isKinematic = false;
            body.WakeUp();
        }

        /// <summary>Thrown or let go of with some speed.</summary>
        public void Launch(Vector3 velocity, Vector3 spin)
        {
            Wake();
            if (body == null) return;
            body.velocity = velocity;
            body.angularVelocity = spin;
            var p = GetComponent<Physical>();
            if (p != null) p.thrown = true;
        }

        /// <summary>Called once everything has been placed, after physics has let it settle.</summary>
        public void RememberHome()
        {
            homePos = transform.position;
            homeRot = transform.rotation;
        }

        /// <summary>When you last had it in your hands (the house leaves alone what you're using).</summary>
        public float touchedAt = -999f;
        /// <summary>Something you're working with or just had: the hauntings don't move it.</summary>
        public bool InUse { get { return held || Time.time - touchedAt < 240f || GetComponent<Tool>() != null; } }

        public void SetHeld(bool on)
        {
            held = on;
            touchedAt = Time.time;
            resting = false;
            body.isKinematic = on;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = !on;      // so it can't shove the player while carried
            if (!on) body.WakeUp();
        }

        /// <summary>Moved by the house, not the player: no physics, just somewhere else when you next look.</summary>
        public void Teleport(Vector3 pos, Quaternion rot)
        {
            bool wasKinematic = body.isKinematic;
            body.isKinematic = true;
            transform.SetPositionAndRotation(pos, rot);
            body.isKinematic = wasKinematic || resting;
            body.WakeUp();
        }
    }
}
