using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Two things that happen at the edges of sleep, both real and both common.
    ///
    /// Sleep paralysis. You wake and can't move. Cheyne's surveys of thousands of people found three kinds of
    /// experience, and they come in an order: the intruder (a sense that someone is in the room, then a shadow,
    /// then footsteps coming closer), the incubus (weight on the chest, not being able to breathe), and floating.
    /// Here: you wake in the dark facing the doorway, unable to move anything but your eyes. Something is in
    /// the room. A shadow in the doorway. Steps. The mattress takes its weight. Then the weight is on your
    /// chest and its face is over yours. The way out is the one people really use: move one finger, over and
    /// over (press E). Break it early and it's only a shadow in the doorway.
    ///
    /// The false awakening. You wake, it's morning, the chores are on the list, and then you notice: it's still
    /// dark outside, the clock has stopped, the list won't read, the doors are all open. Something is standing
    /// at the end of the hall. Then you wake again, for real.
    ///
    /// Both are chosen by the storyteller's learner ("night" domain): measured, and done more for players they work on.
    /// </summary>
    public class Nights : MonoBehaviour
    {
        GameDirector g;
        int lastParalysis = -10, lastFalse = -10;
        static readonly string[] SpTags = { "night", "hunted", "near", "trapped", "body", "self" };
        static readonly string[] FaTags = { "night", "uncanny", "self", "home", "far" };

        public void Init(GameDirector director) { g = director; }

        public void NewRun() { lastParalysis = lastFalse = -10; }

        bool Choose(string id, string[] tags, int day, int last, int minDay, int guaranteeBy)
        {
            if (day < minDay || day - last < 2) return false;
            if (last < 0 && day >= guaranteeBy) return true;
            float expect = g.teller.learner.Expect("night", id, tags);
            float p = Mathf.Clamp(0.3f + 0.6f * expect, 0.15f, 0.65f);
            return Random.value < p;
        }

        public bool ParalysisTonight(int day)
        {
            if (Tour.ForceParalysis) return true;
            bool yes = Choose("sleep paralysis", SpTags, day, lastParalysis, 2, 4);
            if (yes) lastParalysis = day;
            return yes;
        }

        public bool FalseAwakening(int nextDay)
        {
            if (Tour.ForceFalseAwakening) return true;
            bool yes = Choose("false awakening", FaTags, nextDay, lastFalse, 3, 5);
            if (yes) lastFalse = nextDay;
            return yes;
        }

        // ---------------------------------------------------------------- sleep paralysis

        public IEnumerator Paralysis()
        {
            var house = g.house;
            var player = g.player;
            var bedroom = house.Find("bedroom");
            g.Log("night: sleep paralysis. You wake and can't move");
            // Lying in bed, head on the pillow, facing out at the room and its doorway.
            Vector3 pillow = house.InRoom("bedroom", new Vector3(6.3f, 0f, 6.25f));
            Vector3 doorMid = bedroom.door.transform.position + Quaternion.Euler(0f, bedroom.door.baseYaw, 0f) * Vector3.forward * 0.58f;
            player.Drop();
            player.eyeHeight = 0.98f;                                     // head on the pillow, looking down the bed
            player.Teleport(pillow, Mathf.Atan2(doorMid.x - pillow.x, doorMid.z - pillow.z) * Mathf.Rad2Deg);
            player.LookPitch(9f);
            bedroom.door.Snap(true);
            house.SetTime(1, g.day);
            bedroom.darkUntil = Time.time + 60f;                          // the room is dark; the hall light is on behind the door
            RenderSettings.ambientLight *= 0.5f;
            var moon = Build.Lamp("Moonlight", house.transform, Vector3.zero, new Color(0.45f, 0.55f, 0.85f), 0.5f, 7f);
            moon.transform.position = bedroom.center + Vector3.up * 2.2f;
            player.Paralyse(true);
            g.hud.fadeSpeed = 0.35f;
            g.hud.fadeTarget = 0f;
            float before = Mathf.Clamp01(Reactions.Level);
            var heart = Sfx.Loop("heart", null, 0.25f, false, 1f);
            var breath = Sfx.Loop("breath", null, 0.25f, false, 1.1f);
            g.hud.Say("You're awake. You can't move.", 4f);

            float escape = 0f, t = 0f, peak = before;
            bool broke = false;
            Monster it = null;
            Light rim = null;
            int stage = 0;
            float nextStep = 0f;
            // Where it ends up: beside the bed, in front of you, where you can't not see it.
            Vector3 bedside = pillow + Vis.Flat(doorMid - pillow).normalized * 1.35f + Vis.Flat(Vector3.Cross(Vector3.up, doorMid - pillow)).normalized * 0.35f;
            Light faceLight = null;
            while (t < 34f)
            {
                t += Time.deltaTime;
                peak = Mathf.Max(peak, Mathf.Clamp01(Reactions.Level));
                // The one thing you can move: a finger. Again. Again.
                if (In.Down(Btn.Interact)) { escape += 0.13f; Sfx.Ui("click", 0.15f, 0.6f); }
                escape = Mathf.Max(0f, escape - Time.deltaTime * 0.06f);
                if (stage >= 1 && t > 9f && t < 10f) g.hud.Say("Move a finger. Just one. (E, again and again)", 4f);
                if (escape >= 1f) { broke = true; break; }

                float f = Mathf.Clamp01(t / 30f);
                heart.pitch = 1f + f * 0.9f;
                heart.volume = 0.25f + f * 0.6f;
                Look.Fear(0.5f + f * 0.5f);

                if (stage == 0 && t > 4f)
                {
                    // The intruder: first only a presence. A breath at your ear, from no one.
                    stage = 1;
                    Sfx.Ui("whisper", 0.35f, 0.7f);
                }
                else if (stage == 1 && t > 8f)
                {
                    // A shadow in the doorway.
                    stage = 2;
                    var look = g.fear.Compose(2);
                    look[Monster.Shift] = 0;
                    look[Monster.Legs] = Monster.TwoLegs;
                    it = Monster.Create(null, look);
                    it.transform.position = doorMid - Vis.Flat(doorMid - bedroom.center).normalized * 0.1f;
                    it.FaceToward(pillow);
                    // Backlit by the hall: a shape in the doorway before it's anything else.
                    rim = Build.Lamp("Behind it", house.transform, Vector3.zero, new Color(1f, 0.85f, 0.65f), 1.6f, 3.5f);
                    rim.transform.position = doorMid + Vis.Flat(doorMid - bedroom.center).normalized * 0.9f + Vector3.up * 1.9f;
                    g.Log("night: a shadow in the doorway (" + g.fear.DescribeLook(look) + ")");
                }
                else if (stage == 2 && t > 13f)
                {
                    stage = 3;
                    nextStep = t;
                    // As it comes in, just enough light from the doorway to make out its face.
                    faceLight = Build.Lamp("Its face", house.transform, Vector3.zero, new Color(0.7f, 0.75f, 0.9f), 0.9f, 1.6f);
                }
                else if (stage == 3)
                {
                    // Footsteps, coming closer, a little at a time (at each blink, if the camera can see you blink).
                    bool blink = FaceReader.Present && FaceReader.Blinking;
                    if (t >= nextStep || blink)
                    {
                        nextStep = t + 1.7f;
                        Vector3 to = Vector3.MoveTowards(it.transform.position, bedside, 1.25f);
                        it.transform.position = to;
                        it.FaceToward(pillow);
                        Sfx.At("step", to, 0.45f, 0.8f);
                        if ((to - bedside).sqrMagnitude < 0.01f) stage = 4;
                    }
                }
                else if (stage == 4)
                {
                    // It sits. The mattress takes its weight, and tips you toward it.
                    stage = 5;
                    Sfx.Ui("sink", 0.6f);
                    nextStep = t;
                }
                else if (stage == 5)
                {
                    player.Roll = Mathf.Lerp(player.Roll, 4f, Time.deltaTime * 1.5f);
                    if (t - nextStep > 3f) { stage = 6; g.Log("night: the weight is on your chest"); }
                }
                else if (stage == 6)
                {
                    // The incubus: it leans over you, onto you. You can't breathe.
                    Vector3 face = player.cam.transform.position + player.cam.transform.forward * 0.55f + Vector3.up * 0.05f;
                    float h = Mathf.Max(1f, it.height);
                    Vector3 want = face - Vector3.up * h * 0.92f;
                    it.transform.position = Vector3.Lerp(it.transform.position, want, Time.deltaTime * 1.6f);
                    it.FaceToward(player.cam.transform.position);
                    breath.pitch = 0.7f + 0.15f * Mathf.Sin(t * 9f);
                    breath.volume = 0.5f * (0.5f + 0.5f * Mathf.Sin(t * 3f));
                }
                if (faceLight != null && it != null) faceLight.transform.position = it.HeadPoint + (player.cam.transform.position - it.HeadPoint).normalized * 0.45f;
                yield return null;
            }
            if (faceLight != null) Destroy(faceLight.gameObject);
            Destroy(heart.gameObject);
            Destroy(breath.gameObject);
            player.Roll = 0f;
            Sfx.Ui("gasp", 0.9f);
            if (!broke) { Sfx.Ui("sting", 0.7f, 0.8f); g.Shake(0.1f, "sleep paralysis: it was on your chest"); }
            else g.Shake(0.05f, "sleep paralysis: you moved a finger and it broke");
            g.Log("night: " + (broke ? "you broke it after " + t.ToString("0") + " s (stage " + stage + ")" : "it ran its course"));
            float y = peak - before;
            g.Log("night: learned from sleep paralysis: " + g.teller.Teach("night", "sleep paralysis", SpTags, y));
            g.hud.fadeSpeed = 6f;
            g.hud.fadeTarget = 1f;
            while (g.hud.fade < 0.99f) yield return null;
            if (it != null) Destroy(it.gameObject);
            if (rim != null) Destroy(rim.gameObject);
            Destroy(moon.gameObject);
            bedroom.darkUntil = 0f;
            player.Paralyse(false);
            player.eyeHeight = 1.65f;
        }

        // ---------------------------------------------------------------- the false awakening

        public IEnumerator FalseMorning(int nextDay)
        {
            var house = g.house;
            var player = g.player;
            g.Log("night: a false awakening. It looks like morning");
            g.haunt.paused = true;
            house.SetTime(1, nextDay);                                   // the windows are still dark
            house.Clock(false);                                          // and the hall clock has stopped
            foreach (var d in house.doors) if (d != house.frontDoor) d.Snap(true);
            player.eyeHeight = 1.65f;
            player.Teleport(house.bedSpawn, 200f);
            g.hud.objectives.Clear();
            foreach (var c in g.chores.Preview()) g.hud.objectives.Add(new KeyValuePair<string, bool>(Garble(c), false));
            g.hud.Say("Day " + nextDay, 3f);
            g.hud.fadeSpeed = 0.8f;
            g.hud.fadeTarget = 0f;
            player.frozen = false;
            float before = Mathf.Clamp01(Reactions.Level), peak = before;

            // At the far end of the hall, by the front door, facing in. Perfectly still.
            var look = g.fear.Compose(2);
            look[Monster.Shift] = 0;
            look[Monster.Legs] = Monster.TwoLegs;
            look[Monster.Body] = 2;                                         // too tall for the room: it has to fill the hall
            if (look[Monster.Skin] == 12) look[Monster.Skin] = 0;           // and not a shadow you could miss
            var it = Monster.Create(null, look);
            it.transform.position = new Vector3(0f, 0f, 3.6f);                // halfway down the hall, facing the bedrooms
            it.FaceToward(new Vector3(0f, 0f, -8f));
            // A cold light on it, from nowhere, so you can't not see it.
            var glow = Build.Lamp("On it", house.transform, Vector3.zero, new Color(0.75f, 0.8f, 0.95f), 2.2f, 4.5f);
            glow.transform.position = it.transform.position + new Vector3(0f, 2.2f, -1.0f);
            g.Log("night: at the end of the hall, by the door: " + g.fear.DescribeLook(look));

            float t = 0f, seen = 0f;
            while (t < 45f && seen < 1.2f && (it.transform.position - player.transform.position).magnitude > 2.6f)
            {
                t += Time.deltaTime;
                peak = Mathf.Max(peak, Mathf.Clamp01(Reactions.Level));
                if (Vis.Sees(player.cam, it.HeadPoint, it.transform) || Vis.Sees(player.cam, it.transform.position + Vector3.up * 1f, it.transform)) seen += Time.deltaTime;
                yield return null;
            }
            // Then it is right in front of you.
            player.frozen = true;
            Vector3 f = Vis.Flat(player.cam.transform.forward).normalized;
            it.transform.position = player.transform.position + f * 0.7f;
            it.FaceToward(player.transform.position);
            Sfx.Ui("sting", 0.9f, 0.9f);
            Look.Kick(1f);
            yield return new WaitForSeconds(0.35f);
            g.hud.fade = 1f;
            g.hud.fadeTarget = 1f;
            Sfx.Ui("gasp", 0.9f);
            Destroy(it.gameObject);
            Destroy(glow.gameObject);
            house.Clock(true);
            g.haunt.paused = false;
            float y = peak - before;
            g.Log("night: you wake again. For real, this time. Learned: " + g.teller.Teach("night", "false awakening", FaTags, y));
            yield return new WaitForSeconds(1.2f);
        }

        // Text you can't read in a dream: the letters are all there, in the wrong order.
        static string Garble(string s)
        {
            var words = s.Split(' ');
            var rng = new System.Random(s.Length * 7);
            for (int i = 0; i < words.Length; i++)
            {
                var w = words[i].ToCharArray();
                if (w.Length > 3)
                    for (int k = 1; k < w.Length - 1; k++) { int j = 1 + rng.Next(w.Length - 2); char c = w[k]; w[k] = w[j]; w[j] = c; }
                words[i] = new string(w);
            }
            return string.Join(" ", words);
        }
    }
}
