using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// A thing you work with: a sponge, a cloth, the broom, the watering can, the kettle, a log, a plate. Sits on a
    /// Movable (so you can pick it up). Containers hold a liquid (water, coffee) and pour it when tipped.
    /// </summary>
    public class Tool : MonoBehaviour
    {
        public string kind;
        public float liquid, capacity;
        public string holds = "water";
        /// <summary>The working end, in the tool's own space: the broom's bristles, the sponge, the spout.</summary>
        public Vector3 tip;
        public Movable mov;
        public bool hot;

        public Vector3 Tip { get { return transform.TransformPoint(tip); } }
        public bool Container { get { return capacity > 0f; } }
        public float Full { get { return capacity > 0f ? liquid / capacity : 0f; } }

        public static Tool On(Movable m, string kind, Vector3 tip, float capacity = 0f)
        {
            var t = m.gameObject.AddComponent<Tool>();
            t.kind = kind;
            t.tip = tip;
            t.capacity = capacity;
            t.mov = m;
            return t;
        }

        public static Tool Of(Movable m) { return m == null ? null : m.GetComponent<Tool>(); }

        /// <summary>The scrubbing job on a thing (a mug also has a pouring one), or null.</summary>
        public static Work Scrub(Component c)
        {
            if (c == null) return null;
            foreach (var w in c.GetComponents<Work>()) if (w.mode == WorkMode.Rub) return w;
            return null;
        }
    }

    public enum WorkMode { Rub, Turn, Fill }

    /// <summary>
    /// Something that takes work: a dirty plate (scrub it), shards on the floor (sweep them), the quilt (smooth it),
    /// the clock's winding key (turn it), a fern (water it). Has a trigger collider for its area.
    ///   Rub:  any back-and-forth with the right tool (on a flat screen, hold the left mouse button and move the mouse;
    ///         in VR, rub the tool in your hand over it). `need` is how much rubbing, in degrees of mouse travel.
    ///   Turn: circles (mouse circles, or turning your wrist in VR). `need` is in whole turns.
    ///   Fill: pour a liquid onto/into it. `need` is in litres.
    /// </summary>
    public class Work : MonoBehaviour
    {
        public static readonly List<Work> All = new List<Work>();
        public static void ResetStatics() { All.Clear(); }

        public string verb = "Scrub", what = "it";
        public string[] tools = { "sponge" };
        public WorkMode mode = WorkMode.Rub;
        public string liquid = "water";
        public float need = 600f, done;
        public System.Func<bool> can;
        public System.Func<string> why;
        public System.Action<float> progress;
        public System.Action finished;
        public Collider area;
        /// <summary>Renderers that fade out as it's cleaned (dirt, shards, a smear).</summary>
        public readonly List<Renderer> fades = new List<Renderer>();
        public bool removeWhenDone;

        public float Progress { get { return Mathf.Clamp01(done / Mathf.Max(0.0001f, need)); } }
        public bool Finished { get { return done >= need; } }
        public bool Ready { get { return can == null || can(); } }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public bool Takes(string toolKind)
        {
            foreach (var t in tools) if (t == toolKind) return true;
            return false;
        }

        public static Work Make(GameObject g, string verb, string what, WorkMode mode, float need, params string[] tools)
        {
            var w = g.AddComponent<Work>();
            w.verb = verb; w.what = what; w.mode = mode; w.need = need; w.tools = tools;
            w.area = g.GetComponent<Collider>();
            if (w.area == null)
            {
                var b = g.AddComponent<BoxCollider>();
                b.isTrigger = true;
                w.area = b;
            }
            if (!All.Contains(w)) All.Add(w);
            return w;
        }

        public void Add(float amount)
        {
            if (Finished || amount <= 0f) return;
            float before = Progress;
            done = Mathf.Min(need, done + amount);
            float p = Progress;
            foreach (var r in fades)
            {
                if (r == null) continue;
                var c = r.material.color;
                c.a = 1f - p;
                r.material.color = c;
                if (p > 0.97f) r.enabled = false;
            }
            if (progress != null) progress(p);
            // A little sound now and then while you're at it.
            if (mode == WorkMode.Rub && Mathf.FloorToInt(before * 9f) != Mathf.FloorToInt(p * 9f))
                Sfx.At(tools.Length > 0 && tools[0] == "broom" ? "drag" : "scrub", transform.position, tools.Length > 0 && tools[0] == "broom" ? 0.12f : 0.3f, 0.85f + Random.value * 0.4f);
            if (mode == WorkMode.Turn && Mathf.FloorToInt(before * need * 2f) != Mathf.FloorToInt(p * need * 2f))
                Sfx.At("ratchet", transform.position, 0.5f, 0.95f + Random.value * 0.1f);
            if (Finished)
            {
                foreach (var r in fades) if (r != null) r.enabled = false;
                if (finished != null) finished();
                if (removeWhenDone) gameObject.SetActive(false);
            }
        }

        /// <summary>Make something look like it needs cleaning: a darker, grimier layer that fades as you work.</summary>
        public static Renderer Grime(Transform on, Vector3 localPos, Vector3 size, Color c)
        {
            var g = Build.Box("Grime", on, localPos, size, Build.Mat(c, 0f, 0.15f), false);
            var r = g.GetComponent<Renderer>();
            Fade(r.material);
            r.material.color = c;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        /// <summary>Switch a Standard material to transparent (so it can fade).</summary>
        public static void Fade(Material m)
        {
            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
        }

        public string Prompt(string toolKind)
        {
            if (!Ready && why != null) return why();
            string how = mode == WorkMode.Turn ? "move the mouse in circles" : mode == WorkMode.Fill ? "tip it" : "move the mouse";
            int pct = Mathf.RoundToInt(Progress * 100f);
            string n = mode == WorkMode.Turn ? "  (" + Mathf.FloorToInt(done) + " of " + Mathf.RoundToInt(need) + ")" : pct > 0 ? "  (" + pct + "%)" : "";
            return "[Hold left mouse]  " + verb + " " + what + ": " + how + n;
        }
    }

    /// <summary>A tap. When it's running, a container held under it fills, and the sink below it fills if it's plugged.</summary>
    public class Source : MonoBehaviour
    {
        public static readonly List<Source> All = new List<Source>();
        public static void ResetStatics() { All.Clear(); }
        public bool running;
        public Transform spout;
        public float rate = 0.35f;              // litres a second
        public Basin basin;
        public GameObject stream;
        AudioSource sound;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void Set(bool on)
        {
            running = on;
            if (stream != null) stream.SetActive(on);
            if (on && sound == null) { sound = Sfx.Loop("water", transform, 0.35f, true); sound.transform.position = spout.position; sound.maxDistance = 9f; }
            if (sound != null) { if (on && !sound.isPlaying) sound.Play(); else if (!on) sound.Stop(); }
        }
    }

    /// <summary>A sink: a plug, water that rises while the tap runs and the plug is in, and drains when it's pulled.</summary>
    public class Basin : MonoBehaviour
    {
        public Source tap;
        public bool plugged;
        public float level;                     // 0..1
        public Transform water;
        public Bounds inside;                    // house space... kept in world space
        public float bottom, depth = 0.17f;

        public bool Contains(Vector3 world)
        {
            var b = inside;
            return b.Contains(world);
        }

        public bool Wet(Vector3 world) { return Contains(world) && world.y < bottom + depth * level + 0.04f && level > 0.15f; }

        void Update()
        {
            float dt = Time.deltaTime;
            if (tap != null && tap.running && plugged) level = Mathf.Min(1f, level + dt * 0.055f);
            else if (!plugged) level = Mathf.Max(0f, level - dt * 0.09f);
            if (water != null)
            {
                water.gameObject.SetActive(level > 0.02f);
                var p = water.position;
                p.y = bottom + depth * level;
                water.position = p;
            }
        }
    }

    /// <summary>
    /// A place something goes: the drying rack, the kettle's base, the grate, the bin outside, the hall table.
    /// Put the right thing down near it (E on a flat screen, let go in VR) and it settles into a free slot.
    /// </summary>
    public class Spot : MonoBehaviour
    {
        public static readonly List<Spot> All = new List<Spot>();
        public static void ResetStatics() { All.Clear(); }
        public string[] takes;
        public System.Func<Movable, bool> accept;
        public System.Action<Movable> placed;
        public string verb = "Put it here";
        public float reach = 0.6f;
        /// <summary>Where things go (local space). Each holds one thing.</summary>
        public readonly List<Vector3> slots = new List<Vector3>();
        public readonly List<Quaternion> slotRots = new List<Quaternion>();
        readonly List<Movable> inSlot = new List<Movable>();
        public bool keep = true;                 // false: the thing is taken away once it's in (the bin)

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static Spot Make(GameObject g, string verb, params string[] takes)
        {
            var s = g.AddComponent<Spot>();
            s.verb = verb;
            s.takes = takes;
            if (!All.Contains(s)) All.Add(s);
            return s;
        }

        public void Slot(Vector3 local, Quaternion rot) { slots.Add(local); slotRots.Add(rot); inSlot.Add(null); }

        public bool Wants(Movable m)
        {
            var t = Tool.Of(m);
            if (t == null) return false;
            bool kindOk = false;
            foreach (var k in takes) if (k == t.kind) kindOk = true;
            if (!kindOk) return false;
            if (accept != null && !accept(m)) return false;
            return FreeSlot() >= 0 || slots.Count == 0;
        }

        int FreeSlot()
        {
            for (int i = 0; i < inSlot.Count; i++) if (inSlot[i] == null || inSlot[i].held || (inSlot[i].transform.position - transform.TransformPoint(slots[i])).magnitude > 0.15f) return i;
            return -1;
        }

        public int Count { get { int n = 0; for (int i = 0; i < inSlot.Count; i++) if (inSlot[i] != null && !inSlot[i].held && (inSlot[i].transform.position - transform.TransformPoint(slots[i])).magnitude < 0.15f) n++; return n; } }
        public bool Holds(Movable m) { int i = inSlot.IndexOf(m); return i >= 0 && !m.held && (m.transform.position - transform.TransformPoint(slots[i])).magnitude < 0.15f; }

        public void Place(Movable m)
        {
            int i = FreeSlot();
            if (i >= 0)
            {
                inSlot[i] = m;
                // Set down by hand: never quite the same way twice.
                var jitter = Quaternion.Euler(Random.Range(-4f, 4f), Random.Range(-10f, 10f), Random.Range(-3f, 3f));
                m.Teleport(transform.TransformPoint(slots[i] + new Vector3(Random.Range(-0.012f, 0.012f), 0f, Random.Range(-0.008f, 0.008f))), transform.rotation * slotRots[i] * jitter);
                m.Rest();
            }
            Sfx.At("putdown", m.transform.position, 0.45f, 0.95f + Random.value * 0.1f);
            if (placed != null) placed(m);
            if (!keep) m.gameObject.SetActive(false);
        }

        /// <summary>The spot that wants this thing near `at`, if any.</summary>
        public static Spot For(Movable m, Vector3 at)
        {
            Spot best = null;
            float bestD = float.MaxValue;
            foreach (var s in All)
            {
                if (s == null || !s.isActiveAndEnabled || !s.Wants(m)) continue;
                float d = (s.transform.position - at).magnitude;
                if (d < s.reach && d < bestD) { best = s; bestD = d; }
            }
            return best;
        }
    }

    /// <summary>Something that turns on and off: a tap, a hob, the kettle's switch, a breaker. Use it to flip it.</summary>
    public class Knob : MonoBehaviour
    {
        public bool on;
        public Transform turn;
        public Vector3 offEuler, onEuler;
        public System.Action<bool> changed;
        public string onPrompt = "Turn it off", offPrompt = "Turn it on";

        public static Knob Make(GameObject g, Transform turn, Vector3 offEuler, Vector3 onEuler, string offPrompt, string onPrompt, System.Action<bool> changed)
        {
            var k = g.AddComponent<Knob>();
            k.turn = turn; k.offEuler = offEuler; k.onEuler = onEuler; k.changed = changed;
            k.offPrompt = offPrompt; k.onPrompt = onPrompt;
            var it = Build.Usable(g, "");
            it.promptFn = () => k.on ? k.onPrompt : k.offPrompt;
            it.use = () => k.Set(!k.on);
            k.Show();
            return k;
        }

        public void Set(bool v)
        {
            if (on == v) return;
            on = v;
            Show();
            Sfx.At("click", transform.position, 0.4f, on ? 1.15f : 0.95f);
            if (changed != null) changed(on);
        }

        /// <summary>Set it without a sound or a callback (building the house, a new morning).</summary>
        public void Quiet(bool v) { on = v; Show(); }

        void Show() { if (turn != null) turn.localRotation = Quaternion.Euler(on ? onEuler : offEuler); }
    }

    /// <summary>
    /// Your hands. On a flat screen: what you're carrying, and the left mouse button to work with it (scrub, sweep,
    /// turn, pour, fill at the tap). In VR: grip to pick things up with either hand, and the work happens by itself
    /// when the tool in your hand touches what needs it (rub, pour by tipping, fill by holding under the tap).
    /// </summary>
    public class Hands : MonoBehaviour
    {
        public static Hands I;
        Player p;
        public Work working;            // what the flat-screen hand is working on this frame
        public Vector3 workPoint;
        Vector2 lastDir;
        float turnAcc;
        AudioSource pourSound;
        public float pourTilt;          // 0..1, how far the carried thing is tipped (flat screen)

        // VR: what each hand is holding.
        public readonly Movable[] held = new Movable[2];
        readonly Vector3[] heldPos = new Vector3[2];
        readonly Quaternion[] heldRot = new Quaternion[2];
        readonly Vector3[] lastHandPos = new Vector3[2];
        readonly Vector3[] handVel = new Vector3[2];
        float lastRoll;

        public static Hands Attach(Player player)
        {
            var h = player.gameObject.AddComponent<Hands>();
            h.p = player;
            I = h;
            return h;
        }

        /// <summary>Flat screen: called by Player each frame it can act. Returns a prompt, or null. Sets `working`.</summary>
        public string FlatScreen(Movable carrying, Vector3 eye, Vector3 aim, float lookDegrees, Vector2 look, float dt)
        {
            working = null;
            filling = false;
            pourTilt = Mathf.MoveTowards(pourTilt, 0f, dt * 3f);
            var tool = Tool.Of(carrying);
            string kind = carrying == null ? "hand" : tool != null ? tool.kind : null;
            Work w = null;
            Source tap = null;
            float wd = float.MaxValue, td = float.MaxValue;
            Vector3 wp = Vector3.zero;
            foreach (var h in Physics.RaycastAll(eye, aim, 2.4f, ~0, QueryTriggerInteraction.Collide))
            {
                if (h.collider.transform.IsChildOf(p.transform)) continue;
                if (carrying != null && h.collider.transform.IsChildOf(carrying.transform)) continue;
                if (kind != null && h.distance < wd)
                    foreach (var ww in h.collider.GetComponentsInParent<Work>())
                        if (ww != null && ww.enabled && !ww.Finished && ww.Takes(kind)) { w = ww; wd = h.distance; wp = h.point; break; }
                var s = h.collider.GetComponentInParent<Source>();
                if (s != null && h.distance < td) { tap = s; td = h.distance; }
            }
            bool pressing = In.Held(Btn.Work);
            // Pouring something onto something that wants it.
            if (w != null && w.mode == WorkMode.Fill)
            {
                if (tool == null || !tool.Container || tool.liquid <= 0.001f || tool.holds != w.liquid)
                    return tool != null && tool.Container ? "It's empty." : null;
                if (!w.Ready) return w.why != null ? w.why() : null;
                if (!pressing) { StopPour(); return "[Hold left mouse]  Pour onto " + w.what; }
                pourTilt = Mathf.MoveTowards(pourTilt, 1f, dt * 4f);
                if (pourTilt > 0.6f)
                {
                    float amount = Mathf.Min(tool.liquid, dt * 0.25f);
                    tool.liquid -= amount;
                    w.Add(amount);
                    Pouring(tool.Tip);
                }
                working = w;
                workPoint = wp;
                return "Pouring onto " + w.what + "  (" + Mathf.RoundToInt(w.Progress * 100f) + "%)";
            }
            StopPour();
            // Filling a container at the tap.
            if (tap != null && tool != null && tool.Container)
            {
                if (!tap.running) return "Turn the tap on to fill it.";
                if (tool.liquid >= tool.capacity - 0.01f) return "It's full.";
                if (!pressing) return "[Hold left mouse]  Hold it under the tap";
                tool.holds = "water";
                tool.liquid = Mathf.Min(tool.capacity, tool.liquid + dt * tap.rate);
                workPoint = tap.spout.position + Vector3.down * 0.18f;
                working = null;
                filling = true;
                return "Filling  (" + Mathf.RoundToInt(tool.Full * 100f) + "%)";
            }
            if (w == null) return null;
            if (!w.Ready) return w.why != null ? w.why() : null;
            if (pressing)
            {
                working = w;
                workPoint = wp;
                if (w.mode == WorkMode.Rub) w.Add(look.magnitude * 2.2f);
                else if (w.mode == WorkMode.Turn)
                {
                    // Circles: keep adding the turn of the mouse's direction while it keeps turning the same way.
                    if (look.sqrMagnitude > 0.02f)
                    {
                        Vector2 d = look.normalized;
                        if (lastDir != Vector2.zero)
                        {
                            float a = Vector2.SignedAngle(lastDir, d);
                            if (Mathf.Abs(a) < 100f)
                            {
                                if (Mathf.Sign(a) != Mathf.Sign(turnAcc) && Mathf.Abs(turnAcc) > 20f) turnAcc = 0f;
                                turnAcc += a;
                                if (Mathf.Abs(turnAcc) >= 36f) { w.Add(Mathf.Abs(turnAcc) / 360f); turnAcc = 0f; }
                            }
                        }
                        lastDir = d;
                    }
                }
            }
            else lastDir = Vector2.zero;
            return w.Prompt(kind);
        }

        bool filling;
        public bool Filling { get { return filling; } }
        /// <summary>Busy with the mouse (scrubbing, turning, pouring, filling): the view barely moves.</summary>
        public bool Busy { get { return In.Held(Btn.Work) && (working != null || filling); } }

        void Pouring(Vector3 at)
        {
            if (pourSound == null) { pourSound = Sfx.Loop("pour", transform, 0.35f, true); pourSound.maxDistance = 6f; }
            pourSound.transform.position = at;
            if (!pourSound.isPlaying) pourSound.Play();
        }

        void StopPour() { if (pourSound != null && pourSound.isPlaying) pourSound.Stop(); }

        // ---------------------------------------------------------------- VR

        void Update()
        {
            if (!VRRig.Active || VRRig.I == null) { DropAll(); return; }
            var hud = Hud.I;
            bool canAct = !p.frozen && (hud == null || hud.modal == Modal.None);
            Transform[] hands = { VRRig.I.leftHand, VRRig.I.rightHand };
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            bool anyPour = false;
            for (int i = 0; i < 2; i++)
            {
                var hand = hands[i];
                handVel[i] = Vector3.Lerp(handVel[i], (hand.position - lastHandPos[i]) / dt, 0.5f);
                lastHandPos[i] = hand.position;
                bool gripDown = i == 0 ? VRRig.GripDownL : VRRig.GripDownR;
                bool gripHeld = i == 0 ? VRRig.GripL : VRRig.GripR;
                if (held[i] == null && gripDown && canAct) Grab(i, hand);
                else if (held[i] != null && (!gripHeld || !canAct)) Release(i);
                var m = held[i];
                if (m != null)
                {
                    m.transform.SetPositionAndRotation(hand.TransformPoint(heldPos[i]), hand.rotation * heldRot[i]);
                    var tool = Tool.Of(m);
                    if (tool != null) anyPour |= VRWork(tool, handVel[i], dt);
                }
                else if (gripHeld && canAct)
                {
                    // An empty hand pressed flat on something (smoothing the quilt).
                    foreach (var w in Work.All)
                        if (w != null && !w.Finished && w.Takes("hand") && w.mode == WorkMode.Rub && w.Ready && w.area != null && w.area.bounds.Contains(hand.position))
                            w.Add(handVel[i].magnitude * dt * 1800f);
                }
            }
            if (!anyPour) StopPour();
            // Turning: the right trigger held on a winding key, and the controller turned like a key.
            float roll = VRRig.I.rightHand.localEulerAngles.z;
            float dRoll = Mathf.DeltaAngle(lastRoll, roll);
            lastRoll = roll;
            if (VRRig.Held(Btn.Interact) && canAct)
                foreach (var w in Work.All)
                    if (w != null && !w.Finished && w.mode == WorkMode.Turn && w.Ready && w.area != null && (w.area.ClosestPoint(VRRig.I.rightHand.position) - VRRig.I.rightHand.position).magnitude < 0.2f)
                        w.Add(Mathf.Abs(dRoll) / 360f);
        }

        bool VRWork(Tool tool, Vector3 vel, float dt)
        {
            Vector3 tip = tool.Tip;
            // Rubbing.
            foreach (var w in Work.All)
            {
                if (w == null || w.Finished || w.mode != WorkMode.Rub || !w.Takes(tool.kind) || !w.Ready || w.area == null) continue;
                var b = w.area.bounds;
                b.Expand(0.06f);
                if (b.Contains(tip)) w.Add(vel.magnitude * dt * 1800f);
            }
            if (!tool.Container) return false;
            // Filling under a running tap.
            foreach (var s in Source.All)
                if (s != null && s.running && s.spout != null && Vector3.Distance(Vis.Level(s.spout.position), Vis.Level(tool.transform.position)) < 0.14f && tool.transform.position.y < s.spout.position.y)
                {
                    tool.holds = "water";
                    tool.liquid = Mathf.Min(tool.capacity, tool.liquid + dt * s.rate);
                }
            // Pouring: tipped past about 70 degrees, onto whatever is below the spout.
            float tilt = Vector3.Angle(tool.transform.up, Vector3.up);
            if (tilt < 70f || tool.liquid <= 0.001f) return false;
            float amount = Mathf.Min(tool.liquid, dt * 0.25f * Mathf.InverseLerp(70f, 120f, tilt) + dt * 0.05f);
            tool.liquid -= amount;
            RaycastHit hit;
            if (Physics.Raycast(tip, Vector3.down, out hit, 0.9f, ~0, QueryTriggerInteraction.Collide))
            {
                var w = hit.collider.GetComponentInParent<Work>();
                if (w != null && w.mode == WorkMode.Fill && w.liquid == tool.holds && w.Ready) w.Add(amount);
            }
            Pouring(tip);
            return true;
        }

        void Grab(int i, Transform hand)
        {
            Movable best = null;
            float bestD = 0.14f;
            foreach (var c in Physics.OverlapSphere(hand.position, 0.12f, ~0, QueryTriggerInteraction.Ignore))
            {
                var m = c.GetComponentInParent<Movable>();
                if (m == null || m.held || m.Heavy) continue;
                float d = (c.ClosestPoint(hand.position) - hand.position).magnitude;
                if (d < bestD) { best = m; bestD = d; }
            }
            if (best == null) return;
            held[i] = best;
            best.SetHeld(true);
            heldPos[i] = hand.InverseTransformPoint(best.transform.position);
            heldRot[i] = Quaternion.Inverse(hand.rotation) * best.transform.rotation;
            Sfx.At("pickup", best.transform.position, 0.4f, 0.9f + Random.value * 0.2f);
        }

        void Release(int i)
        {
            var m = held[i];
            held[i] = null;
            if (m == null) return;
            var spot = Spot.For(m, m.transform.position);
            m.SetHeld(false);
            if (spot != null) { spot.Place(m); return; }
            if (m.Body != null) m.Body.velocity = Vector3.ClampMagnitude(handVel[i], 4f);
        }

        void DropAll() { for (int i = 0; i < 2; i++) if (held[i] != null) Release(i); }

        public bool Holding(Movable m) { return held[0] == m || held[1] == m; }
    }
}
