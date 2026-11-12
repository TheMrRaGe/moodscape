using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Clues, rebuilt: the truth's four papers are hidden in the house at the start of a run, each in one of two or
    /// three places chosen by the run's seed, and found by doing (going through a jacket, the bin, the books, the drain).
    /// Searching the wrong place finds something ordinary. Found clues pin themselves to the clue board (the study wall
    /// from day 2, or B anywhere), where pairs that belong together become deductions written on index cards in
    /// Grandma's hand. Endings ask for deductions now, not a count of notes. With your grip low, a card can be written
    /// wrong, and a wrong card is how the gentler story happens; look again when you're steadier and it can be put right.
    /// (The story's own notes still turn up at dusk; they tell the tale but don't make deductions.)
    /// </summary>
    public partial class GameDirector
    {
        class Hide { public string id, prompt, empty, how; public System.Func<Transform> at; }
        class ClueDef { public int index; public string id, title; public string[] places; }
        class Deduction { public string id, a, b, text, wrong; }

        static readonly string[][] ClueTitles =
        {
            new[] { "The pharmacy receipt", "The child's drawing", "The condolence card", "The clipping" },
            new[] { "The court letter", "The train tickets", "Grandpa's ledger", "A note in a stranger's hand" },
            new[] { "The deed", "The 1911 floor plan", "The photograph of the hall", "Grandma's diary" },
        };
        static readonly string[][][] CluePlaces =
        {
            new[] { new[] { "wardrobe", "cupboard", "bedside" }, new[] { "halltable", "books", "bedside" }, new[] { "hallclock", "dustbin", "halltable" }, new[] { "dustbin", "books", "kitchentable" } },
            new[] { new[] { "halltable", "bedside", "books" }, new[] { "kitchentable", "cupboard", "wardrobe" }, new[] { "wardrobetop", "books", "hallclock" }, new[] { "sinktrap", "basin", "halltable" } },
            new[] { new[] { "halltable", "books", "bedside" }, new[] { "hallclock", "wardrobetop", "cupboard" }, new[] { "darkroom", "shoebox", "books" }, new[] { "bedside", "wardrobe", "cupboard" } },
        };
        static readonly Deduction[][] Deductions =
        {
            new[]
            {
                new Deduction { id = "night", a = "c3", b = "c1", text = "I was in the house that night.", wrong = "Someone else was in the house that night." },
                new Deduction { id = "tablets", a = "c0", b = "c2", text = "Both of them in one night. And the tablets were bought in my name.", wrong = "They were old. It was their hearts. It happens." },
            },
            new[]
            {
                new Deduction { id = "away", a = "c0", b = "c1", text = "They were taking me away. Someone stopped them.", wrong = "They were going on holiday without me." },
                new Deduction { id = "buried", a = "c2", b = "c3", text = "Something was buried in the cellar that week.", wrong = "He was mending the cellar floor. That's all the concrete was." },
            },
            new[]
            {
                new Deduction { id = "before", a = "c1", b = "c2", text = "The house was here before the house.", wrong = "The house was extended once. That's all." },
                new Deduction { id = "waiting", a = "c0", b = "c3", text = "Nobody leaves this house. It has been waiting for me.", wrong = "An old house, an old family. Nothing more." },
            },
        };

        List<Hide> hides;
        readonly Dictionary<string, string> hiddenIn = new Dictionary<string, string>();     // place -> clue id
        readonly HashSet<string> searched = new HashSet<string>();
        /// <summary>Clue ids found this run ("c0".."c3"), deductions made (right), and cards written wrong.</summary>
        public readonly List<string> found = new List<string>();
        public readonly List<string> deduced = new List<string>(), deducedWrong = new List<string>();
        readonly List<GameObject> spotObjects = new List<GameObject>();
        GameObject board;
        Renderer[] wallThings;
        string pinned;

        /// <summary>Correct deductions the deeper endings need (easy 1, otherwise 2).</summary>
        public static int DeductionsNeeded { get { return Level == 0 ? 1 : 2; } }
        public bool Knows(string deduction) { return deduced.Contains(deduction); }

        List<Hide> Hides()
        {
            var h = house;
            System.Func<string, Transform> named = n =>
            {
                foreach (var t in h.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
                return null;
            };
            return new List<Hide>
            {
                new Hide { id = "wardrobe", how = "front", prompt = "Go through the jacket in the wardrobe", empty = "A handkerchief, a button, a bus ticket from 1979.", at = () => h.wardrobe },
                new Hide { id = "wardrobetop", how = "top", prompt = "Feel along the top of the wardrobe", empty = "Dust, thick as felt. A dead moth.", at = () => h.wardrobe },
                new Hide { id = "shoebox", how = "low", prompt = "Open the shoebox at the bottom of the wardrobe", empty = "Her good shoes, wrapped in tissue. They still smell of her.", at = () => h.wardrobe },
                new Hide { id = "cupboard", how = "front", prompt = "Reach into the back of the cupboard", empty = "Tins with no labels. A jar of something that was jam.", at = () => named("Cabinets") },
                new Hide { id = "halltable", how = "front", prompt = "Open the hall table drawer", empty = "Keys to nothing in the house. Rubber bands. A dead battery.", at = () => named("Hall table") },
                new Hide { id = "books", how = "front", prompt = "Take the books down, one by one", empty = "Pressed flowers in one. Nothing in the rest.", at = () => named("Bookshelf") },
                new Hide { id = "bedside", how = "front", prompt = "Lift the drawer lining (bedside table)", empty = "Newspaper from 1974, used as lining. Nothing under it.", at = () => named("Bedside table") },
                new Hide { id = "kitchentable", how = "low", prompt = "Feel under the kitchen table", empty = "Old chewing gum, gone hard. Someone sat here a lot.", at = () => named("Kitchen table") },
                new Hide { id = "dustbin", how = "top", prompt = "Go through the dustbin", empty = "Peelings. Tea leaves. A broken cup, wrapped carefully in newspaper.", at = () => named("Dustbin") },
                new Hide { id = "hallclock", how = "front", prompt = "Open the case of the hall clock", empty = "The key to wind it, on a hook. The pendulum, still.", at = () => h.hallClock },
                new Hide { id = "sinktrap", how = "low", prompt = "Undo the trap under the sink", empty = "Grey water and hair. You wish you hadn't.", at = () => h.sink != null ? h.sink.transform : null },
                new Hide { id = "basin", how = "front", prompt = "Reach down the basin's drain", empty = "A hairpin. The water down there is warm.", at = () => h.washbasin != null ? h.washbasin.transform : null },
                new Hide { id = "darkroom", how = "front", prompt = "Go through his old negatives", empty = "Strips of the house, the garden, her. Never the cellar.", at = () => h.enlarger != null ? h.enlarger.transform : null },
            };
        }

        /// <summary>
        /// A new run (or one continued): hide this truth's four papers, one place each, chosen by the house's seed so
        /// a continued run hides them in the same places. Already-found clues aren't hidden again.
        /// </summary>
        void HideClues()
        {
            foreach (var o in spotObjects) if (o != null) Destroy(o);
            spotObjects.Clear();
            hiddenIn.Clear();
            searched.Clear();
            pinned = null;
            if (board != null) { Destroy(board); board = null; }
            hides = Hides();
            var rng = new System.Random(House.BuiltSeed ^ ((int)truth * 7919));
            var places = CluePlaces[(int)truth];
            var used = new HashSet<string>();
            var report = new List<string>();
            for (int c = 0; c < places.Length; c++)
            {
                var order = new List<string>(places[c]);
                for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = order[i]; order[i] = order[j]; order[j] = t; }
                string id = "c" + c, where = null;
                foreach (var p in order) { var hd = hides.Find(x => x.id == p); if (!used.Contains(p) && hd != null && hd.at() != null) { where = p; break; } }
                if (where == null) foreach (var hd in hides) if (!used.Contains(hd.id) && hd.at() != null) { where = hd.id; break; }
                if (where == null) { Log("clues: nowhere left to hide '" + ClueTitles[(int)truth][c] + "'"); continue; }
                used.Add(where);
                if (!found.Contains(id)) hiddenIn[where] = id;
                report.Add(ClueTitles[(int)truth][c] + (found.Contains(id) ? " (already found)" : " -> " + where));
            }
            // Every place any of this truth's papers could be is searchable; only the chosen ones hold one.
            var candidates = new HashSet<string>();
            foreach (var arr in places) foreach (var p in arr) candidates.Add(p);
            int made = 0;
            foreach (var hd in hides)
            {
                if (!candidates.Contains(hd.id)) continue;
                var t = hd.at();
                if (t == null) continue;
                Vector3 pos;
                if (!SpotFor(t, hd.how, out pos)) continue;
                var go = new GameObject("Search: " + hd.id);
                go.transform.SetParent(house.transform, true);
                go.transform.position = pos;
                var col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(0.45f, 0.4f, 0.45f);
                var it = Build.Usable(go, hd.prompt);
                var hide = hd;
                it.promptFn = () => searched.Contains(hide.id) ? "" : hide.prompt;
                it.can = () => !searched.Contains(hide.id) && phase != Phase.Dream && phase != Phase.Title;
                it.use = () => Search(hide);
                spotObjects.Add(go);
                made++;
            }
            Log("clues: the " + truth + " truth's papers are hidden: " + string.Join("; ", report.ToArray()) + ". " + made + " places can be searched; endings need " +
                DeductionsNeeded + " deduction" + (DeductionsNeeded == 1 ? "" : "s"));
        }

        // Where to stand the search point for a piece of furniture: in front of the face toward the room, on top, or low.
        bool SpotFor(Transform t, string how, out Vector3 pos)
        {
            pos = Vector3.zero;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { pos = t.position; return true; }
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var room = house.RoomAt(b.center);
            Vector3 dir = room != null ? room.center - b.center : -t.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir.Normalize();
            float reach = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
            if (how == "top") pos = new Vector3(b.center.x, b.max.y + 0.12f, b.center.z);
            else if (how == "low") pos = b.center + dir * (reach + 0.1f) + Vector3.up * (Mathf.Max(b.min.y + 0.25f, 0.25f) - b.center.y);
            else pos = b.center + dir * (reach + 0.12f) + Vector3.up * (Mathf.Clamp(b.center.y, 0.6f, 1.4f) - b.center.y);
            return true;
        }

        void Search(Hide h)
        {
            if (searched.Contains(h.id)) return;
            searched.Add(h.id);
            Sfx.Ui("paper", 0.5f, 0.9f + Random.value * 0.2f);
            string id;
            if (!hiddenIn.TryGetValue(h.id, out id))
            {
                Log("clues: searched " + h.id + ": nothing");
                hud.Say(h.empty, 4f);
                return;
            }
            hiddenIn.Remove(h.id);
            FindClue(id, h.id);
        }

        void FindClue(string id, string where)
        {
            if (found.Contains(id)) return;
            found.Add(id);
            int c = int.Parse(id.Substring(1));
            clues++;
            tagFace++;
            Shake(0.02f, "found " + ClueTitles[(int)truth][c].ToLowerInvariant() + " (" + where + ")");
            Log("clues: found '" + ClueTitles[(int)truth][c] + "' in " + where + " (" + found.Count + " of 4); it goes on the board");
            hud.header = ClueTitles[(int)truth][c];
            hud.body = ClueTexts[(int)truth][c];
            hud.footer = "[E] or [Esc] to put it down.  It goes on the board" + (day >= 2 ? " in the study" : "") + " (B).";
            player.frozen = true;
            hud.Open(Modal.Reading);
            Sfx.Ui("pageturn", 0.6f);
            UpdateBoard();
            RefreshObjectives();
        }

        // ---------------------------------------------------------------- the board

        /// <summary>The corkboard on the study wall, from day 2: found papers pinned to it, and the index cards.</summary>
        void UpdateBoard()
        {
            if (day < 2 || house == null) return;
            var study = house.rooms.Find(r => r.name == "study");
            if (study == null) return;
            if (board == null)
            {
                // A clear stretch of study wall at eye height: nearest walls first, sliding along each until nothing
                // (a painting, a shelf, a window) is in the way.
                Vector3 best = Vector3.zero, normal = Vector3.zero;
                float bestD = float.MaxValue;
                foreach (float hgt in new[] { 1.45f, 1.25f })
                    foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, d);
                        foreach (float off in new[] { 0f, 0.6f, -0.6f, 1.2f, -1.2f, 1.8f, -1.8f })
                        {
                            Vector3 from = new Vector3(study.center.x, hgt, study.center.z) + side * off;
                            RaycastHit hit;
                            if (!Physics.Raycast(from, d, out hit, 6f, ~0, QueryTriggerInteraction.Ignore) || hit.distance >= bestD) continue;
                            if (hit.collider.GetComponentInParent<Movable>() != null || house.RoomAt(hit.point - d * 0.2f) != study) continue;
                            var rot = Quaternion.LookRotation(-hit.normal, Vector3.up);
                            bool clear = true;
                            foreach (var c in Physics.OverlapBox(hit.point + hit.normal * 0.1f, new Vector3(0.55f, 0.4f, 0.08f), rot, ~0, QueryTriggerInteraction.Ignore))
                                if (c != hit.collider) { clear = false; break; }
                            // The wall has to carry on behind the whole board (not a doorway or a window edge).
                            Vector3 r = Vector3.Cross(Vector3.up, -hit.normal);
                            foreach (var corner in new[] { r * 0.5f + Vector3.up * 0.33f, -r * 0.5f + Vector3.up * 0.33f, r * 0.5f - Vector3.up * 0.33f, -r * 0.5f - Vector3.up * 0.33f })
                                if (!Physics.Raycast(hit.point + hit.normal * 0.15f + corner, -hit.normal, 0.3f, ~0, QueryTriggerInteraction.Ignore)) clear = false;
                            // Things without colliders (paintings, frames) by their drawn size.
                            if (clear)
                            {
                                var an = new Vector3(Mathf.Abs(hit.normal.x), 0f, Mathf.Abs(hit.normal.z));
                                var zone = new Bounds(hit.point + hit.normal * 0.28f, an * 0.5f + (new Vector3(1f, 0f, 1f) - an) * 1.1f + Vector3.up * 0.85f);
                                foreach (var rr in wallThings ?? (wallThings = house.GetComponentsInChildren<Renderer>()))
                                {
                                    if (rr == null || rr.gameObject == hit.collider.gameObject) continue;
                                    var bs = rr.bounds.size;
                                    if ((bs.x > 2f ? 1 : 0) + (bs.y > 2f ? 1 : 0) + (bs.z > 2f ? 1 : 0) >= 2) continue;     // walls, floors, ceilings
                                    if (rr.bounds.Intersects(zone)) { clear = false; break; }
                                }
                            }
                            if (!clear) continue;
                            bestD = hit.distance; best = hit.point; normal = hit.normal;
                        }
                    }
                wallThings = null;
                if (bestD == float.MaxValue) { Log("clues: no clear stretch of study wall for the board (B still opens it)"); return; }
                board = new GameObject("Clue board");
                board.transform.SetParent(house.transform, true);
                board.transform.position = best + normal * 0.03f;
                board.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);
                var cork = Build.Box("Cork", board.transform, Vector3.zero, new Vector3(0.95f, 0.65f, 0.03f), new Color(0.55f, 0.4f, 0.26f), false);
                cork.AddComponent<BoxCollider>();
                Build.Box("Frame", board.transform, new Vector3(0f, 0.335f, 0f), new Vector3(0.99f, 0.025f, 0.04f), new Color(0.3f, 0.2f, 0.12f), false);
                Build.Box("Frame", board.transform, new Vector3(0f, -0.335f, 0f), new Vector3(0.99f, 0.025f, 0.04f), new Color(0.3f, 0.2f, 0.12f), false);
                Build.Usable(cork, "Look at the board", OpenBoard);
                Log("clues: the board is up on the study wall");
            }
            var pins = board.transform.Find("Pins");
            if (pins != null) Destroy(pins.gameObject);
            pins = new GameObject("Pins").transform;
            pins.SetParent(board.transform, false);
            for (int i = 0; i < found.Count; i++)
            {
                float x = -0.32f + (i % 4) * 0.21f, y = 0.16f - (i / 4) * 0.2f;
                Build.Box("Paper", pins, new Vector3(x, y, -0.02f), new Vector3(0.15f, 0.12f, 0.004f), new Color(0.92f, 0.89f, 0.8f), false);
                Build.Box("Pin", pins, new Vector3(x, y + 0.045f, -0.026f), new Vector3(0.012f, 0.012f, 0.01f), new Color(0.7f, 0.1f, 0.08f), false);
            }
            // Red string between the two papers of each deduction.
            foreach (var d in Deductions[(int)truth])
            {
                if (!deduced.Contains(d.id) && !deducedWrong.Contains(d.id)) continue;
                int ia = found.IndexOf(d.a), ib = found.IndexOf(d.b);
                if (ia < 0 || ib < 0) continue;
                Vector3 pa = new Vector3(-0.32f + (ia % 4) * 0.21f, 0.16f - (ia / 4) * 0.2f + 0.045f, -0.03f);
                Vector3 pb = new Vector3(-0.32f + (ib % 4) * 0.21f, 0.16f - (ib / 4) * 0.2f + 0.045f, -0.03f);
                Vector3 mid = (pa + pb) * 0.5f + Vector3.down * 0.03f;
                var str = Build.Box("String", pins, mid, new Vector3((pb - pa).magnitude, 0.004f, 0.003f), deduced.Contains(d.id) ? new Color(0.65f, 0.08f, 0.06f) : new Color(0.35f, 0.25f, 0.22f), false);
                str.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) * Mathf.Rad2Deg);
            }
            int k = 0;
            foreach (var dd in deduced.Concat(deducedWrong))
            {
                Build.Box("Card", pins, new Vector3(-0.3f + k * 0.2f, -0.18f, -0.02f), new Vector3(0.16f, 0.1f, 0.004f), new Color(0.97f, 0.96f, 0.9f), false);
                k++;
            }
        }

        string Title(string id) { return ClueTitles[(int)truth][int.Parse(id.Substring(1))]; }

        void OpenBoard()
        {
            if (hud.modal != Modal.None || phase == Phase.Dream || phase == Phase.Title || phase == Phase.Ending) return;
            EnsureMenu();
            player.frozen = true;
            menu.Show(BoardPage());
        }

        void CloseBoard()
        {
            menu.Close();
            player.frozen = false;
            pinned = null;
            pauseClosedFrame = Time.frameCount;
            if (!VRRig.Active) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        MenuPage BoardPage()
        {
            var cards = new System.Text.StringBuilder();
            foreach (var d in Deductions[(int)truth])
            {
                if (deduced.Contains(d.id)) cards.Append("• <i>").Append(d.text).Append("</i>\n");
                else if (deducedWrong.Contains(d.id)) cards.Append("• <i>").Append(d.wrong).Append("</i>  (the writing shakes)\n");
            }
            var p = new MenuPage
            {
                title = "The board",
                back = CloseBoard,
                intro = (found.Count == 0 ? "Nothing pinned up yet. Whatever's been hidden in this house, it's in the places you'd least want to put your hand."
                         : "Pick one, then another. If they belong together, it holds.") + (cards.Length > 0 ? "\n\n" + cards : ""),
            };
            foreach (var id in found)
            {
                var cid = id;
                p.Add(new MenuEntry
                {
                    label = Title(cid),
                    value = () => pinned == cid ? "picked" : "",
                    activate = () => Pick(cid),
                    hint = ClueTexts[(int)truth][int.Parse(cid.Substring(1))].Replace("\n\n", " "),
                });
            }
            p.Add(MenuEntry.Act("Step back", CloseBoard));
            return p;
        }

        void Pick(string id)
        {
            if (pinned == null) { pinned = id; return; }
            if (pinned == id) { pinned = null; return; }
            string a = pinned, b = id;
            pinned = null;
            Deduction match = null;
            foreach (var d in Deductions[(int)truth]) if ((d.a == a && d.b == b) || (d.a == b && d.b == a)) match = d;
            if (match == null)
            {
                Sfx.Ui("click", 0.3f, 0.6f);
                hud.Say("They don't belong together. The string won't hold.", 2.5f);
                Log("clues: tried " + Title(a) + " with " + Title(b) + ": they don't go together");
                return;
            }
            if (deduced.Contains(match.id)) { hud.Say("You've already written that one down.", 2f); return; }
            bool shaky = grip < 0.35f;
            if (shaky && !deducedWrong.Contains(match.id) && rng.NextDouble() < 0.45)
            {
                deducedWrong.Add(match.id);
                Shake(0.02f, "wrote a card while shaken");
                Log("clues: deduction '" + match.id + "' WRITTEN WRONG (grip " + grip.ToString("0.00") + " under 0.35): \"" + match.wrong + "\". Look at it again when you're steadier");
            }
            else
            {
                bool fixedIt = deducedWrong.Remove(match.id);
                deduced.Add(match.id);
                tagFace++;
                Shake(0.04f, "worked it out: " + match.text);
                Log("clues: deduction '" + match.id + "' made (" + Title(a) + " + " + Title(b) + ")" + (fixedIt ? ", correcting the card written while shaken" : "") +
                    ": \"" + match.text + "\" (" + deduced.Count + " of " + DeductionsNeeded + " the endings need)");
            }
            Sfx.Ui("pageturn", 0.5f, 0.8f);
            UpdateBoard();
            if (menu != null && menu.Showing) menu.Show(BoardPage());
        }

        /// <summary>
        /// Studio: for each truth, all four papers find a place and every place stands somewhere you can reach; then,
        /// for this run's truth, search everything, make every deduction, and photograph the board.
        /// </summary>
        System.Collections.IEnumerator ClueChecks(System.Text.StringBuilder summary)
        {
            var keepTruth = truth;
            for (int t = 0; t < 3; t++)
            {
                truth = (Truth)t;
                found.Clear(); deduced.Clear(); deducedWrong.Clear();
                HideClues();
                if (hiddenIn.Count != 4) Problem("clues: only " + hiddenIn.Count + " of 4 papers found a hiding place for the " + truth + " truth");
                foreach (var o in spotObjects)
                {
                    var room = house.RoomAt(o.transform.position);
                    if (room == null) Problem("clues: '" + o.name + "' stands outside every room (" + o.transform.position + ")");
                    if (o.transform.position.y < 0.05f || o.transform.position.y > 2.6f) Problem("clues: '" + o.name + "' is out of reach at height " + o.transform.position.y.ToString("0.00"));
                }
            }
            truth = keepTruth;
            found.Clear(); deduced.Clear(); deducedWrong.Clear();
            HideClues();
            int keepDay = day;
            float keepGrip = grip;
            day = 2;
            grip = 0.8f;
            foreach (var h in hides.ToArray()) if (spotObjects.Exists(o => o != null && o.name == "Search: " + h.id)) Search(h);
            hud.modal = Modal.None;
            foreach (var d in Deductions[(int)truth]) { pinned = d.a; Pick(d.b); }
            if (found.Count != 4) Problem("clues: searching every place found " + found.Count + " of 4 papers");
            if (deduced.Count != Deductions[(int)truth].Length) Problem("clues: only " + deduced.Count + " deductions held");
            if (board == null) Problem("clues: the board didn't go up in the study");
            else
            {
                Vector3 front = board.transform.position - board.transform.forward * 1.5f;
                front.y = 0f;
                house.SetHour(10f, 2);
                foreach (var r in house.rooms) r.lit = true;
                yield return Pose(front, board.transform.position, 0f, "the clue board, every paper found and both cards written");
            }
            Log("clues: studio check: " + found.Count + " papers found, " + deduced.Count + " deductions (" + string.Join(", ", deduced.ToArray()) + ")");
            summary.Append("  \"deductions\": ").Append(deduced.Count).Append(",\n");
            day = keepDay; grip = keepGrip;
            found.Clear(); deduced.Clear(); deducedWrong.Clear(); clues = 0;
            HideClues();
            player.frozen = true;
        }

        /// <summary>The bot (brave runs): search every place and pin every pair, the way a thorough player would.</summary>
        void BotClues()
        {
            if (hides == null) return;
            foreach (var h in hides.ToArray()) if (spotObjects.Exists(o => o != null && o.name == "Search: " + h.id)) Search(h);
            hud.modal = Modal.None;
            player.frozen = false;
            foreach (var d in Deductions[(int)truth])
                if (found.Contains(d.a) && found.Contains(d.b) && !deduced.Contains(d.id)) { pinned = d.a; Pick(d.b); }
            if (menu != null) menu.Close();
            hud.modal = Modal.None;
            player.frozen = false;
        }
    }

    static class SeqExt
    {
        public static IEnumerable<string> Concat(this List<string> a, List<string> b) { foreach (var x in a) yield return x; foreach (var x in b) yield return x; }
    }
}
