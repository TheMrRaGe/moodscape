using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// What the house shows you that isn't there. Each one is an incident the storyteller can choose (slot 17 of
    /// the fear engine, "hallucination"): it is chosen and learned like any other fright, and the learned score
    /// goes back into the fear engine so the dreams know too. Most are a second long and leave nothing behind,
    /// so the player is never sure.
    /// </summary>
    public class Hallucinations : MonoBehaviour
    {
        GameDirector g;
        Monster shown;
        readonly List<Texture2D> portraits = new List<Texture2D>();
        Material portraitMat;
        bool busy;

        public void Init(GameDirector director) { g = director; }

        Camera Cam { get { return g.player.cam; } }
        Vector3 PlayerPos { get { return g.player.transform.position; } }
        Vector3 Eye { get { return Cam.transform.position; } }

        // The options of slot 17, in order (see FearEngine.Slots).
        public const int CornerOfEye = 1, WallsBreathe = 2, PaintingLooks = 3, ManuscriptWrote = 4, Reflection = 5, Whisper = 6, HallStretches = 7, Doorway = 8;

        static readonly string[][] Tags =
        {
            null,
            new[] { "sight", "edge", "far" },
            new[] { "sight", "close", "room" },
            new[] { "sight", "far", "object" },
            new[] { "word", "far", "object" },
            new[] { "sight", "far", "window" },
            new[] { "voice", "close", "behind" },
            new[] { "sight", "far", "space" },
            new[] { "sight", "far", "doorway" },
        };

        /// <summary>Add every hallucination to the storyteller.</summary>
        public void Register(FearCore.Storyteller teller)
        {
            var opts = FearEngine.Slots[FearEngine.Hallucination].options;
            for (int k = 1; k < opts.Length; k++)
            {
                int opt = k;
                var tags = new List<string>(FearEngine.Tags(FearEngine.Hallucination, k));
                tags.AddRange(Tags[k]);
                teller.Add(new FearCore.Incident
                {
                    id = "hallucination:" + opts[k].label,
                    label = "hallucination: " + opts[k].label.ToLowerInvariant(),
                    domain = "hallucination",
                    tier = opts[k].intensity >= 2 ? 1 : 0,
                    cost = opts[k].intensity >= 2 ? 1.5f : 1f,
                    minEpisode = 2,
                    cooldown = 60f,
                    tags = tags.ToArray(),
                    can = () => !busy && !FearEngine.Banned.Contains(opts[opt].label) && opts[opt].intensity <= (g.day <= 4 ? 1 : 2) && Possible(opt),
                    fire = () => Show(opt),
                });
            }
        }

        public static int OptionOf(FearCore.Incident i)
        {
            if (i.domain != "hallucination") return -1;
            var opts = FearEngine.Slots[FearEngine.Hallucination].options;
            for (int k = 0; k < opts.Length; k++) if (i.id == "hallucination:" + opts[k].label) return k;
            return -1;
        }

        public void Clear()
        {
            StopAllCoroutines();
            if (shown != null) Destroy(shown.gameObject);
            shown = null;
            if (g != null && g.player != null) g.player.fovOffset = 0f;
            busy = false;
        }

        /// <summary>For the screenshot tour.</summary>
        public bool Force(int opt) { return Possible(opt) && Show(opt); }

        bool Possible(int opt)
        {
            switch (opt)
            {
                case CornerOfEye: return EdgeSpot().HasValue;
                case PaintingLooks: return Painting() != null;
                case ManuscriptWrote: return g.house.RoomAt(PlayerPos).name != "study";
                case Reflection: return WindowAhead().HasValue;
                case HallStretches: return g.house.RoomAt(PlayerPos) == g.house.hall && Mathf.Abs(g.player.Forward.z) > 0.8f;
                case Doorway: return DoorwaySpot().HasValue;
                default: return true;
            }
        }

        bool Show(int opt)
        {
            switch (opt)
            {
                case CornerOfEye:
                {
                    var spot = EdgeSpot();
                    if (!spot.HasValue) return false;
                    StartCoroutine(Glimpse(spot.Value, 2.5f, 22f, 0f));
                    return true;
                }
                case WallsBreathe: StartCoroutine(Breathe()); return true;
                case PaintingLooks:
                {
                    var p = Painting();
                    if (p == null) return false;
                    StartCoroutine(Portrait(p));
                    return true;
                }
                case ManuscriptWrote: StartCoroutine(Wrote()); return true;
                case Reflection:
                {
                    var w = WindowAhead();
                    if (!w.HasValue) return false;
                    StartCoroutine(FaceAtWindow(w.Value));
                    return true;
                }
                case Whisper:
                {
                    var a = Sfx.Loop("whisper", g.player.transform, 0.55f, true, 0.8f);
                    a.transform.localPosition = new Vector3(-0.25f, 1.6f, -0.35f);
                    Destroy(a.gameObject, 1.4f);
                    g.Shake(0.015f, "a whisper, right at your ear (hallucination)");
                    return true;
                }
                case HallStretches: StartCoroutine(Stretch()); return true;
                case Doorway:
                {
                    var spot = DoorwaySpot();
                    if (!spot.HasValue) return false;
                    StartCoroutine(Glimpse(spot.Value, 8f, 0f, 0.45f));
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- places

        bool Clear(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return !Physics.Raycast(from, d.normalized, d.magnitude - 0.2f, ~0, QueryTriggerInteraction.Ignore);
        }

        // Somewhere at the edge of your vision, 35-42 degrees off where you're looking, with a clear line to it.
        Vector3? EdgeSpot()
        {
            Vector3 f = g.player.Forward;
            foreach (float side in Random.value < 0.5f ? new[] { 1f, -1f } : new[] { -1f, 1f })
                foreach (float dist in new[] { 6f, 4.5f, 3.2f })
                {
                    Vector3 dir = Quaternion.Euler(0f, side * (35f + Random.value * 7f), 0f) * f;
                    Vector3 p = PlayerPos + dir * dist;
                    if (!Clear(Eye, p + Vector3.up * 1.3f) || !Clear(Eye, p + Vector3.up * 0.3f)) continue;
                    if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, 2f) == false) continue;     // something to stand on
                    return p;
                }
            return null;
        }

        KeyValuePair<Vector3, Vector3>? WindowAhead()
        {
            foreach (var w in g.house.windowSpots)
            {
                Vector3 to = w.Key - Eye;
                if (to.magnitude > 8f || to.magnitude < 1.5f) continue;
                if (Vector3.Dot(w.Value, -to.normalized) < 0.3f) continue;          // we're on the room side, facing the glass
                if (!Vis.Sees(Cam, w.Key)) continue;
                return w;
            }
            return null;
        }

        Vector3? DoorwaySpot()
        {
            foreach (var r in g.house.rooms)
            {
                var d = r.door;
                if (d == null || !d.open || !d.gameObject.activeInHierarchy || g.house.RoomAt(PlayerPos) == r) continue;
                Vector3 mid = d.transform.position + Quaternion.Euler(0f, d.baseYaw, 0f) * new Vector3(0f, 0f, 0.58f);
                Vector3 into = Vis.Flat(r.center - mid);
                Vector3 p = mid + into * 0.1f;     // standing in the frame
                float dist = (p - PlayerPos).magnitude;
                if (dist < 2.5f || dist > 11f) continue;
                if (!Vis.Sees(Cam, p + Vector3.up * 1.2f)) continue;
                return p;
            }
            return null;
        }

        Renderer Painting()
        {
            Renderer best = null;
            float bestD = 9f;
            foreach (var p in g.house.paintings)
            {
                if (p == null) continue;
                float d = (p.transform.position - Eye).magnitude;
                if (d < 1.5f || d > bestD || !Vis.Sees(Cam, p.transform.position, p.transform)) continue;
                best = p; bestD = d;
            }
            return best;
        }

        // ---------------------------------------------------------------- the hallucinations

        // It stands there until you look straight at it (or for a little while), then it was never there.
        // lookAngle > 0: gone the moment you turn within that many degrees of it. seenFor > 0: gone that long after you see it.
        // lit: where to hang a faint cold light so it can be seen at all (outside a window at night), or Vector3.zero.
        IEnumerator Glimpse(Vector3 at, float limit, float lookAngle, float seenFor, Vector3 lit = default(Vector3))
        {
            busy = true;
            Light glow = null;
            if (lit != Vector3.zero)
            {
                glow = Build.Lamp("Glimpse light", transform, lit, new Color(0.6f, 0.7f, 0.9f), 1.4f, 3f);
                glow.transform.position = lit;
            }
            var look = g.fear.Compose(g.day <= 4 ? 1 : 2);
            look[Monster.Shift] = 0;
            if (lookAngle <= 0f) look[Monster.Legs] = Monster.TwoLegs;      // a figure in a doorway reads only if it stands
            shown = Monster.Create(null, look);
            shown.transform.position = at;
            shown.FaceToward(PlayerPos);
            g.Log("hallucination: it is " + (lookAngle > 0f ? "at the edge of your vision" : "there") + " (" + g.fear.DescribeLook(look) + ")");
            float t0 = Time.time, seenAt = -1f, seenTime = 0f;
            int blinks = FaceReader.Blinks;
            bool firstFrame = true;
            while (shown != null && Time.time - t0 < limit)
            {
                shown.FaceToward(PlayerPos);
                Vector3 to = shown.HeadPoint - Eye;
                bool seen = Vis.Sees(Cam, shown.HeadPoint, shown.transform);
                if (lookAngle > 0f && Vector3.Angle(Cam.transform.forward, to) < lookAngle) break;
                if (seen && seenAt < 0f) seenAt = Time.time;
                // Count only frames it was actually on screen, a few hundredths at most each: building it causes a
                // hitch, and it mustn't vanish before anyone could have seen it.
                if (seen && !firstFrame) seenTime += Mathf.Min(Time.deltaTime, 0.04f);
                firstFrame = false;
                if (seenFor > 0f && seenTime > seenFor) break;
                if (seenAt > 0f && FaceReader.Present && FaceReader.Blinks != blinks) { g.Log("hallucination: you blinked, and it was gone"); break; }
                yield return null;
            }
            bool wasSeen = seenAt > 0f;
            if (glow != null) Destroy(glow.gameObject);
            if (shown != null) Destroy(shown.gameObject);
            shown = null;
            if (wasSeen) g.Shake(0.03f, "saw something that wasn't there");
            busy = false;
        }

        IEnumerator Breathe()
        {
            busy = true;
            var room = g.house.RoomAt(PlayerPos);
            var breath = Sfx.Loop("breath", null, 0.0f, true, 0.6f);
            breath.transform.position = room.center + Vector3.up * 1.4f;
            float baseI = room.light != null ? room.light.intensity : 0f;
            for (float t = 0f; t < 11f; t += Time.deltaTime)
            {
                float env = Mathf.Sin(Mathf.PI * t / 11f);
                float b = Mathf.Sin(t * 1.25f);
                g.player.fovOffset = b * 2.4f * env;
                if (room.light != null && Time.time > room.darkUntil) room.light.intensity = baseI * (1f + 0.12f * b * env);
                breath.volume = 0.3f * env;
                yield return null;
            }
            g.player.fovOffset = 0f;
            if (room.light != null && Time.time > room.darkUntil) room.light.intensity = baseI;
            Destroy(breath.gameObject);
            busy = false;
        }

        IEnumerator Stretch()
        {
            busy = true;
            g.Log("hallucination: the hall is longer than it was");
            for (float t = 0f; t < 2.6f; t += Time.deltaTime) { g.player.fovOffset = -26f * Mathf.SmoothStep(0f, 1f, t / 2.6f); yield return null; }
            yield return new WaitForSeconds(1.4f);
            Sfx.Ui("sting", 0.35f, 0.7f);
            g.player.fovOffset = 0f;
            g.Shake(0.02f, "the hall snapped back");
            busy = false;
        }

        IEnumerator Wrote()
        {
            string[] lines =
            {
                "He is not alone when he writes.",
                "Whatever stands behind him reads over his shoulder.",
                "He thinks the house is quiet. It is listening.",
                "Tonight he will dream of the cellar again.",
                "Don't turn round.",
            };
            g.script.foreign = lines[Random.Range(0, lines.Length)];
            var a = Sfx.Loop("typing", null, 0.45f, true);
            a.transform.position = g.house.typewriter.transform.position;
            Destroy(a.gameObject, 2.2f);
            g.Log("hallucination: the typewriter, from the study. The manuscript now has a line you didn't write: \"" + g.script.foreign + "\"");
            yield break;
        }

        // The glass is opaque in this house (it glows with the sky), so what is outside is drawn on it: by night a pale
        // face pressed to the window, by day a dark shape against the light. Gone once you've seen it for a moment.
        Texture2D faceNight, faceDay;

        IEnumerator FaceAtWindow(KeyValuePair<Vector3, Vector3> w)
        {
            busy = true;
            var pane = g.house.PaneNear(w.Key);
            if (pane == null) { busy = false; yield break; }
            Color oldColor = pane.color, oldEmit = pane.GetColor("_EmissionColor");
            bool night = oldEmit.maxColorComponent < 0.3f;          // the glass is dark: it's night outside
            if (faceNight == null) { faceNight = WindowFace(true); faceDay = WindowFace(false); }
            var tex = night ? faceNight : faceDay;
            pane.mainTexture = tex;
            pane.SetTexture("_EmissionMap", tex);
            pane.color = night ? new Color(0.5f, 0.52f, 0.55f) : oldColor;
            pane.SetColor("_EmissionColor", night ? new Color(0.45f, 0.47f, 0.52f) : oldEmit);
            g.Log("hallucination: something at the window, looking in");
            float t0 = Time.time, seenAt = -1f;
            int blinks = FaceReader.Blinks;
            while (Time.time - t0 < 8f)
            {
                bool seen = Vis.Sees(Cam, w.Key);
                if (seen && seenAt < 0f) seenAt = Time.time;
                if (seenAt > 0f && Time.time - seenAt > 1.1f) break;
                if (seenAt > 0f && FaceReader.Present && FaceReader.Blinks != blinks) break;
                yield return null;
            }
            pane.mainTexture = null;
            pane.SetTexture("_EmissionMap", null);
            pane.color = oldColor;
            pane.SetColor("_EmissionColor", oldEmit);
            if (seenAt > 0f) g.Shake(0.03f, "a face at the window");
            busy = false;
        }

        // The window seen from inside: four panes, and a head and shoulders pressed close behind them.
        static Texture2D WindowFace(bool night)
        {
            const int W = 110, H = 120;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[W * H];
            var rng = new System.Random(night ? 11 : 12);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x - W * 0.5f) / W, v = (y - H * 0.5f) / H;
                    float n = (float)rng.NextDouble() * 0.04f;
                    Color sky = night ? new Color(0.06f + n, 0.07f + n, 0.1f + n) : new Color(1f, 1f, 1f);
                    // head (slightly tilted), neck, shoulders
                    float hu = u + v * 0.12f;
                    float head = (hu * hu) / 0.022f + ((v - 0.08f) * (v - 0.08f)) / 0.045f;
                    float shoulders = v < -0.2f ? (u * u) / 0.16f - (v + 0.2f) * 2.2f : 9f;
                    float neck = Mathf.Abs(u) < 0.07f && v < 0f && v > -0.25f ? 0f : 9f;
                    bool body = head < 1f || shoulders < 1f || neck < 1f;
                    Color c = sky;
                    if (body)
                    {
                        if (night)
                        {
                            float shade = head < 1f ? 0.75f - 0.35f * head : 0.32f;
                            c = new Color(0.62f, 0.64f, 0.66f) * shade;
                            // hollow eyes, pressed close to the glass
                            for (int s = -1; s <= 1; s += 2)
                            {
                                float ex = W * 0.5f + s * 8.5f + (y - H * 0.6f) * -0.12f, ey = H * 0.6f;
                                float e = ((x - ex) * (x - ex)) / 14f + ((y - ey) * (y - ey)) / 7f;
                                if (e < 1f) c = Color.Lerp(c, new Color(0.01f, 0.01f, 0.01f), 1f - e * 0.4f);
                            }
                            float m = ((x - W * 0.5f - 1f) * (x - W * 0.5f - 1f)) / 18f + ((y - H * 0.47f) * (y - H * 0.47f)) / 5f;
                            if (m < 1f && head < 1f) c = Color.Lerp(c, new Color(0.03f, 0.02f, 0.02f), 0.9f);
                        }
                        else c = new Color(0.12f, 0.12f, 0.13f) + new Color(n, n, n);
                    }
                    // the glazing bars
                    if (Mathf.Abs(x - W * 0.5f) < 1.5f || Mathf.Abs(y - H * 0.5f) < 1.5f) c = night ? new Color(0.3f, 0.29f, 0.27f) : new Color(0.85f, 0.83f, 0.78f);
                    px[y * W + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // A painting turns into a portrait whose eyes follow you, until you have looked at it properly and away.
        IEnumerator Portrait(Renderer painting)
        {
            busy = true;
            if (portraits.Count == 0) MakePortraits();
            var original = painting.sharedMaterial;
            if (portraitMat == null) portraitMat = Build.TexMat(portraits[1]);
            painting.sharedMaterial = portraitMat;
            g.Log("hallucination: a painting is looking at you (" + painting.name + ")");
            float seen = 0f, t0 = Time.time;
            while (Time.time - t0 < 25f)
            {
                // Its eyes are on you from wherever you stand.
                Vector3 local = painting.transform.InverseTransformPoint(Eye);
                int k = local.x < -0.6f ? 0 : local.x > 0.6f ? 2 : 1;
                portraitMat.mainTexture = portraits[k];
                bool sees = Vis.Sees(Cam, painting.transform.position, painting.transform);
                if (sees) seen += Time.deltaTime;
                else if (seen > 1.5f) break;
                yield return null;
            }
            painting.sharedMaterial = original;
            if (seen > 0.5f) g.Shake(0.02f, "a painting was looking at you");
            busy = false;
        }

        void MakePortraits()
        {
            const int W = 96, H = 120;
            var rng = new System.Random(7);
            for (int look = 0; look < 3; look++)
            {
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color[W * H];
                float eyeShift = (look - 1) * 1.6f;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = (x - W * 0.5f) / W, v = (y - H * 0.56f) / H;
                        float n = (float)rng.NextDouble() * 0.03f;
                        Color c = new Color(0.07f + n, 0.05f + n, 0.035f + n);            // dark varnish
                        float face = (u * u) / 0.032f + (v * v) / 0.07f;
                        if (face < 1f)
                        {
                            float shade = 0.55f - 0.25f * face - v * 0.6f;
                            c = Color.Lerp(c, new Color(0.62f, 0.55f, 0.45f) * shade * 1.6f, 0.92f);
                            // eye sockets, and the eyes in them, turned toward you
                            for (int s = -1; s <= 1; s += 2)
                            {
                                float ex = W * 0.5f + s * 9.5f, ey = H * 0.62f;
                                float sock = ((x - ex) * (x - ex)) / 30f + ((y - ey) * (y - ey)) / 12f;
                                if (sock < 1f) c = Color.Lerp(c, new Color(0.08f, 0.06f, 0.05f), 0.85f * (1f - sock * 0.5f));
                                float pupil = (x - ex - eyeShift) * (x - ex - eyeShift) + (y - ey) * (y - ey);
                                if (pupil < 3.2f) c = new Color(0.85f, 0.82f, 0.7f);
                                if (pupil < 1.1f) c = new Color(0.02f, 0.02f, 0.02f);
                            }
                            // a mouth, slightly open
                            float mx = (x - W * 0.5f) / 7f, my = (y - H * 0.43f) / 1.6f;
                            if (mx * mx + my * my < 1f) c = new Color(0.12f, 0.05f, 0.04f);
                        }
                        float frame = Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y));
                        if (frame < 3f) c *= 0.5f;
                        px[y * W + x] = c;
                    }
                tex.SetPixels(px);
                tex.Apply();
                portraits.Add(tex);
            }
        }
    }
}
