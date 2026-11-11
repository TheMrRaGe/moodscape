using System.Collections.Generic;
using System.Text;

namespace NightPages
{
    public enum Tag { Face, Away, Blame, Knows }

    public class Line
    {
        public string text;
        public Tag tag;
        public Line(Tag tag, string text) { this.tag = tag; this.text = text; }
    }

    public class Passage
    {
        public string ask;
        public Line[] options;
        public Passage(string ask, params Line[] options) { this.ask = ask; this.options = options; }
    }

    /// <summary>
    /// The novel the character writes each afternoon. Choosing a line is the story choice:
    /// each line faces the truth, looks away from it, or blames someone, and from Day 3 one line
    /// "knows" something it shouldn't. At low Grip, earlier chapters quietly change on a reread.
    /// </summary>
    public class Manuscript
    {
        class Entry { public int day, passage, option; }

        readonly GameDirector g;
        readonly List<Entry> written = new List<Entry>();
        Passage[][] days;
        string[] dreamLines = new string[GameDirector.Days + 1];
        int curDay, cur;
        bool warnedRewrite;

        public Manuscript(GameDirector director) { g = director; }

        static Line F(string t) { return new Line(Tag.Face, t); }
        static Line A(string t) { return new Line(Tag.Away, t); }
        static Line B(string t) { return new Line(Tag.Blame, t); }

        Line K(string killer, string keepers, string house)
        {
            return new Line(Tag.Knows, g.truth == Truth.Killer ? killer : g.truth == Truth.Keepers ? keepers : house);
        }

        public void Reset()
        {
            written.Clear();
            dreamLines = new string[GameDirector.Days + 1];
            warnedRewrite = false;
            days = g.job != null ? g.job.Passages(this) : WriterDays();
        }

        /// <summary>The novel's passages (the writer's job).</summary>
        public Passage[][] WriterDays()
        {
            return new[]
            {
                new[]
                {
                    new Passage("The chapter opens with the house.",
                        F("The house had not changed, and that was the problem."),
                        A("It was a good house. Anyone would have said so."),
                        B("They had left it to him the way you leave someone a debt.")),
                    new Passage("He thinks about his parents.",
                        F("He could not remember their faces, only the shape of the quiet afterward."),
                        A("They had gone away. People did. He was fine."),
                        B("Nobody ever told him the truth, so he stopped asking for it.")),
                },
                new[]
                {
                    new Passage("What does he hear at night?",
                        F("Something walked upstairs at night, and he made himself listen."),
                        A("Old houses settle. He turned the radio up."),
                        B("His grandfather used to pace like that, when he had something to hide.")),
                    new Passage("The door he doesn't open.",
                        F("He opened the cellar door an inch and let the cold come up."),
                        A("There was no reason to go down there, so he didn't."),
                        B("She had always kept that door locked. She must have had a reason.")),
                },
                new[]
                {
                    new Passage("A memory he isn't sure is his.",
                        F("He remembered small hands, and something heavy in them."),
                        A("Children invent things. Everyone knows that."),
                        B("If he had done something, they should have stopped him.")),
                    new Passage("He finds something.",
                        F("He read the letter twice, standing up, like a telegram."),
                        A("He put it back in the drawer without unfolding it."),
                        K("Behind the third shelf of the bookcase there is a shoebox, and in it a child's shoe.",
                          "Under the loose stair there are two wedding rings, tied together with string.",
                          "The cellar has one more step than it did yesterday. He counted.")),
                },
                new[]
                {
                    new Passage("The grandparents.",
                        F("They loved him. That was true, and it was not the whole truth."),
                        A("They were good people. He wrote it down so it would stay true."),
                        B("They made him what he was and then they left him alone with it.")),
                    new Passage("What the house wants.",
                        F("The house wanted him to look. So he looked."),
                        A("He kept the lights on and his back to the hall."),
                        K("The fuse marked CELLAR was pulled the night they died. He was the one who pulled it.",
                          "The lullaby in the walls is the one his mother sang. His grandmother never knew the words.",
                          "Every door in the house opens inward. He has only now noticed there is nowhere to go but in.")),
                },
                new[]
                {
                    new Passage("He looks in the mirror.",
                        F("The man in the mirror looked like someone who knew."),
                        A("He shaved without looking up."),
                        B("He had his grandfather's face. That was not his fault.")),
                    new Passage("The last night they were alive.",
                        F("He made himself write the night down in order, minute by minute."),
                        A("They went peacefully. That was the sentence, and he kept it."),
                        K("There were three plates on the table that night, and only one was ever washed.",
                          "They set two extra places every anniversary. He had thought it was for guests.",
                          "They did not die. They went downstairs, and the stairs kept going.")),
                },
                new[]
                {
                    new Passage("Who is in the house with him?",
                        F("He said their names out loud in the hall and waited."),
                        A("He was alone. He had always been alone here."),
                        B("Whatever it was, it had come for them, not for him.")),
                    new Passage("The cellar.",
                        F("He went down."),
                        A("He nailed the door shut and felt better for almost an hour."),
                        K("He already knows what is under the cellar floor, because he remembers how heavy the spade was.",
                          "What is under the cellar floor has been waiting twenty years to be called by name.",
                          "There is no floor in the cellar anymore. There is only further.")),
                },
                new[]
                {
                    new Passage("The last chapter. How does it start?",
                        F("This is what happened."),
                        A("It was a good house. Anyone would have said so."),
                        B("None of this was ever up to him.")),
                    new Passage("And how does it end?",
                        F("He wrote the last line and picked up the telephone."),
                        A("He lit the fire and fed it the pages, one at a time."),
                        B("He left it unfinished, like everything they gave him.")),
                },
            };
        }

