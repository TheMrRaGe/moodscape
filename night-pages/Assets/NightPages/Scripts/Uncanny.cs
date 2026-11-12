using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The quieter frights: things that are wrong rather than things that jump. Each is an incident the
    /// storyteller can choose, learned like any other. They come from three kinds of real experience:
    ///
    ///  Someone else lives here. People who find a stranger has been living in their house (it happens) first
    ///    notice small things: food gone, a chair pulled out, the bed slept in, the television on, a door they
    ///    shut standing open. None of it is a monster, and all of it means someone was here.
    ///
    ///  The familiar made wrong (what made P.T. so frightening: one ordinary hallway, small changes, footsteps
    ///    that match yours). Every photograph in the house turned face down. A stranger in the family photos.
    ///    The telephone ringing, and on the line, only your own house. Your own footsteps, a beat late, behind you.
    ///
    ///  The mirror. Something stands behind you in the reflection and isn't there when you turn. Your reflection
    ///    runs half a second late.
    /// </summary>
    public class Uncanny : MonoBehaviour
    {
        GameDirector g;
        FearCore.Storyteller teller;
        bool ringing, tvOn, projecting, stepsOn;
        Monster inMirror;
        readonly Dictionary<Material, Texture> photoOriginals = new Dictionary<Material, Texture>();
        Texture2D tvTex;
        readonly List<Texture2D> film = new List<Texture2D>();

        Camera Cam { get { return g.player.cam; } }
        Vector3 PlayerPos { get { return g.player.transform.position; } }
        House H { get { return g.house; } }

        public void Init(GameDirector director)
        {
            g = director;
            teller = g.teller;
            Add("phone", "the telephone rings", 1, 2f, 2, Phone, () => H.phoneBody != null && !ringing && (H.phoneBody.position - PlayerPos).magnitude > 3.5f, "unknown", "sound", "voice", "far", "home");
            Add("tv", "the television comes on by itself", 0, 1f, 2, TV, () => H.tvScreen != null && !tvOn && !Vis.Sees(Cam, H.tvScreen.transform.position) && (H.tvScreen.transform.position - PlayerPos).magnitude < 14f, "unknown", "sight", "sound", "light", "far", "home");
            Add("projector", "the projector runs by itself", 1, 2f, 3, Projector, () => H.projection != null && !projecting && (H.projector.position - PlayerPos).magnitude < 12f && !Vis.Sees(Cam, H.projector.position), "loss", "sight", "sound", "far", "home", "memory");
            Add("wheelchair", "her wheelchair is somewhere else, facing you", 1, 2f, 3, Wheelchair, () => H.wheelchair != null && !H.wheelchair.held && !Vis.Sees(Cam, H.wheelchair.transform.position + Vector3.up * 0.6f), "loss", "uncanny", "moved", "close");
            Add("chair", "a kitchen chair pulled out, turned to face the room", 0, 1f, 2, Chair, null, "someone", "home", "moved", "close");
            Add("bed", "the bed has been slept in", 0, 1f, 3, SleptIn, () => H.bedTop != null && !Vis.Sees(Cam, H.bedTop.position), "someone", "home", "moved", "self");
            Add("food", "the fruit is gone, and there's a core on the table", 0, 1f, 2, Food, null, "someone", "home", "moved");
            Add("facedown", "every photograph in the house is face down", 1, 2f, 3, FaceDown, null, "uncanny", "moved", "far", "family");
            Add("stranger", "someone new in the family photographs", 1, 2f, 4, Stranger, null, "uncanny", "sight", "self", "family");
            Add("wardrobe", "knocking from inside the wardrobe", 1, 2f, 3, Wardrobe, () => H.wardrobe != null && (H.wardrobe.position - PlayerPos).magnitude < 7f, "trapped", "sound", "close", "someone");
            Add("steps", "footsteps behind you that match yours", 1, 2f, 2, Steps, () => !stepsOn && g.player.velocity.magnitude > 0.5f, "hunted", "sound", "behind", "self");
            Add("calendar", "every day on the calendar crossed off", 0, 1f, 4, Calendar, null, "uncanny", "sight", "time", "home");
            var mb = Add("mirrorbehind", "in the mirror, something behind you", 2, 3f, 1, MirrorBehind, () => g.RungOk(3) && H.hallMirror != null && inMirror == null && H.hallMirror.Watched(Cam), "hunted", "sight", "behind", "self");
            mb.needsCreature = true;
            Add("mirrorlate", "your reflection is late", 1, 2f, 3, MirrorLate, () => H.hallMirror != null && H.hallMirror.lag <= 0f && H.hallMirror.Watched(Cam), "self", "uncanny", "sight");
            Wire();
        }

        FearCore.Incident Add(string id, string label, int tier, float cost, int minDay, System.Func<bool> fire, System.Func<bool> can, params string[] tags)
        {
            var inc = new FearCore.Incident { id = id, label = label, tier = tier, cost = cost, minEpisode = minDay, tags = tags, cooldown = tier == 2 ? 120f : 60f, can = can };
            inc.fire = () => { if (!fire()) return false; g.Log("haunting: " + label); return true; };
            teller.Add(inc);
            return inc;
        }

        /// <summary>Make one happen now, whatever the storyteller would say (the screenshot tour).</summary>
        public bool Force(string id)
        {
            foreach (var i in teller.incidents) if (i.id == id) return i.fire();
            return false;
        }

        /// <summary>The telephone, when nothing is happening to it (again after the house is rebuilt).</summary>
        public void Wire()
        {
            if (H.phone != null) H.phone.use = Answer;
            if (g.player != null) Mirror.AddReflection(g.player);
        }

        /// <summary>A new day: the photographs are back as they were, the calendar is right, everything is off.</summary>
        public void BeginDay(int day)
        {
            StopAllCoroutines();
            ringing = tvOn = projecting = stepsOn = false;
            Player.Stepped = null;
            if (H.tvScreen != null) { H.tvScreen.enabled = false; H.tvLight.enabled = false; }
            if (H.projection != null) { H.projection.enabled = false; H.projectorLight.enabled = false; }
            if (H.hallMirror != null) H.hallMirror.lag = 0f;
            if (inMirror != null) Destroy(inMirror.gameObject);
            foreach (var kv in photoOriginals) if (kv.Key != null) kv.Key.mainTexture = kv.Value;
            photoOriginals.Clear();
            H.CrossOff(8 + day);
            H.Clock(true);
        }

        // ---------------------------------------------------------------- the telephone

        bool Phone() { if (g.job != null && g.job.PhoneBusy) return false; StartCoroutine(Ring()); return true; }

        IEnumerator Ring()
        {
            ringing = true;
            H.phone.promptFn = () => "Answer the telephone";
            for (int i = 0; i < 7 && ringing; i++)
            {
                Sfx.At("ring", H.phoneBody.position + Vector3.up * 0.1f, 0.9f);
                for (float t = 0f; t < 4f && ringing; t += Time.deltaTime) yield return null;
            }
            if (ringing) g.Log("haunting: nobody answered. The telephone stops mid-ring");
            ringing = false;
            H.phone.promptFn = null;
        }

        void Answer()
        {
            if (g.job != null && g.job.OnPhone()) return;
            if (!ringing)
            {
                g.hud.Say(g.day >= 4 ? "The line is dead. Then, very faintly, someone breathing." : "The line is dead.", 3.5f);
                return;
            }
            ringing = false;
            StartCoroutine(OnTheLine());
        }

        // Nobody speaks. On the line is a room: a clock ticking, someone breathing close to the phone. Your house.
        IEnumerator OnTheLine()
        {
            g.Log("haunting: you answered. On the line: your own house");
            g.hud.Say("Nobody speaks. You can hear a room on the other end.", 4f);
            var tick = Sfx.Loop("clocktick", null, 0.25f, false, 1f);
            yield return new WaitForSeconds(3.5f);
            g.hud.Say("A clock, ticking. It's the hall clock. It's this hall.", 4f);
            var breath = Sfx.Loop("breath", null, 0.0f, false, 0.8f);
            for (float t = 0f; t < 3f; t += Time.deltaTime) { breath.volume = t / 3f * 0.35f; yield return null; }
            g.hud.Say("And someone breathing, close to the receiver. Close to this receiver.", 4f);
            g.Shake(0.05f, "the telephone: your own house on the line");
            yield return new WaitForSeconds(3f);
            Sfx.Ui("click", 0.8f);
            Destroy(tick.gameObject);
            Destroy(breath.gameObject);
        }

        // ---------------------------------------------------------------- the television

        bool TV() { StartCoroutine(Television()); return true; }

        IEnumerator Television()
        {
            tvOn = true;
            if (tvTex == null) tvTex = new Texture2D(64, 48, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var mat = H.tvScreen.sharedMaterial;
            mat.mainTexture = tvTex;
            mat.SetTexture("_EmissionMap", tvTex);
            mat.color = Color.white;
            mat.SetColor("_EmissionColor", new Color(0.9f, 0.95f, 1f));
            H.tvScreen.enabled = true;
            H.tvLight.enabled = true;
            var hiss = Sfx.Loop("static", H.tvScreen.transform, 0.35f, true);
            hiss.transform.localPosition = Vector3.zero;
            var px = new Color[64 * 48];
            float watched = 0f, t0 = Time.time;
            bool figure = g.Rung >= 2;
            while (Time.time - t0 < 45f)
            {
                bool sees = Vis.Sees(Cam, H.tvScreen.transform.position);
                if (sees) watched += Time.deltaTime;
                // Snow. After a while of watching it, on later days, a shape in it: head and shoulders, close to the glass.
                float shape = figure ? Mathf.Clamp01((watched - 2f) / 3f) : 0f;
                for (int y = 0; y < 48; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float v = Random.value;
                        float u = (x - 32f) / 64f, w = (y - 26f) / 48f;
                        bool head = u * u / 0.018f + (w - 0.05f) * (w - 0.05f) / 0.05f < 1f, body = w < -0.2f && u * u < 0.06f - (w + 0.2f) * 0.1f;
                        if ((head || body) && shape > 0f) v *= 1f - 0.85f * shape;
                        px[y * 64 + x] = new Color(v, v, v);
                    }
                tvTex.SetPixels(px);
                tvTex.Apply();
                H.tvLight.intensity = 0.5f + Random.value * 0.4f;
                if (watched > 6f) break;
                yield return new WaitForSeconds(0.05f);
            }
            if (watched > 6f) { g.Shake(0.04f, "something in the snow on the television"); Sfx.At("click", H.tvScreen.transform.position, 0.8f); }
            H.tvScreen.enabled = false;
            H.tvLight.enabled = false;
            Destroy(hiss.gameObject);
            tvOn = false;
        }

        // ---------------------------------------------------------------- the projector

        bool Projector() { StartCoroutine(HomeMovie()); return true; }

        // Grainy home-movie frames: two figures (them), and on each reel a third one behind them, a little nearer.
        void MakeFilm()
        {
            var rng = new System.Random(3);
            for (int f = 0; f < 6; f++)
            {
                var t = new Texture2D(96, 72, TextureFormat.RGBA32, false);
                var px = new Color[96 * 72];
                float near = f / 5f;
                for (int y = 0; y < 72; y++)
                    for (int x = 0; x < 96; x++)
                    {
                        float n = (float)rng.NextDouble() * 0.12f;
                        Color c = new Color(0.62f + n, 0.52f + n, 0.36f + n);                            // sepia day
                        if (y < 26) c *= 0.75f;                                                       // the lawn
                        for (int k = 0; k < 2; k++)
                        {
                            float cx = 34f + k * 22f, top = 52f - k * 3f;
                            if (Mathf.Abs(x - cx) < 5f && y > 18 && y < top) c *= 0.45f;            // a person
                            if ((x - cx) * (x - cx) + (y - top - 4f) * (y - top - 4f) < 18f) c *= 0.5f;
                        }
                        // the third one, between them and behind, coming closer reel by reel
                        float tx = 45f, th = Mathf.Lerp(14f, 52f, near), tw = Mathf.Lerp(2f, 7f, near), tb = Mathf.Lerp(30f, 8f, near);
                        if (Mathf.Abs(x - tx) < tw && y > tb && y < tb + th) c *= 0.15f;
                        if ((x - tx) * (x - tx) + (y - tb - th - tw) * (y - tb - th - tw) < tw * tw * 1.3f) c *= 0.15f;
                        float vig = 1f - 0.9f * ((x - 48f) * (x - 48f) / 2304f + (y - 36f) * (y - 36f) / 1296f) * 0.5f;
                        px[y * 96 + x] = c * vig;
                    }
                t.SetPixels(px);
                t.Apply();
                film.Add(t);
            }
        }

        IEnumerator HomeMovie()
        {
            projecting = true;
            if (film.Count == 0) MakeFilm();
            var mat = H.projection.sharedMaterial;
            H.projection.enabled = true;
            H.projectorLight.enabled = true;
            var whir = Sfx.Loop("projector", H.projector, 0.4f, true);
            float t0 = Time.time, watched = 0f;
            int reel = 0;
            while (Time.time - t0 < 40f && watched < 9f)
            {
                if (Vis.Sees(Cam, H.projection.transform.position)) watched += Time.deltaTime;
                reel = Mathf.Min(film.Count - 1, (int)(watched / 1.5f));
                var tex = film[reel];
                mat.mainTexture = tex;
                mat.SetTexture("_EmissionMap", tex);
                float flick = 0.75f + Random.value * 0.35f;
                mat.color = Color.white * flick;
                mat.SetColor("_EmissionColor", new Color(0.9f, 0.85f, 0.7f) * flick);
                H.projectorLight.intensity = 1.4f * flick;
                yield return new WaitForSeconds(1f / 18f);
            }
            if (watched >= 9f)
            {
                // The film burns through, white, and the reel flaps round.
                mat.mainTexture = null;
                mat.SetTexture("_EmissionMap", null);
                mat.SetColor("_EmissionColor", Color.white * 2f);
                g.Shake(0.05f, "the home movie: someone behind them, nearer every reel");
                yield return new WaitForSeconds(0.25f);
            }
            H.projection.enabled = false;
            H.projectorLight.enabled = false;
            Destroy(whir.gameObject);
            Sfx.At("click", H.projector.position, 0.7f);
            projecting = false;
        }

        // ---------------------------------------------------------------- someone else lives here

        bool Wheelchair()
        {
            // Somewhere you'll see it: 3-5 m away in the room you're in or the doorway out of it, facing you.
            for (int k = 0; k < 12; k++)
            {
                Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                Vector3 p = PlayerPos + dir * Random.Range(3f, 5f);
                p.y = 0f;
                if (H.Outside(p) || Vis.Sees(Cam, p + Vector3.up * 0.6f)) continue;
                if (Physics.CheckBox(p + Vector3.up * 0.6f, new Vector3(0.45f, 0.5f, 0.55f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                Sfx.At("creak", H.wheelchair.transform.position, 0.5f, 1.4f);
                H.wheelchair.Teleport(p + Vector3.up * 0.02f, Quaternion.Euler(0f, Models.Face(PlayerPos.x - p.x, PlayerPos.z - p.z), 0f));
                return true;
            }
            return false;
        }

        bool Chair()
        {
            foreach (var c in H.kitchenChairs)
            {
                if (c == null || c.held || Vis.Sees(Cam, c.transform.position + Vector3.up * 0.5f)) continue;
                Vector3 table = H.InRoom("kitchen", new Vector3(-4f, 0f, -4.4f));
                Vector3 out_ = Vis.Flat(c.transform.position - table).normalized;
                Vector3 to = c.transform.position + out_ * 0.6f;
                Vector3 look = H.Find("kitchen").center - to;
                c.Teleport(to + Vector3.up * 0.02f, Quaternion.Euler(0f, Models.Face(look.x, look.z) + 180f, 0f));
                return true;
            }
            return false;
        }

        bool SleptIn()
        {
            // The covers turned back, and the shape of someone in the sheet.
            var top = H.bedTop;
            var dent = Build.Box("Someone slept here", top, new Vector3(0f, 0.52f, 0.05f), new Vector3(0.45f, 0.06f, 0.65f), new Color(0.72f, 0.7f, 0.66f), false);
            dent.transform.localScale = new Vector3(0.45f, 0.06f, 0.65f);
            Build.Box("Covers turned back", top, new Vector3(0f, 0.6f, -0.35f), new Vector3(1.02f, 0.12f, 0.3f), new Color(0.5f, 0.35f, 0.4f), false);
            return true;
        }

        bool Food()
        {
            bool any = false;
            foreach (var f in H.food)
                if (f != null && f.gameObject.activeSelf && !Vis.Sees(Cam, f.transform.position)) { f.gameObject.SetActive(false); any = true; }
            if (!any) return false;
            Vector3 table = H.InRoom("kitchen", new Vector3(-4.3f, 0.8f, -4.15f));
            var core = Build.Prim(PrimitiveType.Capsule, "Apple core", H.transform, table, new Vector3(0.035f, 0.035f, 0.035f), Build.Mat(new Color(0.75f, 0.62f, 0.4f)), false);
            core.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            return true;
        }

        // ---------------------------------------------------------------- the familiar made wrong

        bool FaceDown()
        {
            foreach (var m in H.standingPhotos) if (m != null && Vis.Sees(Cam, m.transform.position)) return false;
            foreach (var m in H.standingPhotos)
                if (m != null && !m.held) m.Teleport(m.transform.position + Vector3.up * 0.05f, m.transform.rotation * Quaternion.Euler(90f, 0f, 0f));
            // The ones on the walls: turned round, so you see only the brown paper backs.
            var back = new Texture2D(4, 4);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = new Color(0.36f, 0.27f, 0.18f);
            back.SetPixels(px);
            back.Apply();
            foreach (var mat in H.photoMats)
            {
                if (!photoOriginals.ContainsKey(mat)) photoOriginals[mat] = mat.mainTexture;
                mat.mainTexture = back;
            }
            return true;
        }

        bool Stranger()
        {
            int changed = 0;
            foreach (var mat in H.photoMats)
            {
                var src = mat.mainTexture as Texture2D;
                if (src == null) continue;
                Texture2D t;
                try
                {
                    t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                    t.SetPixels(src.GetPixels());
                }
                catch (System.Exception) { continue; }        // a texture we can't read: leave that one alone
                // A pale figure at the edge of the group, half in the frame, face blurred.
                int w = t.width, h = t.height;
                float cx = w * (Random.value < 0.5f ? 0.12f : 0.88f), cy = h * 0.58f, r = w * 0.07f;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x - cx) / r, dy = (y - cy) / (r * 1.3f);
                        float headD = dx * dx + dy * dy;
                        bool body = Mathf.Abs(x - cx) < r * 1.3f && y < cy - r * 1.1f;
                        if (headD < 1f || body)
                        {
                            Color c = t.GetPixel(x, y);
                            t.SetPixel(x, y, Color.Lerp(c, new Color(0.82f, 0.8f, 0.78f), headD < 1f ? 0.8f : 0.45f));
                        }
                    }
                t.Apply();
                if (!photoOriginals.ContainsKey(mat)) photoOriginals[mat] = mat.mainTexture;
                mat.mainTexture = t;
                changed++;
            }
            return changed > 0;
        }

        bool Wardrobe() { StartCoroutine(Knocking()); return true; }

        IEnumerator Knocking()
        {
            Vector3 at = H.wardrobe.position + Vector3.up * 1.2f;
            Sfx.At("knock", at, 0.45f, 1.2f);
            for (float t = 0f; t < 5f; t += Time.deltaTime) { if ((at - PlayerPos).magnitude < 1.4f) yield break; yield return null; }
            Sfx.At("knock", at, 0.3f, 1.25f);
            for (float t = 0f; t < 6f; t += Time.deltaTime) { if ((at - PlayerPos).magnitude < 1.4f) break; yield return null; }
            // Close enough to open it, and it stops. Later on, a scratch, from inside, low down.
            if (g.day >= 5) Sfx.At("scratch", H.wardrobe.position + Vector3.up * 0.3f, 0.35f, 0.9f);
        }

        bool Steps() { StartCoroutine(Following()); return true; }

        // Your footsteps, a beat late, a few paces behind you. Stop, and it takes one more step. Turn round, and it's gone.
        IEnumerator Following()
        {
            stepsOn = true;
            float lastStep = Time.time, startYaw = Cam.transform.eulerAngles.y;
            System.Action<Vector3> echo = p => StartCoroutine(Echo(p));
            Player.Stepped += echo;
            float t0 = Time.time;
            while (Time.time - t0 < 16f)
            {
                if (g.player.velocity.magnitude > 0.3f) lastStep = Time.time;
                if (Mathf.Abs(Mathf.DeltaAngle(Cam.transform.eulerAngles.y, startYaw)) > 120f) break;      // you turned round
                if (Time.time - lastStep > 1.4f)
                {
                    Player.Stepped -= echo;
                    yield return new WaitForSeconds(0.5f);
                    Sfx.At("step", PlayerPos - Vis.Flat(Cam.transform.forward).normalized * 2.4f, 0.32f, 0.9f);
                    g.Shake(0.02f, "it took one more step after you stopped");
                    break;
                }
                startYaw = Mathf.LerpAngle(startYaw, Cam.transform.eulerAngles.y, Time.deltaTime * 0.5f);
                yield return null;
            }
            Player.Stepped -= echo;
            stepsOn = false;
        }

        IEnumerator Echo(Vector3 p)
        {
            yield return new WaitForSeconds(0.3f);
            Vector3 behind = PlayerPos - Vis.Flat(Cam.transform.forward).normalized * 2.8f;
            Sfx.At("step", behind, 0.26f, 0.88f + Random.value * 0.1f);
        }

        bool Calendar()
        {
            H.CrossOff(35);
            return true;
        }

        // ---------------------------------------------------------------- the mirror

        bool MirrorBehind() { StartCoroutine(Reflected()); return true; }

        IEnumerator Reflected()
        {
            var look = g.fear.Compose(g.day <= 4 ? 1 : 2);
            look[Monster.Shift] = 0;
            look[Monster.Legs] = Monster.TwoLegs;
            inMirror = Monster.Create(null, look);
            Mirror.SetLayer(inMirror.gameObject, Mirror.OnlyInMirror);
            g.Log("haunting: in the mirror, behind you: " + g.fear.DescribeLook(look));
            float t0 = Time.time, seen = 0f;
            float startYaw = Cam.transform.eulerAngles.y;
            bool shook = false;
            while (Time.time - t0 < 9f && inMirror != null)
            {
                Vector3 back = -Vis.Flat(Cam.transform.forward).normalized;
                inMirror.transform.position = PlayerPos + back * 0.85f;
                inMirror.FaceToward(PlayerPos);
                if (H.hallMirror.Watched(Cam)) { seen += Time.deltaTime; if (seen > 0.5f && !shook) { shook = true; g.Shake(0.05f, "something behind you in the mirror"); g.RungSeen(3, "behind you, in the mirror"); } }
                else if (seen > 0.3f) break;                                   // you looked away from the glass
                if (Mathf.Abs(Mathf.DeltaAngle(Cam.transform.eulerAngles.y, startYaw)) > 100f) break;   // you turned round: nothing there
                yield return null;
            }
            if (inMirror != null) Destroy(inMirror.gameObject);
            inMirror = null;
        }

        bool MirrorLate() { StartCoroutine(Late()); return true; }

        IEnumerator Late()
        {
            var m = H.hallMirror;
            for (float t = 0f; t < 9f; t += Time.deltaTime)
            {
                m.lag = Mathf.Lerp(0.25f, 0.7f, t / 9f);
                if (!m.Watched(Cam, 6f) && t > 2f) break;
                yield return null;
            }
            m.lag = 0f;
            if (m.Watched(Cam, 6f)) g.Shake(0.03f, "your reflection caught up with you");
        }
    }
}
