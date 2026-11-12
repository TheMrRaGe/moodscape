using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The house's frights, handed to the FEAR Engine's storyteller (FearCore.Storyteller), which decides when,
    /// how big and which. Early days only unlock deniable things (a door, a knock); later days unlock failing
    /// lights, a figure, a doorway that is no longer there. Almost everything happens only while the player
    /// isn't looking. Each event is logged to the Mind overlay with its reason.
    /// </summary>
    public partial class Hauntings : MonoBehaviour
    {
        GameDirector g;
        FearCore.Storyteller teller;
        Transform figure;
        Monster houseMonster;
        bool stalking;
        float figureSeen, figureUntil;
        Room sealedRoom;
        float sealedUntil;
        bool sealNoticed;
        AudioSource bleedLoop;
        bool bleedFigure;
        Monster behind;
        float behindSince, behindShown = -1f, behindDist;
        AudioSource behindBreath;
        bool pending;           // a cued fright is on its way
        int lastBlinks;
        float lastAwayEvent = -100f;
        bool awayUsed;
        /// <summary>Test runs hold the storyteller while they take photographs.</summary>
        public bool paused;

        public void Init(GameDirector director)
        {
            g = director;
            teller = g.teller;
            // id, label, tier, cost, first day, tags (kinds of fear, the sense it uses, where), what it does
            Add("door", "a door moved", 0, 1f, 1, DoorMoves, FearTypes.Unknown, "moved", "far");
            Add("knock", "knocking in another room", 0, 1f, 1, Knock, FearTypes.Unknown, "sound", "far");
            Add("armchair", "the armchair moved", 0, 1f, 2, ChairMoves, FearTypes.Uncanny, "moved", "close");
            Add("putback", "something you moved is back where it belongs", 0, 1f, 1, PutBack, FearTypes.Uncanny, "moved", "far");
            Add("overhead", "footsteps overhead", 0, 1f, 2, Footsteps, FearTypes.Hunted, "sound", "far");
            Add("lights", "the lights failed", 1, 2f, 3, LightsFail, FearTypes.Unknown, "light", "dark");
            // The kinds of fear the dreams have been learning about, each as the house would do it.
            Add("family:" + FearTypes.Animal, "scratching inside the wall beside you", 1, 2f, 3, () => Family(FearTypes.Animal), FearTypes.Animal, FearTypes.Disgust, "sound", "close");
            Add("family:" + FearTypes.Nature, "water dripping from the ceiling", 1, 2f, 3, () => Family(FearTypes.Nature), FearTypes.Nature, "sound", "close");
            Add("family:" + FearTypes.Uncanny, "a doll in the hall", 1, 2f, 3, () => Family(FearTypes.Uncanny), FearTypes.Uncanny, "sight", "far");
            Add("family:" + FearTypes.Loss, "Grandma's rocking chair, rocking", 1, 2f, 3, () => Family(FearTypes.Loss), FearTypes.Loss, "moved", "far");
            Add("family:" + FearTypes.Self, "typing from the study, with nobody at the typewriter", 1, 2f, 3, () => Family(FearTypes.Self), FearTypes.Self, "sound", "far");
            Add("family:" + FearTypes.Hunted, "footsteps coming toward you", 1, 2f, 3, () => Family(FearTypes.Hunted), FearTypes.Hunted, "sound", "near");
            Add("family:" + FearTypes.Trapped, "the door slamming shut behind you", 1, 2f, 3, () => Family(FearTypes.Trapped), FearTypes.Trapped, "sound", "close");
            Add("family:" + FearTypes.Medical, "a heart monitor beeping in another room", 1, 2f, 3, () => Family(FearTypes.Medical), FearTypes.Medical, "sound", "far");
            Add("family:" + FearTypes.Body, "joints cracking just behind you", 1, 2f, 3, () => Family(FearTypes.Body), FearTypes.Body, "sound", "behind");
            Add("family:" + FearTypes.Unknown, "knocking on the door of the room you're in", 1, 2f, 3, () => Family(FearTypes.Unknown), FearTypes.Unknown, "sound", "close");
            Add("whisper", "whispering", 1, 2f, 5, Whisper, FearTypes.Unknown, "voice", "behind");
            var fig = Add("figure", "a figure in the hall", 2, 3f, 1, Figure, FearTypes.Hunted, "sight", "far");
            fig.can = () => g.day >= 5 || bleedFigure;
            fig.needsCreature = true;
            Add("doorgone", "a doorway is gone", 2, 3f, 6, DoorGone, FearTypes.Trapped, FearTypes.Uncanny, "sight");
            Add("behind", "it was right behind you", 2, 3f, 3, Behind, FearTypes.Hunted, "near", "behind").needsCreature = true;
            // The grounded ones: what people really report, done the way they report it (see HauntingsGrounded).
            InitGrounded();
            InitLadder();
            InitMore();

            // Where you hide gets the worst of it, once the house has learned where that is.
            teller.Bias = i =>
            {
                var d = g.dread;
                bool inRefuge = d != null && d.RefugeCorrupt && g.house.RoomAt(PlayerPos) == d.Refuge;
                return (inRefuge && i.tier >= 1 ? 0.4f : 0f) + GroundBias(i);
            };
            // In the rests, the music box alone, once it has been paired with frights: it does the work by itself.
            teller.DreadMoment = () =>
            {
                var d = g.dread;
                if (d == null || !d.ProbeCue()) return false;
                g.Log("haunting: the music box, and nothing after it (" + d.Describe() + ")");
                return true;
            };
        }

        FearCore.Incident Add(string id, string label, int tier, float cost, int minDay, System.Func<bool> act, params string[] tags)
        {
            var inc = new FearCore.Incident { id = id, label = label, tier = tier, cost = cost, minEpisode = minDay, tags = tags, cooldown = tier == 2 ? 90f : 30f };
            inc.fire = () => Fire(inc, act);
            teller.Add(inc);
            return inc;
        }

        Camera Cam { get { return g.player.cam; } }
        Vector3 PlayerPos { get { return g.player.transform.position; } }

        public void BeginDay(int day)
        {
            ClearTransient();
            teller.BeginEpisode(day, Time.time);
            BeginDayMore();
            Bleed(day);
        }

        public void Evening() { }

        // Whatever unsettled the player most in last night's dream follows them into the house.
        void Bleed(int day)
        {
            if (day < 3) return;
            int slot, opt;
            g.fear.Best(out slot, out opt);
            if (slot < 0) { g.Log("bleed: nothing from the dreams has landed yet, so nothing follows you today"); return; }
            string label = FearEngine.Slots[slot].options[opt].label;
            if (slot == FearEngine.Sound)
            {
                string clip = FearEngine.SoundClips[opt];
                Room far = FarRoom();
                bleedLoop = Sfx.Loop(clip, null, 0.35f, true);
                bleedLoop.transform.position = far.center + Vector3.up * 1.5f;
                g.Log("bleed: '" + label + "' scored highest in the dreams, so it now plays in the " + far.name);
            }
            else if (slot == FearEngine.Behavior || FearEngine.IsLook(slot))
            {
                bleedFigure = true;
                g.Log("bleed: '" + label + "' scored highest in the dreams, so the figure can appear from today");
            }
            else g.Log("bleed: '" + label + "' scored highest in the dreams (shown as today's waking line)");
        }

        public void ClearTransient()
        {
            if (figure != null) Destroy(figure.gameObject);
            figure = null;
            houseMonster = null;
            if (behind != null) EndBehind(false);
            pending = false;
            StopAllCoroutines();
            if (bleedLoop != null) Destroy(bleedLoop.gameObject);
            bleedLoop = null;
            bleedFigure = false;
            glimpse = false;
            Unseal();
            if (g.halluc != null) g.halluc.Clear();
        }

        Room FarRoom()
        {
            Room here = g.house.RoomAt(PlayerPos), best = null;
            float bestD = -1f;
            foreach (var r in g.house.rooms)
            {
                if (r == here || r == g.house.hall) continue;
                float d = (r.center - PlayerPos).sqrMagnitude;
                if (d > bestD) { bestD = d; best = r; }
            }
            return best ?? g.house.hall;
        }

        void Update()
        {
            if (g == null || teller == null) return;
            float now = Time.time;
            bool inHouse = g.phase == Phase.Morning || g.phase == Phase.Afternoon || g.phase == Phase.Evening;
            // The storyteller only works while you're in the house and free to move; its clocks stop otherwise.
            if (paused || !inHouse || g.hud.modal != Modal.None || g.house.Outside(PlayerPos)) { teller.Hold(now); return; }

            if (figure != null) UpdateFigure(now);
            if (sealedRoom != null) UpdateSeal(now);
            if (behind != null) UpdateBehind(now);

            // How it reads you: the reaction level (face, heart, voice, how you move), and whether you're there to see.
            bool watching = Reactions.Watching && !(FaceReader.Present && FaceReader.LookingAway);
            teller.Tick(now, Mathf.Clamp01(Reactions.Level), watching);
            TickLadder(now);
            TickMore(now);

            // The monster's leash: while it is in the house and close, the gauge fills.
            float dist = figure != null ? (figure.position - PlayerPos).magnitude : float.PositiveInfinity;
            bool seen = houseMonster != null && Vis.Sees(Cam, houseMonster.HeadPoint, figure);
            teller.menace.Tick(now, Time.deltaTime, dist, seen);
            if (teller.menace.Backstage && figure != null && stalking && !seen)
            {
                Destroy(figure.gameObject);
                figure = null; houseMonster = null;
                g.Log("haunting: it went back into the walls (the storyteller pulled it back so you could breathe)");
            }

            // The real you looked away from the screen: the house uses the moment.
            if (FaceReader.Present || FaceReader.On)
            {
                if (!FaceReader.LookingAway) awayUsed = false;
                else if (!awayUsed && FaceReader.AwayFor > 0.6f && now - lastAwayEvent > 35f && teller.beat != FearCore.Beat.Relax)
                {
                    awayUsed = true;
                    if (ChairMoves() || DoorMoves() || PutBack())
                    {
                        lastAwayEvent = now;
                        teller.Stimulus(0.1f, now);
                        g.Log("haunting: you looked away from the screen, and the house moved something while you did");
                    }
                }
            }
        }

        // The storyteller picked it: do it now, or announce the big ones with the music box first.
        bool Fire(FearCore.Incident inc, System.Func<bool> act)
        {
            var d = g.dread;
            if (pending) return false;
            if (d != null && inc.tier >= 1 && d.AnnounceWithCue() && (inc.can == null || inc.can()))
            {
                StartCoroutine(Cued(inc, act));
                return true;
            }
            if (!act()) return false;
            After(inc, false);
            return true;
        }

        void After(FearCore.Incident inc, bool cued)
        {
            var d = g.dread;
            if (d != null)
            {
                d.Used(inc.id);
                if (inc.tier >= 1) { if (cued) d.Paired(); else d.Unannounced(); }
            }
            bool refuge = d != null && d.RefugeCorrupt && g.house.RoomAt(PlayerPos) == d.Refuge;
            g.Log("haunting: " + inc.label + (cued ? " (after the music box)" : "") + (refuge ? " (in the room you hide in)" : ""));
        }

        // The music box, a pause just long enough to wonder, then the fright.
        IEnumerator Cued(FearCore.Incident inc, System.Func<bool> act)
        {
            pending = true;
            g.dread.PlayCue(FarRoom().center);
            yield return new WaitForSeconds(2.4f + Random.value * 1.2f);
            pending = false;
            if (g.hud.modal == Modal.None && !g.house.Outside(PlayerPos) && act()) After(inc, true);
            else g.Log("haunting: the music box played, but '" + inc.label + "' couldn't happen unseen after it");
        }

        // ---------------------------------------------------------------- events

        bool DoorMoves()
        {
            var unseen = new List<Door>();
            foreach (var d in g.house.doors)
                if (d.gameObject.activeSelf && !Vis.Sees(Cam, d.Middle, d.transform) && (d.Middle - PlayerPos).magnitude > 2.5f) unseen.Add(d);
            if (unseen.Count == 0) return false;
            var door = unseen[Random.Range(0, unseen.Count)];
            door.Set(!door.open, g.day >= 3 && door.open);
            return true;
        }

        bool Knock()
        {
            var d = g.dread;
            Room where = d != null && d.RefugeCorrupt && Random.value < 0.6f ? d.Refuge : FarRoom();
            Sfx.At("knock", where.center + Vector3.up * 1.5f, 1f, 0.9f + Random.value * 0.2f);
            return true;
        }

        bool ChairMoves()
        {
            var m = g.house.armchair;
            if (m == null || m.held) return false;
            var chair = m.transform;
            if (Vis.Sees(Cam, chair.position + Vector3.up * 0.5f, chair)) return false;
            // Put it somewhere the player will walk into: the hall, turned to face where they are now.
            Vector3[] spots = { new Vector3(0f, 0f, -2.5f), new Vector3(0f, 0f, 2.5f), g.house.InRoom("living room", new Vector3(2.15f, 0f, -4f)), new Vector3(-0.6f, 0f, 6f) };
            var open = new List<Vector3>();
            foreach (var s in spots)
                if (!Vis.Sees(Cam, s + Vector3.up * 0.5f) && (s - PlayerPos).magnitude > 3f && (s - chair.position).magnitude > 1.5f) open.Add(s);
            if (open.Count == 0) return false;
            Vector3 to = open[Random.Range(0, open.Count)];
            to.y = chair.position.y + 0.02f;
            m.Teleport(to, Quaternion.Euler(0f, Models.Face(PlayerPos.x - to.x, PlayerPos.z - to.z), 0f));
            return true;
        }

        // The house keeps things where your grandparents kept them. Move something, look away, and it's back.
        bool PutBack()
        {
            var moved = new List<Movable>();
            foreach (var m in g.house.movables)
            {
                if (m == null || m.InUse || m.FromHome < 0.7f) continue;
                if (Vis.Sees(Cam, m.transform.position, m.transform) || Vis.Sees(Cam, m.homePos)) continue;
                if ((m.homePos - PlayerPos).magnitude < 2f) continue;
                moved.Add(m);
            }
            if (moved.Count == 0) return false;
            var pick = moved[Random.Range(0, moved.Count)];
            pick.Teleport(pick.homePos + Vector3.up * 0.02f, pick.homeRot);
            g.Log("haunting: put '" + pick.name + "' back where it was kept");
            return true;
        }

        // From Day 3 the house can use each kind of fear the way it would happen in a house. The storyteller's learner
        // picks among them (a first impression from what it has learned in the dreams, then each one's own record).
        bool Family(string fam)
        {
            Room here = g.house.RoomAt(PlayerPos), far = FarRoom();
            string what;
            switch (fam)
            {
                case FearTypes.Animal:
                case FearTypes.Disgust:
                    LoopAt("scratch", WallNear(PlayerPos), 0.5f, 9f);
                    what = "scratching inside the wall beside you";
                    break;
                case FearTypes.Nature:
                    LoopAt("drip", here.center + Vector3.up * 2.6f, 0.45f, 12f, 0.9f);
                    what = "water dripping from the ceiling of the " + here.name;
                    break;
                case FearTypes.Uncanny:
                    if (!Doll()) return false;
                    what = "a doll in the hall";
                    break;
                case FearTypes.Loss:
                    if (g.house.rocker == null || g.house.rocker.held || Vis.Sees(Cam, g.house.rocker.transform.position + Vector3.up * 0.5f, g.house.rocker.transform)) return false;
                    StartCoroutine(Rock(g.house.rocker.transform));
                    what = "Grandma's rocking chair, rocking";
                    break;
                case FearTypes.Self:
                    LoopAt("typing", g.house.typewriter.transform.position, 0.6f, 6f);
                    g.hud.Say("Someone is typing in the study.", 3f);
                    what = "typing from the study, with nobody at the typewriter";
                    break;
                case FearTypes.Hunted:
                    StartCoroutine(Approach());
                    what = "footsteps coming toward you";
                    break;
                case FearTypes.Trapped:
                    if (here.door == null || !here.door.open || !here.door.gameObject.activeSelf || Vis.Sees(Cam, here.door.Middle, here.door.transform)) return false;
                    here.door.Set(false, true);
                    what = "the " + here.name + " door slamming shut behind you";
                    break;
                case FearTypes.Medical:
                    LoopAt("beep", far.center + Vector3.up * 1.2f, 0.4f, 14f);
                    what = "a heart monitor beeping in the " + far.name;
                    break;
                case FearTypes.Body:
                    StartCoroutine(Joints());
                    what = "joints cracking just behind you";
                    break;
                default:
                    Sfx.At("knock", (here.door != null ? here.door.Middle : far.center + Vector3.up * 1.5f), 1f, 0.8f);
                    what = "knocking on the door of the room you're in";
                    break;
            }
            g.Log("haunting: the house is using " + FearTypes.Name(fam) + ": " + what);
            g.Shake(0.02f, what);
            return true;
        }

        void LoopAt(string clip, Vector3 pos, float vol, float seconds, float pitch = 1f)
        {
            var a = Sfx.Loop(clip, null, vol, true, pitch);
            a.transform.position = pos;
            Destroy(a.gameObject, seconds);
        }

        Vector3 WallNear(Vector3 p)
        {
            RaycastHit hit;
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
                if (Physics.Raycast(p + Vector3.up * 1f, d, out hit, 3f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
            return p + Vector3.up;
        }

        Transform doll;

        bool Doll()
        {
            if (doll != null) return false;
            Vector3 spot = (g.house.hallNorth - PlayerPos).sqrMagnitude > (g.house.hallSouth - PlayerPos).sqrMagnitude ? g.house.hallNorth : g.house.hallSouth;
            spot += new Vector3(0.6f, 0f, spot.z > 0f ? -0.4f : 0.4f);
            if (Vis.Sees(Cam, spot + Vector3.up * 0.5f) || (spot - PlayerPos).magnitude < 5f) return false;
            var look = new int[Monster.Traits];
            look[Monster.Kind] = Monster.Doll;
            look[Monster.Body] = 4;
            var m = Monster.Create(null, look);
            m.transform.position = spot;
            m.FaceToward(PlayerPos);
            doll = m.transform;
            StartCoroutine(DollWatch(m));
            return true;
        }

        IEnumerator DollWatch(Monster m)
        {
            float seen = 0f, until = Time.time + 70f;
            while (m != null && Time.time < until)
            {
                if (Vis.Sees(Cam, m.HeadPos, m.transform)) seen += Time.deltaTime;
                else
                {
                    m.FaceToward(PlayerPos);          // it is always facing you when you look back
                    if (seen > 1.2f) break;           // seen properly, then looked away from: gone
                }
                yield return null;
            }
            if (m != null) Destroy(m.gameObject);
            doll = null;
        }

        IEnumerator Rock(Transform chair)
        {
            Quaternion rest = chair.rotation;
            var body = chair.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = true;
            for (float t = 0f; t < 10f; t += Time.deltaTime)
            {
                chair.rotation = rest * Quaternion.Euler(Mathf.Sin(t * 2.6f) * 7f * Mathf.Min(1f, (10f - t) * 0.5f), 0f, 0f);
                if (Mathf.Repeat(t * 2.6f / (2f * Mathf.PI), 1f) < Time.deltaTime * 0.45f) Sfx.At("creak", chair.position, 0.5f, 1.3f);
                yield return null;
            }
            chair.rotation = rest;
            if (body != null) body.isKinematic = false;
        }

        IEnumerator Approach()
        {
            Vector3 from = g.house.FarPoint(PlayerPos);
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = Vector3.Lerp(from, PlayerPos, i / 13f);
                Sfx.At("step", p + Vector3.up * 0.1f, 0.9f, 0.75f);
                yield return new WaitForSeconds(0.55f);
            }
        }

        IEnumerator Joints()
        {
            for (int i = 0; i < 7; i++)
            {
                Sfx.At("click", PlayerPos - g.player.Forward * 1.3f + Vector3.up * (1.2f + Random.value * 0.5f), 1f, 0.35f + Random.value * 0.2f);
                yield return new WaitForSeconds(Random.Range(0.08f, 0.45f));
            }
        }

        bool Footsteps()
        {
            StartCoroutine(Steps());
            return true;
        }

        IEnumerator Steps()
        {
            float z = PlayerPos.z > 0f ? -7f : 7f, dir = z < 0f ? 1f : -1f;
            for (int i = 0; i < 9; i++)
            {
                Sfx.At("step", new Vector3(0f, 3.4f, z + dir * i * 0.9f), 0.9f, 0.7f);
                yield return new WaitForSeconds(0.52f);
            }
        }

        bool LightsFail()
        {
            var room = g.house.RoomAt(PlayerPos);
            if (Time.time < room.darkUntil) return false;
            StartCoroutine(Flicker(room));
            return true;
        }

        IEnumerator Flicker(Room room)
        {
            for (int i = 0; i < (Settings.S.reduceFlashing ? 1 : 6); i++)
            {
                room.darkUntil = Time.time + Random.Range(0.05f, 0.2f);
                room.light.intensity = 0f;
                yield return new WaitForSeconds(Random.Range(0.12f, 0.4f));
            }
            room.darkUntil = Time.time + 22f + 4f * g.day;
            Sfx.At("click", room.center + Vector3.up * 2.5f, 1f, 0.6f);
            g.Shake(0.03f, "the lights went out in the " + room.name);
            if (g.dread != null) g.dread.BigFright();
        }

        bool Whisper()
        {
            var a = Sfx.Loop("whisper", g.player.transform, 0.45f, true);
            a.transform.localPosition = new Vector3(0.4f, 1.5f, -1.2f);
            Destroy(a.gameObject, 6f);
            g.Shake(0.02f, "whispering behind you");
            return true;
        }

        // ---------------------------------------------------------------- right behind you

        // It stands at your back, close enough to breathe on your neck, and only shows itself when you turn round.
        bool Behind()
        {
            if (behind != null || figure != null) return false;
            Vector3 back = -Vis.Flat(Cam.transform.forward).normalized;
            Vector3 eye = PlayerPos + Vector3.up * 1.2f;
            float dist = 1.15f;
            if (Physics.Raycast(eye, back, dist + 0.45f)) { dist = 0.8f; if (Physics.Raycast(eye, back, dist + 0.35f)) return false; }
            var look = g.fear.Compose(Mathf.Min(g.day <= 4 ? 1 : 2, Settings.ScareCap));
            look[Monster.Shift] = 0;
            behind = Monster.Create(null, look);
            behind.transform.position = new Vector3(PlayerPos.x, PlayerPos.y, PlayerPos.z) + back * dist;
            behind.FaceToward(PlayerPos);
            behind.SetVisible(false);
            behindDist = dist;
            behindSince = Time.time;
            behindShown = -1f;
            behindBreath = Sfx.Loop("breath", behind.transform, 0.0f, true, 0.85f);
            behindBreath.transform.localPosition = Vector3.up * Mathf.Min(1.7f, behind.height * 0.85f);
            behindBreath.minDistance = 0.5f; behindBreath.maxDistance = 6f;
            g.Log("it is standing right behind you, composed from what frightens you (" + g.fear.DescribeLook(look) + "). It will show itself when you turn round");
            return true;
        }

        void UpdateBehind(float now)
        {
            if (behindShown < 0f)
            {
                // It keeps to your back while you move, and breathes a little louder the longer you don't turn.
                Vector3 back = -Vis.Flat(Cam.transform.forward).normalized;
                Vector3 want = PlayerPos + back * behindDist;
                bool clear = !Physics.Raycast(PlayerPos + Vector3.up * 1.2f, back, behindDist + 0.3f);
                bool seen = Vis.Sees(Cam, behind.HeadPoint, behind.transform) || Vis.Sees(Cam, behind.transform.position + Vector3.up * 0.9f, behind.transform);
                if (clear && !seen) behind.transform.position = Vector3.Lerp(behind.transform.position, want, Time.deltaTime * 6f);
                behind.FaceToward(PlayerPos);
                if (behindBreath != null) behindBreath.volume = Mathf.Min(0.55f, (now - behindSince) * 0.06f);
                if (seen && (behind.transform.position - PlayerPos).magnitude < 2.2f)
                {
                    behindShown = now;
                    behind.SetVisible(true);
                    Sfx.Ui("sting", 1f);
                    Sfx.At("slam", behind.transform.position + Vector3.up, 0.5f, 0.6f);
                    g.Shake(0.12f, "it was right behind you");
                    g.RungSeen(3, "right behind you");
                    if (g.dread != null) g.dread.BigFright();
                }
                else if (now - behindSince > 15f)
                {
                    g.Log("it stood behind you for fifteen seconds. You never turned round");
                    EndBehind(false);
                }
                return;
            }
            // Shown: it lunges, and the lights go with it.
            float t = now - behindShown;
            behind.transform.position += (PlayerPos - behind.transform.position).normalized * Time.deltaTime * 1.6f;
            if (t > 0.42f)
            {
                var room = g.house.RoomAt(PlayerPos);
                room.darkUntil = now + 1.4f;
                room.light.intensity = 0f;
                EndBehind(true);
            }
        }

        /// <summary>For the screenshot tour: put it behind the player now.</summary>
        public bool ForceBehind() { return Behind(); }

        void EndBehind(bool shown)
        {
            if (behindBreath != null) Destroy(behindBreath.gameObject);
            if (behind != null) Destroy(behind.gameObject);
            behind = null;
            behindBreath = null;
        }

        bool Figure()
        {
            if (figure != null) return false;
            var h = g.house;
            Vector3 spot = (h.hallNorth - PlayerPos).sqrMagnitude > (h.hallSouth - PlayerPos).sqrMagnitude ? h.hallNorth : h.hallSouth;
            if (Vis.Sees(Cam, spot + Vector3.up * 1.6f) || (spot - PlayerPos).magnitude < 6f) return false;
            var look = g.fear.Compose(FigureCap);
            houseMonster = Monster.Create(null, look);
            figure = houseMonster.transform;
            figure.position = spot;
            glimpse = false;
            stalking = g.RungOk(4);
            g.Log("it is in the house, composed from what frightens you: " + g.fear.DescribeLook(look) +
                  (stalking ? " (rung 4: it comes for you while you aren't looking)" : " (rung " + g.Rung + ": seen whole, at a distance)"));
            figureSeen = 0f;
            figureUntil = Time.time + 75f;
            return true;
        }

        void UpdateFigure(float now)
        {
            Vector3 head = houseMonster != null ? houseMonster.HeadPoint : figure.position + Vector3.up * 1.9f;
            figure.rotation = Quaternion.LookRotation(Vis.Flat(PlayerPos - figure.position));
            float dist = (figure.position - PlayerPos).magnitude;
            bool seen = Vis.Sees(Cam, head, figure);
            // If the camera can see the player's face: it doesn't count as watched when they look away from the screen.
            if (FaceReader.Present && FaceReader.LookingAway) seen = false;
            if (seen) figureSeen += Time.deltaTime;
            if (UpdateGlimpse(now, seen, dist)) return;
            if (stalking && seen) g.RungSeen(4, "it was in the house, and it didn't go when you looked");
            // And late in the run, every time they really blink, it is closer.
            if (stalking && FaceReader.Present && FaceReader.Blinks != lastBlinks)
            {
                if (dist > 2.2f && Vis.Sees(Cam, head, figure))
                {
                    Vector3 step = Vis.Flat(PlayerPos - figure.position).normalized * Mathf.Min(0.8f, dist - 2f);
                    figure.position += step;
                    g.Log("haunting: you blinked, and it was closer (" + (dist - step.magnitude).ToString("0.0") + " m)");
                }
                lastBlinks = FaceReader.Blinks;
            }
            if (stalking)
            {
                // Late in the run it doesn't vanish when seen. It waits, and walks down the hall when you look away.
                if (!seen && dist > 1.2f)
                {
                    // It walks the house the way you would: down the hall and through the doorways.
                    Vector3 target = PlayerPos;
                    if (g.house.RoomAt(figure.position) != g.house.RoomAt(PlayerPos))
                        foreach (var p in g.house.Path(figure.position, PlayerPos))
                        {
                            Vector3 d = p - figure.position; d.y = 0f;
                            if (d.magnitude > 0.4f) { target = p; break; }
                        }
                    Vector3 to = target - figure.position; to.y = 0f;
                    figure.position += to.normalized * Mathf.Min(1.7f * Time.deltaTime, to.magnitude);
                    foreach (var door in g.house.doors)
                        if (!door.open && door.gameObject.activeSelf && (door.Middle - figure.position).magnitude < 1.4f) door.Set(true, false);
                }
                if (dist < 1.3f)
                {
                    Sfx.Ui("sting", 0.9f);
                    var room = g.house.RoomAt(PlayerPos);
                    room.darkUntil = now + 12f;
                    Destroy(figure.gameObject);
                    figure = null; houseMonster = null;
                    g.Shake(0.1f, "the monster reached you in the " + room.name);
                    if (g.Rung >= 5) g.RungSeen(5, "it reached you in the " + room.name);
                    g.hud.Say("When the light comes back, you are alone.", 4f);
                    return;
                }
                if (now > figureUntil + 60f) { Destroy(figure.gameObject); figure = null; houseMonster = null; }
                return;
            }
            if (figureSeen > 0.7f || dist < 4.5f)
            {
                Sfx.Ui("sting", 0.7f);
                Destroy(figure.gameObject);
                figure = null;
                g.Shake(0.06f, "saw the figure in the hall");
                g.RungSeen(2, "standing in the hall");
                if (g.dread != null) g.dread.BigFright();
                g.hud.Say("There was someone standing in the hall.", 3.5f);
            }
            else if (now > figureUntil)
            {
                Destroy(figure.gameObject);
                figure = null;
                g.Log("haunting: the figure was never seen, so it left");
            }
        }

        bool DoorGone()
        {
            if (sealedRoom != null) return false;
            Room here = g.house.RoomAt(PlayerPos);
            var ok = new List<Room>();
            foreach (var r in g.house.rooms)
            {
                if (r.seal == null || r == here) continue;
                if (r.name == "bedroom" || r.name == "study") continue;                   // never lock away the bed or the typewriter
                if (g.chores.PendingTargetPosition(r.name) != null) continue;             // or a chore that still has to be done
                Vector3 mid = r.seal.transform.position;
                if (Vis.Sees(Cam, mid) || (mid - PlayerPos).magnitude < 4f) continue;
                ok.Add(r);
            }
            if (ok.Count == 0) return false;
            sealedRoom = ok[Random.Range(0, ok.Count)];
            sealedRoom.seal.SetActive(true);
            sealedRoom.door.gameObject.SetActive(false);
            sealedUntil = Time.time + 45f;
            sealNoticed = false;
            return true;
        }

        void UpdateSeal(float now)
        {
            Vector3 mid = sealedRoom.seal.transform.position;
            bool seen = Vis.Sees(Cam, mid, sealedRoom.seal.transform);
            if (!sealNoticed && seen && (mid - PlayerPos).magnitude < 4f)
            {
                sealNoticed = true;
                g.Shake(0.05f, "noticed the " + sealedRoom.name + " doorway is gone");
                g.hud.Say("There was a door to the " + sealedRoom.name + " here.", 4f);
            }
            if (now > sealedUntil && !seen) Unseal();
        }

        void Unseal()
        {
            if (sealedRoom == null) return;
            sealedRoom.seal.SetActive(false);
            sealedRoom.door.gameObject.SetActive(true);
            sealedRoom = null;
        }
    }
}