        /// <summary>Which of today's pieces of work is next (0, 1; 2 once the day's work is done).</summary>
        public int Cur { get { return cur; } }
        public int CurDay { get { return curDay; } }
        public Passage PassageAt(int day, int passage)
        {
            var d = Day(day);
            return passage >= 0 && passage < d.Length ? d[passage] : null;
        }

        /// <summary>A new day: nothing written yet today.</summary>
        public void NewDay(int day) { curDay = day; cur = 0; }

        /// <summary>Open the page again for the next piece of today's work (jobs that do one at a time).</summary>
        public void Resume()
        {
            if (cur >= Day(curDay).Length) return;
            g.player.frozen = true;
            g.hud.Open(Modal.Writing);
            Show();
        }

        /// <summary>Bed with the day's work not done: what's left of it never gets done.</summary>
        public void SkipRest()
        {
            bool any = false;
            while (cur < Day(curDay).Length) { written.Add(new Entry { day = curDay, passage = cur, option = -1 }); g.tagAway++; cur++; any = true; }
            if (any) g.Shake(0.04f, "the day's work left undone");
        }

        /// <summary>A piece of work that never got done (a call nobody answered): counts as looking away.</summary>
        public void Skip()
        {
            if (cur >= Day(curDay).Length) return;
            written.Add(new Entry { day = curDay, passage = cur, option = -1 });
            g.tagAway++;
            g.Shake(0.03f, "let a piece of the day's work go undone");
            cur++;
            if (cur >= Day(curDay).Length) g.OnChapterDone();
        }

        Passage[] Day(int day) { return days[(day - 1) % days.Length]; }

        public bool Writing { get { return g.hud.modal == Modal.Writing; } }

        public void Begin(int day, string dreamLine)
        {
            if (curDay != day) { curDay = day; cur = 0; }
            if (cur == 0) dreamLines[day] = dreamLine;
            if (cur >= Day(day).Length) return;
            g.player.frozen = true;
            g.hud.Open(Modal.Writing);
            Show();
        }

        void Show()
        {
            var p = Day(curDay)[cur];
            g.hud.header = g.job != null ? g.job.Header(curDay, cur) : "Chapter " + curDay;
            string extra = g.job != null ? g.job.Extra(curDay, cur) : null;
            g.hud.body = ChapterText(curDay, false) + (extra != null ? extra + "\n\n" : "");
            g.hud.footer = p.ask + (g.job != null && g.job.FooterExtra != null ? "   <size=20>" + g.job.FooterExtra + "</size>" : "");
            var opts = new string[p.options.Length];
            for (int i = 0; i < opts.Length; i++) opts[i] = p.options[i].text;
            g.hud.options = opts;
        }

