using System.Collections.Generic;

namespace NightPages
{
    /// <summary>
    /// The kinds of fear the game knows about. Every dream option and monster trait is tagged with one or
    /// more of these, and the fear engine learns a score for each kind as well as for each option. So when
    /// a spider gets to you, the hound and the swarm (also "animals") start out more likely to be tried,
    /// before the engine has ever shown you either.
    ///
    /// Drawn from three ways of sorting fear:
    ///   - DSM-5 specific phobia types: animal, natural environment, blood-injection-injury, situational,
    ///     and "other" (which includes costumed characters in children).
    ///   - Karl Albrecht's five basic fears: extinction, mutilation, loss of autonomy, separation, ego-death.
    ///   - Stephen King's three levels in horror: terror (what you imagine), horror (what you see),
    ///     and the gross-out.
    /// The ids are saved in the player's fear profile; don't rename them.
    /// </summary>
    public static class FearTypes
    {
        public class Family
        {
            public string id, name, source, about;
            public Family(string id, string name, string source, string about)
            {
                this.id = id; this.name = name; this.source = source; this.about = about;
            }
        }

        public const string Unknown = "unknown", Hunted = "hunted", Body = "body", Animal = "animal", Disgust = "disgust",
            Uncanny = "uncanny", Trapped = "trapped", Nature = "nature", Medical = "medical", Loss = "loss", Self = "self";

        public static readonly Family[] All =
        {
            new Family(Unknown, "the unknown", "King: terror; fear of the unknown",
                "What you can't see or name: sounds with no source, a shape in the dark, a door left open."),
            new Family(Hunted, "being hunted", "Albrecht: extinction",
                "Something coming for you: pursuit, footsteps keeping pace, a thing that closes in when you look away."),
            new Family(Body, "the body made wrong", "Albrecht: mutilation; King: horror",
                "Anatomy that is almost right: too many limbs, joints that bend the wrong way, a head at the wrong angle."),
            new Family(Animal, "animals", "DSM-5: animal type",
                "Spiders, snakes, dogs, swarms of small things."),
            new Family(Disgust, "disgust", "King: the gross-out",
                "Rot, wetness, crawling masses, clustered holes. No gore: disgust, not injury."),
            new Family(Uncanny, "the uncanny", "DSM-5: other type (costumed characters); the uncanny valley",
                "Dolls, clowns, faces that are painted on, things that are nearly human."),
            new Family(Trapped, "being trapped", "Albrecht: loss of autonomy; DSM-5: situational type",
                "Tight spaces, rooms that shrink, doors that won't open, no way out."),
            new Family(Nature, "heights, water and the dark", "DSM-5: natural environment type",
                "A drop with no rail, deep water, the dark."),
            new Family(Medical, "needles and doctors", "DSM-5: blood-injection-injury type",
                "Hospital sounds, needles, a figure in a surgical mask. Never wounds or blood."),
            new Family(Loss, "being left", "Albrecht: separation",
                "The people who raised you, turned away. Being alone. Something you love leaving."),
            new Family(Self, "yourself", "Albrecht: ego-death",
                "Your reflection acting on its own, your own coat on something else, your book writing itself."),
        };

        static Dictionary<string, Family> byId;

        public static Family Get(string id)
        {
            if (byId == null)
            {
                byId = new Dictionary<string, Family>();
                foreach (var f in All) byId[f.id] = f;
            }
            Family fam;
            return byId.TryGetValue(id, out fam) ? fam : null;
        }

        public static string Name(string id)
        {
            var f = Get(id);
            return f == null ? id : f.name;
        }
    }
}
