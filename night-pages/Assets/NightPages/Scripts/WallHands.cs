using System.Collections;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Stand too close to a wall, and sometimes a hand comes out of it at your face.
    ///
    /// It can happen in any room: in the house in the evening and at night, and anywhere in the dream. Only when you
    /// walk right up to a plain stretch of wall and look at it (not furniture, not a door), never twice in a row, and
    /// not often: the fear engine's learner decides whether it's worth it this time (tagged as the body made wrong
    /// and the uncanny), and is told afterwards how much it got to you. Out of the wallpaper: a long grey arm, fingers
    /// spread, reaching for your face; a moment where it gropes; then it's pulled back in. The hole it leaves
    /// closes behind it.
    /// </summary>
    public static class WallHands
    {
        static float nextAllowed = 30f, lastNear;
        static bool busy;
        public static int count;
        static readonly string[] Tags = { FearTypes.Body, FearTypes.Uncanny, FearTypes.Unknown };

        public static void ResetStatics() { nextAllowed = 30f; busy = false; count = 0; }

        public static void Tick(GameDirector g)
        {
            if (busy || g.player == null || g.player.frozen || g.hud.modal != Modal.None) return;
            bool dream = g.phase == Phase.Dream && g.dream.active;
            bool night = g.phase == Phase.Evening;
            if (!dream && !night) return;
            if (Time.time < nextAllowed) return;
            var cam = g.player.cam;
            RaycastHit hit;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, 0.95f, ~0, QueryTriggerInteraction.Ignore)) return;
            if (Mathf.Abs(hit.normal.y) > 0.25f) return;
            var col = hit.collider;
            if (col.GetComponentInParent<Movable>() != null || col.GetComponentInParent<Interactable>() != null || col.GetComponentInParent<Door>() != null) return;
            Bounds b = col.bounds;
            if (b.size.y < 2f || Mathf.Max(b.size.x, b.size.z) < 1.4f) return;            // a wall, not a cupboard
            // Only after you've been standing at it a moment.
            if (lastNear <= 0f) { lastNear = Time.time; return; }
            if (Time.time - lastNear < 0.7f) return;
            lastNear = 0f;
            nextAllowed = Time.time + (dream ? Random.Range(25f, 60f) : Random.Range(70f, 160f));
            var brain = g.fear != null ? g.fear.brain : null;
            float want = brain != null ? brain.Draw("wallhand", "hand", Tags) : Random.value;
            if (want < (dream ? 0.2f : 0.35f)) { g.Log("wall hand: you were close to a wall; not this time (" + want.ToString("0.00") + ")"); return; }
            g.StartCoroutine(Reach(g, hit.point, hit.normal, cam));
        }

        static IEnumerator Reach(GameDirector g, Vector3 at, Vector3 normal, Camera cam)
        {
            busy = true;
            count++;
            float before = Reactions.Level, peak = before;
            Vector3 n = new Vector3(normal.x, 0f, normal.z).normalized;
            Vector3 p = at;
            p.y = Mathf.Clamp(cam.transform.position.y - 0.1f, g.player.transform.position.y + 1.1f, g.player.transform.position.y + 1.8f);
            g.Log("wall hand: out of the wall at your face");

            // The hole: black, ragged, opening.
            var hole = new GameObject("Hole in the wall").transform;
            hole.position = p + n * 0.012f;
            hole.rotation = Quaternion.LookRotation(n);
            var black = Build.Mat(new Color(0.005f, 0.004f, 0.004f));
            for (int i = 0; i < 7; i++)
            {
                var shard = Build.Prim(PrimitiveType.Cube, "Hole", hole, new Vector3(Random.Range(-0.08f, 0.08f), Random.Range(-0.1f, 0.1f), 0f), new Vector3(Random.Range(0.12f, 0.24f), Random.Range(0.12f, 0.26f), 0.01f), black, false);
                shard.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 90f));
            }
            hole.localScale = Vector3.zero;

            // The arm: grey, too long, fingers too long.
            var arm = new GameObject("Hand from the wall").transform;
            arm.rotation = Quaternion.LookRotation(n);
            var skin = Build.Mat(new Color(0.55f, 0.53f, 0.5f), 0f, 0.35f);
            var dark = Build.Mat(new Color(0.3f, 0.28f, 0.27f), 0f, 0.3f);
            var fore = Build.Prim(PrimitiveType.Capsule, "Forearm", arm, new Vector3(0f, 0f, -0.35f), new Vector3(0.09f, 0.4f, 0.09f), skin, false);
            fore.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Build.Prim(PrimitiveType.Sphere, "Wrist", arm, new Vector3(0f, 0f, 0.05f), new Vector3(0.085f, 0.07f, 0.09f), skin, false);
            var palm = Build.Prim(PrimitiveType.Cube, "Palm", arm, new Vector3(0f, 0f, 0.14f), new Vector3(0.1f, 0.03f, 0.13f), skin, false).transform;
            var fingers = new Transform[5];
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * 0.024f;
                var f = new GameObject("Finger").transform;
                f.SetParent(arm, false);
                f.localPosition = i == 0 ? new Vector3(-0.06f, 0f, 0.1f) : new Vector3(x + 0.012f, 0f, 0.2f);
                f.localRotation = Quaternion.Euler(0f, i == 0 ? -40f : (i - 2.5f) * 9f, 0f);
                float len = i == 0 ? 0.09f : 0.15f + 0.03f * (i == 2 ? 1 : 0);
                var s1 = Build.Prim(PrimitiveType.Capsule, "Joint", f, new Vector3(0f, 0f, len * 0.5f), new Vector3(0.018f, len * 0.5f, 0.018f), skin, false);
                s1.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var tip = new GameObject("Tip").transform;
                tip.SetParent(f, false);
                tip.localPosition = new Vector3(0f, 0f, len);
                var s2 = Build.Prim(PrimitiveType.Capsule, "Joint", tip, new Vector3(0f, 0f, len * 0.4f), new Vector3(0.016f, len * 0.4f, 0.016f), skin, false);
                s2.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Build.Prim(PrimitiveType.Sphere, "Nail", tip, new Vector3(0f, 0.008f, len * 0.75f), new Vector3(0.014f, 0.006f, 0.02f), dark, false);
                fingers[i] = f;
            }
            foreach (var r in arm.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Out, fast, at your face.
            Sfx.At("crack", p, 1f, 0.8f);
            Sfx.Ui("sting", 0.9f, 1.05f);
            Vector3 inside = p - n * 0.75f, outside = p + n * Mathf.Min(0.55f, Vector3.Distance(cam.transform.position, p) - 0.18f);
            Vector3 toFace = (cam.transform.position - p).normalized;
            for (float t = 0f; t < 0.14f; t += Time.deltaTime)
            {
                float k = t / 0.14f;
                arm.position = Vector3.Lerp(inside, outside, 1f - (1f - k) * (1f - k));
                arm.rotation = Quaternion.LookRotation(Vector3.Slerp(n, toFace, k * 0.7f));
                hole.localScale = Vector3.one * Mathf.Min(1f, k * 2f);
                peak = Mathf.Max(peak, Reactions.Level);
                yield return null;
            }
            Look.Kick(1f);
            g.Shake(0.05f, "a hand came out of the wall at you");
            Sfx.At("gasp", cam.transform.position, 0.7f, 1f);
            // Groping for your face.
            for (float t = 0f; t < 0.85f; t += Time.deltaTime)
            {
                for (int i = 0; i < 5; i++)
                {
                    float curl = 25f + 35f * Mathf.Abs(Mathf.Sin(t * 14f + i * 1.3f));
                    fingers[i].localRotation = Quaternion.Euler(curl * 0.5f, i == 0 ? -40f : (i - 2.5f) * 9f, 0f);
                    fingers[i].GetChild(fingers[i].childCount - 1).localRotation = Quaternion.Euler(curl, 0f, 0f);
                }
                arm.position = outside + Random.insideUnitSphere * 0.012f;
                peak = Mathf.Max(peak, Reactions.Level);
                yield return null;
            }
            // Pulled back in.
            Sfx.At("creak", p, 0.6f, 0.6f);
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                arm.position = Vector3.Lerp(outside, inside, t / 0.3f);
                yield return null;
            }
            Object.Destroy(arm.gameObject);
            for (float t = 0f; t < 1.8f; t += Time.deltaTime)
            {
                hole.localScale = Vector3.one * (1f - t / 1.8f);
                peak = Mathf.Max(peak, Reactions.Level);
                yield return null;
            }
            Object.Destroy(hole.gameObject);
            var brain = g.fear != null ? g.fear.brain : null;
            if (brain != null) g.Log("wall hand: " + brain.Learn("wallhand", "hand", Tags, Mathf.Clamp(peak - before, -1f, 1f)));
            busy = false;
        }

        /// <summary>For tests and demos: a hand out of whatever wall the camera is facing, now.</summary>
        public static bool Force(GameDirector g)
        {
            var cam = g.player.cam;
            RaycastHit hit;
            if (busy || !Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, 2.5f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(hit.normal.y) > 0.25f) return false;
            g.StartCoroutine(Reach(g, hit.point, hit.normal, cam));
            return true;
        }
    }
}