        public void HandleInput()
        {
            if (g.hud.Fresh) return;
            if (g.job != null) g.job.PageInput();
            int pick = In.Choice();
            if (pick >= 0) Choose(pick);
        }

        void Choose(int i)
        {
            var p = Day(curDay)[cur];
            if (i >= p.options.Length) return;
            var line = p.options[i];
            written.Add(new Entry { day = curDay, passage = cur, option = i });
            Sfx.Ui("typing", 0.5f, 1.1f);
            switch (line.tag)
            {
                case Tag.Face:
                    g.tagFace++;
                    g.Steady(0.03f, "wrote a line that faces it");
                    break;
                case Tag.Away:
                    g.tagAway++;
                    g.Shake(0.03f, "wrote a line that looks away");
                    break;
                case Tag.Blame:
                    g.tagBlame++;
                    g.Log("manuscript: wrote a line that blames someone");
                    break;
                case Tag.Knows:
                    g.tagFace++;
                    g.clues++;
                    g.Shake(0.04f, "wrote something he shouldn't know (counts as a clue)");
                    g.hud.Say("You don't remember deciding to write that.");
                    break;
            }
            if (g.job != null) g.job.Chose(curDay, cur, line);
            if (DayClock.I != null) DayClock.I.Advance(g.job != null ? g.job.MinutesPer : 50f);
            cur++;
            if (cur >= Day(curDay).Length) g.OnChapterDone();
            else if (g.job != null && g.job.OneAtATime) { g.hud.Close(); g.player.frozen = false; }
            else Show();
        }

        /// <summary>Debug skip and test runs: does the rest of today's work with random choices.</summary>
        public void AutoWrite(int day)
        {
            if (curDay != day) { curDay = day; cur = 0; }
            int guard = 0;
            while (cur < Day(curDay).Length && guard++ < 10)
            {
                if (!Writing) { g.player.frozen = true; g.hud.Open(Modal.Writing); Show(); }
                Choose(g.rng.Next(3));
            }
        }

        string ChapterText(int day, bool unreliable)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(dreamLines[day])) sb.Append("<i>").Append(dreamLines[day]).Append("</i>\n\n");
            foreach (var e in written)
            {
                if (e.day != day) continue;
                var p = Day(day)[e.passage];
                if (e.option < 0) { sb.Append("<i>(nothing)</i>\n\n"); continue; }
                int shown = e.option;
                // Low Grip: some lines from earlier days are no longer the ones you chose.
                if (unreliable && (e.day * 7 + e.passage * 3) % 3 == g.rewriteSeed % 3) shown = (e.option + 1) % p.options.Length;
                sb.Append(p.options[shown].text).Append("\n\n");
            }
            return sb.ToString();
        }

        /// <summary>A line the player didn't write (a hallucination puts it there; it stays for the day).</summary>
        public string foreign;

        public string FullText()
        {
            if (written.Count == 0) return string.IsNullOrEmpty(foreign) ? "The page is blank." : "<i>" + foreign + "</i>";
            bool slipping = g.grip < 0.45f;
            if (slipping && !warnedRewrite)
            {
                warnedRewrite = true;
                g.Log("manuscript: Grip is low, so earlier chapters now read differently from what was chosen");
            }
            var sb = new StringBuilder();
            for (int d = System.Math.Max(1, g.day - 2); d <= g.day; d++)
            {
                string text = ChapterText(d, slipping && d < g.day);
                if (text.Length == 0) continue;
                sb.Append("<b>").Append(g.job != null ? g.job.Header(d, 0) : "Chapter " + d).Append("</b>\n").Append(text);
            }
            if (!string.IsNullOrEmpty(foreign)) sb.Append("<i>").Append(foreign).Append("</i>\n\n");
            return sb.ToString();
        }
    }
}
