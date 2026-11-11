using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The antagonist. It has no fixed shape: it is a genome. What it stands on (two legs, a spider's legs, four
    /// legs, a tail, its hands, a carpet of insects, dozens of legs), its body, its skin, its head, its arms, its
    /// face, the way it holds itself, and what else is wrong with it are all separate genes, each tagged with the
    /// kinds of fear it plays on (see FearTypes). "What it is like" (a person, a spider, a doll, a clown...) is only
    /// the default for genes the fear engine hasn't set, so the engine can build a doll's head on a spider's legs
    /// in Grandma's cardigan with needles for fingers if that is what frightens this player. The pieces are melted
    /// into one body of flesh (Flesh.cs), so whatever it becomes looks like it grew that way.
    /// It changes while you look away, or, if the engine finds that works better on you, in front of you.
    /// </summary>
    public class Monster : MonoBehaviour
    {
        public const int Body = 0, Head = 1, Arms = 2, Face = 3, Gait = 4, Kind = 5, Shift = 6, Legs = 7, Skin = 8, HeadKind = 9, Extra = 10, Traits = 11;

        /// <summary>What it resembles. The order matches the "what it is like" slot in FearEngine.</summary>
        public const int Person = 0, Spider = 1, Doll = 2, Clown = 3, Snake = 4, Hound = 5, Swarm = 6, Holes = 7,
            Drowned = 8, Doctor = 9, Her = 10, You = 11, Shadow = 12;

        // Gene values (the option order of the matching FearEngine slots; 0 always means "as its kind").
        public const int TwoLegs = 1, SpiderLegs = 2, FourLegs = 3, Tail = 4, OnHands = 5, Carpet = 6, Centipede = 7;
        const int SDark = 1, SHoles = 2, SDrowned = 3, SPorcelain = 4, SPaint = 5, SGown = 6, SCardigan = 7, SCoat = 8, SShell = 9, SScales = 10, SFur = 11, SShadow = 12;
        const int HBare = 1, HDoll = 2, HClown = 3, HSpider = 4, HSnake = 5, HDog = 6, HMask = 7, HGrandma = 8, HYou = 9, HWet = 10;
        const int XNeedles = 1, XArmHoles = 2, XCrawling = 3, XDrips = 4, XChestFace = 5, XStrings = 6, XRibs = 7;

        public int[] look = new int[Traits];
        public float height = 2.1f;

        // The genes as resolved for this body.
        int plan, skinG, headG, extraG;
        bool torsoOn;

        int[] wanted;
        float morphT = -1f;
        bool morphSwapped;
        readonly List<Transform> chain = new List<Transform>();     // a tail, or a snake
        /// <summary>True if the last change happened in plain view.</summary>
        public bool changedInView;
        Transform rig, torso, neck, headT;
        readonly List<Transform> arms = new List<Transform>();
        readonly List<Quaternion> armRest = new List<Quaternion>();
        readonly List<Transform> legPivots = new List<Transform>();
        readonly List<Transform> hips = new List<Transform>(), knees = new List<Transform>();
        // Walking: worked out from how it actually moves, whoever moves it.
        Vector3 lastPos;
        bool hasLast;
        float pace, stride;
        /// <summary>Does it crawl (spider, centipede, on its hands, a carpet of things)? Those can go up walls and across ceilings.</summary>
        public bool Crawler { get { return plan == SpiderLegs || plan == Centipede || plan == OnHands || plan == Carpet; } }

        /// <summary>How fast it's going, metres a second (smoothed).</summary>
        public float Pace { get { return pace; } }
        readonly List<Transform> ribs = new List<Transform>();
        readonly List<Vector3> ribRest = new List<Vector3>();
        Quaternion neckRest = Quaternion.identity;
        float twitchUntil, nextTwitch, nextDrip, seed, tailRise = 1.65f;
        Material skin, headSkin;
        readonly List<Transform> swarm = new List<Transform>();
        readonly List<Vector3> swarmHome = new List<Vector3>();
        readonly List<Renderer> bodyRends = new List<Renderer>(), eyeRends = new List<Renderer>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        public static void ResetStatics() { mats.Clear(); }

        static Material M(string key, Color c, float emit = 0f, float smooth = 0.25f)
        {
            Material m;
            if (mats.TryGetValue(key, out m) && m != null) return m;
            m = Build.Mat(c, emit, smooth);
            mats[key] = m;
            return m;
        }

        // ---------------------------------------------------------------- genes

        void Resolve()
        {
            int k = look[Kind];
            plan = look[Legs] != 0 ? look[Legs] : k == Spider ? SpiderLegs : k == Hound ? FourLegs : k == Snake ? Tail : k == Swarm ? Carpet : TwoLegs;
            if (look[Legs] == 0 && look[Body] == 3 && plan == TwoLegs) plan = TwoLegs;     // "on all fours" stays a lean
            skinG = look[Skin] != 0 ? look[Skin] : k == Holes ? SHoles : k == Drowned ? SDrowned : k == Doll ? SPorcelain : k == Clown ? SPaint :
                    k == Doctor ? SGown : k == Her ? SCardigan : k == You ? SCoat : k == Spider || k == Swarm ? SShell : k == Snake ? SScales :
                    k == Hound ? SFur : k == Shadow ? SShadow : SDark;
            headG = look[HeadKind] != 0 ? look[HeadKind] : k == Doll ? HDoll : k == Clown ? HClown : k == Spider ? HSpider : k == Snake ? HSnake :
                    k == Hound ? HDog : k == Doctor ? HMask : k == Her ? HGrandma : k == You ? HYou : k == Drowned ? HWet : HBare;
            extraG = look[Extra] != 0 ? look[Extra] : k == Doctor ? XNeedles : k == Drowned ? XDrips : 0;
            // An animal on its own stays an animal; any human gene on it gives it a body rising out of it.
            bool animalKind = k == Spider || k == Hound || k == Snake || k == Swarm;
            bool humanGenes = look[Body] != 0 || look[Arms] != 0 || (look[HeadKind] != 0 && look[HeadKind] != HSpider && look[HeadKind] != HSnake && look[HeadKind] != HDog);
            torsoOn = plan == TwoLegs || plan == OnHands || !animalKind || humanGenes;
        }

        // Body and head surfaces from the skin and head genes (see MonsterSkin). A shadow stays flat black.
        void Skins()
        {
            Material head = null;
            switch (skinG)
            {
                case SHoles: skin = MonsterSkin.Pale("pale", new Color(0.66f, 0.56f, 0.5f)); break;
                case SDrowned: skin = MonsterSkin.Drowned(); break;
                case SPorcelain: skin = MonsterSkin.Cloth("doll dress", new Color(0.46f, 0.56f, 0.66f), 0.3f, false, true); head = MonsterSkin.Porcelain(); break;
                case SPaint: skin = MonsterSkin.Harlequin(); head = MonsterSkin.Greasepaint(); break;
                case SGown: skin = MonsterSkin.Cloth("gown", new Color(0.55f, 0.66f, 0.62f), 0.35f); head = MonsterSkin.Pale("pallid", new Color(0.74f, 0.7f, 0.66f)); break;
                case SCardigan: skin = MonsterSkin.Knit(); head = MonsterSkin.Pale("flesh old", new Color(0.7f, 0.58f, 0.52f)); break;
                case SCoat: skin = MonsterSkin.Cloth("your coat", new Color(0.2f, 0.22f, 0.3f), 0.1f, true); head = MonsterSkin.Pale("flesh", new Color(0.66f, 0.52f, 0.44f)); break;
                case SShell: skin = MonsterSkin.Chitin(); break;
                case SScales: skin = MonsterSkin.Scales(); break;
                case SFur: skin = MonsterSkin.Mange(); break;
                case SShadow: skin = M("shadow", new Color(0f, 0f, 0f), 0f, 0f); break;
                default: skin = MonsterSkin.Hide(); break;
            }
            if (skin == null) skin = M("dark", new Color(0.02f, 0.018f, 0.022f));
            // The head gene can bring its own surface: a doll's porcelain on anything, Grandma's grey hair, your face.
            switch (headG)
            {
                case HDoll: head = MonsterSkin.Porcelain(); break;
                case HClown: head = MonsterSkin.Greasepaint(); break;
                case HGrandma: head = MonsterSkin.Hair("grey hair", new Color(0.66f, 0.66f, 0.64f), 0.25f); break;
                case HYou: head = MonsterSkin.Pale("flesh", new Color(0.66f, 0.52f, 0.44f)); break;
                case HMask: if (head == null) head = MonsterSkin.Pale("pallid", new Color(0.74f, 0.7f, 0.66f)); break;
            }
            if (skinG == SShadow) head = skin;
            headSkin = head ?? skin;
        }

        /// <summary>The forms that show their bones: ribs, spine and collarbones under the skin.</summary>
        bool Bare { get { return skinG == SDark || skinG == SHoles || skinG == SDrowned || skinG == SShadow || extraG == XRibs; } }

        public static Material faceMaterial;     // reserved: lets a face show what Moodscape is painting

        public Vector3 HeadPoint { get { return HeadPos; } }

        public Vector3 HeadPos { get { return headT != null ? headT.position : transform.position + Vector3.up * height; } }

        public static Monster Create(Transform parent, int[] look)
        {
            var g = new GameObject("It");
            if (parent != null) g.transform.SetParent(parent, false);
            var m = g.AddComponent<Monster>();
            m.seed = Random.value * 100f;
            m.skin = Build.Mat(new Color(0.02f, 0.018f, 0.022f), 0f, 0.25f);
            m.Rebuild(look);
            return m;
        }

        /// <summary>Ask for a new look. It takes effect the next time ApplyIfUnseen is called while unseen.</summary>
        public void Want(int[] newLook) { wanted = (int[])newLook.Clone(); }

        public bool Pending { get { return wanted != null || morphT >= 0f || Transforming; } }

        /// <summary>True while it is becoming something else in front of you.</summary>
        public bool Transforming { get; private set; }

        Material BodyMaterial()
        {
            if (rig == null) return skin;
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (smr.sharedMaterial != null) return smr.sharedMaterial;
            return skin;
        }

        /// <summary>
        /// Becoming something else in front of you, the way a shapeshifter does it in a film: one body grows out of the
        /// other. New limbs bud from its core and stretch out one at a time while the old ones draw back into it; for
        /// most of it, it is both things at once, melted together. The flesh slides continuously between the shapes
        /// (never stepping), writhes, heaves and swells; the new skin spreads over it like a stain with a raw wet edge;
        /// eyes and teeth sink into it and new ones surface after. It is never at one speed: it surges, stalls, slides
        /// back a little, surges again. In the dark, by a failing bulb, it looks like something that can't decide what
        /// it is. `pace` stretches or shortens it (1 = about six seconds).
        /// </summary>
        public IEnumerator TransformInto(int[] to, float pace = 1f)
        {
            if (Transforming || rig == null) yield break;
            Transforming = true;
            var next = Create(null, to);
            next.enabled = false;
            next.transform.SetPositionAndRotation(transform.position, transform.rotation);
            next.transform.localScale = transform.localScale;
            next.rig.localScale = rig.localScale;
            next.SetVisible(false);
            Vector3 home = transform.position;
            Quaternion homeRot = transform.rotation;
            const int Steps = 56;
            var job = Flesh.Morph(rig, next.rig, transform, 0.034f, 0.035f, Steps, seed);
            var shader = Shader.Find("NightPages/Becoming");
            float clock = 0f;
            // While the first shapes come, it seizes where it stands: a slow shudder, not a jitter.
            Sfx.At("creak", HeadPos, 0.7f, 0.5f);
            while (shader != null && !job.Failed && job.Ready < 8 && !job.Done)
            {
                clock += Time.deltaTime;
                Heave(home, homeRot, clock, 0.5f);
                yield return null;
            }
            if (shader == null || job.Failed || job.MeshAt(0) == null)
            {
                if (shader == null) Debug.LogWarning("[NightPages] the Becoming shader is missing: it changes in one step");
                Rebuild(to);
                Destroy(next.gameObject);
                transform.SetPositionAndRotation(home, homeRot);
                Transforming = false;
                yield break;
            }

            var mat = new Material(shader) { name = "Becoming" };
            Feed(BodyMaterial(), mat, "");
            Feed(next.BodyMaterial(), mat, "2");
            mat.SetVector("_Core", job.core);
            mat.SetFloat("_Radius", job.radius);
            mat.SetFloat("_Seed", seed % 10f);
            var shapeGo = new GameObject("Becoming");
            shapeGo.transform.SetParent(transform, false);
            var mf = shapeGo.AddComponent<MeshFilter>();
            var mr = shapeGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mf.sharedMesh = job.MeshAt(0);

            // Its body becomes the shifting flesh; its eyes, teeth and loose parts stay, and sink into it.
            var sinking = new List<KeyValuePair<Transform, Vector3>>();
            var oldBody = new List<Renderer>();
            foreach (var r in rig.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (r is SkinnedMeshRenderer) oldBody.Add(r);
                else sinking.Add(new KeyValuePair<Transform, Vector3>(r.transform, r.transform.localScale));
            }
            // The shifting flesh comes up through its skin in patches (no cut from one body to the other).
            for (float h = 0f; h < 0.45f; h += Time.deltaTime)
            {
                clock += Time.deltaTime;
                mat.SetFloat("_Dissolve", 1f - h / 0.45f);
                mat.SetFloat("_Writhe", 0.3f * h / 0.45f);
                Heave(home, homeRot, clock, 0.35f);
                yield return null;
            }
            mat.SetFloat("_Dissolve", 0f);
            foreach (var r in oldBody) if (r != null) r.enabled = false;

            float duration = 6f * pace, speed = (Steps - 1) / duration;
            float q = 0f, most = 0f, tm = 0f;
            int shown = 0, lastSound = 0;
            var rng = new System.Random((int)(seed * 977f));
            while (q < Steps - 1 && tm < 60f)
            {
                float dt = Time.deltaTime;
                tm += dt;
                clock += dt;
                // Surges and lulls, and now and then it slides back a little before going on.
                float n = Mathf.PerlinNoise(tm * 0.85f, seed * 0.37f);
                float rate = speed * (0.2f + 1.9f * n * n);
                if (Mathf.PerlinNoise(tm * 0.55f + 17f, seed * 0.21f) < 0.22f && q > 3f) rate = -speed * 0.4f;
                q = Mathf.Clamp(q + rate * dt, Mathf.Max(0f, most - 4f), Steps - 1);
                float can = job.Ready >= Steps ? Steps - 1 : job.Ready - 1.001f;
                q = Mathf.Min(q, Mathf.Max(0f, can));                 // it can't get ahead of the shapes being worked out
                most = Mathf.Max(most, q);
                int fi = Mathf.Min(Mathf.FloorToInt(q), Steps - 1);
                float sub = q - fi;
                var m = job.MeshAt(fi);
                if (m != null && fi != shown) { mf.sharedMesh = m; shown = fi; }
                else if (m == null) sub = 0f;
                float p = q / (Steps - 1);
                float mid = Mathf.Sin(Mathf.PI * p);
                mat.SetFloat("_Sub", sub);
                mat.SetFloat("_Blend", Mathf.SmoothStep(0f, 1f, (p - 0.1f) / 0.8f));
                mat.SetFloat("_Writhe", 0.3f + 0.7f * mid);
                // Its eyes and teeth sink into the flesh as it starts.
                float keep = 1f - Mathf.SmoothStep(0f, 1f, p / 0.25f);
                foreach (var kv in sinking) if (kv.Key != null) kv.Key.localScale = kv.Value * keep;
                // The whole body heaves and swells, slowly.
                Heave(home, homeRot, clock, 0.35f + 0.65f * mid);
                float b = 0.35f + 0.65f * mid;
                shapeGo.transform.localScale = new Vector3(1f + 0.035f * Mathf.Sin(clock * 2.1f) * b, 1f + 0.05f * Mathf.Sin(clock * 1.6f + 1f) * b, 1f + 0.035f * Mathf.Sin(clock * 2.4f + 2f) * b);
                // Something gives, now and then: wet cracks as it goes forward.
                if (fi >= lastSound + 5 + rng.Next(4))
                {
                    lastSound = fi;
                    Sfx.At(rng.NextDouble() < 0.6 ? "crack" : "creak", home + Vector3.up * 1.2f, 0.5f, 0.55f + (float)rng.NextDouble() * 0.5f);
                }
                yield return null;
            }
            // It settles into what it is now.
            for (float h = 0f; h < 0.5f; h += Time.deltaTime)
            {
                clock += Time.deltaTime;
                float k = 1f - h / 0.5f;
                mat.SetFloat("_Writhe", 0.3f * k);
                Heave(home, homeRot, clock, 0.35f * k);
                shapeGo.transform.localScale = Vector3.Lerp(Vector3.one, shapeGo.transform.localScale, k);
                yield return null;
            }
            if (GameDirector.I != null) GameDirector.I.Log("monster: transformation shapes took " + job.seconds.ToString("0.0") + " s to work out");
            transform.SetPositionAndRotation(home, homeRot);
            Rebuild(to);
            SetVisible(true);
            Destroy(next.gameObject);

            // Its new eyes and teeth surface through the skin.
            var surfacing = new List<KeyValuePair<Transform, Vector3>>();
            if (rig != null)
                foreach (var r in rig.GetComponentsInChildren<Renderer>())
                    if (r.enabled && !(r is SkinnedMeshRenderer)) surfacing.Add(new KeyValuePair<Transform, Vector3>(r.transform, r.transform.localScale));
            foreach (var kv in surfacing) if (kv.Key != null) kv.Key.localScale = Vector3.zero;
            shapeGo.transform.localScale = Vector3.one;
            for (float h = 0f; h < 0.8f; h += Time.deltaTime)
            {
                // The last of the shifting flesh breaks up over the finished body as its eyes and teeth come through.
                mat.SetFloat("_Dissolve", Mathf.Clamp01(h / 0.55f));
                float k = Mathf.SmoothStep(0f, 1f, h / 0.8f);
                foreach (var kv in surfacing) if (kv.Key != null) kv.Key.localScale = kv.Value * k;
                yield return null;
            }
            foreach (var kv in surfacing) if (kv.Key != null) kv.Key.localScale = kv.Value;
            Destroy(shapeGo);
            job.Free();
            Destroy(mat);
            Transforming = false;
        }

        // A slow heave of the whole body: it sways and lists as if its weight keeps shifting. Smooth noise, not a jitter.
        void Heave(Vector3 home, Quaternion homeRot, float clock, float amount)
        {
            float s = seed * 0.73f;
            Vector3 drift = new Vector3(Mathf.PerlinNoise(clock * 0.9f, s) - 0.5f, (Mathf.PerlinNoise(clock * 1.3f, s + 5f) - 0.5f) * 0.5f, Mathf.PerlinNoise(clock * 0.8f, s + 9f) - 0.5f) * 0.06f * amount;
            var tilt = Quaternion.Euler((Mathf.PerlinNoise(clock * 1.1f, s + 13f) - 0.5f) * 10f * amount,
                                        (Mathf.PerlinNoise(clock * 0.7f, s + 17f) - 0.5f) * 16f * amount,
                                        (Mathf.PerlinNoise(clock * 1.2f, s + 21f) - 0.5f) * 9f * amount);
            transform.SetPositionAndRotation(home + drift, homeRot * tilt);
        }

        // Hand a body material's colour, textures and gloss to the Becoming material (sfx "" = old skin, "2" = new).
        static void Feed(Material src, Material dst, string sfx)
        {
            Texture main = src != null && src.HasProperty("_MainTex") ? src.mainTexture : null;
            dst.SetTexture("_MainTex" + sfx, main != null ? main : Texture2D.whiteTexture);
            if (src != null && src.HasProperty("_MainTex"))
            {
                dst.SetTextureScale("_MainTex" + sfx, src.mainTextureScale);
                dst.SetTextureOffset("_MainTex" + sfx, src.mainTextureOffset);
            }
            Texture bump = src != null && src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;
            dst.SetTexture("_BumpMap" + sfx, bump != null ? bump : Texture2D.normalTexture);
            dst.SetFloat("_BumpScale" + sfx, bump != null && src.HasProperty("_BumpScale") ? src.GetFloat("_BumpScale") : 1f);
            dst.SetColor("_Color" + sfx, src != null && src.HasProperty("_Color") ? src.color : Color.gray);
            dst.SetFloat("_Glossiness" + sfx, src != null && src.HasProperty("_Glossiness") ? src.GetFloat("_Glossiness") : 0.2f);
        }

        /// <summary>
        /// Make a wanted change real, if it can happen now. Normally that means waiting until nobody is
        /// looking. If the new look says "while you watch", it reshapes itself in plain view instead.
        /// Returns true when a change starts.
        /// </summary>
        public bool ApplyIfUnseen(Camera cam)
        {
            if (wanted == null || morphT >= 0f) return false;
            bool seen = cam != null && (Vis.Sees(cam, HeadPos, transform) || Vis.Sees(cam, transform.position + Vector3.up * 0.6f, transform));
            bool openly = wanted.Length > Shift && wanted[Shift] == 1;
            if (seen && !openly) return false;
            changedInView = seen;
            if (seen)
            {
                // It changes while you watch: slowly, a piece at a time, not all the way forward.
                var to = wanted;
                wanted = null;
                StartCoroutine(TransformInto(to));
                return true;
            }
            Rebuild(wanted);
            wanted = null;
            return true;
        }

        // ---------------------------------------------------------------- parts

        GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            return Build.Prim(type, name, parent, pos, scale, skin, false);
        }

        GameObject Dress(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            return Build.Prim(type, name, parent, pos, scale, m, false);
        }

        static Transform Node(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        // A capsule running between two points.
        Transform Bone(Transform parent, Vector3 a, Vector3 b, float thick)
        {
            var g = Part(PrimitiveType.Capsule, "Bone", parent, (a + b) / 2f, new Vector3(thick, (b - a).magnitude / 2f + thick * 0.3f, thick));
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            return g.transform;
        }

        // An arm hanging from a pivot at the shoulder: rotate the pivot and the whole arm swings from its top.
        // Upper arm, elbow a little bent, a thinner forearm, a narrow hand and fingers a little too long.
        Transform Limb(string name, Transform parent, Vector3 at, float length, float thick)
        {
            var pivot = Node(name, parent, at);
            float upper = length * 0.47f, fore = length * 0.43f;
            Part(PrimitiveType.Sphere, "Shoulder", pivot, Vector3.zero, Vector3.one * thick * 1.45f);
            Part(PrimitiveType.Capsule, "Upper arm", pivot, new Vector3(0f, -upper / 2f, 0f), new Vector3(thick * 1.15f, upper / 2f, thick * 1.05f));
            var elbow = Node("Elbow", pivot, new Vector3(0f, -upper, 0f));
            elbow.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            Part(PrimitiveType.Sphere, "Joint", elbow, Vector3.zero, Vector3.one * thick * 1.15f);
            Part(PrimitiveType.Capsule, "Forearm", elbow, new Vector3(0f, -fore / 2f, 0f), new Vector3(thick * 0.85f, fore / 2f, thick * 0.75f));
            var wrist = Node("Wrist", elbow, new Vector3(0f, -fore, 0f));
            Hand(wrist, thick, length * 0.12f);
            return pivot;
        }

        // A palm and four long fingers, slightly curled, and a thumb. Needles instead, if it has those.
        void Hand(Transform wrist, float thick, float fingerLen)
        {
            Part(PrimitiveType.Sphere, "Palm", wrist, new Vector3(0f, -thick * 0.9f, 0f), new Vector3(thick * 1.3f, thick * 1.9f, thick * 0.6f));
            if (extraG == XNeedles) return;
            for (int i = 0; i < 4; i++)
            {
                float fx = (i - 1.5f) * thick * 0.32f;
                float len = fingerLen * (i == 0 || i == 3 ? 0.8f : 1f);
                Vector3 knuckle = new Vector3(fx, -thick * 1.75f, 0f);
                Vector3 mid = knuckle + new Vector3(fx * 0.2f, -len * 0.55f, len * 0.12f);
                Vector3 tip = mid + new Vector3(fx * 0.1f, -len * 0.45f, len * 0.3f);
                Bone(wrist, knuckle, mid, thick * 0.24f);
                Bone(wrist, mid, tip, thick * 0.19f);
            }
            Bone(wrist, new Vector3(thick * 0.55f, -thick * 0.9f, thick * 0.2f), new Vector3(thick * 0.75f, -thick * 1.9f, fingerLen * 0.4f), thick * 0.24f);
        }

        // A leg from the hip: thigh, a knee bent a little forward, shin, a long flat foot.
        void Leg(Transform parent, float x, float legs, float thick)
        {
            Vector3 hip = new Vector3(x, legs, 0f), knee = new Vector3(x * 1.05f, legs * 0.5f, 0.06f), ankle = new Vector3(x, 0.08f, -0.02f);
            // Jointed at the hip and the knee, so it can walk.
            var hipJ = Node("Hip", parent, hip);
            var kneeJ = Node("Knee joint", hipJ, knee - hip);
            Bone(hipJ, Vector3.zero, knee - hip, thick * 1.15f);
            Part(PrimitiveType.Sphere, "Knee", kneeJ, Vector3.zero, Vector3.one * thick * 1.05f);
            Bone(kneeJ, Vector3.zero, ankle - knee, thick * 0.85f);
            Part(PrimitiveType.Sphere, "Foot", kneeJ, new Vector3(x, 0.045f, 0.07f) - knee, new Vector3(thick * 0.95f, 0.08f, 0.26f));
            hips.Add(hipJ);
            knees.Add(kneeJ);
        }

        // Ribs, spine and collarbones showing through tight skin.
        void Bones(float trunk, float width, float half)
        {
            float front = width * 0.33f, back = -width * 0.33f;
            for (int i = 0; i < 5; i++)
            {
                float y = trunk * (0.8f - i * 0.11f);
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    var rib = Bone(torso, new Vector3(sgn * 0.035f, y, front), new Vector3(sgn * width * 0.49f, y - 0.07f, width * 0.04f), 0.032f);
                    ribs.Add(rib);
                    ribRest.Add(rib.localPosition);
                }
            }
            for (int i = 0; i < 10; i++)
                Part(PrimitiveType.Sphere, "Vertebra", torso, new Vector3(0f, trunk * (0.05f + i * 0.1f), back - 0.012f), new Vector3(0.055f, 0.045f, 0.05f));
            for (int sgn = -1; sgn <= 1; sgn += 2)
                Bone(torso, new Vector3(sgn * 0.03f, trunk * 0.96f, front * 0.9f), new Vector3(sgn * (half - 0.03f), trunk + 0.01f, 0f), 0.034f);
            Part(PrimitiveType.Sphere, "Hips", torso, new Vector3(0f, 0.02f, 0f), new Vector3(width * 0.78f, 0.16f, width * 0.55f));
        }

        // ---------------------------------------------------------------- building it

        public void Rebuild(int[] newLook)
        {
            look = new int[Traits];
            for (int i = 0; i < Traits && i < newLook.Length; i++) look[i] = newLook[i];
            if (rig != null) { FreeMeshes(); Destroy(rig.gameObject); }
            arms.Clear(); armRest.Clear(); legPivots.Clear(); hips.Clear(); knees.Clear(); chain.Clear(); swarm.Clear(); swarmHome.Clear();
            bodyRends.Clear(); eyeRends.Clear(); ribs.Clear(); ribRest.Clear(); mouths.Clear();
            torso = neck = headT = null;
            rig = new GameObject("Rig").transform;
            rig.SetParent(transform, false);
            Resolve();
            Skins();

            // What it stands on. Each gives a place for a body to rise from, and a place for an animal's head.
            float s = look[Body] == 2 ? 1.4f : look[Body] == 4 ? 0.6f : 1f;
            Transform waist = null, animalHead = null;
            switch (plan)
            {
                case SpiderLegs: LowerSpider(s, out waist, out animalHead); break;
                case FourLegs: LowerFour(s, out waist, out animalHead); break;
                case Tail: LowerTail(s, out waist, out animalHead); break;
                case Carpet: LowerCarpet(s, out waist, out animalHead); break;
                case Centipede: LowerCentipede(s, out waist, out animalHead); break;
            }
            if (torsoOn) Upper(waist);
            else if (animalHead != null)
            {
                headT = animalHead;
                if (plan != Carpet) AddHead(Vector3.zero, (plan == SpiderLegs ? 0.26f : plan == Tail ? 0.3f : 0.28f) * s);
            }
            AddExtras();

            // How tall it ended up.
            float top = 0.6f;
            foreach (var r in rig.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y - transform.position.y);
            height = top / Mathf.Max(0.01f, transform.lossyScale.y);
            if (headT == null) headT = Node("Head", rig, new Vector3(0f, height * 0.85f, 0f));
            Melt();
            Sort();
        }

        // The upper body: a torso, arms, a neck and a head. On two legs it stands on them; on anything else it
        // rises out of whatever it has instead of legs.
        void Upper(Transform waist)
        {
            int kind = look[Kind];
            float legs = 1.05f, trunk = 0.85f, lean = 0f, width = 0.36f;
            switch (kind == Doll && look[Body] == 0 ? 4 : look[Body])
            {
                case 1: legs = 0.9f; trunk = 0.9f; lean = 42f; break;                // hunched
                case 2: legs = 1.7f; trunk = 1.25f; lean = 10f; width = 0.3f; break;  // too tall for the room
                case 3: legs = 0.62f; trunk = 1.1f; lean = 86f; break;                // on all fours
                case 4: legs = 0.5f; trunk = 0.45f; width = 0.26f; break;             // the size of a child
            }
            if (look[Body] == 0 && skinG == SCoat) { legs = 0.82f; trunk = 0.6f; width = 0.4f; }               // exactly your height
            if (look[Body] == 0 && skinG == SCardigan) { legs = 0.78f; trunk = 0.66f; lean = 16f; width = 0.4f; }  // a small woman, a little stooped
            if (skinG == SShadow) width *= 0.6f;

            torso = new GameObject("Torso").transform;
            if (plan == TwoLegs)
            {
                float legThick = skinG == SPorcelain ? 0.1f : 0.11f;
                Leg(rig, -0.1f, legs, legThick);
                Leg(rig, 0.1f, legs, legThick);
                torso.SetParent(rig, false);
                torso.localPosition = new Vector3(0f, legs, 0f);
            }
            else if (plan == OnHands)
            {
                // No legs: the body drags behind, low, and the arms do the walking.
                torso.SetParent(rig, false);
                torso.localPosition = new Vector3(0f, 0.32f, -0.35f);
                lean = 78f;
                Part(PrimitiveType.Capsule, "Drag", rig, new Vector3(0f, 0.16f, -0.75f), new Vector3(width * 0.7f, 0.3f, width * 0.6f)).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                torso.SetParent(waist, false);
                torso.localPosition = Vector3.zero;
                if (look[Body] == 3) lean = 55f;
            }
            torso.localRotation = Quaternion.Euler(lean, 0f, 0f);
            if (Bare)
            {
                // Gaunt: a narrow waist under a ribcage that's too big for it.
                Part(PrimitiveType.Capsule, "Trunk", torso, new Vector3(0f, trunk * 0.42f, 0f), new Vector3(width * 0.62f, trunk * 0.42f, width * 0.5f));
                Part(PrimitiveType.Sphere, "Ribcage", torso, new Vector3(0f, trunk * 0.72f, 0.01f), new Vector3(width * 1.02f, trunk * 0.55f, width * 0.66f));
                Bones(trunk, width, width * 0.55f + 0.06f);
            }
            else Part(PrimitiveType.Capsule, "Trunk", torso, new Vector3(0f, trunk / 2f, 0f), new Vector3(width, trunk / 2f + 0.08f, width * 0.7f));

            Vector3 shoulder = torso.TransformPoint(new Vector3(0f, trunk, 0f)) - transform.position;
            float shoulderHeight = Mathf.Max(0.3f, shoulder.y / Mathf.Max(0.01f, transform.lossyScale.y));
            float worldLean = Vector3.Angle(torso.up, rig.up);

            // Arms hang straight down in the world, whatever the torso is doing.
            float hang = -worldLean;
            float armLen = Mathf.Min(0.85f, shoulderHeight * 0.75f);
            bool walksOnArms = look[Body] == 3 && plan == TwoLegs || plan == OnHands;
            if (walksOnArms) armLen = shoulderHeight;
            float half = width * 0.55f + 0.1f;          // arms hang clear of the body
            int armGene = look[Arms];
            if (plan == OnHands && armGene == 4) armGene = 0;      // it has to walk on something
            switch (armGene)
            {
                case 0:
                case 5:
                    AddArm(new Vector3(-half, trunk, 0f), armLen, hang, 8f);
                    AddArm(new Vector3(half, trunk, 0f), armLen, hang, -8f);
                    break;
                case 1:     // arms that reach the floor
                    AddArm(new Vector3(-half, trunk, 0f), shoulderHeight * 1.02f, hang, 3f);
                    AddArm(new Vector3(half, trunk, 0f), shoulderHeight * 1.02f, hang, -3f);
                    break;
                case 2:     // too many arms
                    for (int i = 0; i < 3; i++)
                    {
                        float y = trunk - i * trunk * 0.3f, spread = 10f + i * 16f, len = walksOnArms ? shoulderHeight * (1f - i * 0.12f) : armLen * (1f - i * 0.1f);
                        AddArm(new Vector3(-half, y, 0f), len, hang, spread);
                        AddArm(new Vector3(half, y, 0f), len, hang, -spread);
                    }
                    break;
                case 3:     // reaching for you
                    AddArm(new Vector3(-half, trunk, 0f), armLen * 1.15f, hang - 78f, 6f);
                    AddArm(new Vector3(half, trunk, 0f), armLen * 1.15f, hang - 84f, -6f);
                    break;
                case 4:     // no arms at all
                    break;
            }

            // What it wears, by its skin.
            if (skinG == SPorcelain)
            {
                Dress(PrimitiveType.Cylinder, "Dress", torso, new Vector3(0f, -0.02f, 0f), new Vector3(0.62f, 0.3f, 0.62f), skin);
                Dress(PrimitiveType.Cylinder, "Bodice", torso, new Vector3(0f, trunk * 0.6f, 0f), new Vector3(0.34f, trunk * 0.3f, 0.3f), skin);
                Dress(PrimitiveType.Cylinder, "Collar", torso, new Vector3(0f, trunk * 0.92f, 0f), new Vector3(0.4f, 0.02f, 0.36f), M("lace", new Color(0.86f, 0.83f, 0.76f), 0f, 0.1f));
            }
            if (skinG == SCardigan && plan == TwoLegs)
                Dress(PrimitiveType.Cylinder, "Skirt", rig, new Vector3(0f, legs * 0.5f, 0f), new Vector3(0.5f, legs * 0.5f, 0.42f), M("skirt", new Color(0.3f, 0.28f, 0.3f), 0f, 0.05f));
            if (skinG == SCoat)
                Dress(PrimitiveType.Cylinder, "Coat hem", torso, new Vector3(0f, -0.18f, 0f), new Vector3(width * 1.2f, 0.25f, width * 0.95f), skin);
            if (skinG == SGown)
                Dress(PrimitiveType.Cylinder, "Gown", torso, new Vector3(0f, trunk * 0.2f, 0f), new Vector3(width * 1.25f, trunk * 0.55f, width * 1.0f), skin);
            if (skinG == SHoles) AddHoles(torso, new Vector3(0f, trunk * 0.5f, 0f), width * 0.5f, width * 0.35f, trunk * 0.42f, 7, 11);
            if (skinG == SPaint)
            {
                // A ruff gone yellow and limp: a ring of drooping frills, not a plate.
                var ruff = M("ruff", new Color(0.78f, 0.74f, 0.6f), 0f, 0.1f);
                for (int i = 0; i < 14; i++)
                {
                    float a = i * Mathf.PI * 2f / 14f;
                    var fr = Build.Prim(PrimitiveType.Sphere, "Ruff", torso, new Vector3(Mathf.Cos(a) * 0.17f, trunk + 0.02f - (i % 2) * 0.03f, Mathf.Sin(a) * 0.14f),
                        new Vector3(0.11f, 0.05f, 0.08f), ruff, false);
                    fr.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 25f + (i % 3) * 10f);
                }
                var pom = M("pompom", new Color(0.36f, 0.06f, 0.05f), 0f, 0.1f);
                for (int i = 0; i < 3; i++)
                    Build.Prim(PrimitiveType.Sphere, "Pompom", torso, new Vector3(0f, trunk * (0.25f + i * 0.25f), width * 0.36f), Vector3.one * 0.09f, pom, false);
            }
            if (extraG == XChestFace) ChestFace(trunk, width);

            // Neck and head. The head faces the way the body faces, level with the ground.
            neck = Node("Neck", torso, new Vector3(0f, trunk + 0.06f, 0f));
            float headSize = headG == HDoll ? 0.4f : look[Body] == 4 ? 0.26f : 0.3f;
            float neckLen = 0.1f;
            neckRest = Quaternion.Euler(-worldLean, 0f, 0f);
            if (look[Body] == 2) neckRest = Quaternion.Euler(-worldLean + 48f, 0f, 0f);  // bent down under the ceiling
            if (look[Head] == 1) neckRest *= Quaternion.Euler(0f, 0f, 82f);              // tilted much too far
            if (look[Head] == 2) neckLen = 0.75f;                                        // a long neck
            neck.localRotation = neckRest;
            Part(PrimitiveType.Capsule, "Neck bone", neck, new Vector3(0f, neckLen / 2f, 0f), new Vector3(0.09f, neckLen / 2f + 0.05f, 0.09f));
            headT = Node("Head", neck, new Vector3(0f, neckLen + headSize * 0.5f, 0f));

            if (look[Head] == 4) { /* nothing above the neck */ }
            else if (look[Head] == 3)
            {
                AddHead(new Vector3(0f, 0.05f, 0f), headSize);
                AddHead(new Vector3(-0.23f, -0.06f, 0.02f), headSize * 0.8f);
                AddHead(new Vector3(0.2f, -0.1f, -0.03f), headSize * 0.7f);
            }
            else AddHead(Vector3.zero, headSize);
        }

        // ---------------------------------------------------------------- what it stands on

        // A spider's abdomen and eight legs with the knees above its back.
        void LowerSpider(float s, out Transform waist, out Transform head)
        {
            Part(PrimitiveType.Sphere, "Abdomen", rig, new Vector3(0f, 0.6f, -0.45f) * s, new Vector3(0.8f, 0.65f, 0.95f) * s);
            Part(PrimitiveType.Sphere, "Thorax", rig, new Vector3(0f, 0.52f, 0.18f) * s, new Vector3(0.5f, 0.4f, 0.55f) * s);
            for (int i = 0; i < 8; i++)
            {
                float side = i < 4 ? -1f : 1f;
                float fan = Mathf.Lerp(-55f, 55f, (i % 4) / 3f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(side * Mathf.Cos(fan), 0f, Mathf.Sin(fan));
                Vector3 hip = new Vector3(side * 0.2f, 0.52f, 0.15f) * s;
                Vector3 knee = hip + (dir * 0.75f + Vector3.up * 0.5f) * s;
                Vector3 foot = hip + dir * 1.35f * s;
                foot.y = 0f;
                var legPivot = Node("Leg", rig, Vector3.zero);
                Bone(legPivot, hip, knee, 0.05f * s);
                Bone(legPivot, knee, foot, 0.04f * s);
                legPivots.Add(legPivot);
            }
            waist = Node("Waist", rig, new Vector3(0f, 0.7f, 0.25f) * s);
            head = Node("Head", rig, new Vector3(0f, 0.55f, 0.45f) * s);
        }

        // Four long legs and a long low body, like a dog nearly.
        void LowerFour(float s, out Transform waist, out Transform head)
        {
            var body = Part(PrimitiveType.Capsule, "Body", rig, new Vector3(0f, 0.85f, 0f) * s, new Vector3(0.42f, 0.7f, 0.42f) * s);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Vector3[] hips = { new Vector3(-0.15f, 0.8f, 0.5f), new Vector3(0.15f, 0.8f, 0.5f), new Vector3(-0.15f, 0.8f, -0.5f), new Vector3(0.15f, 0.8f, -0.5f) };
            for (int i = 0; i < 4; i++)
            {
                var leg = Node("Leg", rig, Vector3.zero);
                Vector3 hip = hips[i] * s, knee = hip + new Vector3(0f, -0.42f, i < 2 ? -0.08f : 0.12f) * s, foot = new Vector3(hip.x, 0f, hip.z + (i < 2 ? 0.05f : -0.05f) * s);
                Bone(leg, hip, knee, 0.08f * s);
                Bone(leg, knee, foot, 0.06f * s);
                legPivots.Add(leg);
            }
            waist = Node("Waist", rig, new Vector3(0f, 1.0f, 0.55f) * s);
            if (!torsoOn) Bone(rig, new Vector3(0f, 1.0f, 0.6f) * s, new Vector3(0f, 1.2f, 0.95f) * s, 0.16f * s);
            head = Node("Head", rig, new Vector3(0f, 1.22f, 1.05f) * s);
        }

        // A tail: a coil on the floor that rises. Alone, it rises to head height; with a body, to the waist.
        void LowerTail(float s, out Transform waist, out Transform head)
        {
            float thick = look[Body] == 2 ? 0.36f : look[Body] == 4 ? 0.14f : 0.24f;
            tailRise = torsoOn ? 0.75f : 1.65f;
            for (int i = 0; i < 16; i++)
            {
                float taper = i < 4 ? 0.5f + i * 0.125f : 1f;
                var seg = Part(PrimitiveType.Sphere, "Coil", rig, Vector3.zero, Vector3.one * thick * taper).transform;
                chain.Add(seg);
            }
            PoseSnake(0f);
            waist = Node("Waist", rig, chain[chain.Count - 1].localPosition + new Vector3(0f, 0.05f, 0f));
            head = Node("Head", rig, Vector3.zero);
        }

        // Hundreds of small things that move as one. No single one is frightening; all of them together are.
        void LowerCarpet(float s, out Transform waist, out Transform head)
        {
            int n = look[Body] == 2 ? 140 : look[Body] == 4 ? 50 : 90;
            var rng = new System.Random(n);
            for (int i = 0; i < n; i++)
            {
                float a = (float)(rng.NextDouble() * Mathf.PI * 2), r = 1.4f * Mathf.Sqrt((float)rng.NextDouble());
                var home = new Vector3(Mathf.Cos(a) * r, 0.03f + (float)rng.NextDouble() * (look[Body] == 3 ? 0.05f : 0.5f) * (1f - r / 1.4f), Mathf.Sin(a) * r);
                var bug = Part(PrimitiveType.Capsule, "Crawler", rig, home, new Vector3(0.05f, 0.06f, 0.05f)).transform;
                bug.localRotation = Quaternion.Euler(90f, (float)rng.NextDouble() * 360f, 0f);
                swarm.Add(bug);
                swarmHome.Add(home);
            }
            waist = Node("Waist", rig, new Vector3(0f, 0.2f, 0f));
            head = Node("Head", rig, new Vector3(0f, 0.3f, 0f));
        }

        // Dozens of legs: a long segmented body low to the floor, rearing at the front.
        void LowerCentipede(float s, out Transform waist, out Transform head)
        {
            int segs = 11;
            for (int i = 0; i < segs; i++)
            {
                float u = i / (float)(segs - 1);
                float z = Mathf.Lerp(-1.3f, 0.7f, u) * s, y = (0.16f + (u > 0.8f ? (u - 0.8f) * 1.6f : 0f)) * s;
                float r = Mathf.Lerp(0.2f, 0.3f, Mathf.Sin(u * Mathf.PI)) * s;
                Part(PrimitiveType.Sphere, "Segment", rig, new Vector3(0f, y, z), new Vector3(r * 1.3f, r * 0.8f, r * 1.1f));
                for (int side = -1; side <= 1; side += 2)
                {
                    var leg = Node("Leg", rig, Vector3.zero);
                    Vector3 hip = new Vector3(side * r * 0.5f, y, z), knee = hip + new Vector3(side * 0.22f, 0.12f, 0.03f) * s, foot = new Vector3(side * (r * 0.5f + 0.38f * s), 0f, z + 0.05f * s);
                    Bone(leg, hip, knee, 0.035f * s);
                    Bone(leg, knee, foot, 0.03f * s);
                    legPivots.Add(leg);
                }
            }
            waist = Node("Waist", rig, new Vector3(0f, 0.45f, 0.75f) * s);
            head = Node("Head", rig, new Vector3(0f, 0.5f, 0.95f) * s);
        }

        void PoseSnake(float t)
        {
            int n = chain.Count;
            if (n == 0) return;
            for (int i = 0; i < n; i++)
            {
                Vector3 p;
                if (i < 6) p = new Vector3(Mathf.Sin(i * 1.1f + t * 0.8f) * 0.22f, 0.12f, -1.3f + i * 0.22f);
                else
                {
                    float u = (i - 6) / (float)(n - 7);
                    p = new Vector3(Mathf.Sin(t * 1.3f + u * 4f) * 0.22f * u, 0.12f + u * tailRise, Mathf.Sin(u * Mathf.PI) * 0.3f);
                }
                chain[i].localPosition = p;
            }
            Vector3 top = chain[n - 1].localPosition;
            if (torso != null && torso.parent != null && torso.parent.name == "Waist") torso.parent.localPosition = top + new Vector3(0f, 0.05f, 0f);
            else if (headT != null && plan == Tail && !torsoOn)
            {
                headT.localPosition = top + new Vector3(0f, 0.08f, 0.14f);
                headT.localRotation = Quaternion.Euler(18f, Mathf.Sin(t * 1.3f) * 14f, 0f);
            }
        }

        // ---------------------------------------------------------------- arms, heads, extras

        void AddArm(Vector3 at, float length, float pitch, float roll)
        {
            Transform a;
            if (look[Arms] == 5)
            {
                // Bending the wrong way: the forearm folds back and up from the elbow.
                a = Node("Arm", torso, at);
                float upper = length * 0.5f;
                Part(PrimitiveType.Capsule, "Bone", a, new Vector3(0f, -upper / 2f, 0f), new Vector3(0.075f, upper / 2f, 0.075f));
                var elbow = Node("Elbow", a, new Vector3(0f, -upper, 0f));
                elbow.localRotation = Quaternion.Euler(-150f, 0f, 0f);
                Part(PrimitiveType.Sphere, "Joint", elbow, Vector3.zero, Vector3.one * 0.1f);
                Part(PrimitiveType.Capsule, "Bone", elbow, new Vector3(0f, -upper / 2f, 0f), new Vector3(0.065f, upper / 2f, 0.065f));
                Part(PrimitiveType.Sphere, "Hand", elbow, new Vector3(0f, -upper, 0f), new Vector3(0.11f, 0.18f, 0.11f));
            }
            else a = Limb("Arm", torso, at, length, 0.075f);
            Transform hand = look[Arms] == 5 ? a.Find("Elbow") : a.Find("Elbow/Wrist");
            if (hand == null) hand = a;
            if (extraG == XNeedles)
            {
                // Fingers that are needles. Thin, bright, and never anywhere near you unless you get close.
                var steel = M("steel", new Color(0.78f, 0.8f, 0.82f), 0.3f, 0.95f);
                Vector3 p = look[Arms] == 5 ? new Vector3(0f, -length * 0.5f - 0.08f, 0f) : new Vector3(0f, -0.1f, 0f);
                for (int i = 0; i < 3; i++)
                    Build.Prim(PrimitiveType.Cylinder, "Needle", hand, p + new Vector3((i - 1) * 0.025f, -0.17f, 0f), new Vector3(0.008f, 0.17f, 0.008f), steel, false);
            }
            if (extraG == XArmHoles) AddHoles(a, new Vector3(0f, -length * 0.22f, 0f), 0.06f, 0.06f, length * 0.18f, 3, 8);
            if (extraG == XStrings) PuppetString(hand);
            var rest = Quaternion.Euler(pitch, 0f, roll);
            a.localRotation = rest;
            arms.Add(a);
            armRest.Add(rest);
        }

        // A black thread from here up into the dark, as if something above were working it.
        void PuppetString(Transform from)
        {
            var thread = M("thread", new Color(0.03f, 0.03f, 0.03f), 0f, 0.3f);
            var s = Build.Prim(PrimitiveType.Cylinder, "Needle", from, Vector3.zero, new Vector3(0.006f, 2.5f, 0.006f), thread, false).transform;
            s.rotation = Quaternion.identity;
            s.position = from.position + Vector3.up * 2.5f;
        }

        // Another face, in its chest: two eyes and a mouth that opens when yours does.
        void ChestFace(float trunk, float width)
        {
            var eye = M("pale eye", new Color(0.9f, 0.9f, 0.8f), 1.6f);
            var socket = M("socket", new Color(0.02f, 0.012f, 0.012f), 0f, 0.3f);
            float z = width * 0.36f, y = trunk * 0.62f;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Build.Prim(PrimitiveType.Sphere, "Socket", torso, new Vector3(sgn * width * 0.18f, y, z), new Vector3(0.07f, 0.055f, 0.04f), socket, false);
                Build.Prim(PrimitiveType.Sphere, "Eye", torso, new Vector3(sgn * width * 0.18f, y, z + 0.012f), Vector3.one * 0.03f, eye, false);
            }
            var mouth = Build.Prim(PrimitiveType.Sphere, "Mouth", torso, new Vector3(0f, y - 0.13f, z), new Vector3(0.09f, 0.03f, 0.04f), M("mouth dark", new Color(0.12f, 0.01f, 0.01f), 0.2f), false);
            mouths.Add(mouth.transform);
            mouthRest = 0.03f;
        }

        void AddExtras()
        {
            if (extraG == XCrawling)
            {
                // Small things crawling over it, and it lets them.
                var bugMat = MonsterSkin.Chitin();
                Transform on = torso != null ? torso : rig;
                var rng = new System.Random(look[Body] * 13 + look[Kind]);
                for (int i = 0; i < 32; i++)
                {
                    var home = new Vector3((float)(rng.NextDouble() - 0.5) * 0.32f, (float)rng.NextDouble() * 0.8f, (float)(rng.NextDouble() - 0.5) * 0.26f);
                    var bug = Build.Prim(PrimitiveType.Capsule, "Crawler", on, home, new Vector3(0.022f, 0.028f, 0.022f), bugMat, false).transform;
                    swarm.Add(bug);
                    swarmHome.Add(home);
                }
            }
            if (extraG == XStrings && headT != null) PuppetString(headT);
            if (extraG == XChestFace && torso == null)
            {
                // No chest to put it in: the face is in its back instead.
                var eye = M("pale eye", new Color(0.9f, 0.9f, 0.8f), 1.6f);
                Build.Prim(PrimitiveType.Sphere, "Eye", rig, new Vector3(-0.08f, 0.95f, -0.4f), Vector3.one * 0.05f, eye, false);
                Build.Prim(PrimitiveType.Sphere, "Eye", rig, new Vector3(0.08f, 0.95f, -0.4f), Vector3.one * 0.05f, eye, false);
            }
        }

        void AddHead(Vector3 at, float size)
        {
            var h = Node("Skull", headT, at);
            float z = size * 0.47f;
            if (headG == HDoll || headG == HClown)
            {
                // A painted face on white: a doll's glass eyes and small mouth, or a clown's nose and grin.
                Dress(PrimitiveType.Sphere, "Skull", h, Vector3.zero, new Vector3(size, size * 1.15f, size), headSkin);
                var black = Build.Mat(new Color(0.02f, 0.02f, 0.02f), 0f, 0.9f);
                var red = Build.Mat(new Color(0.7f, 0.06f, 0.05f), 0.3f);
                float eyeSize = headG == HDoll ? 0.2f : 0.1f;
                Build.Prim(PrimitiveType.Sphere, "Eye", h, new Vector3(-size * 0.2f, size * 0.1f, z * 0.9f), Vector3.one * size * eyeSize, black, false);
                Build.Prim(PrimitiveType.Sphere, "Eye", h, new Vector3(size * 0.2f, size * 0.1f, z * 0.9f), Vector3.one * size * eyeSize, black, false);
                if (headG == HDoll)
                {
                    var dm = Build.Prim(PrimitiveType.Sphere, "Mouth", h, new Vector3(0f, -size * 0.22f, z * 0.93f), new Vector3(size * 0.12f, size * 0.06f, size * 0.05f), red, false);
                    mouths.Add(dm.transform); mouthRest = size * 0.06f;
                    var hair = Build.Mat(new Color(0.25f, 0.15f, 0.08f));
                    Build.Prim(PrimitiveType.Sphere, "Hair", h, new Vector3(0f, size * 0.2f, -size * 0.12f), new Vector3(size * 1.1f, size * 1.05f, size * 1.0f), hair, false);
                }
                else
                {
                    // Paint that has run: dark hollows dragged down from the eyes, a nose the colour of old blood,
                    // and a grin painted far wider than the mouth under it, with the teeth showing through.
                    var hollow = M("clown hollow", new Color(0.05f, 0.03f, 0.04f), 0f, 0.4f);
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        Build.Prim(PrimitiveType.Sphere, "Socket", h, new Vector3(sgn * size * 0.2f, size * 0.08f, z * 0.9f), new Vector3(size * 0.2f, size * 0.32f, size * 0.08f), hollow, false);
                        Build.Prim(PrimitiveType.Sphere, "Socket", h, new Vector3(sgn * size * 0.2f, -size * 0.1f, z * 0.93f), new Vector3(size * 0.05f, size * 0.22f, size * 0.04f), hollow, false);
                    }
                    var oldRed = M("old red", new Color(0.32f, 0.03f, 0.03f), 0f, 0.55f);
                    Build.Prim(PrimitiveType.Sphere, "Nose", h, new Vector3(0f, -size * 0.02f, z * 1.06f), Vector3.one * size * 0.16f, oldRed, false);
                    var teeth = M("clown teeth", new Color(0.75f, 0.7f, 0.55f), 0f, 0.6f);
                    for (int i = -6; i <= 6; i++)
                    {
                        float x = i * size * 0.07f, y = -size * 0.3f + i * i * size * 0.009f;
                        Build.Prim(PrimitiveType.Sphere, "Grin", h, new Vector3(x, y, z * Mathf.Cos(i * 0.13f) * 0.97f), new Vector3(size * 0.09f, size * 0.07f, size * 0.05f), oldRed, false);
                        if (Mathf.Abs(i) <= 4)
                            Build.Prim(PrimitiveType.Cube, "Tooth", h, new Vector3(x, y + size * 0.01f, z * Mathf.Cos(i * 0.13f) * 1.0f), new Vector3(size * 0.035f, size * 0.07f, size * 0.02f), teeth, false);
                    }
                    var tuft = M("tuft", new Color(0.38f, 0.17f, 0.07f), 0f, 0.05f);
                    Build.Prim(PrimitiveType.Sphere, "Hair", h, new Vector3(-size * 0.5f, size * 0.25f, 0f), Vector3.one * size * 0.5f, tuft, false);
                    Build.Prim(PrimitiveType.Sphere, "Hair", h, new Vector3(size * 0.5f, size * 0.25f, 0f), Vector3.one * size * 0.5f, tuft, false);
                }
                return;
            }
            if (headG == HSpider)
            {
                // A cluster of eyes over two fangs.
                Dress(PrimitiveType.Sphere, "Skull", h, Vector3.zero, Vector3.one * size, headSkin);
                var eye = M("spider eye", new Color(0.9f, 0.85f, 0.7f), 1.5f);
                for (int i = 0; i < 8; i++)
                {
                    float x = ((i % 4) - 1.5f) * 0.21f * size, y = (i < 4 ? 0.19f : -0.04f) * size;
                    Build.Prim(PrimitiveType.Sphere, "Eye", h, new Vector3(x, y, z), Vector3.one * (i < 4 ? 0.13f : 0.1f) * size, eye, false);
                }
                var fang = M("fang", new Color(0.12f, 0.08f, 0.06f), 0f, 0.7f);
                for (int sgn = -1; sgn <= 1; sgn += 2)
                    Build.Prim(PrimitiveType.Capsule, "Tooth", h, new Vector3(sgn * size * 0.15f, -size * 0.42f, z * 0.8f), new Vector3(size * 0.09f, size * 0.22f, size * 0.09f), fang, false)
                        .transform.localRotation = Quaternion.Euler(-25f, 0f, sgn * 12f);
                return;
            }
            if (headG == HSnake)
            {
                Dress(PrimitiveType.Sphere, "Skull", h, new Vector3(0f, 0f, size * 0.2f), new Vector3(size * 1.0f, size * 0.62f, size * 1.5f), headSkin);
                var eye = M("snake eye", new Color(0.9f, 0.75f, 0.2f), 1.6f);
                Build.Prim(PrimitiveType.Sphere, "Eye", h, new Vector3(-size * 0.33f, size * 0.12f, size * 0.55f), new Vector3(size * 0.16f, size * 0.16f, size * 0.1f), eye, false);
                Build.Prim(PrimitiveType.Sphere, "Eye", h, new Vector3(size * 0.33f, size * 0.12f, size * 0.55f), new Vector3(size * 0.16f, size * 0.16f, size * 0.1f), eye, false);
                return;
            }
            if (headG == HDog)
            {
                Dress(PrimitiveType.Sphere, "Skull", h, Vector3.zero, new Vector3(size * 0.85f, size * 0.85f, size), headSkin);
                Dress(PrimitiveType.Cube, "Upper jaw", h, new Vector3(0f, 0f, size * 0.8f), new Vector3(size * 0.5f, size * 0.2f, size * 1.05f), headSkin);
                var jaw = Dress(PrimitiveType.Cube, "Lower jaw", h, new Vector3(0f, -size * 0.3f, size * 0.7f), new Vector3(size * 0.42f, size * 0.14f, size * 0.95f), headSkin);
                jaw.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);
                mouths.Add(jaw.transform); mouthRest = size * 0.14f;
                var tooth = M("tooth", new Color(0.82f, 0.8f, 0.7f), 0.5f);
                for (int i = 0; i < 6; i++)
                    Build.Prim(PrimitiveType.Cube, "Tooth", h, new Vector3((i % 2 == 0 ? -0.17f : 0.17f) * size, -size * 0.14f, size * (0.45f + (i / 2) * 0.3f)), new Vector3(size * 0.05f, size * 0.14f, size * 0.05f), tooth, false);
                var dogEye = M("pale eye", new Color(0.9f, 0.9f, 0.8f), 1.6f);
                Build.Prim(PrimitiveType.Sphere, "Eye", h, new Vector3(-size * 0.27f, size * 0.2f, size * 0.37f), Vector3.one * size * 0.12f, dogEye, false);
                Build.Prim(PrimitiveType.Sphere, "Eye", h, new Vector3(size * 0.27f, size * 0.2f, size * 0.37f), Vector3.one * size * 0.12f, dogEye, false);
                return;
            }
            Dress(PrimitiveType.Sphere, "Skull", h, Vector3.zero, new Vector3(size, size * 1.2f, size), headSkin);
            if (headG == HGrandma)
            {
                // You only ever see the back of her head: grey hair in a bun. Turned away from you, always.
                Dress(PrimitiveType.Sphere, "Bun", h, new Vector3(0f, size * 0.25f, -size * 0.45f), Vector3.one * size * 0.42f, headSkin);
                if (!(torsoOn && plan == TwoLegs)) h.localRotation = Quaternion.Euler(0f, 180f, 0f);
                return;
            }
            if (headG == HWet)
            {
                var hair = M("wet hair", new Color(0.03f, 0.03f, 0.03f), 0f, 0.9f);
                for (int i = -3; i <= 3; i++)
                {
                    var strand = Dress(PrimitiveType.Capsule, "Hair", h, new Vector3(i * size * 0.09f, -size * 0.25f, z * 1.02f), new Vector3(size * 0.08f, size * 0.62f, size * 0.05f), hair);
                    strand.transform.localRotation = Quaternion.Euler(-6f, 0f, i * 2f);
                }
                Dress(PrimitiveType.Sphere, "Hair", h, new Vector3(0f, size * 0.15f, -size * 0.05f), new Vector3(size * 1.08f, size * 1.05f, size * 1.08f), hair);
                return;
            }
            if (headG == HMask)
            {
                Dress(PrimitiveType.Sphere, "Mask", h, new Vector3(0f, -size * 0.2f, z * 0.82f), new Vector3(size * 0.7f, size * 0.45f, size * 0.32f), M("mask", new Color(0.55f, 0.7f, 0.78f)));
                Dress(PrimitiveType.Sphere, "Cap", h, new Vector3(0f, size * 0.3f, -size * 0.04f), new Vector3(size * 1.06f, size * 0.7f, size * 1.06f), M("cap", new Color(0.55f, 0.66f, 0.62f), 0f, 0.15f));
            }
            if (skinG == SHoles) AddHoles(h, Vector3.zero, size * 0.45f, size * 0.5f, size * 0.42f, 3, 9);
            int face = look[Face];
            if (skinG == SShadow && face == 0) face = 1;          // a shadow is mostly eyes
            switch (headG == HMask && face == 2 ? 0 : face)
            {
                case 1:     // pale eyes, deep in their sockets
                    var socket = M("socket", new Color(0.02f, 0.012f, 0.012f), 0f, 0.3f);
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                        Build.Prim(PrimitiveType.Sphere, "Socket", h, new Vector3(sgn * size * 0.2f, size * 0.11f, z * 0.93f), new Vector3(size * 0.24f, size * 0.2f, size * 0.12f), socket, false);
                    var eye = M("pale eye", new Color(0.9f, 0.9f, 0.8f), 1.6f);
                    Build.Prim(PrimitiveType.Sphere, "Eye", h, new Vector3(-size * 0.2f, size * 0.12f, z), Vector3.one * size * 0.11f, eye, false);
                    Build.Prim(PrimitiveType.Sphere, "Eye", h, new Vector3(size * 0.2f, size * 0.12f, z), Vector3.one * size * 0.11f, eye, false);
                    break;
                case 2:     // a mouth and nothing else
                    var mm = Build.Prim(PrimitiveType.Sphere, "Mouth", h, new Vector3(0f, -size * 0.12f, z * 0.96f), new Vector3(size * 0.28f, size * 0.62f, size * 0.12f), Build.Mat(new Color(0.35f, 0.02f, 0.02f), 0.8f), false);
                    mouths.Add(mm.transform); mouthRest = size * 0.62f;
                    break;
                case 3:     // a smile
                    var tooth = Build.Mat(new Color(0.85f, 0.83f, 0.75f), 0.9f);
                    for (int i = -4; i <= 4; i++)
                    {
                        float x = i * size * 0.085f, y = -size * 0.22f + i * i * size * 0.016f;
                        Build.Prim(PrimitiveType.Cube, "Tooth", h, new Vector3(x, y, z * Mathf.Cos(i * 0.16f)), new Vector3(size * 0.06f, size * 0.09f, size * 0.03f), tooth, false);
                    }
                    break;
                case 4:     // too many eyes, scattered, none of them level
                    var many = M("pale eye", new Color(0.9f, 0.9f, 0.8f), 1.6f);
                    var er = new System.Random(look[Body] + 11);
                    for (int i = 0; i < 11; i++)
                    {
                        float ex = (float)(er.NextDouble() * 0.7 - 0.35) * size, ey = (float)(er.NextDouble() * 0.75 - 0.4) * size;
                        float ez = Mathf.Sqrt(Mathf.Max(0.001f, size * size * 0.25f - ex * ex - ey * ey * 0.7f));
                        Build.Prim(PrimitiveType.Sphere, "Eye", h, new Vector3(ex, ey, ez), Vector3.one * size * (0.05f + 0.06f * (float)er.NextDouble()), many, false);
                    }
                    break;
            }
        }

        /// <summary>
        /// Clusters of small, close-set holes on a curved surface (trypophobia). Each is a dark dent, never a
        /// wound. Placed deterministically so the same look always has the same pattern.
        /// </summary>
        void AddHoles(Transform parent, Vector3 centre, float rx, float rz, float ry, int clusters, int perCluster)
        {
            var hole = M("hole", new Color(0.12f, 0.05f, 0.05f), 0f, 0.6f);
            var rim = M("rim", new Color(0.72f, 0.58f, 0.52f), 0f, 0.4f);
            var rng = new System.Random(look[Body] * 31 + look[Head] * 7 + 3);
            for (int c = 0; c < clusters; c++)
            {
                float side = rng.NextDouble() < 0.5 ? 1f : -1f;
                float cx = (float)(rng.NextDouble() * 1.4 - 0.7) * rx, cy = (float)(rng.NextDouble() * 1.6 - 0.8) * ry;
                for (int i = 0; i < perCluster; i++)
                {
                    float ang = i * 2.39996f, rad = 0.022f * Mathf.Sqrt(i);          // a sunflower spiral: tightly packed, uneven
                    float x = cx + Mathf.Cos(ang) * rad, y = cy + Mathf.Sin(ang) * rad;
                    float u = Mathf.Clamp(x / Mathf.Max(0.01f, rx), -0.98f, 0.98f);
                    float z = side * rz * Mathf.Sqrt(1f - u * u);
                    float size = 0.014f + 0.01f * (float)rng.NextDouble();
                    var at = centre + new Vector3(x, y, z * 1.01f);
                    var normal = new Vector3(x / Mathf.Max(0.01f, rx * rx), 0f, z / Mathf.Max(0.01f, rz * rz)).normalized;
                    var r = Build.Prim(PrimitiveType.Sphere, "Rim", parent, at, new Vector3(size * 2.6f, size * 2.6f, size * 0.7f), rim, false);
                    r.transform.localRotation = Quaternion.LookRotation(normal);
                    var h = Build.Prim(PrimitiveType.Sphere, "Hole", parent, at + normal * 0.002f, new Vector3(size * 1.7f, size * 1.7f, size * 0.5f), hole, false);
                    h.transform.localRotation = Quaternion.LookRotation(normal);
                }
            }
        }

        // Sort every renderer into body or eyes: a shadow shows only its eyes some of the time.
        void Sort()
        {
            foreach (var r in rig.GetComponentsInChildren<Renderer>())
                if (r.enabled) (r.gameObject.name == "Eye" ? eyeRends : bodyRends).Add(r);
        }

        /// <summary>
        /// Melt the shapes into one body of flesh (see Flesh): limbs grow out of the torso, ribs and spine push up
        /// under the skin, and it all bends with the skeleton. Painted things (dolls, clowns) stay smoother.
        /// </summary>
        void Melt()
        {
            if (plan == Carpet && !torsoOn) return;           // hundreds of separate things: nothing to melt
            bool painted = skinG == SPorcelain || skinG == SPaint;
            bool clothed = skinG == SGown || skinG == SCoat || skinG == SCardigan;
            bool hard = skinG == SShell || skinG == SScales;
            float rough = painted ? 0.0015f : clothed ? 0.003f : hard ? 0.003f : 0.008f;
            float blend = plan == Tail ? 0.06f : painted ? 0.022f : plan == SpiderLegs || plan == Centipede ? 0.03f : 0.032f;
            float t0 = Time.realtimeSinceStartup;
            try
            {
                var body = Flesh.Skin(rig, skin, 0.025f, blend, rough, "Flesh");
                bool paintedHead = headG == HDoll || headG == HClown;
                if (headSkin != skin) Flesh.Skin(rig, headSkin, 0.016f, paintedHead ? 0.015f : 0.025f, rough * 0.6f, "Flesh (head)");
                if (body != null && GameDirector.I != null && (Time.realtimeSinceStartup - t0) > 0.25f)
                    GameDirector.I.Log("monster: melting the body took " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[NightPages] couldn't melt the monster's body (shapes left as they are): " + e);
                foreach (var r in rig.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            }
        }

        // ---------------------------------------------------------------- alive

        void Update()
        {
            if (rig == null) return;
            float t = Time.time + seed;
            int gait = look[Gait];

            // Changing in front of you: it pulls thin and shudders, becomes the new thing, and settles.
            Vector3 warp = Vector3.one;
            bool shudder = false;
            if (morphT >= 0f)
            {
                morphT += Time.deltaTime;
                const float half = 0.8f;
                if (morphT >= half && !morphSwapped)
                {
                    morphSwapped = true;
                    if (wanted != null) Rebuild(wanted);
                    wanted = null;
                }
                float k = morphT < half ? morphT / half : 1f - Mathf.Min(1f, (morphT - half) / half);
                warp = Vector3.Lerp(Vector3.one, new Vector3(0.55f, 1.3f, 0.55f), k) + Random.insideUnitSphere * 0.08f * k;
                shudder = k > 0.15f;
                if (morphT >= half * 2f) morphT = -1f;
                if (rig == null) return;
            }

            // How it is moving (whatever moves it: the house's hunts, the dream's, a test).
            Vector3 now = transform.position;
            float moved = hasLast ? (now - lastPos).magnitude : 0f;            // (up a wall counts)
            lastPos = now; hasLast = true;
            if (moved > 1.5f) moved = 0f;                                   // (it was put somewhere, it didn't walk there)
            float dtSafe = Mathf.Max(Time.deltaTime, 1e-4f);
            pace = Mathf.Lerp(pace, moved / dtSafe, Mathf.Clamp01(Time.deltaTime * 8f));
            float go = Mathf.Clamp01(pace / 1.2f);                          // 0 standing, 1 walking or faster
            // Stride: a step for every so many metres, faster and longer when it runs. Wrong, a little: it hitches.
            float stepLen = plan == SpiderLegs ? 0.55f : plan == OnHands ? 0.5f : plan == FourLegs ? 0.8f : 0.72f;
            stride += moved / stepLen * Mathf.PI * (1f + 0.25f * Mathf.Sin(t * 3.1f));

            // Everything breathes a little, so it never reads as a statue.
            float breathe = 1f + 0.015f * Mathf.Sin(t * 1.3f);
            rig.localScale = new Vector3(warp.x, warp.y * breathe, warp.z);
            // The body rises and falls with each step and leans into the way it goes.
            float bob = Mathf.Abs(Mathf.Sin(stride)) * 0.045f * go;
            rig.localPosition = new Vector3(Mathf.Sin(stride * 0.5f) * 0.025f * go, bob, 0f);
            if (chain.Count > 0) PoseSnake(t);
            for (int i = 0; i < swarm.Count; i++)
            {
                // Each crawler wanders a little around its place in the mass, never in step with its neighbours.
                Vector3 home = swarmHome[i];
                float p = i * 1.37f;
                float r = swarm[i].parent == rig ? 0.09f : 0.03f;
                swarm[i].localPosition = home + new Vector3(Mathf.Sin(t * 2.3f + p) * r, 0f, Mathf.Cos(t * 1.9f + p * 0.7f) * r);
                swarm[i].localRotation = Quaternion.Euler(90f, (t * 60f + p * 40f) % 360f, 0f);
            }
            if ((skinG == SDrowned || extraG == XDrips) && t > nextDrip)
            {
                nextDrip = t + Random.Range(0.5f, 1.4f);
                Sfx.At("drip", transform.position + Vector3.up * 0.1f, 0.4f, 0.9f + Random.value * 0.25f);
            }
            if (skinG == SShadow && !Transforming)                    // (while it changes, the shifting flesh is all there is)
            {
                // You can never quite hold it in view: the body comes and goes, the eyes stay.
                bool show = !hiddenAll && (Mathf.PerlinNoise(t * 1.6f, seed) > 0.5f || morphT >= 0f);
                foreach (var r in bodyRends) if (r != null) r.enabled = show;
            }
            for (int i = 0; i < legPivots.Count; i++)
            {
                // Many legs: alternate sets (a spider's are in two groups of four, opposite corners together), each
                // swinging forward through the air and dragging back along the floor.
                float ph = stride + ((i % 2 == 0) == (i < legPivots.Count / 2) ? 0f : Mathf.PI) + i * 0.35f;
                float swing = Mathf.Sin(ph) * 16f * go, lift = Mathf.Max(0f, Mathf.Cos(ph)) * 9f * go;
                legPivots[i].localRotation = Quaternion.Euler(Mathf.Sin(t * 2.2f + i * 1.7f) * (gait == 0 ? 1.5f : 5f) * (1f - go) - lift, swing, Mathf.Cos(t * 1.9f + i) * (gait == 0 ? 1f : 4f) * (1f - go));
            }
            for (int i = 0; i < hips.Count; i++)
            {
                // Two legs: hip forward and back, the knee folding as the foot comes through.
                float ph = stride + i * Mathf.PI;
                hips[i].localRotation = Quaternion.Euler(-Mathf.Sin(ph) * 30f * go, 0f, 0f);
                knees[i].localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(ph + 1.2f)) * 55f * go, 0f, 0f);
            }
            // Ribs that open, a little, and close again.
            if (extraG == XRibs)
            {
                float open = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 0.9f)), 6f) * 0.05f;
                for (int i = 0; i < ribs.Count; i++)
                    if (ribs[i] != null) ribs[i].localPosition = ribRest[i] + new Vector3(Mathf.Sign(ribRest[i].x) * open, 0f, open * 0.8f);
            }

            Quaternion sway = Quaternion.identity;
            if (gait == 1) sway = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.8f) * 5f);
            // Grandma's hair: whatever way the rest of the game turns it to face you, she has her back to you.
            rig.localRotation = headG == HGrandma && torsoOn && plan == TwoLegs ? sway * Quaternion.Euler(0f, 180f, 0f) : sway;

            Quaternion headExtra = Quaternion.identity;
            if (gait == 3) headExtra = Quaternion.Euler(0f, Mathf.Sin(t * 0.9f) * 38f, 0f);     // shaking its head, slowly
            // It copies your face, half a second late: your head's turn and tilt (mirrored, as in a mirror), your mouth.
            if (FaceReader.Present && (headG == HYou || headG == HBare || skinG == SShadow || headG == HDoll || extraG == XChestFace))
            {
                mimicYaw = Mathf.Lerp(mimicYaw, -FaceReader.HeadYaw, Time.deltaTime * 2f);
                mimicRoll = Mathf.Lerp(mimicRoll, -FaceReader.HeadRoll, Time.deltaTime * 2f);
                mimicJaw = Mathf.Lerp(mimicJaw, FaceReader.Jaw, Time.deltaTime * 2.5f);
                headExtra *= Quaternion.Euler(0f, mimicYaw, mimicRoll);
                foreach (var m in mouths) if (m != null) m.localScale = new Vector3(m.localScale.x, mouthRest * (1f + mimicJaw * 2.5f), m.localScale.z);
            }

            // It watches you. Within a few rooms' distance its head turns to follow you: slowly, mostly; now and
            // then it snaps round. Further than a head should turn.
            if (Player.I != null && neck != null && !Transforming)
            {
                Vector3 eye = Player.I.cam.transform.position;
                float far = (eye - neck.position).magnitude;
                float want = far < 9f ? 1f : 0f;
                watchW = Mathf.MoveTowards(watchW, want, Time.deltaTime * 0.8f);
                if (watchW > 0.01f && neck.parent != null)
                {
                    Vector3 d = neck.parent.InverseTransformDirection(eye - neck.position);
                    float yaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -110f, 110f);
                    float pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -45f, 45f);
                    if (t > nextSnap) { nextSnap = t + Random.Range(2.5f, 7f); snapUntil = t + 0.12f; }
                    float rate = t < snapUntil ? 30f : 2.2f;
                    watchYaw = Mathf.Lerp(watchYaw, yaw, Time.deltaTime * rate);
                    watchPitch = Mathf.Lerp(watchPitch, pitch, Time.deltaTime * rate);
                    headExtra = Quaternion.Euler(watchPitch * watchW, watchYaw * watchW, 0f) * headExtra;
                }
            }

            bool twitching = false;
            if (gait == 2)
            {
                if (t > nextTwitch) { twitchUntil = t + 0.09f; nextTwitch = t + Random.Range(0.5f, 2.6f); }
                twitching = t < twitchUntil;
            }
            twitching |= shudder;
            if (!torsoOn) return;
            if (neck != null)
                neck.localRotation = neckRest * headExtra * (twitching ? Quaternion.Euler(Random.Range(-30f, 30f), Random.Range(-40f, 40f), Random.Range(-35f, 35f)) : Quaternion.identity);
            for (int i = 0; i < arms.Count; i++)
            {
                var idle = Quaternion.Euler(Mathf.Sin(t * 0.7f + i) * 2.5f, 0f, 0f);
                // Walking: arms swing against the legs. On its hands: they ARE the legs, big strides, hand over hand.
                bool walksOnThem = plan == OnHands || look[Body] == 3;
                float aph = stride + (i % 2 == 0 ? Mathf.PI : 0f) + (i / 2) * 0.8f;
                idle *= Quaternion.Euler(Mathf.Sin(aph) * (walksOnThem ? 34f : 22f) * go, 0f, 0f);
                arms[i].localRotation = armRest[i] * idle * (twitching ? Quaternion.Euler(Random.Range(-25f, 25f), 0f, Random.Range(-20f, 20f)) : Quaternion.identity);
            }
        }

        bool hiddenAll;
        float watchW, watchYaw, watchPitch, nextSnap, snapUntil;
        float mimicYaw, mimicRoll, mimicJaw, mouthRest = 0.1f;
        readonly List<Transform> mouths = new List<Transform>();

        /// <summary>Show or hide the whole thing at once (for when it must be there before it's seen).</summary>
        public void SetVisible(bool on)
        {
            hiddenAll = !on;
            foreach (var r in bodyRends) if (r != null) r.enabled = on;
            foreach (var r in eyeRends) if (r != null) r.enabled = on;
        }

        void FreeMeshes()
        {
            if (rig == null) return;
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null) Destroy(smr.sharedMesh);
        }

        void OnDestroy() { FreeMeshes(); }

        public void FaceToward(Vector3 point)
        {
            transform.rotation = Quaternion.LookRotation(Vis.Flat(point - transform.position));
        }
    }
}
