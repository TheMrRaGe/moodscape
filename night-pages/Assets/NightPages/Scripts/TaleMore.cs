using System.Collections.Generic;

namespace NightPages
{
    /// <summary>
    /// The story engine's second library: what is really going on (it's a ghost in only one answer of eight), the act
    /// the dead repeat (Okiku counting nine plates and screaming at the tenth), and more of everyone and everything
    /// that can have happened here. Built from horror scholarship and folklore (Carroll's discovery plot, Thompson's
    /// Motif-Index section E for why the dead return, Todorov's hesitation between natural and supernatural) and the
    /// fear research notes. See the story engine plan.
    /// </summary>
    public partial class Tale
    {
        static void BuildMore(List<Piece> L)
        {
            const Truth K = Truth.Killer, Ke = Truth.Keepers, H = Truth.House;

            // ======== WHAT IS REALLY HAPPENING (one per run; the player isn't told until the end, if then)
            // lines: [0] a note that supports this reading, [1] a page in the dreamer's voice, [2] the ending's last word.
            L.Add(P("explain", "x-ghost").T("x:ghost").W(1.7f)
                .L("A list in Grandpa's hand of everything they tried: the vicar, salt on the sills, a mirror facing the door, sleeping in shifts. Every line crossed out but the last: \"Live with it.\"",
                   "It isn't in my head. I've checked. I've done everything you're meant to do to check. It's in the house.",
                   "Whatever it was, it was real. It's still there, in the house, being patient."));
            L.Add(P("explain", "x-grief").T("x:grief").Needs("family|grandparent|parent|animal|carer").W(1.1f)
                .L("A leaflet from the surgery, folded small: \"Many bereaved people sense, hear or even see the person they have lost. This is common, and it is not a sign that anything is wrong.\"\n\nSomeone has written \"then why does it hurt\" in the margin.",
                   "I still make two cups. I still leave the landing light on for {him}. I know. I know. But then who is drinking the second cup?",
                   "Later, people tell you it's common: half of everyone who loses someone feels them near. You tell no one that yours still makes the tea."));
            L.Add(P("explain", "x-trauma").T("x:trauma").Needs("sudden|secret|trapped|guilt|kept|buried").W(1f)
                .L("A school report from the year it happened. \"A bright child, but since the spring very quiet. Draws the same house over and over. Will not say what is in the cellar window.\"",
                   "I was here that night. I was small enough to fit under the stairs. I've always known that. I just didn't know that I knew.",
                   "You were here that night, and some part of you never left it. Now you remember. That will have to be enough."));
            L.Add(P("explain", "x-mind").T("x:mind").W(0.9f)
                .L("Your own handwriting on the back of a chores list. You don't remember writing it: \"Don't trust the watch. Don't trust the list. Count the plates yourself.\"",
                   "I moved the cup. The cup moved. I know which of those happened, because I was there. I was there, wasn't I?",
                   "When they find you, the house has been empty for years. There's no furniture. Just you, and the plates, counted out on the bare boards."));
            L.Add(P("explain", "x-guilt").T("x:guilt").Only(K).W(1f)
                .L("The same sheet of paper you've found three times now, in three rooms. Each time it has more written on it, and each time the hand is more like yours.",
                   "It doesn't need to haunt me. I'm doing it for it. I'm the one who keeps setting the place at the table.",
                   "The house never needed a ghost. It only needed you, and what you did, and enough quiet to hear it."));
            L.Add(P("explain", "x-human").T("x:human").Needs("lodger|stranger|alive|hoard|hidden").Bans("notperson").W(0.9f)
                .L("A child's drawing of the house in cross-section, every room labelled. Between the bathroom and the bedroom, a narrow space with a stick figure in it, lying down. It's labelled \"the quiet man\".",
                   "Ghosts don't eat. Ghosts don't leave hairs in the soap. Ghosts don't breathe through the wall at night, slowly, so you won't hear.",
                   "It was never a ghost. It was a man, in the wall, for most of a year. Ghosts can't follow you home."));
            L.Add(P("explain", "x-air").T("x:air").W(0.8f)
                .L("A gas board leaflet, 1960s, under the boiler: \"Headaches? Drowsiness? Seeing things? Check your flue.\" The tear-off slip to book a visit is still attached.",
                   "My head aches every morning and clears by noon. The house is worst at night with the windows shut. I keep meaning to open them. I keep forgetting why.",
                   "The engineer finds the flue blocked: birds, years of them. It explains the headaches, the footsteps, the figures by the bed. It doesn't explain the plates."));
            L.Add(P("explain", "x-unknown").T("x:unknown").W(0.8f)
                .L("Two letters in the bureau, in the same envelope. One from a priest, one from a doctor. They say opposite things, and both are sure.",
                   "Either the house is haunted or I'm not well. I've been trying to work out which would be worse. I don't think I get to choose.",
                   "You never find out which it was. Some nights you're glad."));

            // ======== THE ACT THEY REPEAT (Okiku counts nine plates; laid to rest when the act is finished)
            // lines: [0] a note about it, [1] a page. V("act") is the short phrase.
            L.Add(P("act", "a-plates").T("act:count").V("act", "counting the plates, nine, and then a scream for the tenth").W(1.2f)
                .L("An inventory of the good dinner service, in Grandma's hand. Twelve plates. Eleven are ticked. Against the twelfth, over and over: \"where\".",
                   "Every night, in the kitchen, someone counts the plates. Quietly, one by one. They never get to the last one. They stop, and then the sound they make instead of the number."));
            L.Add(P("act", "a-place").T("act:table").V("act", "setting one more place at the table").W(1.2f)
                .L("A seating plan for Christmas dinner, years old. Every name crossed out and rewritten many times except one chair, which has never had a name.",
                   "There's always one more place laid. I clear it. In the morning it's laid again: knife, fork, the good glass, turned the way {he} liked."));
            L.Add(P("act", "a-bath").T("act:water").V("act", "running a bath and never getting in").Needs("water|illness|carer|kept")
                .L("A note taped to the bathroom tap: \"Do not run the bath for her. She's not coming up.\"",
                   "The bath runs itself at seven. Hot, the way {he} liked it. Nobody gets in. It goes cold, and drains, and at seven it runs again."));
            L.Add(P("act", "a-bell").T("act:sound").V("act", "ringing the little bell from the sickbed").Needs("illness|bed|carer|slow")
                .L("A brass handbell, the clapper wrapped in a sock and tied with string. Someone has picked at the string.",
                   "The bell. From the front room, where the bed was. Three times, then waiting. I know what it means. It means come. I haven't gone yet."));
            L.Add(P("act", "a-clock").T("act:time").V("act", "stopping every clock at the same minute")
                .L("A list of the clocks in the house, each with the same time written beside it. At the bottom: \"we stopped them all, like you're supposed to. Then we couldn't start them again.\"",
                   "Every clock in the house stops at the same minute. I wind them. I set them. By the morning they've all gone back to it, the minute {he} died."));
            L.Add(P("act", "a-door").T("act:doors").V("act", "checking every door is locked, twice, every night")
                .L("A ring of keys with a luggage label: \"Front, back, cellar, the other one.\" There are five keys.",
                   "Someone goes round the house at night trying every door. The front, the back, the cellar. Then a fourth one, somewhere, that I've never found."));
            L.Add(P("act", "a-window").T("act:waiting").V("act", "waiting at the window for someone to come up the drive")
                .L("A kitchen chair with its feet worn flat on one side from years of being dragged to the front window.",
                   "The chair is at the window again. The cushion is warm. Whoever sits there is watching the drive for someone who isn't coming."));
            L.Add(P("act", "a-fold").T("act:chores").V("act", "folding the washing, over and over, though it's already folded")
                .L("A pile of clean washing in the airing cupboard, folded so many times the creases have worn through.",
                   "The washing basket is full of my clothes, folded the way {he} folded them, corners in. I didn't put them there. I never fold like that."));
            L.Add(P("act", "a-hum").T("act:sound").V("act", "humming the same few bars, never finishing the tune")
                .L("A piece of sheet music, hand-copied, that stops halfway along a line. The last bar has been rubbed out so many times there's a hole.",
                   "The humming again. Five notes and a gap. I find myself finishing it, under my breath, and then it stops, and listens."));
            L.Add(P("act", "a-knock").T("act:sound").V("act", "knocking four times, then waiting to be let in")
                .L("A list of four names, in pencil, on the inside of the cellar door. Each has a short line by it, like a tally.",
                   "Four knocks. Always four, always slow, always on the same door. I've counted the people who used to live here. There were four."));
            L.Add(P("act", "a-sweep").T("act:chores").V("act", "sweeping the same step until the bristles are gone")
                .L("A broom by the back door, its head worn down to a stub, and a hollow worn into the step under it.",
                   "Someone sweeps the back step at dawn. I hear the bristles. When I look, the broom is leaning by the door, and the step is clean, and wet, as if something had to be washed off it."));
            L.Add(P("act", "a-call").T("act:voice").V("act", "calling a child in for supper from the back door").Needs("child|family")
                .L("A dinner bell on a nail by the back door, rusted solid, the clapper missing.",
                   "At six someone calls from the back door: a name, the way you call a child in from the garden. Twice. Then the door shuts. I don't know whose name. I know it isn't mine."));
            L.Add(P("act", "a-light").T("act:light").V("act", "leaving the landing light on for someone who never comes home")
                .L("A box of forty bulbs in the cellar, thirty-nine used, each dated in pencil on the cardboard sleeve.",
                   "I turn the landing light off. It's on again by the time I'm in bed. Someone is still waiting up for someone."));
            L.Add(P("act", "a-pray").T("act:voice").V("act", "saying the same prayer at the bedside, the words wearing away").Bans("x:human")
                .L("A child's prayer card, the words worn off the cardboard by a thumb, all but \"keep\" and \"take\".",
                   "Someone kneels by my bed at night and says a prayer I half know. Every night there are fewer words. Last night there were only two."));
            L.Add(P("act", "a-dig").T("act:earth").V("act", "digging in the vegetable patch at night, and filling it back in").Needs("buried|outside|gone|secret")
                .L("A spade by the back door with fresh earth on it every morning. The handle is worn smooth where two hands held it.",
                   "Someone digs in the vegetable patch at night. In the morning it's been filled back in, flat, neat, the way you'd fill a grave."));

            // ======== more WHO (the new stories)
            L.Add(P("who", "stayed").T("adult", "man", "stranger", "alive", "hidden").Only(K, H).W(0.8f)
                .V("name", "the one who stayed", "rel", "whoever stayed on after", "he", "he", "his", "his", "him", "him", "hers", "his", "walk", "careful",
                   "smell", "smoke and cold fat", "voice", "someone eating, very quietly, in the dark", "room", "kitchen", "hands", "hands that washed up after them")
                .L("", "After they died, someone stayed in the house for three days. Ate from the pantry. Kept the stove lit. Washed up. The neighbours saw smoke and thought nothing of it.")
                .Props("pot_enamel_01", "russian_food_cans_01"));
            L.Add(P("who", "seven").T("child", "girl", "family", "young").W(1f)
                .V("name", "Ada", "rel", "your mother's little sister", "he", "she", "his", "her", "him", "her", "hers", "hers", "walk", "small",
                   "smell", "camphor and fever", "voice", "a little girl asking for water", "room", "bedroom", "hands", "small hot hands")
                .L("", "Ada was seven. They stopped the clocks and covered the mirrors and kept her in the front room for three days, and sat up with her. Then they had her photographed, sitting up, in her best dress.")
                .Props("rubber_duck_toy", "standing_picture_frame_02"));
            L.Add(P("who", "thrower").T("young", "girl", "stranger", "voice").Only(H, K).W(0.8f)
                .V("name", "Carol", "rel", "the girl who lived here in the seventies", "he", "she", "his", "her", "him", "her", "hers", "hers", "walk", "trainers",
                   "smell", "hairspray and burnt toast", "voice", "an old man's voice coming out of a girl", "room", "bedroom", "hands", "bitten-nailed hands")
                .L("", "The knocking started when Carol was eleven. Then things flew. Then the voice, an old man's, out of her mouth. People came with tape recorders. Then one night she was at the bottom of the cellar stairs.")
                .Props("Ukulele_01", "vintage_flashlight"));
            L.Add(P("who", "slaughterman").T("adult", "man", "family", "grandparent", "old").Only(K, Ke).W(0.9f)
                .V("name", "Grandpa", "rel", "your grandfather, the slaughterman", "he", "he", "his", "his", "him", "him", "hers", "his", "walk", "boots",
                   "smell", "bleach over something else", "voice", "a knife being sharpened, patiently", "room", "cellar", "hands", "his big clean hands")
                .L("", "Grandpa killed pigs for forty years and was good at it. He used to say the trick was not to think of it as anything. The cellar floor has a drain in it. I used to think that was normal.")
                .Props("bleach_bottle", "wooden_axe", "metal_tool_chest"));
            L.Add(P("who", "boy-ice").T("child", "boy", "stranger", "town", "young").W(0.8f)
                .V("name", "the Fenner boy", "rel", "the boy from down the lane", "he", "he", "his", "his", "him", "him", "hers", "his", "walk", "wellingtons",
                   "smell", "pondweed", "voice", "a boy tapping on glass", "room", "hall", "hands", "small blue hands")
                .L("", "The Fenner boy went through the pond ice one winter. They didn't find him till the thaw, three yards from the bank, looking up.")
                .Props("rubber_boots", "wooden_bucket_01"));
            L.Add(P("who", "taxidermist").T("adult", "woman", "family", "grandparent", "old", "kept").Only(Ke, H).W(0.8f)
                .V("name", "Grandma", "rel", "your grandmother, as Grandpa kept her", "he", "she", "his", "her", "him", "her", "hers", "hers", "walk", "stiff",
                   "smell", "borax and dust", "voice", "the creak of wire", "room", "bedroom", "hands", "hands that don't bend")
                .L("", "Grandpa stuffed birds for the museum. When Grandma died he couldn't let her go, so he did what he knew. She sat in the attic chair in her Sunday dress for eleven years.")
                .Props("round_spectacles", "wicker_basket_02"));
            L.Add(P("who", "dug").T("adult", "woman", "stranger", "old", "unburied").Only(H, K).W(0.7f)
                .V("name", "Mary Ellery", "rel", "the woman taken from the churchyard", "he", "she", "his", "her", "him", "her", "hers", "hers", "walk", "dragging",
                   "smell", "turned earth", "voice", "a woman whispering the words off a gravestone", "room", "cellar", "hands", "earth-black hands")
                .L("", "The man who built the house robbed fresh graves and sold the bodies to the anatomy school in the city. Mary Ellery was the first. The cellar's back room was where she waited for the cart.")
                .Props("rusted_spade_01", "Lantern_01"));

            // ======== more WHAT happened
            L.Add(P("what", "pillow").T("illness", "slow", "bed", "guilt").Needs("adult|old").Only(K, Ke).W(1f)
                .L("A pillow, in a plastic bag, at the back of the airing cupboard. Inside the pillowcase, a nurse's watch, stopped.",
                   "{Name} was dying anyway. Everybody said so. Everybody said it couldn't go on. On the last night someone in this house agreed with them, with a pillow."));
            L.Add(P("what", "drain").T("buried", "cellar", "secret", "gore").Only(K, Ke).Bans("child").W(0.8f)
                .L("A plumber's bill: \"Cellar drain cleared. Blockage removed (see below).\" The part below has been torn off.",
                   "The cellar drain blocked the winter {name} disappeared. Grandpa cleared it himself. He wouldn't let the plumber see. He burned his overalls after."));
            L.Add(P("what", "laidout").T("illness", "kept", "child").Needs("child|young").W(0.9f)
                .L("A photographer's receipt: \"One portrait, sitting, eyes painted. Mourning frame extra.\"",
                   "They kept {name} in the front room for three days and sat up with {him}. Then a man came with a camera, and they put {him} in the chair, and painted {his} eyes onto {his} lids."));
            L.Add(P("what", "flew").T("sudden", "stairs", "voice").Needs("young|girl").W(0.7f)
                .L("A reel of tape in a tin, labelled \"Session 14\". Written on the tin in a man's hand: \"The voice says it wants the stairs.\"",
                   "Things flew for a year. Then, on the last night of it, {name} did. They found {him} at the bottom of the cellar stairs, and everything in the house was quiet, and stayed quiet, until now."));
            L.Add(P("what", "arm").T("accident", "buried", "outside", "gore").Needs("man|old").W(0.6f)
                .L("A small stone in the vegetable patch, no bigger than a brick. Scratched on it: \"R. ARM, 1958\".",
                   "The threshing machine took {his} arm in 1958. In those days you buried it. {He} did, in the vegetable patch, with a stone. Something has been digging there from underneath."));
            L.Add(P("what", "ninedays").T("sudden", "hurried", "gore", "alone").Needs("adult").Bans("child").W(0.7f)
                .L("A cleaning firm's job sheet for this address, fifteen years ago. \"Two deceased, undiscovered nine days. Summer. Remove bedding, underlay, boards as required.\"",
                   "Nobody came to the house for nine days. It was summer. The firm cut out the boards and painted over, and nobody cleaned it properly, and the stain comes back through the paint every August."));

            // ======== more WHY they stay (Thompson's Motif-Index, section E)
            L.Add(P("why", "norites").T("unburied", "rites").Needs("kept|buried|gone|trapped|hidden").W(1f)
                .L("A funeral director's letter: \"We regret we cannot proceed without the deceased.\" It's dated after the funeral.",
                   "There was a funeral. There wasn't anyone in the coffin. {He} was never laid to rest, so {he} never rests. That's the whole of the rule."));
            L.Add(P("why", "unfinished").T("work", "longing").Bans("notperson").W(1f)
                .L("A half-knitted cardigan on the needles, a pattern with the last row marked. The wool is new. Someone bought it this month.",
                   "{He} didn't get to finish. That's all. Every night {he} goes back to it, and every morning it's a row further on."));
            L.Add(P("why", "taken").T("stolen", "angry").W(0.9f)
                .L("An auction catalogue from the house sale, one lot circled: \"{Keepsake}, sentimental value only.\" Beside it, \"bought back\", and a date after the sale.",
                   "Something of {his} was taken out of the house. {He}'s been looking for it ever since. I think I've been using it."));
            L.Add(P("why", "reveal").T("murdered", "secret").Only(K, Ke).Needs("sudden|buried|secret|guilt").W(1.1f)
                .L("A newspaper cutting about the inquest. \"Misadventure.\" Underneath, in pencil, in a hand you know: \"no\".",
                   "{He} wants it told. Not avenged. Told. Out loud, by someone who was there."));
            L.Add(P("why", "farewell").T("love", "longing").Needs("family|grandparent|parent|animal").Bans("guilt").W(0.8f)
                .L("A letter, unsealed, addressed to you in {name}'s hand. Inside, only the first line: \"I didn't get to say...\"",
                   "{He} never got to say goodbye. I think that's all {he} wants. I think if I let {him}, it would be the worst thing that's ever happened to me."));

            // ======== more keepsakes: the proof they leave (the hitchhiker's coat on the grave)
            L.Add(P("keepsake", "k-tooth").V("keepsake", "tooth").W(0.8f)
                .L("A tooth, an adult's, with a filling, in the bottom of a teacup.",
                   "I keep finding teeth. One a day. They have fillings. They aren't mine. I've started counting my own."));
            L.Add(P("keepsake", "k-ribbon").Needs("girl|child|woman").V("keepsake", "hair ribbon")
                .L("A faded blue hair ribbon tied round the newel post, in a bow a child would tie.",
                   "The ribbon was round the bedpost this morning, tied tight. I cut it off. Tonight it was back, and tighter."));
            L.Add(P("keepsake", "k-bell").Needs("act:sound|illness").V("keepsake", "handbell")
                .L("A brass handbell on the hall table, its handle polished by a hand.",
                   "The bell is downstairs again. I put it in a drawer. I put the drawer in the shed. I can still hear it."));
            L.Add(P("keepsake", "k-photo").V("keepsake", "photograph").W(0.9f)
                .L("A photograph of this room, taken from where you're standing. Someone is sitting in your chair.",
                   "There's a photograph of the hall in my coat pocket. It wasn't there this morning. The person in it is in the middle of turning round."));
            L.Add(P("keepsake", "k-glasseyes").Needs("kept|taxidermy|old").V("keepsake", "pair of glass eyes")
                .L("A pair of glass eyes in a matchbox, brown, a woman's. The box is labelled in neat copperplate with a date.",
                   "The glass eyes are on the pillow next to mine. Brown. They're always facing me."));
            L.Add(P("keepsake", "k-nails").Needs("hidden|lodger|alive").V("keepsake", "jar of nail clippings")
                .L("A jam jar of fingernail clippings, sorted by size, labelled by date and initial. The last label has your initials.",
                   "The jar has a new label. Today's date. My initials. I check my hands. I check them again."));
            L.Add(P("keepsake", "k-glove").Needs("man").V("keepsake", "work glove")
                .L("One leather work glove on the back step, full of soil, the fingers crooked as if there's still a hand in it.",
                   "The glove is on the step every morning, full of earth. Today the fingers were wrapped round the door handle."));
            L.Add(P("keepsake", "k-coat").V("keepsake", "coat").W(0.8f)
                .L("{His} coat, on the hook by the door, the shoulders wet with rain. It hasn't rained in a week.",
                   "{His} coat was on the back of my chair this morning. The pockets were full of earth, and the earth was warm."));

            // ======== rules for the new acts
            L.Add(P("rule", "r-plates").Needs("act:count").L("If you hear counting in the kitchen, don't go in. And never, ever say the last number for it."));
            L.Add(P("rule", "r-table").Needs("act:table").L("Leave the extra place laid. Don't sit in that chair, even to clear it."));
            L.Add(P("rule", "r-clocks").Needs("act:time").L("If all the clocks say the same time, don't look at your watch. You'll see it says it too."));
            L.Add(P("rule", "r-door4").Needs("act:doors|act:sound").L("When it knocks four times, don't answer the fourth. It only wants to know you're listening."));
            L.Add(P("rule", "r-window").Needs("act:waiting").L("Don't stand behind the chair at the window. Whatever's sitting there doesn't like to be looked over."));
            L.Add(P("rule", "r-dig").Needs("act:earth").L("Don't dig in the patch after dark, even if you've dropped something. Especially then."));

            // ======== more scraps
            L.Add(P("scrap", "x-newspaper").L("A newspaper on the hall table, the city edition. Nobody here takes the city paper. It's dated the week it happened.",
                "A paper turned up on the mat. Not ours. The date is from before I was born. The crossword is half done, in Grandma's hand, and the last answer is my name."));
            L.Add(P("scrap", "x-snow").L("A note in Grandpa's ledger: \"Tracks in the snow from the woods to the back door. None going back.\"",
                "Footprints in the frost this morning, from the trees to the back door. None going back. I checked the house. I checked it again. I'm checking it now."));
            L.Add(P("scrap", "x-mirrorcover").L("A bedsheet folded on top of the hall mirror's frame, as if someone has just taken it down. Or is about to put it back.",
                "They used to cover the mirrors when someone died, so the dead wouldn't get lost in them. Someone keeps covering ours. I keep uncovering them. I don't know who's right."));
            L.Add(P("scrap", "x-flies").L("Three dead bluebottles on the sill of the bedroom window. Then six. The window hasn't been opened.",
                "There are flies in the back bedroom. Only there. In November. I haven't opened the wardrobe yet."));
            L.Add(P("scrap", "x-register").L("A page photocopied from the parish burial register. Four entries in a row have \"disturbed\" written in the margin, in the same red ink.",
                "The burial register has a word in the margin, against four names: disturbed. All four are from the year the house was built."));
            L.Add(P("scrap", "x-bill").L("A gas bill from 1971 with a line underlined: \"Engineer unable to gain access to flue.\"",
                "The gas man came once, Grandma said, and wouldn't go up into the roof. Said there was something in the flue. Wouldn't say what."));
        }

