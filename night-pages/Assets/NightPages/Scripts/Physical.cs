using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    public enum Matter { Wood, Ceramic, Glass, Metal, Paper, Cloth, Food, Plastic, Egg }

    /// <summary>
    /// What a loose thing is made of, and so how it behaves when it hits something: a cup clinks and, dropped hard
    /// enough, breaks into pieces you have to sweep up (and whatever was in it goes on the floor); a tin clangs; a
    /// book flops. Loud knocks are noise the house hears. Bumping into a table rattles what's on it, a slammed door
    /// shakes the shelves, and small things on the floor get kicked when you walk into them.
    /// Every Movable gets one (Movable.Attach).
    /// </summary>
    public class Physical : MonoBehaviour
    {
        public static readonly List<Physical> All = new List<Physical>();
        public static void ResetStatics() { All.Clear(); breaks = 0; listening = false; }
        static int breaks;
        static bool listening;

        public Matter matter = Matter.Wood;
        public float breakSpeed = 999f;
        public Movable mov;
        /// <summary>Thrown by the player: it may break even if it's only just been put in the world.</summary>
        public bool thrown;
        float born, lastSound, lastKick;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
        // Things are named after they're made (a model is placed, then called what it is), so look again once named.
        void Start() { Rename(); }

        public static Physical On(Movable m)
        {
            var p = m.gameObject.AddComponent<Physical>();
            p.mov = m;
            p.born = Time.time;
            p.matter = Guess(m.name, m.mass);
            p.breakSpeed = p.matter == Matter.Glass ? 4.4f : p.matter == Matter.Ceramic ? 4.3f : p.matter == Matter.Egg ? 2.2f : 999f;
            return p;
        }

        /// <summary>Called when a thing is renamed after it was made (models are named after they're placed).</summary>
        public void Rename()
        {
            matter = Guess(name, mov != null ? mov.mass : 1f);
            breakSpeed = matter == Matter.Glass ? 4.4f : matter == Matter.Ceramic ? 4.3f : matter == Matter.Egg ? 2.2f : 999f;
        }

        static bool Has(string n, params string[] words) { foreach (var w in words) if (n.Contains(w)) return true; return false; }

        public static Matter Guess(string name, float mass)
        {
            string n = name.ToLowerInvariant().Replace('_', ' ');
            if (Has(n, "egg")) return Matter.Egg;
            if (Has(n, "glass", "bottle", "jar", "lightbulb", "light bulb", "spectacles", "decanter", "lantern", "tumbler", "wine")) return Has(n, "plastic", "gallon", "cleaner") ? Matter.Plastic : Matter.Glass;
            if (Has(n, "enamel", "brass", "tin", "can ", " can", "pan", "kettle", "iron", "metal", "bucket", "candlestick", "pocket watch", "key", "spoon", "fork", "knife", "lamp", "dustpan", "mousetrap spring")) return Matter.Metal;
            if (Has(n, "plate", "bowl", "mug", "cup", "saucer", "vase", "ceramic", "porcelain", "teapot", "jug", "figurine", "pot ", "ashtray", "soap dish")) return Matter.Ceramic;
            if (Has(n, "book", "letter", "paper", "page", "newspaper", "envelope", "post", "magazine", "card", "notebook", "manuscript", "photo", "folder")) return Matter.Paper;
            if (Has(n, "towel", "cloth", "rag", "soap", "cardigan", "toilet roll", "bread", "cushion", "pillow", "coat", "slipper", "sock", "quilt", "blanket", "scarf", "teddy", "sponge", "knitting", "wool", "hat", "glove", "apron", "shoe", "boot")) return Matter.Cloth;
            if (Has(n, "apple", "banana", "lemon", "orange", "bread", "loaf", "potato", "onion", "fruit", "cheese", "carrot", "pear")) return Matter.Food;
            if (Has(n, "plastic", "gallon", "cleaner", "rubbish", "bag", "toilet roll", "bucket plastic")) return Matter.Plastic;
            return Matter.Wood;
        }

        bool CanBreak
        {
            get
            {
                if (breakSpeed > 100f || mov == null || mov.held) return false;
                string n = name.ToLowerInvariant();
                if (n.Contains("mug")) return false;                                 // the mugs carry the morning; they survive
                var t = GetComponent<Tool>();
                if (t != null && (t.kind == "kettle" || t.kind == "can" || t.kind == "mug")) return false;
                return thrown || Time.time - born > 3f;
            }
        }

        bool Settling { get { return !thrown && (Time.time - born < 2.5f || Time.timeSinceLevelLoad < 4f); } }

        void OnCollisionEnter(Collision c)
        {
            if (broken || mov == null || mov.held) return;
            float v = c.relativeVelocity.magnitude;
            if (v < 0.45f || Settling) return;
            Vector3 at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            // Something resting where it was put, hit by something moving: it isn't fixed there any more.
            if (mov.Body != null && mov.Body.isKinematic && v > 0.8f) mov.Wake();
            if (v > breakSpeed && CanBreak) { Break(at, v); return; }
            if (Time.time - lastSound > 0.09f) { Knock(at, v); lastSound = Time.time; }
            if (v > 2.2f) thrown = false;
        }

        static readonly string[] SoundOf = { "knock", "clink", "clink", "clang", "flop", "flop", "flop", "knock", "flop" };

        void Knock(Vector3 at, float v)
        {
            float m = mov != null ? mov.mass : 0.5f;
            float vol = Mathf.Clamp01(v / 4.5f) * Mathf.Clamp(0.25f + m * 0.35f, 0.25f, 1f);
            if (matter == Matter.Glass) vol *= 0.7f;
            float pitch = Mathf.Clamp(1.35f - m * 0.25f, 0.55f, 1.6f) * (0.92f + Random.value * 0.16f);
            if (matter == Matter.Glass) pitch *= 1.25f;
            if (matter == Matter.Plastic) pitch *= 1.6f;
            Sfx.At(SoundOf[(int)matter], at, vol, pitch);
            // Loud enough to carry through the house.
            float loud = Mathf.Clamp01((v - 1.8f) / 5f) * Mathf.Clamp01(m / 0.8f + 0.3f);
            if (matter == Matter.Paper || matter == Matter.Cloth || matter == Matter.Food) loud *= 0.4f;
            if (loud > 0.2f) Noise.Make(at, loud, name + " hit the floor");
        }

        // ---------------------------------------------------------------- breaking

        bool broken;
        public void Break(Vector3 at, float v)
        {
            if (broken || this == null || !gameObject.activeInHierarchy) return;
            broken = true;
            var g = GameDirector.I;
            string label = Label(name);
            Bounds b = new Bounds(transform.position, Vector3.one * 0.08f);
            bool any = false;
            Color col = new Color(0.9f, 0.88f, 0.82f);
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; col = ColorOf(r); } else b.Encapsulate(r.bounds);
            }
            // The floor (or worktop) it broke on.
            Vector3 floor = at;
            RaycastHit hit;
            if (Physics.Raycast(at + Vector3.up * 0.2f, Vector3.down, out hit, 3f, ~0, QueryTriggerInteraction.Ignore)) floor = hit.point;

            Sfx.At("crack", at, 0.85f, matter == Matter.Glass ? 1.3f : 0.95f + Random.value * 0.1f);
            if (matter != Matter.Egg) Noise.Make(at, Mathf.Clamp01(0.45f + v * 0.04f), "a " + label + " smashing");

            // Whatever was in it.
            var tool = GetComponent<Tool>();
            var held = GetComponent<Contents>();
            if (held != null && held.litres > 0.01f) Liquid.Spill(floor + Vector3.up * 0.05f, held.litres, held.kind, transform);
            else if (tool != null && tool.Container && tool.liquid > 0.01f) Liquid.Spill(floor + Vector3.up * 0.05f, tool.liquid, tool.holds, transform);

            if (matter == Matter.Egg) Liquid.Spill(floor, 0.06f, "egg");
            else Pieces(label, at, floor, b, col, v, g);

            if (g != null)
            {
                g.Log("broke: " + label + " (hit at " + v.ToString("0.0") + " m/s)");
                if (g.house != null) g.house.movables.Remove(mov);
            }
            Destroy(gameObject);
        }

        void Pieces(string label, Vector3 at, Vector3 floor, Bounds b, Color col, float v, GameDirector g)
        {
            // The pieces fly a little and then lie where they land; the area round them is what you sweep.
            var root = new GameObject("Broken " + label);
            root.transform.position = floor;
            var area = root.AddComponent<BoxCollider>();
            area.isTrigger = true;
            area.center = new Vector3(0f, 0.06f, 0f);
            area.size = new Vector3(1.1f, 0.14f, 1.1f);
            var mat = Build.Mat(col, 0f, matter == Matter.Glass ? 0.95f : 0.65f);
            if (matter == Matter.Glass) { Work.Fade(mat); mat.color = new Color(col.r, col.g, col.b, 0.55f); }
            float size = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z), 0.06f, 0.3f);
            int n = Mathf.Clamp(Mathf.RoundToInt(size * 40f), 5, 12);
            var bodies = new List<Rigidbody>();
            for (int i = 0; i < n; i++)
            {
                float s = size * Random.Range(0.12f, 0.38f);
                var piece = Build.Box("Piece of " + label, root.transform, Vector3.zero, new Vector3(s, Mathf.Max(0.004f, s * Random.Range(0.12f, 0.35f)), s * Random.Range(0.5f, 1f)), mat, true);
                piece.transform.position = at + Vector3.up * 0.03f + Random.insideUnitSphere * size * 0.3f;
                piece.transform.rotation = Random.rotation;
                var rb = piece.AddComponent<Rigidbody>();
                rb.mass = 0.02f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                Vector3 fly = Random.insideUnitSphere; fly.y = Mathf.Abs(fly.y) * 0.6f + 0.2f;
                rb.velocity = fly * Mathf.Clamp(v * 0.35f, 0.6f, 2.2f);
                rb.angularVelocity = Random.insideUnitSphere * 20f;
                bodies.Add(rb);
            }
            // A dusting of tiny bits that don't fly: they mark where it happened.
            for (int i = 0; i < 8; i++)
            {
                var bit = Build.Box("Bit", root.transform, Vector3.zero, new Vector3(0.012f, 0.003f, 0.01f) * Random.Range(0.6f, 1.6f), mat, false);
                bit.transform.position = floor + new Vector3(Random.Range(-0.25f, 0.25f), 0.002f, Random.Range(-0.25f, 0.25f));
                bit.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            }
            var w = Work.Make(root, "Sweep up", "the broken " + label, WorkMode.Rub, 380f, "broom");
            w.removeWhenDone = true;
            w.finished = () => { if (g != null) g.Log("swept up the broken " + label); };
            var settle = root.AddComponent<Settle>();
            settle.bodies = bodies;
            if (g != null && g.chores != null && g.house != null)
            {
                var room = g.house.RoomAt(floor);
                Vector3 where = floor;
                g.chores.Mess("broken" + (++breaks), "Sweep up the broken " + label + (room != null ? " (" + room.name + ")" : ""),
                    () => w == null || w.Finished, () => where, "You sweep up every piece you can find. There's always one more, later, under your foot.");
            }
        }

        /// <summary>After the pieces have landed: they stop being physical (so they don't jitter or get kicked forever).</summary>
        class Settle : MonoBehaviour
        {
            public List<Rigidbody> bodies;
            float t;
            void Update()
            {
                t += Time.deltaTime;
                if (t < 2.5f) return;
                foreach (var rb in bodies)
                {
                    if (rb == null) continue;
                    rb.isKinematic = true;
                    var c = rb.GetComponent<Collider>();
                    if (c != null) c.isTrigger = true;           // still hit by the broom's aim, never trips anyone
                }
                enabled = false;
            }
        }

        public static string Label(string name)
        {
            string n = name;
            int k = n.IndexOf(" (");
            if (k > 0) n = n.Substring(0, k);
            n = n.Replace('_', ' ').Trim();
            if (n.EndsWith(" 01") || n.EndsWith(" 02") || n.EndsWith(" 03")) n = n.Substring(0, n.Length - 3);
            return n.ToLowerInvariant();
        }

        static Color ColorOf(Renderer r)
        {
            var m = r.sharedMaterial;
            if (m == null) return new Color(0.9f, 0.88f, 0.82f);
            if (m.HasProperty("_Color")) return m.color;
            return new Color(0.9f, 0.88f, 0.82f);
        }

        // ---------------------------------------------------------------- being knocked about

        /// <summary>Shake the loose things within `radius` of `center` (a table bumped, a door slammed).</summary>
        public static int Jostle(Vector3 center, float radius, float strength, float above = -10f)
        {
            int n = 0;
            foreach (var p in All.ToArray())
            {
                if (p == null || p.mov == null || p.mov.held || p.mov.Heavy) continue;
                Vector3 d = p.transform.position - center;
                if (d.y < above) continue;
                d.y = 0f;
                if (d.magnitude > radius) continue;
                var rb = p.mov.Body;
                if (rb == null) continue;
                if (rb.isKinematic && Hands.I != null && Hands.I.Holding(p.mov)) continue;
                p.mov.Wake();
                float k = strength * (1f - d.magnitude / radius * 0.6f) * Random.Range(0.6f, 1.2f);
                Vector3 push = new Vector3(Random.Range(-1f, 1f), Random.Range(0.3f, 0.8f), Random.Range(-1f, 1f)) * k;
                rb.AddForce(push * rb.mass, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * k * rb.mass * 0.05f, ForceMode.Impulse);
                if (Time.time - p.lastSound > 0.15f && k > 0.25f)
                {
                    p.lastSound = Time.time;
                    Sfx.At(SoundOf[(int)p.matter], p.transform.position, Mathf.Clamp01(k * 0.35f), 1.1f + Random.value * 0.3f);
                }
                n++;
            }
            return n;
        }

        /// <summary>The player walked into it: small things get kicked along, chairs shift a little.</summary>
        public void Kicked(Vector3 dir, float speed)
        {
            if (mov == null || mov.held || mov.Body == null || Time.time - lastKick < (mov.Heavy ? 1f : 0.25f)) return;
            if (mov.Heavy && speed < 2f) return;
            lastKick = Time.time;
            mov.Wake();
            var rb = mov.Body;
            dir.y = 0f;
            dir.Normalize();
            if (mov.Heavy)
            {
                rb.AddForce(dir * speed * 0.35f * Mathf.Min(rb.mass, 2f), ForceMode.Impulse);
                Sfx.At("drag", transform.position, 0.12f, 1.3f + Random.value * 0.2f);
                return;
            }
            rb.AddForce((dir * speed * 0.55f + Vector3.up * 0.6f) * rb.mass, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * rb.mass * 0.3f, ForceMode.Impulse);
            thrown = speed > 3.5f;          // running into a vase can break it
        }
    }

    /// <summary>
    /// Liquid in something that isn't a pouring tool (a glass of water by the bed, your coffee once it's made).
    /// It goes over if the thing is knocked over, and onto the floor if the thing breaks.
    /// </summary>
    public class Contents : MonoBehaviour
    {
        public string kind = "water";
        public float litres = 0.2f;
        /// <summary>The visible surface of it (hidden when it's gone).</summary>
        public GameObject surface;
        public System.Action emptied;
        Movable mov;

        public static Contents In(GameObject g, string kind, float litres, GameObject surface)
        {
            var c = g.GetComponent<Contents>() ?? g.AddComponent<Contents>();
            c.kind = kind; c.litres = litres; c.surface = surface;
            if (surface != null) surface.SetActive(true);
            return c;
        }

        void Update()
        {
            if (litres <= 0.001f) return;
            if (mov == null) mov = GetComponent<Movable>();
            float tilt = Vector3.Angle(transform.up, Vector3.up);
            if (tilt < 84f) return;
            // Going over: most of it at once, the rest in a dribble.
            float out_ = Mathf.Min(litres, Mathf.Max(litres * 0.85f, Time.deltaTime * 0.3f));
            litres -= out_;
            Vector3 lip = transform.position + transform.up * 0.05f;
            Liquid.Spill(lip, out_, kind, transform);
            if (litres <= 0.001f)
            {
                litres = 0f;
                if (surface != null) surface.SetActive(false);
                if (emptied != null) emptied();
            }
        }
    }
}
