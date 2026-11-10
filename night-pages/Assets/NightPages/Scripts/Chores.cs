using System.Collections.Generic;

namespace NightPages
{
    public class Chore
    {
        public string id, text;
        public string[] lines;      // what you notice doing it: [early days, middle days, late days]
        public bool done;
    }

    /// <summary>The morning routine. Three tasks a day from a pool; the same tasks go a little wrong as the run goes on.</summary>
    public class Chores
    {
        readonly GameDirector g;
        readonly List<Chore> pool = new List<Chore>();
        public readonly List<Chore> today = new List<Chore>();

        public Chores(GameDirector director)
        {
            g = director;
            Add("coffee", "Make coffee (kitchen)",
                "Coffee. The one ritual that still works.",
                "You pour two cups before you notice.",
                "The pot is already warm. You haven't been in here today.");
            Add("mail", "Check the mailbox (end of the drive)",
                "Bills, a catalogue. Nothing with a real stamp.",
                "A letter addressed to your parents. Postmarked this week.",
                "The envelope is in your own handwriting. It's empty.");
            Add("clock", "Wind the hall clock",
                "You wind it the way he showed you. Eleven turns.",
                "It was wound already. Eleven turns exactly.",
                "The hands are moving backward. You wind it anyway.");
            Add("plants", "Water the ferns (kitchen)",
                "Grandma's ferns. Still alive, somehow.",
                "The soil is already wet.",
                "The water runs out of the pot a shade too dark.");
            Add("fire", "Feed the fire (living room)",
                "The fire takes on the first match.",
                "There's something half-burned in the grate. Paper. Your typing.",
                "The ashes are warm. You haven't lit a fire in days.");
            Add("fuse", "Check the fuse box (kitchen)",
                "All the fuses are fine. Old house, old wiring.",
                "One fuse is labeled CELLAR. It's been pulled.",
                "Every label on the box now reads the same name. Yours.");

            Wire();
        }

        /// <summary>Point each chore's thing in the house at its chore (again after the house is rebuilt).</summary>
        public void Wire()
        {
            foreach (var c in pool)
            {
                var chore = c;
                Interactable target;
                if (g.house.choreTargets.TryGetValue(chore.id, out target)) target.use = () => Use(chore);
            }
        }

        void Add(string id, string text, string early, string middle, string late)
        {
            pool.Add(new Chore { id = id, text = text, lines = new[] { early, middle, late } });
        }

        public bool AllDone
        {
            get
            {
                foreach (var c in today) if (!c.done) return false;
                return today.Count > 0;
            }
        }

        public void Pick(int day)
        {
            today.Clear();
            var bag = new List<Chore>(pool);
            for (int i = 0; i < 3 && bag.Count > 0; i++)
            {
                int k = g.rng.Next(bag.Count);
                bag[k].done = false;
                today.Add(bag[k]);
                bag.RemoveAt(k);
            }
            var names = new List<string>();
            foreach (var c in today) names.Add(c.id);
            g.Log("day " + day + ": chores drawn: " + string.Join(", ", names.ToArray()));
        }

        /// <summary>Three chores as they'd read on a list, without drawing them (the false awakening shows these).</summary>
        public List<string> Preview()
        {
            var bag = new List<Chore>(pool);
            var list = new List<string>();
            var r = new System.Random();
            for (int i = 0; i < 3 && bag.Count > 0; i++) { int k = r.Next(bag.Count); list.Add(bag[k].text); bag.RemoveAt(k); }
            return list;
        }

        public UnityEngine.Vector3? PendingTargetPosition(string roomName)
        {
            foreach (var c in today)
            {
                if (c.done) continue;
                var p = g.house.choreTargets[c.id].transform.position;
                if (g.house.RoomAt(p).name == roomName) return p;
            }
            return null;
        }

        void Use(Chore c)
        {
            if (g.phase != Phase.Morning || !today.Contains(c))
            {
                g.hud.Say("Not today's job.", 2.5f);
                return;
            }
            if (c.done)
            {
                g.hud.Say("Done already. You're almost sure.", 2.5f);
                return;
            }
            c.done = true;
            int tier = g.day <= 2 ? 0 : g.day <= 4 ? 1 : 2;
            Sfx.Ui("click", 0.8f);
            g.hud.Say(c.lines[tier]);
            if (tier == 1) g.Shake(0.02f, "a chore went slightly wrong (" + c.id + ")");
            if (tier == 2) g.Shake(0.04f, "a chore went wrong (" + c.id + ")");
            g.OnChoreDone();
        }

        public void CompleteAll()
        {
            foreach (var c in today) c.done = true;
        }
    }
}
