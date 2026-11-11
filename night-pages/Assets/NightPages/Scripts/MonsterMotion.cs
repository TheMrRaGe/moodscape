using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// How the monster moves: made to read as real (weight, planted feet, the lag of dead limbs) and to be
    /// uncomfortable to be near (it doesn't move the way anything alive should).
    ///
    /// Real:
    ///  - Many-legged things walk on planted feet. Each leg is a hip-knee chain solved by two-bone IK to a foot that
    ///    stays put on the floor (or the wall, or the ceiling: wherever it's standing) until the body has moved on far
    ///    enough, then lifts, swings in an arc and plants again ahead, in alternating sets (a spider's two groups of
    ///    four, opposite corners together; an insect's tripods), so it is always standing on half its legs.
    ///  - Limbs hang with weight: arms swing late, like something dangling, when it starts and stops.
    ///  - It breathes, unevenly; now and then a breath catches and shudders.
    ///
    /// Uncomfortable (what makes stop-motion and a still figure in a doorway frightening: motion that isn't smooth,
    /// stillness that isn't rest, attention that is too steady):
    ///  - Looked at, it goes still. Not at rest: stopped, mid-pose, only its head following you. Look away and it
    ///    moves again at once.
    ///  - Unwatched, it sometimes moves in a stutter: a dozen poses a second instead of sixty, like film with frames
    ///    missing. (One way of walking, "like a film with frames missing", does it all the time.)
    ///  - It tilts its head at you slowly, the way a dog listens, holds it there, and snaps it upright.
    ///  - Its joints crack when it twitches.
    ///  - A spider idles by tapping one leg at a time; close to you it rears, front legs up and trembling.
    ///  - A mantis rocks side to side in jerks, like a leaf in wind, and strikes when you're in reach.
    ///  - On two legs it may limp, dragging one leg, the body dipping on it; or glide, legs hardly moving, as if
    ///    carried.
    /// </summary>
    public partial class Monster
    {
        class IKLeg
        {
            public Transform hip, knee, ankle;
            public Vector3 femurRest, tibiaRest, footRestDir;   // hip -> knee, knee -> ankle, ankle -> foot, each in its own joint's frame at rest
            public Vector3 footRest;                  // where the foot rests, in the monster's own space
            public Vector3 restP7Local;               // the foot at rest, in the leg root's space (to measure footRest once everything is placed)
            public Transform root;
            public float l1, l2, l3, reach, lift;
            public Vector3 foot, from, to;
            public bool placed, stepping, raised;
            public float stepT, stepDur, nextTap;
            public int group, index;
        }

        readonly List<IKLeg> ikLegs = new List<IKLeg>();
        int ikLeft, ikRight;

        // Watched, and how still that makes it (0 moving freely .. 1 stopped).
        float freezeW;
        bool watched;
        // Stop-motion: poses held between "frames".
        bool stutter;
        float stutterUntil, nextStutterRoll, nextFrameAt;
        Vector3 snapPos;
        Quaternion snapRot = Quaternion.identity;
        // Head tilt.
        float tiltAt = -1f, tiltSide = 1f, nextTilt;
        // Breath.
        float shudderUntil, nextShudder;
        // Dangling arms.
        float[] armAng = new float[0], armVel = new float[0];
        Vector3 lastVelMotion;
        Vector3 lastPosMotion;
        bool hasMotionPos;
        // The mantis's strike.
        float strikeAt = -10f, nextStrike;

        const int GaitLimp = 4, GaitGlide = 5, GaitStutter = 6;

        void AddIKLeg(Transform hipJ, Transform kneeJ, Transform ankleJ, Vector3 p0, Vector3 p3, Vector3 p5, Vector3 p7, Transform root, float reach, float kneeUp)
        {
            var leg = new IKLeg
            {
                hip = hipJ, knee = kneeJ, ankle = ankleJ, root = root,
                femurRest = p3 - p0, tibiaRest = p5 - p3, footRestDir = p7 - p5,
                l1 = (p3 - p0).magnitude, l2 = (p5 - p3).magnitude, l3 = (p7 - p5).magnitude,
                reach = reach, lift = Mathf.Max(0.08f, kneeUp * 0.45f),
                restP7Local = p7,
                index = ikLegs.Count,
            };
            // Alternating sets: on each side every other leg, the two sides out of step (L1 R2 L3 R4 / R1 L2 R3 L4).
            bool left = p0.x < 0f;
            int k = left ? ikLeft++ : ikRight++;
            leg.group = (k % 2) ^ (left ? 0 : 1);
            ikLegs.Add(leg);
        }

        void ResetMotion()
        {
            ikLegs.Clear(); ikLeft = ikRight = 0;
            armAng = new float[0]; armVel = new float[0];
            stutter = false; freezeW = 0f; tiltAt = -1f;
        }

        // ---------------------------------------------------------------- before the pose: watched? stuttering?

        /// <summary>Returns true if this frame is a held frame (stop-motion): nothing should move.</summary>
        bool MotionPre(float t, float dt)
        {
            int gait = look[Gait];
            // Watched: its head (or its middle) in your view, near enough to matter.
            watched = false;
            if (Player.I != null && Player.I.cam != null)
            {
                var cam = Player.I.cam;
                float d = (HeadPos - cam.transform.position).magnitude;
                watched = d < 16f && (Vis.Sees(cam, HeadPos, transform) || Vis.Sees(cam, transform.position + Vector3.up * 0.6f, transform));
            }
            // (Only while it stands: walking, it walks. The stillness is the stillness of something that has stopped.)
            freezeW = Mathf.MoveTowards(freezeW, watched && !Transforming && pace < 0.3f ? 1f : 0f, dt * (watched ? 2.5f : 7f));

            // Stop-motion: always for that gait; otherwise in short bursts, only while unwatched.
            if (gait == GaitStutter) stutter = true;
            else
            {
                if (t > nextStutterRoll)
                {
                    nextStutterRoll = t + Random.Range(3f, 6f);
                    if (!watched && Random.value < 0.22f) stutterUntil = t + Random.Range(0.8f, 2.2f);
                }
                stutter = t < stutterUntil && !watched;
            }
            if (!stutter || Transforming) return false;
            if (Time.time >= nextFrameAt)
            {
                nextFrameAt = Time.time + 1f / Random.Range(8f, 12f);      // a dozen poses a second, unevenly
                snapPos = transform.position;
                snapRot = transform.rotation;
                return false;
            }
            // Held: the body stays where the last "frame" left it, even while it's carried on underneath.
            rig.position = snapPos + snapRot * rig.localPosition;
            rig.rotation = snapRot * rig.localRotation;
            return true;
        }

        // ---------------------------------------------------------------- feet that stay where they're put

        void LegsIK(float dt, float go)
        {
            if (ikLegs.Count == 0) return;
            Vector3 pos = transform.position;
            Vector3 vel = hasMotionPos ? (pos - lastPosMotion) / Mathf.Max(dt, 1e-4f) : Vector3.zero;
            if (vel.magnitude > 8f) vel = Vector3.zero;
            lastPosMotion = pos; hasMotionPos = true;
            Vector3 up = transform.up;
            float speed = vel.magnitude;
            bool near = Player.I != null && (Player.I.transform.position - pos).magnitude < 2.7f;
            bool rear = near && speed < 0.25f && plan == SpiderLegs;
            foreach (var leg in ikLegs)
            {
                if (leg.hip == null || leg.knee == null) continue;
                if (!leg.placed)
                {
                    // The foot's resting place, pulled in toward the body: a leg at rest is bent, with slack to reach
                    // further (a foot put down at full stretch leaves nothing to walk with).
                    Vector3 restFoot = transform.InverseTransformPoint(leg.root.TransformPoint(leg.restP7Local));
                    Vector3 hipLocal = transform.InverseTransformPoint(leg.hip.position);
                    Vector3 inward = new Vector3(restFoot.x - hipLocal.x, 0f, restFoot.z - hipLocal.z);
                    leg.footRest = restFoot - inward * 0.04f;
                    leg.foot = transform.TransformPoint(leg.footRest);
                    leg.placed = true;
                    leg.nextTap = Time.time + Random.Range(2f, 7f);
                }
                Vector3 home = transform.TransformPoint(leg.footRest);
                // The front pair rear up when you're close: up, forward, trembling.
                bool front = plan == SpiderLegs && (leg.index % 4 == 0);
                // Never quite still: each foot shifts its grip a little, unevenly.
                float tn = Time.time * 0.6f + leg.index * 3.1f;
                Vector3 fidget = (transform.right * (Mathf.PerlinNoise(tn, 0.3f) - 0.5f) + transform.forward * (Mathf.PerlinNoise(0.7f, tn) - 0.5f)) * leg.reach * 0.08f;
                Vector3 target = home + fidget + vel * 0.25f;
                if (rear && front) target = home + up * leg.reach * 0.55f + transform.forward * leg.reach * 0.1f + Random.insideUnitSphere * 0.015f;
                if (!leg.stepping)
                {
                    float dist = (leg.foot - target).magnitude;
                    bool otherStepping = false;
                    foreach (var o in ikLegs) if (o.stepping && o.group != leg.group) { otherStepping = true; break; }
                    float thresh = leg.reach * (speed > 0.2f ? 0.17f : 0.1f);
                    if (rear && front) thresh = 0.04f;
                    // A leg pulled near straight (the body has gone on without it) steps now, whatever the others are doing.
                    bool strained = (leg.foot - leg.hip.position).magnitude > (leg.l1 + leg.l2 + leg.l3) * 0.86f || dist > leg.reach * 0.32f;
                    if (dist > leg.reach * 1.4f) { leg.foot = target; }                       // left behind (it was moved): catch up at once
                    else if (dist > thresh && (!otherStepping || strained || (rear && front)))
                    {
                        leg.stepping = true;
                        leg.from = leg.foot;
                        leg.to = target + vel * 0.12f;
                        leg.stepT = 0f;
                        leg.stepDur = Mathf.Lerp(0.26f, 0.11f, Mathf.Clamp01(speed / 3f));
                    }
                    else if (speed < 0.1f && !(rear && front) && Time.time > leg.nextTap)
                    {
                        // Idle, it taps: one leg lifted and put down again, a little further on. (Watched, less often.)
                        leg.nextTap = Time.time + Random.Range(1.5f, 5f) * (watched ? 1.8f : 1f);
                        leg.stepping = true; leg.from = leg.foot; leg.to = leg.foot + transform.forward * 0.04f; leg.stepT = 0f; leg.stepDur = 0.35f;
                    }
                }
                if (rear && front && !leg.stepping)
                    leg.foot = target + transform.forward * Mathf.Sin(Time.time * 7f + leg.index) * leg.reach * 0.05f + up * Mathf.Sin(Time.time * 11f) * 0.02f;
                if (leg.stepping)
                {
                    leg.stepT += dt / leg.stepDur;
                    float s = Mathf.Clamp01(leg.stepT);
                    float e = s * s * (3f - 2f * s);
                    leg.foot = Vector3.Lerp(leg.from, leg.to, e) + up * leg.lift * Mathf.Sin(Mathf.PI * s);
                    if (leg.stepT >= 1f)
                    {
                        leg.stepping = false;
                        leg.foot = leg.to;
                        if (Random.value < 0.35f) Sfx.At("tap", leg.foot, 0.12f, 1.8f + Random.value * 0.6f);
                    }
                }
                Solve(leg, leg.foot, up);
            }
        }

        // Three joints, solved the way a spider holds them: the last segments (metatarsus and tarsus) come down to the
        // foot steeply from outside-above, so the ankle goes there first; then the hip and knee are a two-bone problem
        // to reach the ankle, the knee put up off the line (toward `pole`), the way a spider's knees stand above its back;
        // then the ankle turns the last segments onto the foot. Every joint bends; no part of the leg is a stick.
        static void Solve(IKLeg leg, Vector3 foot, Vector3 pole)
        {
            leg.hip.localRotation = Quaternion.identity;
            leg.knee.localRotation = Quaternion.identity;
            if (leg.ankle != null) leg.ankle.localRotation = Quaternion.identity;
            Vector3 H = leg.hip.position;
            // Where the ankle wants to be: above the foot and a little inward (toward the body), the metatarsus at about
            // sixty degrees to the floor; nearer vertical when the foot is pulled in close, flatter when it's stretched.
            Vector3 flat = foot - H; flat -= pole * Vector3.Dot(flat, pole);
            Vector3 outward = flat.sqrMagnitude > 1e-6f ? flat.normalized : leg.hip.forward;
            float reachNow = Mathf.Clamp01(flat.magnitude / Mathf.Max(0.01f, leg.l1 + leg.l2 + leg.l3));
            float slope = Mathf.Lerp(72f, 42f, reachNow) * Mathf.Deg2Rad;
            Vector3 A = foot - outward * Mathf.Cos(slope) * leg.l3 + pole * Mathf.Sin(slope) * leg.l3;
            // Hip and knee to the ankle.
            Vector3 toA = A - H;
            float D = Mathf.Clamp(toA.magnitude, Mathf.Abs(leg.l1 - leg.l2) + 0.01f, leg.l1 + leg.l2 - 0.002f);
            Vector3 dir = toA.sqrMagnitude > 1e-8f ? toA.normalized : -pole;
            Vector3 bend = pole - dir * Vector3.Dot(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.Cross(dir, Vector3.right);
            bend.Normalize();
            float a = (leg.l1 * leg.l1 - leg.l2 * leg.l2 + D * D) / (2f * D);
            float h = Mathf.Sqrt(Mathf.Max(0f, leg.l1 * leg.l1 - a * a));
            Vector3 K = H + dir * a + bend * h;
            Vector3 curFemur = leg.hip.TransformDirection(leg.femurRest);
            leg.hip.rotation = Quaternion.FromToRotation(curFemur, K - H) * leg.hip.rotation;
            Vector3 Aat = H + dir * D;
            Vector3 curTibia = leg.knee.TransformDirection(leg.tibiaRest);
            leg.knee.rotation = Quaternion.FromToRotation(curTibia, Aat - leg.knee.position) * leg.knee.rotation;
            // The last segments onto the foot.
            if (leg.ankle != null)
            {
                Vector3 curFoot = leg.ankle.TransformDirection(leg.footRestDir);
                leg.ankle.rotation = Quaternion.FromToRotation(curFoot, foot - leg.ankle.position) * leg.ankle.rotation;
            }
        }

        // ---------------------------------------------------------------- the rest of it

        /// <summary>How much its breath moves it this frame (multiplies the body's height).</summary>
        float Breath(float t)
        {
            if (t > nextShudder)
            {
                nextShudder = t + Random.Range(6f, 14f);
                shudderUntil = t + Random.Range(0.4f, 0.8f);
                if (Player.I != null && (Player.I.transform.position - transform.position).magnitude < 5f)
                    Sfx.At("breath", HeadPos, 0.35f, 0.75f + Random.value * 0.15f);
            }
            float b = 0.018f * Mathf.Sin(t * 1.1f) + 0.008f * Mathf.Sin(t * 2.7f + 1.3f);
            if (t < shudderUntil) b += 0.012f * Mathf.Sin(t * 85f);       // the breath catching
            return 1f + b;
        }

        /// <summary>Its head's slow tilt at you (degrees of roll), and the snap back.</summary>
        float Tilt(float t, bool attending)
        {
            if (tiltAt < 0f)
            {
                if (attending && t > nextTilt) { tiltAt = t; tiltSide = Random.value < 0.5f ? -1f : 1f; }
                return 0f;
            }
            float u = t - tiltAt;
            float roll;
            if (u < 1.6f) roll = Mathf.SmoothStep(0f, 1f, u / 1.6f);          // slowly over
            else if (u < 3.0f) roll = 1f;                                     // held
            else if (u < 3.1f) roll = 1f - (u - 3.0f) / 0.1f;                 // snapped back
            else { tiltAt = -1f; nextTilt = t + Random.Range(4f, 10f); roll = 0f; }
            return roll * 34f * tiltSide;
        }

        /// <summary>Each arm's dangling lag (degrees of pitch), from how the body speeds up and slows down.</summary>
        float ArmLag(int i, float dt, Vector3 accelLocal)
        {
            if (armAng.Length != arms.Count) { armAng = new float[arms.Count]; armVel = new float[arms.Count]; }
            // A loose pendulum: pushed back when it speeds up, swinging forward when it stops, settling slowly.
            float force = -accelLocal.z * 9f + accelLocal.x * (i % 2 == 0 ? 3f : -3f);
            armVel[i] += (-28f * armAng[i] - 3.2f * armVel[i] + force) * dt;
            armAng[i] = Mathf.Clamp(armAng[i] + armVel[i] * dt, -35f, 35f);
            return armAng[i];
        }

        Vector3 BodyAccel(float dt)
        {
            Vector3 v = (transform.position - lastPosArms) / Mathf.Max(dt, 1e-4f);
            lastPosArms = transform.position;
            if (v.magnitude > 8f) v = Vector3.zero;
            Vector3 a = (v - lastVelMotion) / Mathf.Max(dt, 1e-4f);
            lastVelMotion = v;
            if (a.magnitude > 60f) a = Vector3.zero;
            return transform.InverseTransformDirection(a);
        }
        Vector3 lastPosArms;

        /// <summary>The mantis's strike: forelegs flung out and back. Returns the extra pitch for its arms.</summary>
        float Strike(float t)
        {
            if (look[Arms] != 6) return 0f;
            if (t > nextStrike && Player.I != null && (Player.I.transform.position - transform.position).magnitude < 2.3f)
            {
                strikeAt = t;
                nextStrike = t + Random.Range(3f, 6f);
                Sfx.At("crack", HeadPos, 0.6f, 1.3f);
            }
            float u = t - strikeAt;
            if (u < 0f || u > 0.7f) return 0f;
            return u < 0.08f ? -80f * (u / 0.08f) : u < 0.2f ? -80f : -80f * (1f - (u - 0.2f) / 0.5f);
        }

        /// <summary>The body's own motion this frame: a mantis rocking like a leaf, a limp's dip.</summary>
        Quaternion BodyRock(float t, float go)
        {
            var q = Quaternion.identity;
            if (look[Arms] == 6 && go < 0.5f)
            {
                // In jerks: the rock is stepped, three positions, never smooth.
                float r = Mathf.Round(Mathf.Sin(t * 2.4f) * 1.5f) / 1.5f;
                q *= Quaternion.Euler(0f, 0f, r * 6f);
            }
            if (look[Gait] == GaitLimp && plan == TwoLegs) q *= Quaternion.Euler(0f, 0f, Mathf.Max(0f, Mathf.Sin(stride + Mathf.PI)) * 9f * go);
            return q;
        }
    }
}
