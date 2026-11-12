using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace NightPages
{
    /// <summary>
    /// The voice: a language model writes some of the house's words, from the story bible and the hunter's picture of
    /// you. The story engine still decides everything that happens and what's true; the voice only writes, and only
    /// knows what may be told by that day, so it can't give the truth away.
    ///
    /// Each morning, behind the fade, it asks for three things for the day: a note left for you this evening (in the
    /// house's voice, aimed at what moves you), a page for tonight's dream, and the line you wake to tomorrow. It
    /// never holds anything up: if there's no key, no connection, or anything goes wrong, nothing is added and the
    /// written text is used as always. Everything it writes is logged (F1).
    ///
    /// Key: the ANTHROPIC_API_KEY environment variable, or nightpages-voice.json beside the settings
    /// ({"apiKey": "...", "model": "..."}). Playtests only: a released game would go through a small server instead.
    /// </summary>
    public class StoryVoice : MonoBehaviour
    {
        [System.Serializable] class Config { public string apiKey = "", model = "claude-haiku-5-5"; }
        [System.Serializable] class Msg { public string role, content; }
        [System.Serializable] class Req { public string model; public int max_tokens; public string system; public Msg[] messages; }
        [System.Serializable] class Block { public string type, text; }
        [System.Serializable] class Resp { public Block[] content; }
        [System.Serializable] class Out { public string note, page, waking; }

        GameDirector g;
        Config cfg;
        readonly Dictionary<int, Out> written = new Dictionary<int, Out>();
        /// <summary>The last few things the house did (fed from the log), for the voice to draw on.</summary>
        readonly List<string> lately = new List<string>();
        public int asked, answered, failed;
        public string status = "off";

        public static string ConfigPath { get { return Path.Combine(Application.persistentDataPath, "nightpages-voice.json"); } }

        public void Init(GameDirector director)
        {
            g = director;
            cfg = new Config();
            try { if (File.Exists(ConfigPath)) cfg = JsonUtility.FromJson<Config>(File.ReadAllText(ConfigPath)) ?? new Config(); } catch { }
            var env = System.Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrEmpty(cfg.apiKey) && !string.IsNullOrEmpty(env)) cfg.apiKey = env;
            if (string.IsNullOrEmpty(cfg.model)) cfg.model = "claude-haiku-5-5";
            status = Ready ? "ready (" + cfg.model + ")" : "off (no key: set ANTHROPIC_API_KEY or write " + ConfigPath + ")";
            g.Log("voice: " + status + (Settings.S.voice ? "" : "; turned off in Options"));
        }

        public bool Ready { get { return cfg != null && !string.IsNullOrEmpty(cfg.apiKey); } }
        bool On { get { return Ready && Settings.S.voice && !Tour.Requested; } }

        public void Heard(string line)
        {
            if (!line.StartsWith("haunting")) return;
            int k = line.IndexOf(':');
            string s = k > 0 ? line.Substring(k + 1).Trim() : line;
            if (s.Length > 140) s = s.Substring(0, 140);
            lately.Add(s);
            if (lately.Count > 8) lately.RemoveAt(0);
        }

        public string Note(int day) { Out o; return written.TryGetValue(day, out o) ? Clean(o.note) : null; }
        public string Page(int day) { Out o; return written.TryGetValue(day, out o) ? Clean(o.page) : null; }
        public string Waking(int day) { Out o; return written.TryGetValue(day - 1, out o) ? Clean(o.waking) : null; }

        static string Clean(string s) { return string.IsNullOrEmpty(s) ? null : s.Trim().Replace("\\n", "\n"); }

        /// <summary>Morning: ask for today's words (doesn't wait).</summary>
        public void AskForDay(int calendar, int storyDay)
        {
            if (!On || written.ContainsKey(calendar)) return;
            StartCoroutine(Ask(calendar, storyDay));
        }

        public void NewRun() { written.Clear(); lately.Clear(); }

        /// <summary>Studio: the brief it would send for day 3, and (with a key) one real request.</summary>
        public IEnumerator StudioCheck(System.Action<string> problem)
        {
            g.Log("voice: studio: the brief for day 3 would be:\n" + Brief(3, 3).TrimEnd());
            if (!Ready) { g.Log("voice: studio: no key on this machine, so nothing was asked (the written text stands in)"); yield break; }
            yield return Ask(99, 3);
            if (!written.ContainsKey(99)) problem("voice: a key is set but the model didn't answer (" + status + ")");
            written.Remove(99);
        }

        // ---------------------------------------------------------------- the brief

        string SystemPrompt()
        {
            return "You write short pieces of text for a psychological horror game, Night Pages. The player is staying alone in their dead grandparents' " +
                   "house; by day they do chores and their job, at night they dream. The house is learning what frightens this particular player. " +
                   "You are the house's voice. Write in plain, concrete, unsettling English: ordinary domestic details made wrong. No purple prose, no " +
                   "exclamation marks, no cliches (no 'blood-curdling', no 'shiver down your spine'). Never name real people or brands. " +
                   "Never reveal or hint at anything the brief says is hidden. Keep within the content limits given. " +
                   "Reply with JSON only, no other text: {\"note\": \"...\", \"page\": \"...\", \"waking\": \"...\"}. " +
                   "note: 40-90 words, a note found left in the house this evening, as if written by someone who knows the player is reading. " +
                   "page: 40-90 words, a page from a diary found in tonight's dream, first person, as if the player wrote it and doesn't remember. " +
                   "waking: one sentence, under 20 words, what the player notices on waking tomorrow.";
        }

        string Brief(int calendar, int storyDay)
        {
            var t = g.tale;
            var sb = new StringBuilder();
            sb.Append("Day ").Append(calendar).Append(" of ").Append(GameDirector.RunDays).Append(" (how far into the story: ").Append(storyDay).Append(" of 7).\n");
            sb.Append("The player: ").Append(g.hero != null ? g.hero.Name : "unnamed").Append(", addressed as ").Append(g.hero != null ? Character.AddressNames[g.hero.address] : "they / them");
            sb.Append(", working here as ").Append(g.job != null ? g.job.Name : "a writer").Append(".\n");
            if (t != null)
            {
                sb.Append("The story so far may mention: ").Append(t.Var("name", "someone")).Append(" (").Append(t.Var("rel", "someone who lived here")).Append("), ");
                sb.Append("around ").Append(t.Var("year", "years ago")).Append(", ").Append(t.Var("season", "")).Append(".");
                if (t.keepsake != null) sb.Append(" A keepsake keeps turning up: their ").Append(t.Var("keepsake", "things")).Append(".");
                sb.Append("\n");
                sb.Append("Hidden (never reveal, never hint): what really happened, and whether anything supernatural is going on at all.\n");
                if (storyDay <= 2) sb.Append("It is early: keep it deniable. Nothing impossible, only odd.\n");
                else if (storyDay <= 4) sb.Append("Middle of the stay: things should stop making sense, but stay quiet and specific.\n");
                else sb.Append("Late in the stay: it may speak directly to the player now, but still give nothing away.\n");
            }
            // The hunter's picture of this player.
            sb.Append("What moves this player most so far: ").Append(Safe(() => g.fear.TopFamily(), "unknown")).Append(".\n");
            string hide = Safe(() => g.teller.tactics.Most("hide in the ", 0.5f), null);
            if (hide != null) sb.Append("When frightened, they ").Append(hide).Append(". The house has noticed.\n");
            if (lately.Count > 0) sb.Append("Recently in the house: ").Append(string.Join("; ", lately.ToArray())).Append(".\n");
            // Limits.
            sb.Append("Content limits: ");
            sb.Append(Settings.S.gore == 0 ? "gore may be described plainly, briefly" : Settings.S.gore == 1 ? "gore only implied (a smell, a stain, never the thing)" : "no gore at all");
            sb.Append(Settings.S.scares == 2 ? "; keep it gentle" : Settings.S.scares == 1 ? "; nothing too intense" : "");
            if (!string.IsNullOrEmpty(Settings.S.banned)) sb.Append("; never mention: ").Append(Settings.S.banned);
            sb.Append(".\n");
            return sb.ToString();
        }

        static string Safe(System.Func<string> f, string fallback) { try { var s = f(); return string.IsNullOrEmpty(s) ? fallback : s; } catch { return fallback; } }

        // ---------------------------------------------------------------- asking

        IEnumerator Ask(int calendar, int storyDay)
        {
            asked++;
            var req = new Req { model = cfg.model, max_tokens = 700, system = SystemPrompt(), messages = new[] { new Msg { role = "user", content = Brief(calendar, storyDay) } } };
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(req));
            using (var www = new UnityWebRequest("https://api.anthropic.com/v1/messages", "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(body);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("content-type", "application/json");
                www.SetRequestHeader("x-api-key", cfg.apiKey);
                www.SetRequestHeader("anthropic-version", "2023-06-01");
                www.timeout = 40;
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                {
                    failed++;
                    g.Log("voice: day " + calendar + ": no answer (" + www.error + "); the written text is used");
                    yield break;
                }
                Out o = null;
                try
                {
                    var r = JsonUtility.FromJson<Resp>(www.downloadHandler.text);
                    string text = r != null && r.content != null && r.content.Length > 0 ? r.content[0].text : null;
                    if (text != null)
                    {
                        int a = text.IndexOf('{'), b = text.LastIndexOf('}');
                        if (a >= 0 && b > a) o = JsonUtility.FromJson<Out>(text.Substring(a, b - a + 1));
                    }
                }
                catch (System.Exception e) { g.Log("voice: day " + calendar + ": couldn't read the answer (" + e.Message + ")"); }
                if (o == null || (string.IsNullOrEmpty(o.note) && string.IsNullOrEmpty(o.page))) { failed++; yield break; }
                written[calendar] = o;
                answered++;
                g.Log("voice: day " + calendar + " written. Note: \"" + Clean(o.note) + "\"  Page: \"" + Clean(o.page) + "\"  Waking: \"" + Clean(o.waking) + "\"");
            }
        }
    }
}
