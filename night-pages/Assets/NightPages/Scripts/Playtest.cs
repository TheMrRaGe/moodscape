using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Playtesting help. While you play the built game (not a test run), everything the game logs is also written to
    /// night-pages/playtests/&lt;date-time&gt;/session.log, next to the project. Press F8 when something's wrong:
    /// it saves a screenshot and a note of where you are, what you're looking at, what you're holding, the time, the
    /// prompt on screen and the last 40 things that happened, into the same folder as mark-NN.png / mark-NN.txt.
    /// </summary>
    public class Playtest : MonoBehaviour
    {
        public static Playtest I;
        GameDirector g;
        string dir;
        StreamWriter log;
        readonly Queue<string> recent = new Queue<string>();
        int marks;
        float toastUntil;

        public static void Attach(GameDirector director)
        {
            if (Tour.Requested) return;              // test runs keep their own records
            var p = director.gameObject.AddComponent<Playtest>();
            p.g = director;
            I = p;
            p.Open();
        }

        void Open()
        {
            try
            {
                // The build sits in night-pages/Build/NightPages_Data; in the editor, Assets is inside night-pages.
                var data = new DirectoryInfo(Application.dataPath);
                var project = Application.isEditor ? data.Parent : data.Parent != null ? data.Parent.Parent : null;
                if (project == null || !Directory.Exists(Path.Combine(project.FullName, "Assets"))) project = null;
                string root = project != null ? Path.Combine(project.FullName, "playtests") : Path.Combine(Application.persistentDataPath, "playtests");
                dir = Path.Combine(root, System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(dir);
                log = new StreamWriter(Path.Combine(dir, "session.log"), false, Encoding.UTF8) { AutoFlush = true };
                log.WriteLine("Night Pages playtest, " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ". F8 marks a problem.");
            }
            catch (System.Exception e) { Debug.LogWarning("[NightPages] playtest log: " + e.Message); log = null; }
        }

        /// <summary>Every line the game logs (GameDirector.Log calls this).</summary>
        public void Note(string line)
        {
            string stamped = Stamp() + "  " + line;
            recent.Enqueue(stamped);
            while (recent.Count > 40) recent.Dequeue();
            if (log != null) { try { log.WriteLine(stamped); } catch (System.Exception) { } }
        }

        string Stamp()
        {
            float t = Time.realtimeSinceStartup;
            string clock = DayClock.I != null && g != null && (g.phase == Phase.Morning || g.phase == Phase.Afternoon || g.phase == Phase.Evening) ? " " + DayClock.I.Now : "";
            return ((int)t / 60) + ":" + ((int)t % 60).ToString("00") + (g != null ? " d" + g.day + " " + g.phase : "") + clock;
        }

        void Update()
        {
            if (In.Down(Btn.Mark)) Mark();
        }

        void Mark()
        {
            if (dir == null) return;
            marks++;
            string name = "mark-" + marks.ToString("00");
            try { ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png")); } catch (System.Exception) { }
            var sb = new StringBuilder();
            sb.AppendLine("Mark " + marks + " at " + Stamp());
            var p = g.player;
            if (p != null)
            {
                Vector3 pos = p.transform.position;
                var room = g.house != null ? g.house.RoomAt(pos) : null;
                sb.AppendLine("where: " + pos.ToString("F2") + " in " + (room != null ? room.name : g.house != null && g.house.InCellar(pos) ? "the cellar" : "(no room)") + ", facing " + Mathf.RoundToInt(p.transform.eulerAngles.y) + " deg");
                RaycastHit hit;
                if (Physics.Raycast(p.cam.transform.position, p.cam.transform.forward, out hit, 12f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var t = hit.collider.transform;
                    string path = t.name;
                    for (var u = t.parent; u != null && (g.house == null || u != g.house.transform); u = u.parent) path = u.name + "/" + path;
                    sb.AppendLine("looking at: " + path + " (" + hit.distance.ToString("F1") + " m away, at " + hit.point.ToString("F2") + ")");
                }
                else sb.AppendLine("looking at: nothing within 12 m");
                sb.AppendLine("holding: " + (p.carrying != null ? p.carrying.name : "nothing"));
            }
            if (g.hud != null)
            {
                sb.AppendLine("prompt: " + (g.hud.prompt ?? ""));
                sb.AppendLine("modal: " + g.hud.modal);
            }
            if (g.job != null) sb.AppendLine("job: " + g.job.Name + (g.workDone ? " (done today)" : ""));
            if (g.chores != null)
                foreach (var c in g.chores.today) sb.AppendLine("  [" + (c.done ? "x" : " ") + "] " + DayClock.Say(c.by) + " " + c.text + (c.progress != null && !c.done ? " (" + c.progress() + ")" : ""));
            sb.AppendLine("last things that happened:");
            foreach (var r in recent) sb.AppendLine("  " + r);
            try { File.WriteAllText(Path.Combine(dir, name + ".txt"), sb.ToString()); } catch (System.Exception) { }
            Note("PLAYTEST MARK " + marks + " (screenshot " + name + ".png)");
            toastUntil = Time.unscaledTime + 2f;
            if (g.hud != null) g.hud.Say("Marked (" + name + "). Keep playing.", 2f);
        }

        void OnDestroy() { if (log != null) { try { log.Close(); } catch (System.Exception) { } } }
    }
}
