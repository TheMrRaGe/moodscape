using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// What you do for a living, from the house, in the afternoons. Every job carries the same story (each day's two
    /// choices face the truth, look away from it, or blame someone, and from day 3 one choice knows something it
    /// shouldn't), told through the job's own work: a novel at the typewriter, old interview tapes to transcribe, family
    /// negatives to print in the darkroom, or the telephone answering service for the funeral home in town.
    /// One job a run (random, or chosen on the title screen with [2]).
    /// </summary>
    public abstract class Job
    {
        protected GameDirector g;
        public abstract string Id { get; }
        /// <summary>"a writer": for the title screen.</summary>
        public abstract string Name { get; }
        /// <summary>Said on the first morning.</summary>
        public abstract string Intro { get; }
        /// <summary>The line on the day's list.</summary>
        public abstract string ListLine { get; }
        public abstract string RecordTitle { get; }
        public abstract string Header(int day, int passage);
        public abstract Passage[][] Passages(Manuscript m);
        public virtual string Extra(int day, int passage) { return null; }
        /// <summary>One choice per sitting (then hands-on work before the next), or the whole day's in one go.</summary>
        public virtual bool OneAtATime { get { return true; } }
        /// <summary>Game minutes each piece of work takes.</summary>
        public virtual float MinutesPer { get { return 50f; } }
        /// <summary>When the list says to do it.</summary>
        public virtual float From { get { return 12f * 60f; } }
        public virtual float By { get { return 17f * 60f; } }
        public abstract string Confession { get; }
        public abstract string Gentler { get; }
        public abstract string Leave { get; }
        public virtual string DoneLine { get { return "That's the day's work done."; } }
        public virtual string FooterExtra { get { return null; } }
        /// <summary>The telephone is the job's (ringing for it, or mid-call).</summary>
        public virtual bool PhoneBusy { get { return false; } }

        public void Init(GameDirector director) { g = director; }
        public virtual void Wire(House h) { }
        public virtual void BeginDay(int day) { }
        public virtual void Tick() { }
        /// <summary>The telephone was picked up: is it for the job?</summary>
        public virtual bool OnPhone() { return false; }
        /// <summary>Keys while the page is open (beyond choosing a line).</summary>
        public virtual void PageInput() { }
        /// <summary>A piece of work was chosen (the line is written down); the page is closing.</summary>
        public virtual void Chose(int day, int passage, Line line) { }
        public virtual void DayDone() { }

        protected float Now { get { return DayClock.I != null ? DayClock.I.minutes : 0f; } }
        protected bool TooEarly()
        {
            if (Now >= 12f * 60f || g.chores.MorningDone) return false;
            g.hud.Say("Chores first. That was always the rule in this house.", 3f);
            return true;
        }

        protected static Line F(string t) { return new Line(Tag.Face, t); }
        protected static Line A(string t) { return new Line(Tag.Away, t); }
        protected static Line B(string t) { return new Line(Tag.Blame, t); }
        protected Line K(string killer, string keepers, string house)
        {
            return new Line(Tag.Knows, g.truth == Truth.Killer ? killer : g.truth == Truth.Keepers ? keepers : house);
        }
        protected static Passage P(string ask, params Line[] options) { return new Passage(ask, options); }

        public static readonly string[] Ids = { "writer", "transcriber", "darkroom", "phone" };
        public static string NameOf(string id) { var j = Make(id); return j.Name; }

        public static Job Make(string id)
        {
            switch (id)
            {
                case "transcriber": return new TranscriberJob();
                case "darkroom": return new DarkroomJob();
                case "phone": return new PhoneJob();
                default: return new WriterJob();
            }
        }
    }

    // ==================================================================== the writer

    public class WriterJob : Job
    {
        bool sheetIn;
        public override string Id { get { return "writer"; } }
        public override string Name { get { return "a writer"; } }
        public override string Intro { get { return "You write. The novel is due, and the typewriter in the study was his."; } }
        public override string ListLine { get { return "Write today's chapter (the typewriter, study)"; } }
        public override string RecordTitle { get { return "The manuscript"; } }
        public override bool OneAtATime { get { return false; } }
        public override float MinutesPer { get { return 55f; } }
        public override string Header(int day, int passage) { return "Chapter " + day; }
        public override string Confession { get { return "You finish the last chapter as what it always was.\n\nThen you pick up the telephone, and tell them where to dig."; } }
        public override string Gentler { get { return "You feed the fire a page at a time until the story is simple again."; } }
        public override string Leave { get { return "You take your coat and leave the pages on the desk."; } }
        public override string DoneLine { get { return "You pull the page out of the roller. That's today's chapter."; } }

        public override void Wire(House h)
        {
            h.typewriter.promptFn = () => g.workDone ? "Read what you've written" : sheetIn ? "Sit down and type" : "Feed a sheet into the typewriter";
            h.typewriter.use = () =>
            {
                if (g.workDone) { g.ReadWork(); return; }
                if (TooEarly()) return;
                if (!sheetIn)
                {
                    sheetIn = true;
                    if (h.typewriterPage != null) h.typewriterPage.SetActive(true);
                    Sfx.At("paper", h.typewriter.transform.position, 0.55f);
                    Sfx.At("ratchet", h.typewriter.transform.position, 0.35f, 0.7f);
                    g.hud.Say("You roll a fresh sheet in and line it up.", 2.5f);
                    return;
                }
                g.script.Begin(g.day, g.day > 1 ? g.fear.lastLine : null);
            };
        }

        public override void BeginDay(int day)
        {
            sheetIn = false;
            if (g.house.typewriterPage != null) g.house.typewriterPage.SetActive(false);
        }

        public override Passage[][] Passages(Manuscript m) { return m.WriterDays(); }
    }

    // ==================================================================== the transcriber

    /// <summary>
    /// You type up interview tapes for the county's oral-history archive. This week's box is from your own road:
    /// the neighbours, the postman, and then voices that shouldn't be on tape at all. Press play on the deck in the
    /// study, listen, then type what you heard ([R] to rewind and listen again).
    /// </summary>
    public class TranscriberJob : Job
    {
        bool playing;
        AudioSource voice, hiss;
        float openAt = -1f;
        public override string Id { get { return "transcriber"; } }
        public override string Name { get { return "a transcriber"; } }
        public override string Intro { get { return "You type up old interview tapes for the county archive. This week's box came from your own road."; } }
        public override string ListLine { get { return "Transcribe today's tape (the tape deck in the study)"; } }
        public override string RecordTitle { get { return "The transcripts"; } }
        public override float MinutesPer { get { return 70f; } }
        public override string FooterExtra { get { return "[R] Rewind and listen again"; } }
        public override string Header(int day, int passage) { return "Tape " + day + ", side " + (passage == 0 ? "A" : "B"); }
        public override string Confession { get { return "You type the last tape out word for word and post it to the county.\n\nThen you pick up the telephone, and tell them where to dig."; } }
        public override string Gentler { get { return "You record over the tapes with the radio, one by one, until the story is simple again."; } }
        public override string Leave { get { return "You take your coat and leave the tapes in their box."; } }
        public override string DoneLine { get { return "You take the headphones off. The house is very loud without them."; } }

        public override string Extra(int day, int passage)
        {
            var p = g.script.PassageAt(day, passage);
            if (p == null) return null;
            return "<i>On the tape:  " + Garble(p.options[0].text, day * 31 + passage * 7) + "</i>";
        }

        // What you can make out: the true line with words lost under the hiss (more of them lost on later days).
        static string Garble(string s, int seed)
        {
            var r = new System.Random(seed);
            var words = s.Split(' ');
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                bool lost = r.NextDouble() < 0.42;
                sb.Append(lost ? (r.NextDouble() < 0.3 ? "[hiss]" : "—") : words[i]);
                if (i < words.Length - 1) sb.Append(' ');
            }
            return sb.ToString();
        }

        public override void Wire(House h)
        {
            if (h.deck == null) return;
            h.deck.promptFn = () => g.workDone ? "Read the transcripts" : playing ? "Listening..." : "Put the headphones on and play the tape";
            h.deck.use = () =>
            {
                if (g.workDone) { g.ReadWork(); return; }
                if (playing || TooEarly()) return;
                g.StartCoroutine(Play());
            };
        }

        System.Collections.IEnumerator Play()
        {
            playing = true;
            var h = g.house;
            if (h.headphones != null) h.headphones.SetActive(false);
            Sfx.At("click", h.deck.transform.position, 0.6f, 0.8f);
            Voice(true);
            int day = g.day, passage = g.script.Cur;
            g.Log("transcribing: tape " + day + " side " + (passage == 0 ? "A" : "B") + ": listening");
            g.hud.Say("<i>" + Garble(g.script.PassageAt(day, passage).options[0].text, day * 31 + passage * 7) + "</i>", 6f);
            float until = Time.time + 6.5f;
            while (Time.time < until) { Spin(Time.deltaTime); yield return null; }
            g.script.Resume();
            openAt = Time.time;
        }

        void Voice(bool on)
        {
            if (on)
            {
                if (voice == null) { voice = Sfx.Loop("murmur", g.transform, 0.5f, false, 0.92f + g.day * 0.01f); hiss = Sfx.Loop("hiss", g.transform, 0.25f, false); }
                voice.volume = 0.5f; hiss.volume = 0.25f;
                if (!voice.isPlaying) voice.Play();
                if (!hiss.isPlaying) hiss.Play();
            }
            else { if (voice != null) voice.Stop(); if (hiss != null) hiss.Stop(); }
        }

        void Spin(float dt)
        {
            var h = g.house;
            foreach (var reel in h.reels) if (reel != null) reel.Rotate(0f, 0f, -200f * dt, Space.Self);
        }

        public override void Tick()
        {
            if (playing && voice != null && g.hud.modal == Modal.Writing)
            {
                Spin(Time.deltaTime);
                voice.volume = Mathf.MoveTowards(voice.volume, 0.18f, Time.deltaTime * 0.1f);
            }
        }

        public override void PageInput()
        {
            if (In.Down(Btn.Restart) && Time.time > openAt + 0.3f)
            {
                Sfx.Ui("click", 0.5f, 0.7f);
                if (voice != null) { voice.time = 0f; voice.volume = 0.55f; }
                g.Log("transcribing: rewound to listen again");
            }
        }

        public override void Chose(int day, int passage, Line line)
        {
            playing = false;
            Voice(false);
            Sfx.Ui("typing", 0.5f, 1.05f);
            if (g.house.headphones != null) g.house.headphones.SetActive(true);
        }

        public override Passage[][] Passages(Manuscript m)
        {
            return new[]
            {
                new[]
                {
                    P("Tape 1, side A. Mrs Pell, who lived across the road, about the house. You type:",
                        F("\"That house never changed. That was what frightened me about it.\""),
                        A("\"Lovely house. They kept it lovely. Anyone would tell you.\""),
                        B("\"They left it to the boy the way you'd leave somebody a debt.\"")),
                    P("Side B. She talks about your parents.",
                        F("\"Nobody can say what they looked like now. Only how quiet it went after.\""),
                        A("\"They went away. People did, then. The boy was fine.\""),
                        B("\"Nobody told that boy the truth, so he stopped asking.\"")),
                },
                new[]
                {
                    P("Tape 2. The postman, long retired, about his round.",
                        F("\"Footsteps upstairs, and nobody upstairs. I'd stand on that porch and listen.\""),
                        A("\"Old houses settle. I'd whistle going up the path, that's all.\""),
                        B("\"The old man paced like that. A man pacing has something to hide.\"")),
                    P("Side B. About the cellar door.",
                        F("\"She'd open it an inch and let the cold come up, and just stand there.\""),
                        A("\"No call to go down there. Nobody ever did.\""),
                        B("\"She kept it locked. She must have had her reasons.\"")),
                },
                new[]
                {
                    P("Tape 3. A child's voice, a very old recording. It isn't on the box's list.",
                        F("\"My hands were small, and the thing in them was heavy.\""),
                        A("\"Children make things up.\" [laughter]"),
                        B("\"If I did something, somebody should have stopped me.\"")),
                    P("Side B. Mrs Pell again. She says she's found something.",
                        F("\"I read that letter twice, standing up.\""),
                        A("\"I put it back in the drawer. I didn't unfold it.\""),
                        K("\"Behind the third shelf of his bookcase there's a shoebox. In it, a child's shoe.\"",
                          "\"Under the loose stair, two wedding rings, tied together with string.\"",
                          "\"That cellar's got one more step than it had yesterday. I counted.\"")),
                },
                new[]
                {
                    P("Tape 4. Your grandmother's voice. You don't remember her ever being interviewed.",
                        F("\"We loved him. That's true. It isn't all of it.\""),
                        A("\"We were good people. Write that down so it stays true.\""),
                        B("\"We made him what he is, and then we left him with it.\"")),
                    P("Side B. Under the hiss, somebody else is talking.",
                        F("\"The house wants him to look. So let him look.\""),
                        A("\"Keep the lights on. Keep your back to the hall.\""),
                        K("\"The fuse marked CELLAR was pulled that night. The boy pulled it.\"",
                          "\"The lullaby in the walls is his mother's. I never knew the words.\"",
                          "\"Every door in this house opens inward. There's nowhere to go but in.\"")),
                },
                new[]
                {
                    P("Tape 5. A man's voice. It takes you a moment: it's yours, younger.",
                        F("\"The man in the mirror looks like someone who knows.\""),
                        A("\"I shave without looking up.\""),
                        B("\"I've got his face. That isn't my fault.\"")),
                    P("Side B. The last night they were alive.",
                        F("\"I'll tell it in order. Minute by minute.\""),
                        A("\"They went peacefully. That's the sentence.\""),
                        K("\"Three plates on the table that night. Only one was ever washed.\"",
                          "\"Two extra places every anniversary. I thought it was for guests.\"",
                          "\"They didn't die. They went downstairs, and the stairs kept going.\"")),
                },
                new[]
                {
                    P("Tape 6. No label. A room, recorded. Someone in it, breathing.",
                        F("\"Say their names. Out loud, in the hall, and wait.\""),
                        A("\"I'm alone here. I've always been alone here.\""),
                        B("\"Whatever it is, it came for them. Not for me.\"")),
                    P("Side B. Footsteps going down stairs, a long way down.",
                        F("\"I'm going down.\""),
                        A("\"Nail the door shut. You'll feel better for an hour.\""),
                        K("\"I know what's under the floor. I remember how heavy the spade was.\"",
                          "\"What's under the floor has waited twenty years to be called by name.\"",
                          "\"There's no floor down here. There's only further.\"")),
                },
                new[]
                {
                    P("The last tape is labelled in your handwriting. How does it begin?",
                        F("\"This is what happened.\""),
                        A("\"It was a good house. Anyone would have said so.\""),
                        B("\"None of this was ever up to me.\"")),
                    P("And how does it end?",
                        F("[the click of a telephone being picked up]"),
                        A("[a fire, and paper going into it]"),
                        B("[the tape runs out]")),
                },
            };
        }
    }

    // ==================================================================== the darkroom

    /// <summary>
    /// You restore and print old negatives for families in town; this week's box has rolls from this house. The
    /// bathroom is your darkroom: black the window out, expose a print under the enlarger, rock it in the developer
    /// until the picture comes up (hold the left mouse and move it; in VR, grip and rock the tray), write on the back,
    /// and it's pegged up to dry.
    /// </summary>
    public class DarkroomJob : Job
    {
        enum S { Idle, Exposing, Developing, Choosing }
        S state;
        bool setUp;
        Work rock;
        GameObject print;
        readonly List<GameObject> pegged = new List<GameObject>();
        public override string Id { get { return "darkroom"; } }
        public override string Name { get { return "a photo restorer"; } }
        public override string Intro { get { return "You restore and print old negatives for families in town. The bathroom is your darkroom. This week's box has rolls from this house."; } }
        public override string ListLine { get { return "Print today's negatives (the darkroom: the bathroom)"; } }
        public override string RecordTitle { get { return "The prints, pegged up to dry"; } }
        public override float MinutesPer { get { return 60f; } }
        public override string Header(int day, int passage) { return "Roll " + day + ", frame " + (passage == 0 ? 4 + day : 13 + day * 2); }
        public override string Confession { get { return "You print the last frame at full size and peg it on the line.\n\nThen you pick up the telephone, and tell them where to dig."; } }
        public override string Gentler { get { return "You feed the negatives to the fire a strip at a time until the story is simple again."; } }
        public override string Leave { get { return "You take your coat and leave the prints pegged up to dry."; } }
        public override string DoneLine { get { return "You switch the safelight off. The prints drip in the dark."; } }

        public override void Wire(House h)
        {
            if (h.enlarger == null) return;
            h.enlarger.promptFn = () => g.workDone ? "Look at the prints" : state == S.Exposing ? "Exposing..." : state == S.Developing ? "The print is in the developer: rock the tray" : !setUp ? "Black out the window and set up the darkroom" : "Expose a print";
            h.enlarger.use = () =>
            {
                if (g.workDone) { g.ReadWork(); return; }
                if (state != S.Idle || TooEarly()) return;
                if (!setUp) { SetUp(true); return; }
                g.StartCoroutine(Expose());
            };
        }

        void SetUp(bool on)
        {
            var h = g.house;
            setUp = on;
            if (h.blind != null) h.blind.SetActive(on);
            if (h.safelight != null) h.safelight.intensity = on ? 1.6f : 0f;
            if (h.bath != null && on) h.bath.lit = false;
            Sfx.At("drag", h.enlarger.transform.position, 0.2f, 1.4f);
            if (on) { g.hud.Say("Blind down, the white light off, the safelight on. Everything goes the colour of the inside of your eyelids.", 4f); g.Log("darkroom: set up (blind, safelight)"); }
        }

        System.Collections.IEnumerator Expose()
        {
            var h = g.house;
            state = S.Exposing;
            int day = g.day, passage = g.script.Cur;
            g.Log("darkroom: exposing " + Header(day, passage));
            h.enlargerLamp.intensity = 1.8f;
            for (int i = 0; i < 8; i++) { Sfx.At("tap", h.enlarger.transform.position, 0.12f, 2.2f); yield return new WaitForSeconds(0.75f); }
            h.enlargerLamp.intensity = 0f;
            // The print goes into the developer: white paper, the picture hidden under it until it's rocked up.
            var tray = h.trays[0];
            print = new GameObject("Print");
            print.transform.SetParent(h.transform, false);
            print.transform.position = tray.position + Vector3.up * 0.022f;
            var mat = Build.Mat(Color.white, 0f, 0.3f);
            mat.mainTexture = PhotoFor(day, passage);
            Build.Box("Picture", print.transform, Vector3.zero, new Vector3(0.24f, 0.002f, 0.19f), mat, false);
            var paper = Build.Box("Paper", print.transform, Vector3.up * 0.0025f, new Vector3(0.245f, 0.002f, 0.195f), Build.Mat(new Color(0.95f, 0.95f, 0.92f, 1f)), false);
            var r = paper.GetComponent<Renderer>();
            Work.Fade(r.material);
            r.material.color = new Color(0.95f, 0.95f, 0.92f, 1f);
            var area = new GameObject("Developer (rock the tray)");
            area.transform.SetParent(h.transform, false);
            area.transform.position = tray.position + Vector3.up * 0.05f;
            var col = area.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(0.42f, 0.18f, 0.36f);
            rock = Work.Make(area, "Rock", "the developer tray", WorkMode.Rub, 520f, "hand");
            rock.fades.Add(r);
            rock.progress = p => { if (tray != null) tray.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 6f) * 3f, 0f, 0f); };
            rock.finished = () => g.StartCoroutine(Developed(area));
            state = S.Developing;
            g.hud.Say("Into the developer. Rock the tray and watch it come up.", 3f);
        }

        System.Collections.IEnumerator Developed(GameObject area)
        {
            state = S.Choosing;
            yield return new WaitForSeconds(0.6f);
            Object.Destroy(area);
            g.Log("darkroom: the picture has come up");
            g.script.Resume();
        }

        Texture PhotoFor(int day, int passage)
        {
            var mats = g.house.photoMats;
            if (mats.Count == 0) return Tex.Photo(day + passage);
            var m = mats[(day * 2 + passage) % mats.Count];
            return m.mainTexture;
        }

        public override void Chose(int day, int passage, Line line)
        {
            state = S.Idle;
            var h = g.house;
            if (print != null && h.printLine != null)
            {
                // Through the stop and the fixer, then up on the line with a peg.
                print.transform.SetParent(h.printLine, false);
                print.transform.localPosition = new Vector3(0f, -0.11f, pegged.Count * 0.27f);
                print.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                pegged.Add(print);
                print = null;
                Sfx.At("drip", h.printLine.position, 0.3f, 0.9f);
            }
        }

        public override void DayDone() { SetUp(false); }

        public override void BeginDay(int day)
        {
            foreach (var p in pegged) if (p != null) Object.Destroy(p);
            pegged.Clear();
            state = S.Idle;
            if (setUp) SetUp(false);
        }

        public override Passage[][] Passages(Manuscript m)
        {
            return new[]
            {
                new[]
                {
                    P("The house from the road, years ago. It hasn't changed at all. On the back you write:",
                        F("The house had not changed, and that was the problem."),
                        A("A good house. Anyone would have said so."),
                        B("Left to him, the way you leave someone a debt.")),
                    P("Your parents on the porch. Their faces have gone to grain.",
                        F("I can't see their faces. Only the quiet after."),
                        A("Gone away. People do. He was fine."),
                        B("Nobody told him the truth. He stopped asking.")),
                },
                new[]
                {
                    P("The upstairs landing at night, a long exposure. Something on the stairs moved while the shutter was open.",
                        F("Something walked upstairs. I kept the shutter open."),
                        A("Camera shake. Old house, old film."),
                        B("Grandfather pacing. He paced when he was hiding something.")),
                    P("The cellar door, an inch open, a pale smear in the gap.",
                        F("The cold coming up. I kept the print."),
                        A("A light leak. I'll crop it out."),
                        B("She kept it locked. She had her reasons.")),
                },
                new[]
                {
                    P("A boy in the kitchen holding something with both hands. You don't remember this photograph being taken.",
                        F("Small hands. Something heavy in them."),
                        A("Children play. It's a toy."),
                        B("Somebody should have stopped him.")),
                    P("The study shelves. In the print there's something behind the books that isn't in the room.",
                        F("I printed it twice to be sure."),
                        A("I didn't enlarge that corner."),
                        K("Behind the third shelf: a shoebox, and in it a child's shoe.",
                          "Under the loose stair: two wedding rings, tied with string.",
                          "The cellar steps. One more of them than yesterday.")),
                },
                new[]
                {
                    P("Your grandparents at the table, smiling at whoever held the camera.",
                        F("They loved him. That's true, and not all of it."),
                        A("Good people. I wrote it on the back so it stays true."),
                        B("They made him, and left him with it.")),
                    P("An empty hall. In the developer, a shape comes up in the doorway before the walls do.",
                        F("It wants me to look. So I looked."),
                        A("I pulled it out of the tray early."),
                        K("The fuse marked CELLAR, pulled. A small hand on it.",
                          "A woman singing on the stairs. Not Grandma. Mother.",
                          "Every door in the frame opens inward.")),
                },
                new[]
                {
                    P("Someone photographed the bathroom mirror. The face in it is yours, older than you are.",
                        F("He looks like someone who knows."),
                        A("I didn't look at the face."),
                        B("His grandfather's face. Not his fault.")),
                    P("The table, the night they died. It's dated on the negative sleeve.",
                        F("Every detail. I printed it at full size."),
                        A("They went peacefully. That's the caption."),
                        K("Three plates. Only one was ever washed.",
                          "Two extra places set. Every year since.",
                          "Two empty chairs, and the cellar door open behind them.")),
                },
                new[]
                {
                    P("The hall, today. The film was only in the camera this morning. Someone is standing behind you in it.",
                        F("I said their names out loud, and printed it again."),
                        A("I was alone. I've always been alone here."),
                        B("It came for them, not for me.")),
                    P("The last frame on the roll: the cellar steps from the bottom, looking up.",
                        F("I went down."),
                        A("I nailed the door shut and burned the negative."),
                        K("The new concrete, and the spade leaning where I left it.",
                          "Two sets of initials scratched into the floor from underneath.",
                          "No floor at the bottom. Only further.")),
                },
                new[]
                {
                    P("The last roll. You took it yourself, though you don't remember taking it. The first frame:",
                        F("This is what happened."),
                        A("A good house. Anyone would have said so."),
                        B("None of it was up to me.")),
                    P("The last frame:",
                        F("I made one more print, and picked up the telephone."),
                        A("I fed the negatives to the fire, one strip at a time."),
                        B("I left it in the fixer, unfinished.")),
                },
            };
        }
    }

    // ==================================================================== the answering service

    /// <summary>
    /// You take calls from home for Halloway's, the funeral home in town, and old Dr Marsh's surgery. Two calls come in
    /// each afternoon, at their own times: be in earshot of the hall telephone, answer it, and choose what you say or
    /// write down. A call that rings out is missed (and that counts as looking away).
    /// </summary>
    public class PhoneJob : Job
    {
        readonly float[] at = new float[2];
        bool ringing, onCall;
        float ringUntil, nextRing;
        public override string Id { get { return "phone"; } }
        public override string Name { get { return "an answering service"; } }
        public override string Intro { get { return "You answer the telephone for Halloway's, the funeral home in town, and Dr Marsh's surgery. The calls come in the afternoon."; } }
        public override string ListLine { get { return "Take Halloway's calls (the hall telephone)"; } }
        public override string RecordTitle { get { return "The message book"; } }
        public override float MinutesPer { get { return 25f; } }
        public override float From { get { return at[0] > 0f ? at[0] : 13f * 60f; } }
        public override float By { get { return at[1] > 0f ? at[1] + 30f : 17f * 60f; } }
        public override bool PhoneBusy { get { return ringing || onCall; } }
        public override string Header(int day, int passage) { return day == g.day ? "Halloway's line, " + DayClock.Say(Now) : "Halloway's line, day " + day; }
        public override string Confession { get { return "You write the last message in the book in capitals.\n\nThen you pick up the telephone, and this time you're the one calling. You tell them where to dig."; } }
        public override string Gentler { get { return "You tear the pages out of the message book and feed them to the fire until the story is simple again."; } }
        public override string Leave { get { return "You take your coat and leave the telephone ringing."; } }
        public override string DoneLine { get { return "You put the receiver down. That's the last call today."; } }

        public override void BeginDay(int day)
        {
            ringing = onCall = false;
            at[0] = 13f * 60f + g.rng.Next(0, 80);
            at[1] = 15f * 60f + 20f + g.rng.Next(0, 80);
            g.Log("answering service: calls today at " + DayClock.Say(at[0]) + " and " + DayClock.Say(at[1]));
        }

        public override void Tick()
        {
            if (g.workDone || g.phase == Phase.Dream || g.phase == Phase.Title || g.phase == Phase.Ending) return;
            int cur = g.script.Cur;
            if (cur > 1) return;
            var h = g.house;
            if (!ringing && !onCall && Now >= at[cur] && g.hud.modal == Modal.None)
            {
                ringing = true;
                ringUntil = Now + 40f;
                nextRing = 0f;
                g.Log("answering service: the telephone rings (call " + (cur + 1) + ")");
            }
            if (!ringing) return;
            if (Time.time >= nextRing) { nextRing = Time.time + 4f; Sfx.At("ring", h.phoneBody.position + Vector3.up * 0.1f, 0.9f); }
            if (Now > ringUntil)
            {
                ringing = false;
                g.Log("answering service: nobody answered call " + (cur + 1));
                g.hud.Say("The telephone stops ringing. Somebody wanted Halloway's, and got nobody.", 3.5f);
                g.script.Skip();
            }
        }

        public override bool OnPhone()
        {
            if (!ringing) return false;
            ringing = false;
            onCall = true;
            Sfx.At("click", g.house.phoneBody.position, 0.5f, 0.8f);
            g.Log("answering service: answered call " + (g.script.Cur + 1) + " at " + DayClock.Say(Now));
            g.script.Resume();
            return true;
        }

        public override void Chose(int day, int passage, Line line)
        {
            onCall = false;
            Sfx.At("click", g.house.phoneBody.position, 0.5f, 0.7f);
            Sfx.At("scrub", g.house.phoneBody.position, 0.2f, 2f);
        }

        public override Passage[][] Passages(Manuscript m)
        {
            return new[]
            {
                new[]
                {
                    P("A woman, about a funeral next week. Then, before she rings off: \"You're in the old house now, aren't you? How is it?\"",
                        F("\"It hasn't changed. That's what's wrong with it.\""),
                        A("\"It's a good house. Anyone would say so.\""),
                        B("\"They left it to me the way you'd leave someone a debt.\"")),
                    P("A man for the surgery. He asks if you're their boy. \"Whatever happened to your mother and father?\"",
                        F("\"I can't remember their faces. Only the quiet after.\""),
                        A("\"They went away. People did. I was fine.\""),
                        B("\"Nobody ever told me. I stopped asking.\"")),
                },
                new[]
                {
                    P("Halloway's night man, checking in. \"Quiet out there? Mrs Pell used to swear she heard footsteps in your place at night.\"",
                        F("\"I hear them. I make myself listen.\""),
                        A("\"Old houses settle. I turn the radio up.\""),
                        B("\"Grandad paced like that, when he had something to hide.\"")),
                    P("A woman waiting for the doctor says: \"Your grandmother always kept that cellar locked. Do you?\"",
                        F("\"I opened it an inch today. The cold came up.\""),
                        A("\"There's no reason to go down there.\""),
                        B("\"She kept it locked. She must have had a reason.\"")),
                },
                new[]
                {
                    P("A child's voice, or something like one, on a bad line: \"Do you remember what you were holding?\"",
                        F("\"Something heavy. My hands were small.\""),
                        A("\"Children make things up.\" You hang up."),
                        B("\"If I did something, somebody should have stopped me.\"")),
                    P("The surgery, about an old records request. The nurse lowers her voice: \"I found something in your family's file.\"",
                        F("\"Read it to me. I'll write it down.\""),
                        A("\"Put it back. I don't need to know.\""),
                        K("You write down: a child's shoe, in a shoebox, behind the third shelf.",
                          "You write down: two wedding rings, tied together with string, under the loose stair.",
                          "You write down: the cellar has one more step than it did yesterday.")),
                },
                new[]
                {
                    P("Halloway's, confirming an old booking: two plots, paid for long ago by your grandparents. \"They thought the world of you.\"",
                        F("\"They loved me. That's true, and it isn't the whole truth.\""),
                        A("\"They were good people. Write that down.\""),
                        B("\"They made me what I am and left me with it.\"")),
                    P("A long silence on the line. Then breathing. Then, as close as your own ear: \"Look.\"",
                        F("You write down: the house wants me to look."),
                        A("You put the phone down and turn every light on."),
                        K("You write down: the fuse marked CELLAR was pulled the night they died. You pulled it.",
                          "You write down: the lullaby in the walls is your mother's.",
                          "You write down: every door in the house opens inward.")),
                },
                new[]
                {
                    P("A man asks for the doctor. His voice is yours. \"What do you see when you look in the mirror?\"",
                        F("\"Someone who knows.\""),
                        A("\"I don't look.\""),
                        B("\"My grandfather's face. That's not my fault.\"")),
                    P("Halloway's, very formal: \"For the records. The night your grandparents passed. Anything you'd like noted?\"",
                        F("\"Take it down minute by minute. I'll tell you all of it.\""),
                        A("\"They went peacefully. That's all.\""),
                        K("\"Three plates on the table. Only one was ever washed.\"",
                          "\"Two extra places, every anniversary. Note that.\"",
                          "\"They didn't pass. They went downstairs.\"")),
                },
                new[]
                {
                    P("The telephone rings and there's no one, only a hall and a clock ticking. Your hall. Your clock. You say:",
                        F("Their names. Out loud. Then you wait."),
                        A("\"There's nobody here. There's only ever been me.\""),
                        B("\"Whatever you are, you came for them. Not me.\"")),
                    P("Halloway's, a collection. The address they read out is yours. \"Where will we find them?\"",
                        F("\"Downstairs. I'll show you.\""),
                        A("\"Nowhere. There's nobody here. I'm nailing the cellar shut.\""),
                        K("\"Under the new concrete. I remember how heavy the spade was.\"",
                          "\"Under the cellar floor. Call them by name when you dig.\"",
                          "\"There isn't a bottom to the stairs anymore. Bring rope.\"")),
                },
                new[]
                {
                    P("The last call of your last day. Before you can speak, you hear your own voice say:",
                        F("\"This is what happened.\""),
                        A("\"It was a good house. Anyone would have said so.\""),
                        B("\"None of this was ever up to me.\"")),
                    P("And you answer it:",
                        F("\"Put me through to the police.\""),
                        A("You unplug the telephone and light the fire."),
                        B("You let it ring out, and never pick it up again.")),
                },
            };
        }
    }

    // ==================================================================== the house's side of it

    public partial class House
    {
        public GameObject typewriterPage, headphones;
        public Interactable deck;
        public readonly List<Transform> reels = new List<Transform>();

        /// <summary>The tape deck and headphones on the study desk, beside the typewriter (where the work is done).</summary>
        void StudyKit()
        {
            float top = 0.81f;
            var dark = Build.Mat(new Color(0.14f, 0.13f, 0.12f), 0f, 0.5f);
            var deckGo = Box("Tape deck", new Vector3(-6.4f, top + 0.06f, 3.3f), new Vector3(0.32f, 0.12f, 0.38f), dark);
            Box("Tape deck front", new Vector3(-6.235f, top + 0.06f, 3.3f), new Vector3(0.01f, 0.1f, 0.34f), Build.Mat(new Color(0.6f, 0.58f, 0.55f), 0f, 0.6f), false);
            // A little red light on the front so it reads as a machine from across the room.
            Box("Tape deck light", new Vector3(-6.23f, top + 0.09f, 3.17f), new Vector3(0.01f, 0.012f, 0.012f), Build.Mat(new Color(1f, 0.15f, 0.1f), 2f), false);
            var reelMat = Build.Mat(new Color(0.55f, 0.52f, 0.48f), 0f, 0.7f);
            for (int i = 0; i < 2; i++)
            {
                var hub = new GameObject("Reel").transform;
                hub.SetParent(transform, false);
                hub.localPosition = P(-6.4f, top + 0.13f, 3.3f + (i == 0 ? -0.085f : 0.085f));
                hub.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var disc = Build.Prim(PrimitiveType.Cylinder, "Reel", hub, Vector3.zero, new Vector3(0.14f, 0.004f, 0.14f), reelMat, false);
                disc.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                Build.Box("Spoke", hub, new Vector3(0f, 0f, -0.006f), new Vector3(0.12f, 0.012f, 0.004f), dark, false);
                reels.Add(hub);
            }
            deck = Build.Usable(deckGo, "The tape deck");
            var hp = new GameObject("Headphones");
            hp.transform.SetParent(transform, false);
            hp.transform.localPosition = P(-6.05f, top + 0.02f, 3.12f);
            Build.Box("Headband", hp.transform, new Vector3(0f, 0.01f, 0f), new Vector3(0.03f, 0.012f, 0.18f), dark, false);
            Build.Prim(PrimitiveType.Cylinder, "Earpiece", hp.transform, new Vector3(0f, 0.02f, -0.09f), new Vector3(0.07f, 0.02f, 0.07f), dark, false);
            Build.Prim(PrimitiveType.Cylinder, "Earpiece", hp.transform, new Vector3(0f, 0.02f, 0.09f), new Vector3(0.07f, 0.02f, 0.07f), dark, false);
            headphones = hp;
        }
    }
}
