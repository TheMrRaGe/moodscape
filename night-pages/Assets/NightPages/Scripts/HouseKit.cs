using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The house's working parts: what the chores are done with. A kitchen sink with a tap and a plug that holds water,
    /// a drying rack, an electric kettle on its base, a sponge, a floor cloth, the broom, the watering can, the bin and
    /// the dustbin out on the porch, the log basket, the grate and a box of matches, the quilt that's in a heap every
    /// morning, the hall clock's winding key, and the clocks on the walls. Built into each room as it's drawn (so it
    /// mirrors with the room), then set up each morning by the chores.
    /// </summary>
    public partial class House
    {
        // ---- the hall clock
        public Transform hallClock;
        public bool clockWound = true;
        public Work clockKey;
        ClockFace hallDial;

        // ---- the kitchen
        public Basin sink;
        public Source tap;
        public Knob tapKnob, plug, kettleSwitch, breaker;
        public Spot rack, kettleBase, hallTable, outsideBin, grate;
        public Tool sponge, rag, broom, wateringCan, kettle;
        public float kettleHeat;
        public bool kettleBoiled;
        public Movable myMug;
        public Tool mug;
        public bool mugCoffee, mugDrunk;
        public Work mugFill;
        public readonly List<Movable> dishes = new List<Movable>();
        public readonly List<Work> grit = new List<Work>();
        public Work ferns, parlourPlant;
        public Movable trashBag;
        Vector3 dishPile, bagSpot, gritHall, gritKitchen, letterSpot;
        Material dishMat;
        AudioSource boilSound;

        // ---- the living room
        public readonly List<Movable> logs = new List<Movable>();
        public bool fireLit = true;
        public Interactable matches;
        Vector3 logBasket;
        float fireCatch;

        // ---- the bedroom
        public Work quilt;
        Transform quiltT, pillowT;
        Vector3 quiltNeatPos, pillowNeatPos;
        Quaternion quiltNeatRot, pillowNeatRot;

        Vector3 W(Vector3 drawn) { return transform.TransformPoint(P(drawn)); }
        Vector3 Wn(Vector3 drawnDir) { return transform.TransformDirection(P(drawnDir)); }

        /// <summary>A small thing to pick up and work with (a box when there's no model for it).</summary>
        Tool Gadget(string name, string kind, Vector3 drawnFloor, Vector3 size, Color c, float mass, float capacity = 0f, string model = null)
        {
            Movable m = null;
            if (model != null)
            {
                float h = ModelSizes.All.ContainsKey(model) ? ModelSizes.All[model].y : size.y;
                m = ModelsLoose.Place(model, transform, P(drawnFloor), h, Yaw(0f), mass, Mathf.Max(size.x, size.z) * 1.6f);
            }
            if (m == null)
            {
                var g = Build.Box(name, transform, P(drawnFloor) + Vector3.up * size.y / 2f, size, c);
                m = Movable.Attach(g, mass);
            }
            m.gameObject.name = name;
            movables.Add(m);
            // The working end: the bottom for a broom or a sponge, near the top front for anything that pours.
            var col = m.GetComponent<BoxCollider>();
            Vector3 tip = Vector3.zero;
            if (col != null)
            {
                tip = col.center;
                if (kind == "broom") tip = col.center - Vector3.up * col.size.y * 0.47f;
                else if (capacity > 0f) tip = col.center + new Vector3(0f, col.size.y * 0.35f, col.size.z * 0.45f);
            }
            return Tool.On(m, kind, tip, capacity);
        }

        // ================================================================ the hall

        void HallKit(GameObject clock)
        {
            hallClock = clock.transform;
            // The dial and its winding key: turn the key eleven times and the clock goes again (and strikes the hours).
            Vector3 face = W(new Vector3(0.88f, 1.74f, 0f));
            hallDial = ClockFace.Make(transform, face, Wn(Vector3.left), 0.12f, new Color(0.9f, 0.86f, 0.74f), new Color(0.55f, 0.42f, 0.2f), false);
            var key = Build.Box("Winding key", transform, P(0.86f, 1.66f, 0.05f), new Vector3(0.04f, 0.03f, 0.012f), new Color(0.7f, 0.58f, 0.3f), false);
            var hitBox = key.AddComponent<BoxCollider>();
            hitBox.isTrigger = true;
            hitBox.size = new Vector3(4f, 7f, 16f);
            clockKey = Work.Make(key, "Turn", "the winding key", WorkMode.Turn, 11f, "hand");
            clockKey.done = clockKey.need;
            clockKey.finished = () =>
            {
                clockWound = true;
                hallDial.stopped = false;
                Clock(true);
                GameDirector.I.Log("wound the hall clock: it's going again");
            };

            // The hall table takes the post.
            hallTable = Spot.Make(new GameObject("Hall table (post)"), "Put the post on the hall table", "letters");
            hallTable.transform.SetParent(transform, false);
            hallTable.transform.localPosition = P(-1.12f, 0.92f, -2.3f);
            hallTable.Slot(Vector3.zero, Quaternion.Euler(0f, 90f, 0f));
            hallTable.reach = 0.9f;
            letterSpot = P(-1.12f, 0.92f, -2.3f);
        }

        /// <summary>The clock has run down: it stops ticking and striking and the hands stop where they are.</summary>
        public void ClockRunDown(float atMinutes)
        {
            clockWound = false;
            clockKey.done = 0f;
            hallDial.stopped = true;
            hallDial.stoppedAt = atMinutes;
            Clock(false);
        }

        // ================================================================ the kitchen

        void KitchenKit()
        {
            float top = 0.925f;
            var white = Build.Mat(new Color(0.93f, 0.92f, 0.88f), 0f, 0.7f);
            var chrome = Build.Mat(new Color(0.78f, 0.79f, 0.8f), 0f, 0.85f);
            dishMat = Build.Mat(new Color(0.94f, 0.92f, 0.86f), 0f, 0.65f);

            // The cupboards and worktop, with a deep white sink let into them (z -5.5 to -4.9 in the drawing).
            var cab = new Color(0.72f, 0.74f, 0.62f);
            furniture.Add(Box("Cabinets", new Vector3(-7.0f, 0.44f, -6.2f), new Vector3(0.8f, 0.88f, 1.4f), cab));
            furniture.Add(Box("Cabinets", new Vector3(-7.0f, 0.44f, -3.6f), new Vector3(0.8f, 0.88f, 2.6f), cab));
            Box("Under the sink", new Vector3(-7.0f, 0.37f, -5.2f), new Vector3(0.8f, 0.74f, 0.6f), cab);
            var wt = new Color(0.42f, 0.36f, 0.3f);
            Box("Worktop", new Vector3(-6.97f, 0.9f, -6.215f), new Vector3(0.88f, 0.05f, 1.43f), wt);
            Box("Worktop", new Vector3(-6.97f, 0.9f, -3.585f), new Vector3(0.88f, 0.05f, 2.63f), wt);
            Box("Worktop", new Vector3(-7.355f, 0.9f, -5.2f), new Vector3(0.11f, 0.05f, 0.6f), wt);
            Box("Worktop", new Vector3(-6.615f, 0.9f, -5.2f), new Vector3(0.17f, 0.05f, 0.6f), wt);
            // The basin: a floor and four sides, white glazed.
            float bottom = top - 0.18f;
            Box("Sink", new Vector3(-7.0f, bottom - 0.01f, -5.2f), new Vector3(0.6f, 0.02f, 0.6f), white);
            Box("Sink", new Vector3(-7.29f, bottom + 0.09f, -5.2f), new Vector3(0.02f, 0.2f, 0.6f), white);
            Box("Sink", new Vector3(-6.71f, bottom + 0.09f, -5.2f), new Vector3(0.02f, 0.2f, 0.6f), white);
            Box("Sink", new Vector3(-7.0f, bottom + 0.09f, -5.49f), new Vector3(0.6f, 0.2f, 0.02f), white);
            Box("Sink", new Vector3(-7.0f, bottom + 0.09f, -4.91f), new Vector3(0.6f, 0.2f, 0.02f), white);
            var basinGo = new GameObject("Basin");
            basinGo.transform.SetParent(transform, false);
            basinGo.transform.localPosition = P(-7.0f, bottom, -5.2f);
            sink = basinGo.AddComponent<Basin>();
            sink.bottom = transform.TransformPoint(P(-7.0f, bottom, -5.2f)).y;
            sink.inside = new Bounds(W(new Vector3(-7.0f, bottom + 0.15f, -5.2f)), new Vector3(0.58f, 0.32f, 0.58f));
            var waterMat = Build.Mat(new Color(0.55f, 0.62f, 0.62f, 0.55f), 0f, 0.95f);
            Work.Fade(waterMat);
            waterMat.color = new Color(0.5f, 0.58f, 0.6f, 0.55f);
            sink.water = Build.Box("Washing-up water", transform, P(-7.0f, bottom, -5.2f), new Vector3(0.56f, 0.004f, 0.56f), waterMat, false).transform;
            sink.water.gameObject.SetActive(false);

            // The plug on its chain: in or out.
            var plugGo = Build.Prim(PrimitiveType.Cylinder, "Plug", transform, P(-7.0f, bottom + 0.01f, -5.2f), new Vector3(0.06f, 0.006f, 0.06f), Build.Mat(new Color(0.1f, 0.1f, 0.1f)), false);
            var plugHit = plugGo.AddComponent<BoxCollider>();
            plugHit.size = new Vector3(2.4f, 30f, 2.4f);
            plug = Knob.Make(plugGo, null, Vector3.zero, Vector3.zero, "Put the plug in", "Pull the plug out", on =>
            {
                sink.plugged = on;
                plugGo.transform.localPosition = P(on ? -7.0f : -6.85f, bottom + 0.01f, on ? -5.2f : -5.0f);
            });
            plugGo.transform.localPosition = P(-6.85f, bottom + 0.01f, -5.0f);

            // The tap: a swan neck from the back of the worktop over the middle of the sink, one cross-head to turn it.
            Build.Prim(PrimitiveType.Cylinder, "Tap", transform, P(-7.36f, top + 0.13f, -5.2f), new Vector3(0.035f, 0.13f, 0.035f), chrome, false);
            var neck = Build.Box("Tap", transform, P(-7.24f, top + 0.27f, -5.2f), new Vector3(0.26f, 0.025f, 0.025f), chrome, false);
            neck.transform.localRotation = Quaternion.Euler(0f, Yaw(0f), 0f);
            var spout = new GameObject("Spout").transform;
            spout.SetParent(transform, false);
            spout.localPosition = P(-7.1f, top + 0.255f, -5.2f);
            var head = new GameObject("Tap head").transform;
            head.SetParent(transform, false);
            head.localPosition = P(-7.36f, top + 0.08f, -5.0f);
            var cross = new GameObject("Tap handle").transform;
            cross.SetParent(head, false);
            Build.Box("Tap handle bar", cross, Vector3.zero, new Vector3(0.07f, 0.012f, 0.012f), chrome, false);
            Build.Box("Tap handle bar", cross, Vector3.zero, new Vector3(0.012f, 0.012f, 0.07f), chrome, false);
            Build.Box("Tap stem", head, Vector3.down * 0.04f, new Vector3(0.016f, 0.08f, 0.016f), chrome, false);
            var tapHit = head.gameObject.AddComponent<BoxCollider>();
            tapHit.size = new Vector3(0.12f, 0.12f, 0.12f);
            var streamMat = Build.Mat(new Color(0.75f, 0.85f, 0.9f, 0.45f), 0.15f, 0.95f);
            Work.Fade(streamMat);
            streamMat.color = new Color(0.75f, 0.85f, 0.9f, 0.45f);
            var stream = Build.Prim(PrimitiveType.Cylinder, "Running water", transform, P(-7.1f, (top + 0.255f + bottom) / 2f, -5.2f), new Vector3(0.016f, (top + 0.255f - bottom) / 2f, 0.016f), streamMat, false);
            stream.SetActive(false);
            // What you hold a kettle or a can under: a small invisible space below the spout (no hand needed on the tap).
            var under = Build.Box("Under the tap", transform, P(-7.1f, top + 0.12f, -5.2f), new Vector3(0.24f, 0.24f, 0.24f), white, false);
            under.GetComponent<Renderer>().enabled = false;
            var underCol = under.AddComponent<BoxCollider>();
            underCol.isTrigger = true;
            tap = under.AddComponent<Source>();
            tap.spout = spout;
            tap.stream = stream;
            tap.basin = sink;
            sink.tap = tap;
            tapKnob = Knob.Make(head.gameObject, cross, Vector3.zero, new Vector3(0f, 90f, 0f), "Turn the tap on", "Turn the tap off", on =>
            {
                tap.Set(on);
                // Washing-up waiting and no plug in: you put it in without thinking, the way anyone would.
                if (on && !plug.on && DishesLeft > 0) { plug.Set(true); GameDirector.I.hud.Say("You put the plug in and run the hot.", 2.5f); }
            });
            choreTargets["dishes"] = tapKnob.GetComponent<Interactable>();

            // The sponge and the floor cloth on the back of the sink, washing-up liquid beside them.
            sponge = Gadget("Sponge", "sponge", new Vector3(-7.33f, top, -4.83f), new Vector3(0.06f, 0.03f, 0.1f), new Color(0.85f, 0.75f, 0.25f), 0.05f);
            rag = Gadget("Floor cloth", "rag", new Vector3(-6.75f, top, -4.7f), new Vector3(0.16f, 0.02f, 0.14f), new Color(0.62f, 0.6f, 0.52f), 0.1f);
            Thing("multi_cleaner_bottle", new Vector3(-7.32f, top, -5.62f), 0f, 0.4f);

            // The drying rack beside the sink: a tray and wires, six plates stand in it.
            Box("Drying rack", new Vector3(-7.0f, top + 0.012f, -5.92f), new Vector3(0.36f, 0.02f, 0.46f), new Color(0.5f, 0.42f, 0.32f));
            for (int i = 0; i < 7; i++)
            {
                Box("Rack wire", new Vector3(-7.13f, top + 0.08f, -6.13f + i * 0.07f), new Vector3(0.008f, 0.13f, 0.008f), chrome, false);
                Box("Rack wire", new Vector3(-6.87f, top + 0.05f, -6.13f + i * 0.07f), new Vector3(0.008f, 0.07f, 0.008f), chrome, false);
            }
            var rackGo = new GameObject("Rack");
            rackGo.transform.SetParent(transform, false);
            rackGo.transform.localPosition = P(-7.0f, top + 0.13f, -5.92f);
            rack = rackGo.AddComponent<Spot>();
            rack.verb = "Put it in the rack";
            rack.takes = new[] { "dish", "mug" };
            rack.accept = m => { var w = Tool.Scrub(m); return w == null || w.Finished; };
            rack.reach = 0.55f;
            for (int i = 0; i < 7; i++) rack.Slot(new Vector3(0f, 0f, -0.21f + i * 0.07f), Quaternion.Euler(0f, 0f, 90f));
            // The sink takes dirty things: they settle on the bottom, in the water once it's run.
            var inSink = new GameObject("In the sink");
            inSink.transform.SetParent(transform, false);
            inSink.transform.localPosition = P(-7.0f, bottom + 0.012f, -5.2f);
            var sinkSpot = inSink.AddComponent<Spot>();
            sinkSpot.verb = "Put it in the sink";
            sinkSpot.takes = new[] { "dish", "mug" };
            sinkSpot.accept = m => { var w = Tool.Scrub(m); return w != null && !w.Finished; };
            sinkSpot.reach = 0.45f;
            sinkSpot.Slot(new Vector3(-0.13f, 0f, -0.13f), Quaternion.identity);
            sinkSpot.Slot(new Vector3(0.13f, 0f, 0.13f), Quaternion.identity);
            sinkSpot.Slot(new Vector3(-0.13f, 0f, 0.13f), Quaternion.identity);
            sinkSpot.Slot(new Vector3(0.13f, 0f, -0.13f), Quaternion.identity);
            sinkSpot.Slot(new Vector3(0f, 0.03f, 0f), Quaternion.identity);
            dishPile = P(-6.95f, top, -4.5f);

            // The kettle on its base by the canisters: fill it, stand it on the base, switch it on.
            var baseGo = Build.Prim(PrimitiveType.Cylinder, "Kettle base", transform, P(-7.0f, top + 0.012f, -3.05f), new Vector3(0.18f, 0.012f, 0.18f), Build.Mat(new Color(0.15f, 0.15f, 0.15f), 0f, 0.5f), false);
            kettleBase = Spot.Make(baseGo, "Stand it on its base", "kettle");
            kettleBase.Slot(new Vector3(0f, 1f, 0f), Quaternion.identity);
            kettleBase.reach = 0.5f;
            var sw = Build.Box("Kettle switch", transform, P(-6.92f, top + 0.03f, -2.94f), new Vector3(0.03f, 0.02f, 0.02f), Build.Mat(new Color(0.1f, 0.1f, 0.1f)), false);
            var swHit = sw.AddComponent<BoxCollider>();
            swHit.size = new Vector3(3f, 4f, 4f);
            kettleSwitch = Knob.Make(sw, sw.transform, Vector3.zero, new Vector3(0f, 0f, 0f), "Switch the kettle on", "Switch the kettle off", on =>
            {
                sw.GetComponent<Renderer>().sharedMaterial = Build.Mat(on ? new Color(0.9f, 0.3f, 0.15f) : new Color(0.1f, 0.1f, 0.1f), on ? 1.2f : 0f);
                if (on && (kettle == null || !kettleBase.Holds(kettle.mov))) { GameDirector.I.hud.Say("Nothing on the base.", 2f); }
                else if (on && kettle.liquid < 0.15f) { GameDirector.I.hud.Say("It's empty. It would burn out.", 2.5f); }
            });
            choreTargets["coffee"] = kettleSwitch.GetComponent<Interactable>();
            kettle = Gadget("Kettle", "kettle", new Vector3(-7.0f, top + 0.024f, -3.05f), new Vector3(0.2f, 0.24f, 0.16f), new Color(0.6f, 0.62f, 0.64f), 0.9f, 1.5f, "vintage_electric_kettle");
            kettle.liquid = 0f;

            // Two mugs: hers (nobody uses it) and yours. Yours takes coffee, then hot water, then you drink it.
            Mug("Grandma's mug", new Vector3(-6.85f, top, -3.7f), new Color(0.85f, 0.8f, 0.6f));
            myMug = Mug("Your mug", new Vector3(-6.82f, top, -3.45f), new Color(0.3f, 0.45f, 0.55f));
            mug = Tool.On(myMug, "mug", Vector3.up * 0.04f, 0.3f);
            mug.holds = "coffee";
            var coffeeIn = Build.Box("Coffee", myMug.transform, new Vector3(0f, 1.014f, 0f), new Vector3(0.78f, 0.01f, 0.78f), Build.Mat(new Color(0.12f, 0.07f, 0.04f), 0f, 0.9f), false);
            coffeeIn.SetActive(false);
            mugFill = Work.Make(myMug.gameObject, "Pour", "your mug", WorkMode.Fill, 0.25f, "kettle");
            mugFill.liquid = "water";
            mugFill.can = () => mugCoffee && kettleBoiled && !mugDrunk;
            mugFill.why = () => !mugCoffee ? "Coffee in the mug first (the tin by the kettle)." : !kettleBoiled ? "Boil the kettle first." : "You've had it.";
            mugFill.finished = () => { coffeeIn.SetActive(true); GameDirector.I.Log("coffee: poured"); };
            var mugUse = myMug.GetComponent<Interactable>();
            mugUse.promptFn = () => mugFill.Finished && !mugDrunk ? "Drink your coffee" : !mugCoffee && !mugDrunk ? "Pick up your mug  (coffee: the brown tin by the kettle)" : "Pick up your mug";
            mugUse.use = () =>
            {
                if (mugFill.Finished && !mugDrunk)
                {
                    mugDrunk = true;
                    coffeeIn.SetActive(false);
                    Sfx.At("gasp", myMug.transform.position, 0.12f, 1.6f);
                    GameDirector.I.Log("coffee: drunk. The mug goes in the washing-up");
                    MakeDirty(myMug, "the mug");
                    if (CoffeeDrunk != null) CoffeeDrunk();
                }
                else if (Player.I != null) Player.I.PickUp(myMug);
            };
            mugResetCoffee = () => coffeeIn.SetActive(false);
            CoffeeTin();
            KitchenKitRest();
        }

        // The coffee tin (one of the canisters along the back) gives a spoonful.
        void CoffeeTin()
        {
            {
                foreach (var m in movables)
                {
                    if (m == null || m.name != "Coffee canister") continue;
                    var it = m.GetComponent<Interactable>();
                    var tin = m;
                    it.promptFn = () => !mugCoffee && !mugDrunk ? "Spoon coffee into your mug  (the coffee tin)" : "The coffee tin";
                    it.use = () =>
                    {
                        if (!mugCoffee && !mugDrunk)
                        {
                            if ((myMug.transform.position - tin.transform.position).magnitude > 1.4f) { GameDirector.I.hud.Say("Your mug's not here.", 2f); return; }
                            mugCoffee = true;
                            Sfx.At("scrub", tin.transform.position, 0.25f, 1.6f);
                            GameDirector.I.hud.Say("A spoonful. Then half another, the way he did.", 2.5f);
                        }
                        else if (Player.I != null) Player.I.PickUp(tin);
                    };
                }
            }
        }

        void KitchenKitRest()
        {
            // The broom and the dustpan in the corner; the watering can by the ferns.
            broom = Gadget("Broom", "broom", new Vector3(-7.05f, 0f, -7.62f), new Vector3(0.25f, 1.3f, 0.12f), new Color(0.55f, 0.4f, 0.22f), 0.8f, 0f, "wooden_broom");
            Thing("dustpan", new Vector3(-6.6f, 0f, -7.55f), 15f, 0.3f);
            wateringCan = Gadget("Watering can", "can", new Vector3(-2.75f, 0f, -7.35f), new Vector3(0.35f, 0.3f, 0.15f), new Color(0.45f, 0.5f, 0.45f), 0.6f, 2f, "watering_can_metal_01");

            // The fuse box gets a breaker you can reset.
            var lever = Build.Box("Breaker", transform, P(-4.5f, 1.55f, -0.29f), new Vector3(0.05f, 0.09f, 0.04f), Build.Mat(new Color(0.12f, 0.12f, 0.12f)), false);
            var leverHit = lever.AddComponent<BoxCollider>();
            leverHit.size = new Vector3(3f, 2f, 3f);
            breaker = Knob.Make(lever, lever.transform, new Vector3(-40f, 0f, 0f), new Vector3(40f, 0f, 0f), "Push the breaker back up", "Leave it be", on =>
            {
                var k = Find("kitchen");
                if (on && k != null) { k.darkUntil = 0f; if (fridgeHum != null) fridgeHum.Play(); GameDirector.I.Log("fuse box: reset the kitchen breaker; the fridge starts humming again"); }
            });
            breaker.Quiet(true);

            bagSpot = P(-2.3f, 0f, -6.5f);
            gritKitchen = P(-3.6f, 0f, -2.2f);
            // A clock over the kitchen door, and one on the study wall later.
            ClockFace.Make(transform, W(new Vector3(-2.6f, 2.15f, -0.12f)), Wn(Vector3.back), 0.14f, new Color(0.95f, 0.93f, 0.86f), new Color(0.75f, 0.2f, 0.15f));
        }

        public System.Action CoffeeDrunk;

        // A mug: a glazed cylinder with a handle (one thing to pick up).
        Movable Mug(string name, Vector3 drawnFloor, Color c)
        {
            var glaze = Build.Mat(c, 0f, 0.7f);
            var g = Build.Prim(PrimitiveType.Cylinder, name, transform, P(drawnFloor) + Vector3.up * 0.05f, new Vector3(0.085f, 0.05f, 0.085f), glaze, true);
            FlatBottom(g);
            var handle = Build.Box("Handle", g.transform, new Vector3(0.62f, 0.05f, 0f), new Vector3(0.18f, 1.1f, 0.12f), glaze, false);
            handle.transform.localScale = new Vector3(0.22f, 1.05f, 0.14f);
            Build.Box("Inside", g.transform, new Vector3(0f, 1.006f, 0f), new Vector3(0.84f, 0.01f, 0.84f), Build.Mat(c * 0.55f, 0f, 0.5f), false);
            g.transform.Rotate(0f, Random.Range(0f, 360f), 0f);          // the handle wherever it was left
            var m = Movable.Attach(g, 0.3f);
            movables.Add(m);
            return m;
        }

        readonly List<Transform> flames = new List<Transform>();

        // Flames and embers in the grate (only while the fire's lit): a few glowing, flickering shapes.
        void Flames()
        {
            var ember = Build.Mat(new Color(1f, 0.35f, 0.08f), 2.2f, 0.1f);
            var flame = Build.Mat(new Color(1f, 0.62f, 0.2f), 3f, 0.1f);
            for (int i = 0; i < 5; i++)
            {
                var f = Build.Box(i < 2 ? "Embers" : "Flame", grate.transform, new Vector3(0f, i < 2 ? 0.02f : 0.12f, -0.18f + i * 0.09f),
                    i < 2 ? new Vector3(0.08f, 0.03f, 0.3f) : new Vector3(0.05f, 0.2f, 0.07f), i < 2 ? ember : flame, false);
                f.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                flames.Add(f.transform);
            }
        }
        System.Action mugResetCoffee;
        AudioSource fridgeHum;

        /// <summary>The kitchen's power goes (the fuse chore): the fridge falls silent and the light won't come on.</summary>
        public void TripBreaker()
        {
            breaker.Set(false);
            var k = Find("kitchen");
            if (k != null) k.darkUntil = float.MaxValue;
            if (fridgeHum != null) fridgeHum.Stop();
        }

        /// <summary>Give something a grimy layer and a "scrub it" job (a plate, the mug after coffee).</summary>
        public Work MakeDirty(Movable m, string what)
        {
            var old = Tool.Scrub(m);
            if (old != null) { old.done = 0f; foreach (var r in old.fades) if (r != null) { r.enabled = true; var c = r.material.color; c.a = 0.85f; r.material.color = c; } return old; }
            var col = m.GetComponent<Collider>() as BoxCollider;
            Vector3 c0 = col != null ? col.center : Vector3.zero, s0 = col != null ? col.size : Vector3.one * 0.1f;
            var grime = Work.Grime(m.transform, c0 + Vector3.up * (s0.y * 0.5f + 0.0015f / Mathf.Max(0.001f, m.transform.lossyScale.y)), new Vector3(s0.x * 0.75f, 0.003f / Mathf.Max(0.001f, m.transform.lossyScale.y), s0.z * 0.75f), new Color(0.38f, 0.27f, 0.14f, 0.85f));
            var w = m.gameObject.AddComponent<Work>();
            w.verb = "Scrub"; w.what = what; w.mode = WorkMode.Rub; w.need = 650f; w.tools = new[] { "sponge" };
            w.area = m.GetComponent<Collider>();
            w.fades.Add(grime);
            w.can = () => sink.Wet(m.transform.position) && !m.held;
            w.why = () => sink.level < 0.15f ? "Fill the sink to wash it (plug in, tap on)." : "Put it in the water to wash it.";
            w.finished = () => Sfx.At("drip", m.transform.position, 0.2f, 1.3f);
            if (!dishes.Contains(m)) dishes.Add(m);
            if (Tool.Of(m) == null) Tool.On(m, "dish", Vector3.zero);
            return w;
        }

        /// <summary>Last night's washing-up on the worktop by the sink.</summary>
        public void DirtyDishes(int n)
        {
            foreach (var d in dishes) if (d != null && d != myMug) Destroy(d.gameObject);
            dishes.RemoveAll(d => d == null || d != myMug);
            for (int i = 0; i < n; i++)
            {
                bool bowl = i % 4 == 3;
                // Stacked the way people stack: off-centre, the bowls on top, not square.
                Vector3 at = dishPile + Vector3.up * (0.006f + i * (bowl ? 0.03f : 0.016f)) + new Vector3(Random.Range(-0.045f, 0.045f), 0f, Random.Range(-0.045f, 0.045f));
                var g = Build.Prim(PrimitiveType.Cylinder, bowl ? "Bowl" : "Plate", transform, at, bowl ? new Vector3(0.15f, 0.025f, 0.15f) : new Vector3(0.23f, 0.007f, 0.23f), dishMat, true);
                FlatBottom(g);
                g.transform.localRotation = Quaternion.Euler(Random.Range(-3f, 3f), Random.Range(0f, 360f), Random.Range(-3f, 3f));
                var m = Movable.Attach(g, 0.35f);
                m.Rest();
                Tool.On(m, "dish", Vector3.zero);
                MakeDirty(m, bowl ? "the bowl" : "the plate");
            }
        }

        public int DishesLeft
        {
            get
            {
                int n = 0;
                foreach (var d in dishes)
                {
                    if (d == null || !d.gameObject.activeInHierarchy) continue;
                    var w = Tool.Scrub(d);
                    bool clean = w == null || w.Finished;
                    if (!clean || !rack.Holds(d)) n++;
                }
                return n;
            }
        }

        public void ResetCoffee()
        {
            kettle.liquid = 0f; kettle.hot = false; kettleBoiled = false; kettleHeat = 0f;
            mugCoffee = mugDrunk = false;
            mugFill.done = 0f;
            if (mugResetCoffee != null) mugResetCoffee();
            kettleSwitch.Set(false);
        }

        public void PutOutTheRubbish()
        {
            if (trashBag != null) Destroy(trashBag.gameObject);
            Movable m = ModelsLoose.Place("trashbag", transform, bagSpot, 0.6f, Random.Range(0f, 360f), 2f, 0.6f);
            if (m == null) m = Movable.Attach(Build.Box("Bag of rubbish", transform, bagSpot + Vector3.up * 0.3f, new Vector3(0.42f, 0.6f, 0.38f), new Color(0.05f, 0.05f, 0.06f)), 2f);
            m.gameObject.name = "Bag of rubbish";
            Tool.On(m, "bag", Vector3.zero);
            trashBag = m;
            choreTargets["trash"] = m.GetComponent<Interactable>();
        }

        public bool RubbishOut { get { return trashBag == null || !trashBag.gameObject.activeSelf; } }

        /// <summary>Dust and grit on a floor, to sweep up (the broom). Returns the job.</summary>
        public Work Grit(Vector3 at, float size, string what, Color c, string[] tools, float need = 800f)
        {
            var g = new GameObject("Dirt");
            g.transform.SetParent(transform, false);
            g.transform.localPosition = at;
            var col = g.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(size, 0.12f, size * 0.8f);
            col.center = new Vector3(0f, 0.06f, 0f);
            var w = Work.Make(g, tools[0] == "broom" ? "Sweep" : "Wipe", what, WorkMode.Rub, need, tools);
            w.removeWhenDone = true;
            for (int i = 0; i < 9; i++)
            {
                var r = Work.Grime(g.transform, new Vector3(Random.Range(-0.4f, 0.4f) * size, 0.004f, Random.Range(-0.3f, 0.3f) * size), new Vector3(Random.Range(0.08f, 0.22f) * size, 0.003f, Random.Range(0.06f, 0.2f) * size), c);
                r.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                w.fades.Add(r);
            }
            grit.Add(w);
            return w;
        }

        public void Sweepings()
        {
            foreach (var w in grit) if (w != null) Destroy(w.gameObject);
            grit.Clear();
            Grit(gritHall, 1.0f, "the grit by the door", new Color(0.28f, 0.22f, 0.16f, 0.8f), new[] { "broom" });
            Grit(gritKitchen, 0.9f, "the crumbs on the floor", new Color(0.4f, 0.32f, 0.2f, 0.8f), new[] { "broom" });
        }

        // ================================================================ the living room

        void LivingKit(GameObject fireplace)
        {
            logBasket = P(6.55f, 0f, -6.35f);
            var gr = new GameObject("Grate");
            gr.transform.SetParent(transform, false);
            gr.transform.localPosition = P(6.92f, 0.14f, -4f);
            grate = gr.AddComponent<Spot>();
            grate.verb = "Lay it in the grate";
            grate.takes = new[] { "log" };
            grate.reach = 0.75f;
            for (int i = 0; i < 3; i++) grate.Slot(new Vector3(0f, 0.05f * (i / 2), -0.2f + i * 0.2f), Quaternion.Euler(0f, 0f, 90f));
            // The matches on the mantel.
            var box = Box("Matches", new Vector3(7.05f, 1.29f, -3.6f), new Vector3(0.06f, 0.02f, 0.035f), new Color(0.75f, 0.55f, 0.2f));
            matches = Build.Usable(box, "");
            matches.promptFn = () => fireLit ? "A box of matches" : grate.Count >= 2 ? "Light the fire" : "Light the fire (it needs logs first)";
            matches.use = () =>
            {
                if (fireLit) return;
                if (grate.Count < 2) { GameDirector.I.hud.Say("Not with nothing in the grate. The logs are in the basket.", 2.8f); return; }
                Sfx.At("strike", box.transform.position, 0.6f);
                fireCatch = Time.time + 2.2f;
                fireLit = true;
                StartCoroutine(Catch());
            };
            choreTargets["fire"] = matches;
            Flames();
            // Living room plant.
            parlourPlant = WateredPlant(ParlourPlantGo, "the plant by the window");
        }

        GameObject ParlourPlantGo;

        System.Collections.IEnumerator Catch()
        {
            yield return new WaitForSeconds(2.2f);
            Sfx.At("whoosh", fire.transform.position, 0.6f);
            if (fireLoop != null && !fireLoop.isPlaying) fireLoop.Play();
            GameDirector.I.Log("the fire is lit");
            if (FireLit != null) FireLit();
        }

        public System.Action FireLit;

        /// <summary>Morning: last night's fire is ash. Logs in the basket.</summary>
        public void FireOut()
        {
            fireLit = false;
            if (fireLoop != null) fireLoop.Stop();
            foreach (var l in logs) if (l != null) Destroy(l.gameObject);
            logs.Clear();
            var bark = Tex.Real("pine_bark", 0.4f, 0.4f, 0.5f, new Color(0.55f, 0.45f, 0.38f), Tex.Boards);
            for (int i = 0; i < 4; i++)
            {
                var g = Build.Prim(PrimitiveType.Cylinder, "Log", transform, logBasket + new Vector3(Random.Range(-0.08f, 0.08f), 0.12f + i * 0.09f, Random.Range(-0.1f, 0.1f)), new Vector3(0.09f, 0.18f, 0.09f), bark, true);
                g.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 90f);
                FlatBottom(g);
                var m = Movable.Attach(g, 1.2f);
                Tool.On(m, "log", Vector3.zero);
                logs.Add(m);
            }
        }

        Work WateredPlant(GameObject plant, string what)
        {
            if (plant == null) return null;
            var w = Work.Make(plant, "Water", what, WorkMode.Fill, 0.6f, "can");
            w.liquid = "water";
            w.finished = () => Sfx.At("drip", plant.transform.position + Vector3.up * 0.3f, 0.25f, 0.8f);
            return w;
        }

        // ================================================================ the bedroom

        void BedroomKit(GameObject bedGo, GameObject pillowGo)
        {
            quiltT = bedGo.transform;
            pillowT = pillowGo.transform;
            quiltNeatPos = quiltT.localPosition; quiltNeatRot = quiltT.localRotation;
            pillowNeatPos = pillowT.localPosition; pillowNeatRot = pillowT.localRotation;
            var area = new GameObject("Quilt (making the bed)");
            area.transform.SetParent(transform, false);
            area.transform.localPosition = quiltNeatPos + Vector3.up * 0.12f;
            var col = area.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(1.1f, 0.3f, 2.0f);
            area.transform.localRotation = quiltNeatRot;
            quilt = Work.Make(area, "Smooth", "the covers", WorkMode.Rub, 700f, "hand");
            quilt.done = quilt.need;
            quilt.progress = p => Neaten(p);
            choreTargets["bed"] = bed;
            // A clock on the study wall (it's built after the study: the drawing is the study's).
        }

        public void RumpleBed()
        {
            quilt.done = 0f;
            Neaten(0f);
        }

        void Neaten(float p)
        {
            // In a heap: the quilt shoved down and twisted, the pillow on the floor half under the bed.
            quiltT.localPosition = Vector3.Lerp(quiltNeatPos + new Vector3(0.08f, 0.05f, -0.35f), quiltNeatPos, p);
            quiltT.localRotation = Quaternion.Slerp(quiltNeatRot * Quaternion.Euler(4f, 11f, -3f), quiltNeatRot, p);
            quiltT.localScale = Vector3.Lerp(new Vector3(1.04f, 0.34f, 1.5f), new Vector3(1.0f, 0.22f, 1.9f), p);
            pillowT.localPosition = Vector3.Lerp(pillowNeatPos + new Vector3(0.55f, -0.5f, -0.2f), pillowNeatPos, Mathf.Clamp01(p * 1.6f));
            pillowT.localRotation = Quaternion.Slerp(pillowNeatRot * Quaternion.Euler(0f, 35f, 80f), pillowNeatRot, Mathf.Clamp01(p * 1.6f));
        }

        // ================================================================ out on the porch

        void PorchKit()
        {
            Plain();
            // The dustbin by the front step: galvanised, with its lid.
            var tin = Build.Mat(new Color(0.55f, 0.56f, 0.55f), 0f, 0.5f);
            var bin = Build.Prim(PrimitiveType.Cylinder, "Dustbin", transform, new Vector3(2.75f, 0.4f, 8.55f), new Vector3(0.5f, 0.4f, 0.5f), tin, true);
            Build.Prim(PrimitiveType.Cylinder, "Dustbin lid", transform, new Vector3(2.75f, 0.82f, 8.55f), new Vector3(0.54f, 0.02f, 0.54f), tin, false);
            outsideBin = Spot.Make(bin, "Put it in the dustbin", "bag");
            outsideBin.keep = false;
            outsideBin.reach = 0.9f;
            outsideBin.placed = m => { Sfx.At("clunk", bin.transform.position + Vector3.up * 0.8f, 0.6f); GameDirector.I.Log("the rubbish is out"); };
            gritHall = new Vector3(0.1f, 0f, 6.9f);
        }

        // ================================================================ every frame

        void KitTick()
        {
            if (kettle == null) return;
            bool onBase = kettleBase.Holds(kettle.mov);
            bool heating = kettleSwitch.on && onBase && kettle.liquid >= 0.15f;
            if (kettleSwitch.on && !heating && (!onBase || kettle.liquid < 0.15f)) kettleSwitch.Set(false);
            if (heating)
            {
                kettleHeat = Mathf.Min(1f, kettleHeat + Time.deltaTime / 35f);
                if (boilSound == null) { boilSound = Sfx.Loop("boil", transform, 0f, true); boilSound.maxDistance = 9f; }
                boilSound.transform.position = kettle.transform.position;
                boilSound.volume = 0.05f + kettleHeat * 0.4f;
                if (!boilSound.isPlaying) boilSound.Play();
                if (kettleHeat >= 1f)
                {
                    kettleSwitch.Set(false);
                    kettle.hot = true;
                    kettleBoiled = true;
                    GameDirector.I.Log("the kettle has boiled");
                }
            }
            else if (boilSound != null && boilSound.isPlaying) boilSound.Stop();
            if (!heating) kettleHeat = Mathf.Max(0f, kettleHeat - Time.deltaTime / 300f);
            for (int i = 0; i < flames.Count; i++)
            {
                var f = flames[i];
                if (f == null) continue;
                bool on = fireLit && Time.time > fireCatch + (i < 2 ? 0.5f : 1.5f);
                if (f.gameObject.activeSelf != on) f.gameObject.SetActive(on);
                if (on && i >= 2) f.localScale = new Vector3(0.05f, 0.14f + 0.12f * Mathf.PerlinNoise(Time.time * 4f, i * 3.1f), 0.07f);
            }
            if (fire != null && !fireLit) fire.intensity = 0f;
            else if (fire != null && Time.time < fireCatch + 4f) fire.intensity *= Mathf.Clamp01((Time.time - fireCatch) / 4f);
        }
    }
}
