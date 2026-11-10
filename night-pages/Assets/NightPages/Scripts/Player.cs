using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The player's body: walk, look, use things. Works with keyboard and mouse, or in VR when a headset
    /// is running (see VRRig). Low Grip makes the flat-screen view sway; in VR it never moves the camera
    /// for you, because that makes people sick.
    /// </summary>
    public class Player : MonoBehaviour
    {
        public static Player I;
        public Camera cam;
        public bool frozen = true;
        public float speedMul = 1f;
        public float eyeHeight = 1.65f;
        /// <summary>Added to the field of view (the hallucinations stretch the hall and make the walls breathe with it).</summary>
        public float fovOffset;
        /// <summary>Sleep paralysis: you can move your eyes a little and nothing else.</summary>
        public bool paralysed;
        float anchorYaw, anchorPitch;
        public void Paralyse(bool on) { paralysed = on; anchorYaw = yaw; anchorPitch = pitch; }
        public float Roll;
        public Vector3 velocity;
        public float lookSpeed;      // degrees per second, for the in-game reaction signals

        CharacterController cc;
        float yaw, pitch, vy, stepAcc, eyeCur = 1.65f;

        public Vector3 Eye { get { return cam.transform.position; } }

        /// <summary>The way the player is facing, flat along the floor (the headset's facing in VR).</summary>
        public Vector3 Forward { get { return Vis.Flat(VRRig.Active ? cam.transform.forward : transform.forward); } }

        public static Player Create(Vector3 pos)
        {
            var go = new GameObject("Player");
            go.transform.position = pos;
            var p = go.AddComponent<Player>();
            p.cc = go.AddComponent<CharacterController>();
            p.cc.height = 1.8f;
            p.cc.radius = 0.3f;
            p.cc.center = new Vector3(0f, 0.9f, 0f);
            p.cc.stepOffset = 0.3f;
            var eyes = new GameObject("Eyes");
            eyes.transform.SetParent(go.transform, false);
            eyes.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            eyes.tag = "MainCamera";
            p.cam = eyes.AddComponent<Camera>();
            p.cam.nearClipPlane = 0.05f;
            p.cam.farClipPlane = 320f;
            p.cam.fieldOfView = 68f;
            p.cam.clearFlags = CameraClearFlags.SolidColor;
            p.cam.backgroundColor = Color.black;
            eyes.AddComponent<AudioListener>();
            I = p;
            return p;
        }

        public void Teleport(Vector3 pos, float newYaw)
        {
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
            yaw = newYaw;
            pitch = 0f;
            vy = 0f;
            velocity = Vector3.zero;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public Movable carrying;

        public bool crouched;

        /// <summary>On your knees: a low ceiling you can't stand up under (the crawlspace dream).</summary>
        public void Crouch(bool on)
        {
            if (crouched == on || cc == null) return;
            crouched = on;
            cc.height = on ? 0.85f : 1.8f;
            cc.center = new Vector3(0f, on ? 0.425f : 0.9f, 0f);
            speedMul = on ? 0.55f : 1f;
        }

        /// <summary>Turn on the spot (the "watch" test run looks around).</summary>
        public void Turn(float degrees) { yaw += degrees; transform.rotation = Quaternion.Euler(0f, yaw, 0f); }

        /// <summary>Set where the view looks up or down (test captures).</summary>
        public void LookPitch(float p) { pitch = Mathf.Clamp(p, -80f, 80f); }

        // ---- for test playthroughs: walk, aim and use things the way a player does

        /// <summary>Take the body out of physics queries for a moment (so a path search doesn't find the player in its own way).</summary>
        public void Solid(bool on) { if (cc != null) cc.enabled = on; }

        /// <summary>Walk toward a point on the floor (collisions and all). Returns how far there is still to go.</summary>
        public float WalkToward(Vector3 target, float speed)
        {
            Vector3 d = target - transform.position;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.05f) return dist;
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            pitch = Mathf.Lerp(pitch, 4f, 0.2f);
            cc.Move(d / dist * Mathf.Min(speed * Time.deltaTime, dist) + Vector3.down * 2f * Time.deltaTime);
            stepAcc += Mathf.Min(speed * Time.deltaTime, dist);
            if (stepAcc > 2.1f) { stepAcc = 0f; Footstep(); }
            return dist;
        }

        /// <summary>Look straight at a point.</summary>
        public void Aim(Vector3 point)
        {
            Vector3 d = point - cam.transform.position;
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -80f, 80f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // The use ray: the first thing it meets that isn't you. (Looking down at something low, the ray used to start
        // above your own body and stop on it, so beds, plants and the fire couldn't be used from close up.)
        bool UseRay(Vector3 origin, Vector3 dir, float range, out RaycastHit first)
        {
            first = default(RaycastHit);
            var hits = Physics.RaycastAll(origin, dir, range, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            bool any = false;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < best) { best = h.distance; first = h; any = true; }
            }
            return any;
        }

        /// <summary>What the use-ray hits right now, in words (for playthrough reports).</summary>
        public string Probe()
        {
            RaycastHit hit;
            if (!UseRay(cam.transform.position, cam.transform.forward, 6f, out hit)) return "the ray hits nothing within 6 m";
            var it = hit.collider.GetComponentInParent<Interactable>();
            return "the ray hits '" + hit.collider.name + "' at " + hit.distance.ToString("0.00") + " m (reach 2.6)" +
                   (it == null ? ", which isn't usable" : it.Available ? "" : ", which isn't available now");
        }

        /// <summary>What a player pressing E right now would use (the same ray the game uses).</summary>
        public Interactable LookingAt()
        {
            RaycastHit hit;
            if (!UseRay(cam.transform.position, cam.transform.forward, 2.6f, out hit)) return null;
            var it = hit.collider.GetComponentInParent<Interactable>();
            return it != null && it.Available && it.use != null ? it : null;
        }

        // Boards indoors and on the porch; grass and leaves everywhere else outside.
        /// <summary>Every footstep you take (the house can answer them).</summary>
        public static System.Action<Vector3> Stepped;

        void Footstep()
        {
            Vector3 p = transform.position;
            if (Stepped != null) Stepped(p);
            var g = GameDirector.I;
            bool porch = Mathf.Abs(p.x) < 3.3f && p.z > 8f && p.z < 11.7f;
            bool outside = g != null && g.house != null && g.phase != Phase.Dream && g.house.Outside(p) && !porch;
            if (outside) Sfx.At("stepgrass", p, 0.3f, 0.85f + Random.value * 0.3f);
            else Sfx.At("step", p, 0.22f, 0.9f + Random.value * 0.2f);
        }

        /// <summary>Pick something up (E on it). E again puts it down where you're looking.</summary>
        public void PickUp(Movable m)
        {
            if (carrying != null || m == null) return;
            carrying = m;
            m.SetHeld(true);
            pickedFrame = Time.frameCount;
            Sfx.At(m.Heavy ? "putdown" : "pickup", m.transform.position, m.Heavy ? 0.35f : 0.45f, 0.9f + Random.value * 0.2f);
        }

        public void Drop()
        {
            if (carrying == null) return;
            carrying.SetHeld(false);
            Sfx.At("putdown", carrying.transform.position, 0.5f, 0.85f + Random.value * 0.3f);
            carrying = null;
        }

        void Carry(bool vr, float dt)
        {
            var m = carrying;
            if (m == null) return;
            // Light things are held up in front of you; furniture is dragged along the floor at arm's length.
            Vector3 origin = vr ? VRRig.I.AimOrigin : cam.transform.position;
            Vector3 aim = vr ? VRRig.I.AimDirection : cam.transform.forward;
            Vector3 want;
            if (m.Heavy) want = transform.position + Forward * 1.15f + Vector3.up * 0.03f;
            else
            {
                float reach = 1.0f;
                RaycastHit wall;
                if (Physics.Raycast(origin, aim, out wall, reach + 0.25f, ~0, QueryTriggerInteraction.Ignore)) reach = Mathf.Max(0.35f, wall.distance - 0.25f);
                want = origin + aim * reach - Vector3.up * 0.25f;
            }
            m.transform.position = Vector3.Lerp(m.transform.position, want, 1f - Mathf.Exp(-dt * 14f));
            if (!m.Heavy) m.transform.rotation = Quaternion.Slerp(m.transform.rotation, Quaternion.Euler(0f, transform.eulerAngles.y + 180f, 0f), dt * 6f);
        }

        int pickedFrame = -1;

        /// <summary>Called by VRRig with the headset's position and rotation in the play space.</summary>
        public void ApplyVRPose(Vector3 localPos, Quaternion localRot)
        {
            // "Everything slightly too large" lowers the eyes on a flat screen; in VR it lowers the whole play space.
            localPos.y += eyeCur - 1.65f;
            cam.transform.localPosition = localPos;
            cam.transform.localRotation = localRot;
        }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 before = transform.position;
            var hud = Hud.I;
            bool vr = VRRig.Active && VRRig.I != null;
            bool canAct = !frozen && Time.timeScale > 0f && (hud == null || hud.modal == Modal.None);
            eyeCur = Mathf.Lerp(eyeCur, crouched ? 0.72f : eyeHeight, dt * 2f);

            if (paralysed && !vr)
            {
                Vector2 look = In.Look * 0.8f;
                yaw = Mathf.Clamp(yaw + look.x, anchorYaw - 22f, anchorYaw + 22f);
                pitch = Mathf.Clamp(pitch - look.y, anchorPitch - 14f, anchorPitch + 14f);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            else if (canAct)
            {
                Vector3 dir;
                if (vr)
                {
                    yaw += VRRig.I.turnStep;
                    transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 f = Vis.Flat(cam.transform.forward), r = Vector3.Cross(Vector3.up, f);
                    Vector2 m = VRRig.MoveStick + In.Move;
                    dir = r * m.x + f * m.y;
                    lookSpeed = Mathf.Lerp(lookSpeed, VRRig.HeadAngularSpeed, 0.3f);
                }
                else
                {
                    Vector2 look = In.Look * 2.2f;
                    yaw += look.x;
                    pitch = Mathf.Clamp(pitch - look.y, -80f, 80f);
                    lookSpeed = Mathf.Lerp(lookSpeed, look.magnitude / dt, 0.3f);
                    transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    Vector2 m = In.Move;
                    dir = transform.right * m.x + transform.forward * m.y;
                }
                if (dir.sqrMagnitude > 1f) dir.Normalize();
                // In VR, walking is a little slower: it is far more comfortable.
                float speed = (In.Held(Btn.Sprint) ? 4.6f : 2.8f) * speedMul * (vr ? 0.8f : 1f) * (carrying != null && carrying.Heavy ? 0.55f : 1f);
                vy = cc.isGrounded ? -1f : vy - 18f * dt;
                cc.Move((dir * speed + Vector3.up * vy) * dt);
            }
            else lookSpeed = 0f;

            velocity = (transform.position - before) / dt;

            if (vr) ApplyVRPose(VRRig.I.headLocalPos, VRRig.I.headLocalRot);
            else
            {
                float grip = GameDirector.I != null ? GameDirector.I.grip : 1f;
                float loose = (1f - grip) * (1f - grip);
                cam.transform.localPosition = new Vector3(0f, eyeCur, 0f);
                cam.transform.localRotation = Quaternion.Euler(pitch, 0f, Mathf.Sin(Time.time * 0.7f) * loose * 5f + Roll);
                cam.fieldOfView = 68f + fovOffset + Mathf.Sin(Time.time * 0.9f) * loose * 4f;
            }

            Vector3 flat = velocity; flat.y = 0f;
            stepAcc += flat.magnitude * dt;
            if (stepAcc > 2.1f) { stepAcc = 0f; Footstep(); }

            if (hud != null) hud.prompt = "";
            if (carrying != null)
            {
                if (!canAct && frozen) Drop();
                else
                {
                    Carry(vr, dt);
                    if (hud != null) hud.prompt = "[E]  Put it down";
                    if (canAct && Time.frameCount != pickedFrame && In.Down(Btn.Interact)) Drop();
                }
            }
            else if (canAct)
            {
                // On a flat screen you use what you look at. In VR you use what your right hand points at.
                Vector3 origin = vr ? VRRig.I.AimOrigin : cam.transform.position;
                Vector3 aim = vr ? VRRig.I.AimDirection : cam.transform.forward;
                RaycastHit hit;
                if (UseRay(origin, aim, 2.6f, out hit))
                {
                    var it = hit.collider.GetComponentInParent<Interactable>();
                    if (it != null && it.Available && it.use != null)
                    {
                        if (hud != null) hud.prompt = "[E]  " + it.Prompt;
                        if (In.Down(Btn.Interact) && (hud == null || !hud.JustClosed)) it.use();
                    }
                }
            }
        }
    }
}
