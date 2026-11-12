using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The studio's stills (the "studio" job): the monster's anatomy looked at properly in the lit test room
    /// (whole body from three sides, then close on the hands, the legs and the joints), two transformations filmed
    /// between the new kinds of limb, and then the dream's dressed rooms photographed one by one from a corner
    /// (each with a working light, so the furniture, the clutter and the damage can be judged).
    /// </summary>
    public partial class GameDirector
    {
        int[] StudioLook(int kind, int legs, int arms, int skin = 0)
        {
            var l = fear.Compose(1);
            l[Monster.Shift] = 0;
            l[Monster.Kind] = kind;
            l[Monster.Legs] = legs;
            l[Monster.Arms] = arms;
            l[Monster.Skin] = skin;
            l[Monster.Extra] = 0;
            l[Monster.Body] = 0;
            l[Monster.Head] = 0;
            return l;
        }

        // The first part under the monster with this name, or null.
        static Transform FindPart(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        IEnumerator StudioStills(StringBuilder summary)
        {
            yield return StartRunForTest();
            haunt.paused = true;
            if (StudioHouseOnly)
            {
                player.frozen = true;
                hud.fade = hud.fadeTarget = 0f;
                yield return MenuChecks(summary);
                if (StudioCensus) yield return StoryCensus(summary);
                if (StudioMess) yield return MessStills(summary);
                else yield return HouseStills(summary);
                yield break;
            }
            BuildStudio();
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.32f, 0.34f);
            Look.Fear(0f);
            player.frozen = true;
            hud.fade = hud.fadeTarget = 0f;
            hud.ClearSubtitles();
            hud.objectives.Clear();

            var forms = new List<KeyValuePair<string, int[]>>
            {
                new KeyValuePair<string, int[]>("human arms and legs", StudioLook(Monster.Person, Monster.TwoLegs, 0)),
                new KeyValuePair<string, int[]>("human, gaunt (bare skin)", StudioLook(Monster.Person, Monster.TwoLegs, 1, 1)),
                new KeyValuePair<string, int[]>("mantis forelegs", StudioLook(Monster.Person, Monster.TwoLegs, 6, 9)),
                new KeyValuePair<string, int[]>("spider legs for arms", StudioLook(Monster.Person, Monster.TwoLegs, 7, 1)),
                new KeyValuePair<string, int[]>("spider", StudioLook(Monster.Spider, Monster.SpiderLegs, 0, 9)),
                new KeyValuePair<string, int[]>("elbows the wrong way", StudioLook(Monster.Person, Monster.TwoLegs, 5)),
            };
            int shotsBefore = shots.Count;
            foreach (var f in forms)
            {
                Log("studio stills: " + f.Key + ": " + fear.DescribeLook(f.Value));
                var it = Monster.Create(null, f.Value);
                it.transform.position = StudioAt;
                it.transform.rotation = Quaternion.identity;
                yield return null; yield return null;
                float h = Mathf.Max(1f, it.HeadPos.y - StudioAt.y);
                foreach (var ang in new[] { 25f, 90f, 200f })
                {
                    Orbit(ang, 2.2f + h * 0.9f, h * 0.55f, 0.15f);
                    yield return null;
                    Shot("anatomy " + f.Key + " " + ang.ToString("0"));
                }
                // Close: a hand, a foreleg, a leg.
                foreach (var part in new[] { "Hand", "Mantis foreleg", "Spider leg", "Knee joint" })
                {
                    var t = FindPart(it.transform, part);
                    if (t == null) continue;
                    Bounds b = new Bounds(t.position, Vector3.zero);
                    foreach (var r in t.GetComponentsInChildren<Renderer>()) if (r.enabled) b.Encapsulate(r.bounds);
                    Vector3 at = b.center;
                    float d = Mathf.Clamp(b.size.magnitude * 1.4f, 0.45f, 2.2f);
                    Vector3 from = at + new Vector3(Mathf.Sign(at.x - StudioAt.x + 0.001f) * d * 0.55f, d * 0.15f, d * 0.85f);
                    AimCamera(new Vector3(from.x, StudioAt.y + 0.02f, from.z), at);
                    // (AimCamera stands on the floor; lift the eye to the part.)
                    player.cam.transform.position = from;
                    player.cam.transform.LookAt(at);
                    yield return null;
                    Shot("anatomy " + f.Key + " close " + part.ToLowerInvariant());
                    player.cam.transform.localPosition = new Vector3(0f, player.eyeHeight, 0f);
                    break;
                }
                Destroy(it.gameObject);
                yield return null;
            }

            // How it moves, filmed: the spider (rearing at you, then walking on planted feet), a limp, a glide, the
            // stop-motion walk, and the mantis rocking and striking when you're in reach.
            Time.captureFramerate = 30;
            int[] limp = StudioLook(Monster.Person, Monster.TwoLegs, 0); limp[Monster.Gait] = 4;
            int[] glide = StudioLook(Monster.Person, Monster.TwoLegs, 1); glide[Monster.Gait] = 5;
            int[] jerky = StudioLook(Monster.Person, Monster.TwoLegs, 0, 1); jerky[Monster.Gait] = 6;
            var moves = new List<KeyValuePair<string, int[]>>
            {
                new KeyValuePair<string, int[]>("spider", forms[4].Value),
                new KeyValuePair<string, int[]>("limping", limp),
                new KeyValuePair<string, int[]>("gliding", glide),
                new KeyValuePair<string, int[]>("like a film with frames missing", jerky),
                new KeyValuePair<string, int[]>("mantis", forms[2].Value),
            };
            foreach (var mv in moves)
            {
                Log("studio motion: " + mv.Key + " from frame " + filmFrames);
                var it = Monster.Create(null, mv.Value);
                it.transform.position = StudioAt;
                it.transform.rotation = Quaternion.identity;
                yield return null;
                // Close to it first, standing still: it rears, rocks, strikes, tilts its head at you.
                Vector3 near = StudioAt + new Vector3(0.5f, 0.02f, 1.9f);
                for (float t = 0f; t < 5f; t += Time.deltaTime) { AimCamera(near, it.HeadPos * 0.6f + StudioAt * 0.4f + Vector3.up * 0.3f); Frame(); yield return null; }
                if (mv.Key == "spider" || mv.Key == "mantis") Shot("motion " + mv.Key + " close");
                // Then it walks a circle, the camera standing back (from behind the camera's own view it sometimes stutters).
                yield return StudioWalk(it, mv.Key == "spider" ? 1.3f : 1.1f);
                Destroy(it.gameObject);
                yield return null;
            }
            Time.captureFramerate = 0;

            // The dream's rooms.
            day = 4;
            phase = Phase.Dream;
            dream.Begin(4);
            yield return null; yield return null;
            var maze = dream.Maze;
            if (dream.Stalker != null) dream.Stalker.Delay(900f);
            if (maze != null)
            {
                maze.HoldHaunts(900f);
                var views = maze.RoomViews(16, true);
                Log("studio stills: " + views.Count + " dressed rooms to look at (" + maze.dressedRooms + " dressed, " + maze.dressedThings + " things in them)");
                foreach (var v in views)
                {
                    player.Teleport(v.Value.Key - Vector3.up * player.eyeHeight + Vector3.up * 0.05f, 0f);
                    player.Aim(v.Value.Value);
                    yield return null; yield return null;
                    Shot("room " + v.Key);
                }
            }
            summary.Append("  \"mode\": \"studio\",\n  \"frames\": ").Append(filmFrames).Append(",\n  \"fps\": 30,\n  \"shots\": ").Append(shots.Count).Append(",\n");
        }
    }
}