        /// <summary>The story told as a document: every part, its year, and what each line of it is. For the run log and the census.</summary>
        public string Bible(string label)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(label).Append(": ");
            sb.Append(Name(who)).Append(" / ").Append(Name(what)).Append(" / ").Append(Name(why));
            sb.Append(" | really: ").Append(Name(explain)).Append(" | act: ").Append(act != null ? Var("act", act.id) : "none");
            sb.Append(" | proof: ").Append(keepsake != null ? Var("keepsake", keepsake.id) : "none");
            sb.Append(" | year ").Append(Var("year", "?")).Append(", ").Append(Var("season", "?"));
            sb.Append(" | signs: ");
            for (int i = 0; i < signs.Count; i++) sb.Append(i > 0 ? ", " : "").Append(signs[i].sign);
            sb.Append(" | rules ").Append(rules.Count).Append(", scraps ").Append(scraps.Count);
            return sb.ToString();
        }

        static string Name(Piece p) { return p != null ? p.id : "none"; }

        /// <summary>A short key for a story's core, to count how many different ones the engine makes.</summary>
        public string CoreKey { get { return Name(who) + "/" + Name(what) + "/" + Name(why) + "/" + Name(explain) + "/" + Name(act) + "/" + Name(keepsake) + "/" + truth; } }
    }
}
