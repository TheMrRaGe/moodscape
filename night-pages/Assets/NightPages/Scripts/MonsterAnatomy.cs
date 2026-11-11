using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The monster's limbs, built from real anatomy instead of sticks.
    ///
    /// A human arm: the deltoid capping the shoulder; the upper arm tapering to the elbow with the biceps in front
    /// and the triceps behind; the point of the elbow; the forearm fullest just below the elbow (the flexor mass) and
    /// thinning to a flat wrist (forearm about 5/6 of the upper arm); a hand with a padded palm, the ball of the
    /// thumb, knuckles, and fingers in three tapering bones each (about 45 : 30 : 25 of the finger's length; the
    /// index 0.9, ring 0.95 and little finger 0.75 of the middle), curled a little, with nails.
    ///
    /// A human leg: the thigh with the quadriceps in front and the hamstrings behind, the kneecap, the shin and the
    /// calf high at the back, the ankle bones, and a foot with a heel, an arch and toes.
    ///
    /// A spider's leg: seven segments (coxa, trochanter, femur, patella, tibia, metatarsus, tarsus), the femur rising
    /// steeply so the knee stands above the body, the rest coming down and out to a foot with claws, every joint a
    /// hard seam with a slight swelling, and stiff hairs along the lower segments.
    ///
    /// A mantis's raptorial foreleg: a long coxa hanging from the shoulder, a femur like a thick flat blade with a
    /// double row of spines underneath, and the tibia folded back up against it like a jackknife, spined too, ending
    /// in a hook; held up in front, "praying".
    ///
    /// The shapes are FleshShapes (tapered segments, true ellipsoids, rounded boxes) so the melted flesh carries the
    /// muscle and the joints. Hands, spider legs and mantis arms are melted on their own, finer, so the fingers and
    /// spines survive.
    /// </summary>
    public partial class Monster
    {
        Material limbSkin;
        // Each finely-melted part: its root, and the grid size it's melted at.
        readonly List<KeyValuePair<Transform, float>> fine = new List<KeyValuePair<Transform, float>>();

        Material LimbSkin
        {
            get
            {
                if (limbSkin == null) { limbSkin = new Material(skin); limbSkin.name = skin.name + " (limbs)"; }
                return limbSkin;
            }
        }

        // ---------------------------------------------------------------- shapes

        // A segment tapering from radius ra at a to rb at b.
        Transform Seg(Transform parent, string name, Vector3 a, Vector3 b, float ra, float rb, float blend = 1f, Material m = null)
        {
            float len = Mathf.Max(0.001f, (b - a).magnitude), rmax = Mathf.Max(ra, rb);
            var g = Build.Prim(PrimitiveType.Capsule, name, parent, (a + b) / 2f, new Vector3(rmax * 2f, len / 2f + rmax, rmax * 2f), m ?? skin, false);
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a) / len);
            var s = g.AddComponent<FleshShape>();
            s.kind = FleshShape.K.RoundCone; s.halfLen = len / 2f; s.r0 = ra; s.r1 = rb; s.blend = blend;
            return g.transform;
        }

        // An ellipsoid (diameters `size`), turned by `rot`.
        Transform Ell(Transform parent, string name, Vector3 at, Vector3 size, Quaternion rot, float blend = 1f, Material m = null)
        {
            var g = Build.Prim(PrimitiveType.Sphere, name, parent, at, size, m ?? skin, false);
            g.transform.localRotation = rot;
            var s = g.AddComponent<FleshShape>();
            s.kind = FleshShape.K.Ellipsoid; s.blend = blend;
            return g.transform;
        }

        // A box with rounded edges.
        Transform RBox(Transform parent, string name, Vector3 at, Vector3 size, float round, Quaternion rot, float blend = 1f, Material m = null)
        {
            var g = Build.Prim(PrimitiveType.Cube, name, parent, at, size, m ?? skin, false);
            g.transform.localRotation = rot;
            var s = g.AddComponent<FleshShape>();
            s.kind = FleshShape.K.RoundBox; s.round = Mathf.Min(round, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.49f); s.blend = blend;
            return g.transform;
        }

        // ---------------------------------------------------------------- a human arm and hand

        // An arm hanging from a pivot at the shoulder: rotate the pivot and the whole arm swings from its top.
        Transform Limb(string name, Transform parent, Vector3 at, float length, float thick)
        {
            float side = at.x < 0f ? -1f : 1f;
            var pivot = Node(name, parent, at);
            float upper = length * 0.45f, fore = upper * 0.85f, handLen = length * 0.2f;
            float r = thick * 0.6f * (Bare ? 0.85f : 1f);           // the gaunt ones are all tendon
            // The shoulder cap, and the upper arm with its two muscles.
            Ell(pivot, "Deltoid", new Vector3(side * r * 0.25f, -upper * 0.1f, 0f), new Vector3(r * 2.7f, upper * 0.45f, r * 2.5f), Quaternion.Euler(0f, 0f, side * 8f));
            Seg(pivot, "Upper arm", Vector3.zero, new Vector3(0f, -upper, 0f), r * 0.95f, r * 0.7f);
            Ell(pivot, "Biceps", new Vector3(0f, -upper * 0.52f, r * 0.38f), new Vector3(r * 1.55f, upper * 0.55f, r * 1.35f), Quaternion.identity);
            Ell(pivot, "Triceps", new Vector3(0f, -upper * 0.42f, -r * 0.4f), new Vector3(r * 1.6f, upper * 0.62f, r * 1.4f), Quaternion.identity);
            var elbow = Node("Elbow", pivot, new Vector3(0f, -upper, 0f));
            elbow.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            Ell(elbow, "Elbow point", new Vector3(0f, 0.01f, -r * 0.5f), Vector3.one * r * 0.85f, Quaternion.identity, 0.6f);
            Ell(elbow, "Epicondyle", new Vector3(-side * r * 0.6f, 0.005f, 0f), Vector3.one * r * 0.6f, Quaternion.identity, 0.6f);
            // The forearm: fullest just below the elbow, flat and narrow at the wrist.
            Seg(elbow, "Forearm", Vector3.zero, new Vector3(0f, -fore, 0f), r * 0.82f, r * 0.48f);
            Ell(elbow, "Flexors", new Vector3(side * -r * 0.12f, -fore * 0.27f, r * 0.12f), new Vector3(r * 1.75f, fore * 0.55f, r * 1.5f), Quaternion.identity);
            Ell(elbow, "Wrist", new Vector3(0f, -fore * 0.95f, 0f), new Vector3(r * 0.85f, fore * 0.14f, r * 1.15f), Quaternion.identity, 0.8f);
            var wrist = Node("Wrist", elbow, new Vector3(0f, -fore, 0f));
            Hand(wrist, r, handLen, side);
            return pivot;
        }

        // A palm and four fingers, slightly curled, and a thumb; or needles instead, if it has those.
        // The palm faces the body (thumb forward, the way arms hang), and the hand is melted on its own, finely.
        void Hand(Transform wrist, float r, float handLen, float side)
        {
            var mat = LimbSkin;
            var root = Node("Hand", wrist, Vector3.zero);
            float palmLen = handLen * 0.53f, palmW = handLen * 0.46f, palmT = palmW * 0.3f;
            // The palm: a padded slab, its width front to back.
            RBox(root, "Palm", new Vector3(0f, -palmLen * 0.5f, 0f), new Vector3(palmT, palmLen, palmW), palmT * 0.45f, Quaternion.identity, 1f, mat);
            Ell(root, "Ball of the thumb", new Vector3(-side * palmT * 0.3f, -palmLen * 0.38f, palmW * 0.28f), new Vector3(palmT * 1.0f, palmLen * 0.62f, palmW * 0.45f), Quaternion.identity, 1f, mat);
            Ell(root, "Heel of the hand", new Vector3(-side * palmT * 0.25f, -palmLen * 0.5f, -palmW * 0.3f), new Vector3(palmT * 0.8f, palmLen * 0.6f, palmW * 0.3f), Quaternion.identity, 1f, mat);
            if (extraG == XNeedles) { fine.Add(new KeyValuePair<Transform, float>(root, 0.007f)); return; }
            float[] lens = { 0.9f, 1f, 0.95f, 0.75f };            // index, middle, ring, little
            float fingerLen = handLen * 0.47f;
            var nail = M("nail", new Color(0.62f, 0.55f, 0.48f), 0f, 0.45f);
            for (int i = 0; i < 4; i++)
            {
                float z = palmW * (0.36f - i * 0.24f);
                float len = fingerLen * lens[i] * (look[Arms] == 1 ? 1.35f : 1f);       // the long-armed one: long fingers
                float fr = palmT * 0.36f * (i == 3 ? 0.85f : 1f);
                Vector3 knuckle = new Vector3(0f, -palmLen, z * 0.95f);
                Ell(root, "Knuckle", knuckle + new Vector3(side * palmT * 0.18f, 0.004f, 0f), Vector3.one * fr * 2.1f, Quaternion.identity, 0.8f, mat);
                // Three bones each, a little more bent at each joint (relaxed), toward the palm.
                float[] part = { 0.45f, 0.3f, 0.25f };
                Vector3 p = knuckle;
                float bend = 0f;
                float rad = fr;
                for (int k = 0; k < 3; k++)
                {
                    bend += (k == 0 ? 12f : 20f) + (look[Arms] == 3 ? -6f : 4f);           // reaching: straighter
                    Vector3 dir = Quaternion.Euler(0f, 0f, -side * bend) * Vector3.down;
                    Vector3 q = p + dir * len * part[k];
                    float rEnd = rad * (k == 2 ? 0.72f : 0.88f);
                    Seg(root, "Finger", p, q, rad, rEnd, 0.7f, mat);
                    if (k < 2) Ell(root, "Finger joint", q, Vector3.one * rEnd * 2.15f, Quaternion.identity, 0.6f, mat);
                    else
                    {
                        // The nail, on the back of the last bone.
                        var n = Build.Prim(PrimitiveType.Cube, "Nail", root, (p + q) / 2f + Quaternion.Euler(0f, 0f, -side * bend) * new Vector3(side * rEnd * 0.85f, 0f, 0f), new Vector3(0.002f, len * part[k] * 0.6f, rEnd * 1.5f), nail, false);
                        n.transform.localRotation = Quaternion.Euler(0f, 0f, -side * bend);
                    }
                    p = q; rad = rEnd;
                }
            }
            // The thumb: from the base of the palm, forward and down, two bones.
            Vector3 tb = new Vector3(-side * palmT * 0.15f, -palmLen * 0.2f, palmW * 0.45f);
            Vector3 t1 = tb + new Vector3(-side * palmT * 0.5f, -fingerLen * 0.42f, fingerLen * 0.22f);
            Vector3 t2 = t1 + new Vector3(-side * palmT * 0.4f, -fingerLen * 0.3f, fingerLen * 0.1f);
            Seg(root, "Thumb", tb, t1, palmT * 0.5f, palmT * 0.4f, 0.8f, mat);
            Seg(root, "Thumb", t1, t2, palmT * 0.4f, palmT * 0.3f, 0.7f, mat);
            fine.Add(new KeyValuePair<Transform, float>(root, 0.0065f));
        }

        // ---------------------------------------------------------------- a human leg

        // A leg from the hip: the thigh and its muscles, the kneecap, the shin and calf, the ankle, the foot.
        // Jointed at the hip and the knee, so it can walk.
        void Leg(Transform parent, float x, float legs, float thick)
        {
            float side = x < 0f ? -1f : 1f;
            Vector3 hip = new Vector3(x, legs, 0f), knee = new Vector3(x * 1.05f, legs * 0.5f, 0.05f), ankle = new Vector3(x, 0.08f, -0.02f);
            var hipJ = Node("Hip", parent, hip);
            var kneeJ = Node("Knee joint", hipJ, knee - hip);
            float r = thick * 0.62f * (Bare ? 0.82f : 1f);
            Vector3 k = knee - hip;
            // The thigh: thick at the top, the quadriceps in front (fullest low, just above the knee on the inside),
            // the hamstrings behind.
            Seg(hipJ, "Thigh", Vector3.zero, k, r * 1.15f, r * 0.72f);
            Ell(hipJ, "Quadriceps", k * 0.45f + new Vector3(0f, 0f, r * 0.42f), new Vector3(r * 1.8f, k.magnitude * 0.7f, r * 1.5f), Quaternion.FromToRotation(Vector3.up, k.normalized));
            Ell(hipJ, "Vastus", k * 0.78f + new Vector3(-side * r * 0.3f, 0f, r * 0.35f), new Vector3(r * 1.1f, k.magnitude * 0.3f, r * 1.0f), Quaternion.identity);
            Ell(hipJ, "Hamstrings", k * 0.5f + new Vector3(0f, 0f, -r * 0.4f), new Vector3(r * 1.6f, k.magnitude * 0.62f, r * 1.3f), Quaternion.FromToRotation(Vector3.up, k.normalized));
            // The knee: the kneecap in front, the hollow behind.
            Ell(kneeJ, "Kneecap", new Vector3(0f, 0.01f, r * 0.62f), new Vector3(r * 0.95f, r * 1.1f, r * 0.55f), Quaternion.identity, 0.6f);
            Vector3 a = ankle - knee;
            Seg(kneeJ, "Shin", Vector3.zero, a, r * 0.78f, r * 0.42f);
            Ell(kneeJ, "Calf", a * 0.3f + new Vector3(0f, 0f, -r * 0.42f), new Vector3(r * 1.5f, a.magnitude * 0.45f, r * 1.3f), Quaternion.FromToRotation(Vector3.up, a.normalized));
            Ell(kneeJ, "Ankle bone", a + new Vector3(side * r * 0.38f, 0.02f, 0f), Vector3.one * r * 0.5f, Quaternion.identity, 0.6f);
            Ell(kneeJ, "Ankle bone", a + new Vector3(-side * r * 0.36f, 0.03f, 0f), Vector3.one * r * 0.5f, Quaternion.identity, 0.6f);
            // The foot: heel, the arch, the ball, toes.
            Vector3 heel = new Vector3(x, 0.045f, -0.06f) - knee, ball = new Vector3(x, 0.035f, 0.13f) - knee, toes = new Vector3(x, 0.022f, 0.2f) - knee;
            Ell(kneeJ, "Heel", heel, new Vector3(r * 1.0f, 0.075f, r * 1.1f), Quaternion.identity);
            Seg(kneeJ, "Foot", heel + Vector3.up * 0.02f, ball, r * 0.62f, r * 0.55f);
            Ell(kneeJ, "Ball of the foot", ball, new Vector3(r * 1.35f, 0.06f, r * 0.95f), Quaternion.identity);
            Seg(kneeJ, "Toes", ball, toes, r * 0.42f, r * 0.3f, 0.8f);
            hips.Add(hipJ);
            knees.Add(kneeJ);
        }

        // ---------------------------------------------------------------- a spider's leg

        /// <summary>
        /// One spider leg from `hip` reaching out along `dir` (flat), `reach` far, its knee `kneeUp` above the hip.
        /// Built under `parent`; the leg's root is returned (melted on its own).
        /// </summary>
        Transform SpiderLeg(Transform parent, Vector3 hip, Vector3 dir, float reach, float kneeUp, float thick, float floorY = 0f, bool walks = true)
        {
            var mat = LimbSkin;
            var root = Node("Spider leg", parent, Vector3.zero);
            dir = Vis.Flat(dir).normalized;
            Vector3 up = Vector3.up;
            // The seven segments' joints, from the body out.
            Vector3 p0 = hip;
            Vector3 p1 = p0 + dir * reach * 0.05f + up * kneeUp * 0.04f;                // coxa
            Vector3 p2 = p1 + dir * reach * 0.03f + up * kneeUp * 0.04f;                // trochanter
            Vector3 p3 = p2 + dir * reach * 0.24f + up * kneeUp * 0.92f;                // femur, rising steeply
            Vector3 p4 = p3 + dir * reach * 0.12f + up * kneeUp * 0.04f;                // patella: the knee, over the top
            Vector3 p6 = new Vector3(0f, floorY + thick * 0.9f, 0f) + Vis.Level(hip) + dir * reach * 0.86f;
            Vector3 p5 = Vector3.Lerp(p4, p6, 0.52f) + dir * reach * 0.05f + up * kneeUp * 0.08f;   // tibia, down and out
            Vector3 p7 = new Vector3(0f, floorY + thick * 0.3f, 0f) + Vis.Level(hip) + dir * reach;  // tarsus, to the floor
            Vector3[] pts = { p0, p1, p2, p3, p4, p5, p6, p7 };
            // Jointed for real: the hip carries the coxa to the femur; the knee (at the top of the femur) the patella and
            // tibia; the ankle (at the end of the tibia) the rest. So the feet can stay planted while it walks (see Motion).
            var hipJ = Node("Leg hip", root, p0);
            var kneeJ = Node("Leg knee", hipJ, p3 - p0);
            var ankleJ = Node("Leg ankle", kneeJ, p5 - p3);
            System.Func<int, Transform> holder = i => i <= 2 ? hipJ : i <= 4 ? kneeJ : ankleJ;
            System.Func<int, Vector3> origin = i => i <= 2 ? p0 : i <= 4 ? p3 : p5;
            float[] rad = { 1f, 0.92f, 0.9f, 0.84f, 0.7f, 0.6f, 0.42f, 0.3f, 0.22f };
            string[] names = { "Coxa", "Trochanter", "Femur", "Patella", "Tibia", "Metatarsus", "Tarsus" };
            float r = thick * 0.5f;
            var hair = M("setae", new Color(0.05f, 0.04f, 0.035f), 0f, 0.2f);
            for (int i = 0; i < 7; i++)
            {
                var h0 = holder(i);
                Vector3 o = origin(i);
                Vector3 a = pts[i] - o, b = pts[i + 1] - o;
                // Each segment fuller in its first third (where its muscle is), narrower at the far joint.
                float ra = r * rad[i], rb = r * rad[i + 1];
                Seg(h0, names[i], a, b, ra, rb, 0.3f, mat);
                if (i == 2 || i == 4) Ell(h0, names[i] + " muscle", Vector3.Lerp(a, b, 0.35f), new Vector3(ra * 2.3f, (b - a).magnitude * 0.55f, ra * 2.3f), Quaternion.FromToRotation(Vector3.up, (b - a).normalized), 0.8f, mat);
                // A hard joint: a slight swelling, a seam.
                if (i > 0) Ell(h0, "Joint", a, Vector3.one * ra * 2.25f, Quaternion.identity, 0.35f, mat);
                // Stiff hairs on the lower segments, sticking out and forward.
                if (i >= 4)
                {
                    int n = i == 4 ? 7 : 5;
                    Vector3 along = (b - a);
                    for (int hh = 0; hh < n; hh++)
                    {
                        float u = (hh + 0.5f) / n;
                        Vector3 at = a + along * u;
                        float ang = hh * 137.5f;
                        Vector3 outward = Quaternion.AngleAxis(ang, along.normalized) * Vector3.Cross(along, Vector3.up).normalized;
                        float hl = thick * (0.9f + 0.5f * Mathf.Sin(hh * 2.3f));
                        var seta = Build.Prim(PrimitiveType.Cylinder, "Seta", h0, at + outward * (rb + hl * 0.45f), new Vector3(0.0035f, hl * 0.5f, 0.0035f), hair, false);
                        seta.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (outward + along.normalized * 0.6f).normalized);
                    }
                }
            }
            // Two claws at the tip.
            for (int c = -1; c <= 1; c += 2)
            {
                Vector3 side = Vector3.Cross(dir, Vector3.up).normalized * c;
                Seg(ankleJ, "Claw", p7 - p5, p7 - p5 + dir * thick * 0.5f + side * thick * 0.25f - up * thick * 0.25f, r * 0.2f, r * 0.05f, 0.3f, mat);
            }
            fine.Add(new KeyValuePair<Transform, float>(root, Mathf.Clamp(Mathf.Max(thick * 0.18f, reach * 0.0075f), 0.006f, 0.016f)));
            if (walks) AddIKLeg(hipJ, kneeJ, ankleJ, p0, p3, p5, p7, root, reach, kneeUp);
            return root;
        }

        // ---------------------------------------------------------------- a mantis's foreleg

        /// <summary>A raptorial foreleg hanging from a shoulder pivot, folded and held up in front.</summary>
        Transform MantisArm(Transform parent, Vector3 at, float length, float side)
        {
            var mat = LimbSkin;
            var pivot = Node("Arm", parent, at);
            var root = Node("Mantis foreleg", pivot, Vector3.zero);
            float L = length * 1.05f, w = L * 0.085f;                           // the femur's depth (it's a blade: deep, thin)
            // Where it joins the body: a socket of hard shell sunk into the shoulder, so the leg grows out of it and
            // doesn't hang beside it.
            Ell(root, "Socket", new Vector3(-side * w * 0.5f, 0f, 0f), new Vector3(w * 2.2f, w * 1.7f, w * 1.9f), Quaternion.identity, 0.5f, mat);
            // The coxa: long, hanging forward and down from the shoulder (about as long as the femur).
            Vector3 c0 = new Vector3(-side * w * 0.2f, 0f, 0f), c1 = new Vector3(side * w * 0.3f, -L * 0.36f, L * 0.16f);
            Seg(root, "Coxa", c0, c1, w * 0.75f, w * 0.55f, 0.35f, mat);
            Ell(root, "Trochanter", c1, Vector3.one * w * 0.95f, Quaternion.identity, 0.35f, mat);
            // The femur: up and forward from the trochanter, a thick flat blade, spined underneath in two rows.
            Vector3 f1 = c1 + new Vector3(0f, L * 0.24f, L * 0.3f);
            Vector3 fd = (f1 - c1).normalized;
            RBox(root, "Femur", (c1 + f1) / 2f, new Vector3(w * 0.55f, (f1 - c1).magnitude, w * 1.25f), w * 0.22f, Quaternion.FromToRotation(Vector3.up, fd), 0.3f, mat);
            Ell(root, "Femur muscle", Vector3.Lerp(c1, f1, 0.35f), new Vector3(w * 0.75f, (f1 - c1).magnitude * 0.6f, w * 1.45f), Quaternion.FromToRotation(Vector3.up, fd), 0.6f, mat);
            Vector3 below = Quaternion.FromToRotation(Vector3.up, fd) * new Vector3(0f, 0f, -1f);       // the inner (grasping) edge
            for (int row = -1; row <= 1; row += 2)
                for (int i = 0; i < 6; i++)
                {
                    float u = 0.2f + i * 0.13f;
                    Vector3 s0 = Vector3.Lerp(c1, f1, u) + below * w * 0.55f + Vector3.right * side * row * w * 0.18f;
                    float sl = w * (i % 2 == 0 ? 0.75f : 0.45f);
                    Seg(root, "Spine", s0, s0 + below * sl + fd * sl * 0.35f, w * 0.12f, w * 0.02f, 0.25f, mat);
                }
            // The tibia: folded back down along the femur's spined edge, spined too, ending in a hook.
            Ell(root, "Knee", f1, Vector3.one * w * 0.9f, Quaternion.identity, 0.35f, mat);
            Vector3 t1 = f1 - fd * L * 0.27f + below * w * 1.15f;
            Vector3 td = (t1 - f1).normalized;
            Seg(root, "Tibia", f1, t1, w * 0.42f, w * 0.28f, 0.3f, mat);
            Vector3 tBelow = -below;
            for (int i = 0; i < 5; i++)
            {
                Vector3 s0 = Vector3.Lerp(f1, t1, 0.25f + i * 0.15f) + tBelow * w * 0.22f;
                Seg(root, "Spine", s0, s0 + tBelow * w * 0.38f + td * w * 0.1f, w * 0.09f, w * 0.015f, 0.25f, mat);
            }
            Seg(root, "Hook", t1, t1 + td * w * 0.7f + below * w * 0.55f, w * 0.18f, w * 0.03f, 0.25f, mat);
            // The tarsus: a thin jointed foot hanging from the end of the tibia.
            Vector3 ta = t1 + td * w * 0.2f, tb = ta + Vector3.down * L * 0.12f + Vector3.forward * L * 0.02f;
            Seg(root, "Tarsus", ta, tb, w * 0.13f, w * 0.08f, 0.3f, mat);
            fine.Add(new KeyValuePair<Transform, float>(root, Mathf.Clamp(w * 0.12f, 0.005f, 0.01f)));
            return pivot;
        }

        /// <summary>Melt the finely-made parts (hands, spider legs, mantis arms), each on its own grid.</summary>
        void MeltFine(float rough)
        {
            foreach (var kv in fine)
            {
                if (kv.Key == null) continue;
                try { Flesh.Skin(kv.Key, LimbSkin, kv.Value, kv.Value * 1.4f, rough * 0.4f, "Flesh (" + kv.Key.name.ToLowerInvariant() + ")"); }
                catch (System.Exception e) { Debug.LogWarning("[NightPages] couldn't melt " + kv.Key.name + ": " + e.Message); }
            }
        }
    }
}
