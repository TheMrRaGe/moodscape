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
    /// Where it asks (nightpages-voice.json beside the settings, or found by itself): Ollama on this PC by default
    /// (free and offline: install Ollama, then "ollama pull qwen3:4b"); or any OpenAI-compatible endpoint
    /// ({"provider": "openai", "url": "...", "apiKey": "...", "model": "..."}: LM Studio, Groq's or OpenRouter's free
    /// tiers); or Claude ({"provider": "anthropic", "apiKey": "..."}). Keys in GROQ_API_KEY, OPENROUTER_API_KEY or
    /// ANTHROPIC_API_KEY are picked up too.
    /// </summary>
    public class StoryVoice : MonoBehaviour
    {
        /// <summary>provider: "anthropic", "openai" (any OpenAI-compatible endpoint: Ollama, LM Studio, llama.cpp's server,
        /// Groq, OpenRouter, Cerebras, GitHub Models...), or "auto" (a key in the environment, else Ollama on this PC).</summary>
        [System.Serializable] class Config { public string provider = "auto", url = "", apiKey = "", model = ""; }
        [System.Serializable] class OAMsg { public string role, content; }
        [System.Serializable] class OAReq { public string model; public int max_tokens; public float temperature; public OAMsg[] messages; }
        [System.Serializable] class OAChoice { public OAMsg message; }
        [System.Serializable] class OAResp { public OAChoice[] choices; }
        [System.Serializable] class Tags { public TagModel[] models; }
        [System.Serializable] class TagModel { public string name; }
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
            if (string.IsNullOrEmpty(cfg.provider)) cfg.provider = "auto";
            if (cfg.provider == "auto") AutoPick();
            if (cfg.provider == "anthropic" && string.IsNullOrEmpty(cfg.model)) cfg.model = "claude-haiku-5-5";
            if (cfg.provider == "anthropic" && string.IsNullOrEmpty(cfg.url)) cfg.url = "https://api.anthropic.com/v1/messages";
            if (cfg.provider == "ollama") { cfg.provider = "openai"; if (string.IsNullOrEmpty(cfg.url)) cfg.url = "http://localhost:11434/v1/chat/completions"; }
            Report();
            // No key and no endpoint given: look for Ollama on this PC (free, offline), and use what it has installed.
            if (!Ready && cfg.provider == "auto" && !Tour.Requested) StartCoroutine(FindOllama());
        }

        void Report()
        {
            status = Ready ? "ready: " + cfg.provider + " at " + cfg.url + " (" + cfg.model + ")" : "off (no model found: install Ollama, or set a key; see " + ConfigPath + ")";
            g.Log("voice: " + status + (Settings.S.voice ? "" : "; turned off in Options"));
        }

        void AutoPick()
        {
            string Env(string k) { return System.Environment.GetEnvironmentVariable(k); }
            if (!string.IsNullOrEmpty(cfg.apiKey) && !string.IsNullOrEmpty(cfg.url)) { cfg.provider = cfg.url.Contains("anthropic.com") ? "anthropic" : "openai"; return; }
            if (!string.IsNullOrEmpty(Env("ANTHROPIC_API_KEY"))) { cfg.provider = "anthropic"; cfg.apiKey = Env("ANTHROPIC_API_KEY"); return; }
            if (!string.IsNullOrEmpty(Env("GROQ_API_KEY"))) { cfg.provider = "openai"; cfg.apiKey = Env("GROQ_API_KEY"); cfg.url = "https://api.groq.com/openai/v1/chat/completions"; if (string.IsNullOrEmpty(cfg.model)) cfg.model = "llama-3.3-70b-versatile"; return; }
            if (!string.IsNullOrEmpty(Env("OPENROUTER_API_KEY"))) { cfg.provider = "openai"; cfg.apiKey = Env("OPENROUTER_API_KEY"); cfg.url = "https://openrouter.ai/api/v1/chat/completions"; if (string.IsNullOrEmpty(cfg.model)) cfg.model = "openrouter/free"; return; }
            if (!string.IsNullOrEmpty(cfg.url)) cfg.provider = "openai";
        }

        // Ollama answers on localhost:11434; its list of installed models picks the model (a small, recent one by preference).
        IEnumerator FindOllama()
        {
            using (var www = UnityWebRequest.Get("http://localhost:11434/api/tags"))
            {
                www.timeout = 3;
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success) { g.Log("voice: no Ollama on this PC (" + www.error + "); the written text is used"); yield break; }
                Tags t = null;
                try { t = JsonUtility.FromJson<Tags>(www.downloadHandler.text); } catch { }
                if (t == null || t.models == null || t.models.Length == 0) { g.Log("voice: Ollama is running but has no models (try: ollama pull qwen3:4b)"); yield break; }
                string pick = t.models[0].name;
                foreach (var pref in new[] { "qwen3", "gemma3", "llama3.2", "mistral", "qwen2.5", "phi" })
                {
                    var m = System.Array.Find(t.models, x => x.name.StartsWith(pref));
                    if (m != null) { pick = m.name; break; }
                }
                cfg.provider = "openai";
                cfg.url = "http://localhost:11434/v1/chat/completions";
                if (string.IsNullOrEmpty(cfg.model)) cfg.model = pick;
                Report();
            }
        }

        /// <summary>Somewhere to ask: a key for a cloud model, or a local endpoint (which needs none).</summary>
        public bool Ready
        {
            get
            {
                if (cfg == null || string.IsNullOrEmpty(cfg.url) || string.IsNullOrEmpty(cfg.model)) return false;
                bool local = cfg.url.Contains("localhost") || cfg.url.Contains("127.0.0.1");
                return local || !string.IsNullOrEmpty(cfg.apiKey);
            }
        }
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
            if (!Ready && cfg.provider == "auto") yield return FindOllama();
            if (!Ready) { g.Log("voice: studio: no model on this machine (no Ollama, no key), so nothing was asked (the written text stands in)"); yield break; }
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
            bool anthropic = cfg.provider == "anthropic";
            string brief = Brief(calendar, storyDay);
            string json;
            if (anthropic)
                json = JsonUtility.ToJson(new Req { model = cfg.model, max_tokens = 700, system = SystemPrompt(), messages = new[] { new Msg { role = "user", content = brief } } });
            else
                json = JsonUtility.ToJson(new OAReq
                {
                    model = cfg.model, max_tokens = 900, temperature = 0.9f,
                    messages = new[] { new OAMsg { role = "system", content = SystemPrompt() }, new OAMsg { role = "user", content = brief + (cfg.model.Contains("qwen3") ? " /no_think" : "") } },
                });
            var body = Encoding.UTF8.GetBytes(json);
            float t0 = Time.realtimeSinceStartup;
            using (var www = new UnityWebRequest(cfg.url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(body);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("content-type", "application/json");
                if (anthropic) { www.SetRequestHeader("x-api-key", cfg.apiKey); www.SetRequestHeader("anthropic-version", "2023-06-01"); }
                else if (!string.IsNullOrEmpty(cfg.apiKey)) www.SetRequestHeader("Authorization", "Bearer " + cfg.apiKey);
                www.timeout = anthropic ? 40 : 120;          // a local model on a busy PC can take a while
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
                    string text;
                    if (anthropic) { var r = JsonUtility.FromJson<Resp>(www.downloadHandler.text); text = r != null && r.content != null && r.content.Length > 0 ? r.content[0].text : null; }
                    else { var r = JsonUtility.FromJson<OAResp>(www.downloadHandler.text); text = r != null && r.choices != null && r.choices.Length > 0 && r.choices[0].message != null ? r.choices[0].message.content : null; }
                    if (text != null)
                    {
                        // Thinking models: drop the thinking, keep the answer.
                        int th = text.IndexOf("</think>");
                        if (th >= 0) text = text.Substring(th + 8);
                        int a = text.IndexOf('{'), b = text.LastIndexOf('}');
                        if (a >= 0 && b > a) o = JsonUtility.FromJson<Out>(text.Substring(a, b - a + 1));
                    }
                }
                catch (System.Exception e) { g.Log("voice: day " + calendar + ": couldn't read the answer (" + e.Message + ")"); }
                if (o == null || (string.IsNullOrEmpty(o.note) && string.IsNullOrEmpty(o.page))) { failed++; yield break; }
                written[calendar] = o;
                answered++;
                g.Log("voice: day " + calendar + " written by " + cfg.model + " in " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s. Note: \"" + Clean(o.note) + "\"  Page: \"" + Clean(o.page) + "\"  Waking: \"" + Clean(o.waking) + "\"");
            }
        }
    }
}
