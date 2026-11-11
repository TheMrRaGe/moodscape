using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    public class Chore
    {
        public string id, text, room;
        /// <summary>Clock minutes: when it can be started, and when it ought to be done by (what the list says).</summary>
        public float from, by;
        public string[] lines;      // what you notice doing it: [early days, middle days, late days]
        public bool done, mess, late;
        public System.Func<bool> isDone;
        public System.Func<string> progress;
        public System.Action setup, auto;
        public System.Func<Vector3> where;
    }

    /// <summary>
    /// The day's jobs around the house, done with your hands: make coffee (fill the kettle, boil it, a spoon of coffee,
    /// pour, drink), wash up (plug in, run the tap, scrub each plate in the water, stand it in the rack), make the bed,
    /// take the rubbish out, water the plants, wind the hall clock, fetch the post, sweep, reset the fuse, lay and light
    /// the fire in the evening. Each has a time on the list; the clock doesn't wait for them. The hauntings leave messes
    /// that join the list (a broken plate to sweep up, a wet floor, rubbish that isn't yours, something moved).
    /// </summary>
    public class Chores
    {
        readonly GameDirector g;
        readonly List<Chore> pool = new List<Chore>();
        public readonly List<Chore> today = new List<Chore>();
        int tier;
        public bool postWaiting;
        Movable letters;

        House H { get { return g.house; } }
        float Now { get { return DayClock.I != null ? DayClock.I.minutes : DayClock.Wake; } }
        static float T(float h, float m = 0f) { return h * 60f + m; }

        public Chores(GameDirector director)
        {
            g = director;
            Add("coffee", "Make coffee (kitchen)", T(7), T(8, 30),
                () => H.mugDrunk,
                () => !H.mugCoffee && H.kettle.liquid < 0.3f && !H.kettleBoiled ? "fill the kettle" : !H.kettleBoiled ? (H.kettleSwitch.on ? "boiling" : "kettle on its base, switch it on") : !H.mugCoffee ? "a spoon from the brown tin" : !H.mugFill.Finished ? "pour" : "drink it",
                () => H.ResetCoffee(),
                () => { H.kettle.liquid = 1f; H.kettleBoiled = true; H.mugCoffee = true; H.mugFill.done = H.mugFill.need; H.myMug.GetComponent<Interactable>().use(); },
                () => H.myMug.transform.position,
                "Coffee. The one ritual that still works.",
                "You've filled two mugs before you notice.",
                "The kettle is already warm. You haven't been in here today.");
            Add("dishes", "Wash up last night's things (kitchen sink)", T(7), T(10),
                () => H.DishesLeft == 0,
                () => H.DishesLeft + " left" + (H.sink.level < 0.15f ? ", sink empty" : ""),
                () => H.DirtyDishes(3 + Mathf.Min(2, g.day / 3)),
                () => { foreach (var d in new List<Movable>(H.dishes)) { if (d == null) continue; var w = Tool.Scrub(d); if (w != null) w.Add(w.need); if (!H.rack.Holds(d)) { d.SetHeld(false); H.rack.Place(d); } } },
                () => H.sink.transform.position,
                "Warm water, the window, the radio low. For a minute it's just a kitchen.",
                "There's a plate in the rack you didn't wash. It's still wet.",
                "You wash three plates. There were only ever two of you.");
            Add("bed", "Make the bed (bedroom)", T(7), T(9),
                () => H.quilt.Finished,
                () => Mathf.RoundToInt(H.quilt.Progress * 100f) + "%",
                () => H.RumpleBed(),
                () => H.quilt.Add(H.quilt.need),
                () => H.quilt.transform.position,
                "Hospital corners, the way she taught you.",
                "The other side of the bed is turned down. You sleep on the left.",
                "The sheet is warm on her side.");
            Add("trash", "Take the rubbish out (the dustbin by the front step)", T(7, 30), T(11),
                () => H.RubbishOut,
                null,
                () => H.PutOutTheRubbish(),
                () => { if (H.trashBag != null && H.trashBag.gameObject.activeSelf) { H.trashBag.SetHeld(false); H.outsideBin.Place(H.trashBag); } },
                () => H.trashBag != null ? H.trashBag.transform.position : H.outsideBin.transform.position,
                "The lid clangs. A crow somewhere in the woods answers it.",
                "There's already a bag in the dustbin. You don't remember it.",
                "The bag is heavier than it should be. You don't look.");
            Add("plants", "Water the plants (kitchen ferns, the one in the living room)", T(8), T(11),
                () => (H.ferns == null || H.ferns.Finished) && (H.parlourPlant == null || H.parlourPlant.Finished),
                () => (H.ferns != null && H.ferns.Finished ? 1 : 0) + (H.parlourPlant != null && H.parlourPlant.Finished ? 1 : 0) + " of 2" + (H.wateringCan.liquid < 0.05f ? ", can empty" : ""),
                () => { if (H.ferns != null) H.ferns.done = 0f; if (H.parlourPlant != null) H.parlourPlant.done = 0f; H.wateringCan.liquid = 0f; },
                () => { if (H.ferns != null) H.ferns.Add(9f); if (H.parlourPlant != null) H.parlourPlant.Add(9f); },
                () => H.ferns != null ? H.ferns.transform.position : H.sink.transform.position,
                "Grandma's ferns. Still alive, somehow.",
                "The soil is already wet.",
                "The water runs out of the pot a shade too dark.");
            Add("clock", "Wind the hall clock (eleven turns)", T(7), T(10),
                () => H.clockWound,
                () => Mathf.FloorToInt(H.clockKey.done) + " of 11",
                () => H.ClockRunDown(Now - Random.Range(200f, 500f)),
                () => H.clockKey.Add(11f),
                () => H.clockKey.transform.position,
                "You wind it the way he showed you. Eleven turns.",
                "It was wound already. You turn the key eleven times anyway.",
                "The hands are moving backward. You wind it anyway.");
            Add("mail", "Fetch the post (the box at the end of the drive; it comes about 10:30)", T(10, 30), T(13),
                () => H.hallTable.Count > 0,
                () => letters != null ? "on the hall table" : postWaiting ? "it's come" : "not yet",
                () => { postWaiting = false; letters = null; },
                () => { postWaiting = true; TakePost(); if (letters != null) { letters.SetHeld(false); H.hallTable.Place(letters); } },
                () => Outdoors.I != null && Outdoors.I.mailbox != null ? Outdoors.I.mailbox.transform.position : H.hallNorth,
                "Bills, a catalogue. Nothing with a real stamp.",
                "A letter addressed to your parents. Postmarked this week.",
                "The envelope is in your own handwriting. It's empty.");
            Add("sweep", "Sweep the hall and the kitchen floor", T(8), T(12),
                () => { foreach (var w in H.grit) if (w != null && !w.Finished && w.gameObject.activeSelf) return false; return true; },
                () => { int n = 0; foreach (var w in H.grit) if (w != null && !w.Finished) n++; return n + " left"; },
                () => H.Sweepings(),
                () => { foreach (var w in new List<Work>(H.grit)) if (w != null) w.Add(w.need); },
                () => H.grit.Count > 0 && H.grit[0] != null ? H.grit[0].transform.position : H.hallNorth,
                "Grit from the drive, crumbs, a dead fly. Ordinary dirt.",
                "The grit by the door is in the shape of a boot. Not yours.",
                "You sweep up a little pile of grave soil. You know what it is without knowing how.");
            Add("dust", "Dust the front room (a cloth: the floor cloth by the sink)", T(9), T(13),
                () => H.Dusted,
                () => H.DustLeft + " left",
                () => H.Redust(),
                () => { foreach (var w in H.dust) if (w != null) w.Add(w.need); },
                () => { foreach (var w in H.dust) if (w != null && !w.Finished) return w.transform.position; return H.hallNorth; },
                "The mantel, the cabinet, the television. Grey on the cloth, then clean wood.",
                "There are finger marks in the dust on the mantel. Small ones.",
                "You wipe the dust away. It has settled in the shape of a name.");
            Add("fuse", "The kitchen's lost its power (the fuse box)", T(7), T(9, 30),
                () => H.breaker.on,
                null,
                () => H.TripBreaker(),
                () => H.breaker.Set(true),
                () => H.breaker.transform.position,
                "One switch is down. You push it up. The fridge shudders and hums.",
                "The breaker labelled CELLAR is the one that's down. You don't remember a cellar circuit.",
                "Every label on the box now reads the same name. Yours.");
            Add("fire", "Lay the fire and light it (living room, before dark)", T(16), T(18, 30),
                () => H.fireLit,
                () => H.grate.Count + " of 2 logs in",
                () => { },
                () => { foreach (var l in H.logs) if (l != null && !H.grate.Holds(l) && H.grate.Count < 3) { l.SetHeld(false); H.grate.Place(l); } H.matches.use(); },
                () => H.grate.transform.position,
                "The fire takes on the first match.",
                "There's something half-burned in the grate. Paper. Your work.",
                "The ashes were warm before you struck the match.");
            Wire();
        }

        /// <summary>Hook the things in the house that belong to a chore (again after the house is rebuilt).</summary>
        public void Wire()
        {
            if (Outdoors.I != null && Outdoors.I.mailbox != null)
            {
                var box = Outdoors.I.mailbox;
                box.promptFn = () => postWaiting ? "Take the post out" : "Open the mailbox";
                box.use = () =>
                {
                    if (!postWaiting) { g.hud.Say(Now < T(10, 30) ? "Nothing yet. The post comes about half past ten." : "Empty.", 2.5f); Sfx.At("clunk", box.transform.position, 0.3f, 1.3f); return; }
                    TakePost();
                };
            }
            H.CoffeeDrunk = () => { };
        }

        void TakePost()
        {
            postWaiting = false;
            var at = Outdoors.I != null && Outdoors.I.mailbox != null ? Outdoors.I.mailbox.transform.position : H.hallNorth;
            var g0 = Build.Box("The post", H.transform, H.transform.InverseTransformPoint(at + Vector3.up * 0.25f), new Vector3(0.24f, 0.025f, 0.12f), new Color(0.88f, 0.85f, 0.76f));
            letters = Movable.Attach(g0, 0.2f);
            Tool.On(letters, "letters", Vector3.zero);
            Sfx.At("paper", at, 0.5f);
            if (Player.I != null && !VRRig.Active) Player.I.PickUp(letters);
            g.Log("the post: " + (g.day <= 2 ? "bills and a catalogue" : g.day <= 4 ? "a letter for your parents" : "an envelope in your own handwriting"));
        }

        void Add(string id, string text, float from, float by, System.Func<bool> isDone, System.Func<string> progress, System.Action setup, System.Action auto, System.Func<Vector3> where,
                 string early, string middle, string late)
        {
            pool.Add(new Chore { id = id, text = text, from = from, by = by, isDone = isDone, progress = progress, setup = setup, auto = auto, where = where, lines = new[] { early, middle, late } });
        }

        public bool AllDone
        {
            get
            {
                foreach (var c in today) if (!c.done) return false;
                return today.Count > 0;
            }
        }

        /// <summary>The morning chores done (the list for before noon).</summary>
        public bool MorningDone
        {
            get
            {
                foreach (var c in today) if (!c.done && c.by <= T(12, 30)) return false;
                return true;
            }
        }

        /// <summary>A new day's list: coffee, washing-up and the bed every day; two or three of the others; the fire most evenings.</summary>
        public void Pick(int day)
        {
            today.Clear();
            tier = day <= 2 ? 0 : day <= 4 ? 1 : 2;
            foreach (var c in pool) { c.done = false; c.late = false; }
            // The house as you find it in the morning: last night's fire is ash, the clock still going, the power on.
            H.FireOut();
            H.ResetCoffee();
            if (H.breaker != null) H.breaker.Quiet(true);
            var k = H.Find("kitchen");
            if (k != null && k.darkUntil > 1e6f) k.darkUntil = 0f;
            H.Sweepings();
            H.DirtDay(day);
            foreach (var w in H.grit) if (w != null) w.gameObject.SetActive(false);
            foreach (var id in new[] { "coffee", "dishes", "bed" }) Take(id);
            // A clock nobody wound yesterday is still stopped: it stays on the list until it's wound.
            if (!H.clockWound) Take("clock");
            var bag = new List<Chore>();
            foreach (var c in pool) if (c.id != "coffee" && c.id != "dishes" && c.id != "bed" && c.id != "fire" && !today.Contains(c)) bag.Add(c);
            int extra = 2 + (g.rng.NextDouble() < 0.4 ? 1 : 0);
            for (int i = 0; i < extra && bag.Count > 0; i++)
            {
                int kk = g.rng.Next(bag.Count);
                Take(bag[kk].id);
                bag.RemoveAt(kk);
            }
            if (g.rng.NextDouble() < 0.8 || day == 1) Take("fire");
            today.Sort((a, b) => a.by.CompareTo(b.by));
            var names = new List<string>();
            foreach (var c in today) names.Add(c.id + " by " + DayClock.Say(c.by));
            g.Log("day " + day + ": the list: " + string.Join(", ", names.ToArray()));
        }

        void Take(string id)
        {
            var c = pool.Find(x => x.id == id);
            if (c == null) return;
            c.done = false;
            if (c.setup != null) c.setup();
            if (id == "sweep") foreach (var w in H.grit) if (w != null) w.gameObject.SetActive(true);
            today.Add(c);
        }

        /// <summary>A mess a haunting left: it goes on the list until it's cleaned up.</summary>
        public void Mess(string id, string text, System.Func<bool> isDone, System.Func<Vector3> where, string line)
        {
            if (today.Exists(c => c.id == id && !c.done)) return;
            var c = new Chore { id = id, text = text, from = Now, by = Now + 120f, isDone = isDone, where = where, mess = true, lines = new[] { line, line, line } };
            c.auto = () => { };
            today.Add(c);
            g.Log("mess: " + text + " (on the list now)");
            g.RefreshList();
        }

        /// <summary>Three chores as they'd read on a list, without drawing them (the false awakening shows these).</summary>
        public List<string> Preview()
        {
            var bag = new List<Chore>(pool);
            var list = new List<string>();
            var r = new System.Random();
            for (int i = 0; i < 3 && bag.Count > 0; i++) { int k = r.Next(bag.Count); list.Add(DayClock.Say(bag[k].by) + "  " + bag[k].text); bag.RemoveAt(k); }
            return list;
        }

        public Vector3? PendingTargetPosition(string roomName)
        {
            foreach (var c in today)
            {
                if (c.done || c.where == null) continue;
                var p = c.where();
                var r = H.RoomAt(p);
                if (r != null && r.name == roomName) return p;
            }
            return null;
        }

        /// <summary>Every frame: anything finished? Anything overdue?</summary>
        public void Tick()
        {
            if (g.phase == Phase.Dream || g.phase == Phase.Title || g.phase == Phase.Ending) return;
            if (DayClock.I != null && !DayClock.I.running) return;
            bool changed = false;
            if (!postWaiting && letters == null && today.Exists(c => c.id == "mail" && !c.done) && Now >= T(10, 30))
            {
                postWaiting = true;
                g.Log("the post has come (the mailbox flag is up)");
            }
            foreach (var c in today)
            {
                if (c.done) continue;
                if (c.isDone != null && c.isDone()) { Finish(c); changed = true; continue; }
                if (!c.late && Now > c.by + 30f)
                {
                    c.late = true;
                    changed = true;
                    g.Log("chores: '" + c.text + "' wasn't done by " + DayClock.Say(c.by));
                    if (!c.mess) g.Shake(0.01f, "a chore left undone (" + c.id + ")");
                }
            }
            if (changed || Time.frameCount % 30 == 0) g.RefreshList();
        }

        void Finish(Chore c)
        {
            c.done = true;
            Sfx.Ui("click", 0.5f);
            g.hud.Say(c.lines[tier]);
            g.Log("chore done: " + c.id + " at " + DayClock.Say(Now) + (c.late ? " (late)" : ""));
            if (!c.mess && tier == 1) g.Shake(0.02f, "a chore went slightly wrong (" + c.id + ")");
            if (!c.mess && tier == 2) g.Shake(0.04f, "a chore went wrong (" + c.id + ")");
            if (c.mess) g.Steady(0.01f, "cleaned up after it (" + c.id + ")");
            g.OnChoreDone();
        }

        /// <summary>Tests and the skip key: do every chore the quick way.</summary>
        public void CompleteAll()
        {
            foreach (var c in new List<Chore>(today))
            {
                if (c.done) continue;
                try { if (c.auto != null) c.auto(); } catch (System.Exception e) { Debug.LogWarning("[NightPages] auto chore " + c.id + ": " + e.Message); }
                if (c.isDone != null && !c.isDone()) g.Log("chores: the quick way didn't finish '" + c.id + "'; marking it done");
                c.done = true;
            }
        }

        /// <summary>The list as it reads on the fridge: time, job, how far along.</summary>
        public void List(List<KeyValuePair<string, bool>> into, bool afternoon)
        {
            foreach (var c in today)
            {
                if (afternoon && c.done && c.by < T(12)) continue;
                string p = !c.done && c.progress != null ? "  (" + c.progress() + ")" : "";
                into.Add(new KeyValuePair<string, bool>((c.mess ? "now      " : DayClock.Say(c.by).PadRight(9)) + c.text + p, c.done));
            }
        }
    }
}
