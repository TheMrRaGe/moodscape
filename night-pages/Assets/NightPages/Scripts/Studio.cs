using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The studio: a plain, well-lit test room for looking at the monster and the house's models properly. Grey walls,
    /// a floor ruled in metres (so you can judge sizes), a turntable, and three-point lighting (a warm key, a cool fill,
    /// a rim light from behind), no fog, no grading tricks. It sits far above the house, sealed off from it.
    ///
    /// The "studio" job films it (frames, like the monster film) and writes stills: the monster turning in each of its
    /// forms, each transformation filmed with the camera circling it so you can see how the flesh moves from every
    /// side, a close look at its head while it changes, then each furniture model on the turntable.
    /// </summary>
    public partial class GameDirector
    {
        static readonly Vector3 StudioAt = new Vector3(0f, 60f, 0f);

        Transform BuildStudio()
        {
            var root = new GameObject("Studio").transform;
            root.position = StudioAt;
            const float W = 10f, H = 4f;
            var wall = Build.Mat(new Color(0.46f, 0.46f, 0.47f), 0f, 0.08f);
            Build.Box("Studio floor", root, new Vector3(0f, -0.05f, 0f), new Vector3(W, 0.1f, W), FloorGrid());
            Build.Box("Studio ceiling", root, new Vector3(0f, H + 0.05f, 0f), new Vector3(W, 0.1f, W), wall);
            Build.Box("Studio wall", root, new Vector3(0f, H / 2f, W / 2f), new Vector3(W, H, 0.1f), wall);
            Build.Box("Studio wall", root, new Vector3(0f, H / 2f, -W / 2f), new Vector3(W, H, 0.1f), wall);
            Build.Box("Studio wall", root, new Vector3(W / 2f, H / 2f, 0f), new Vector3(0.1f, H, W), wall);
            Build.Box("Studio wall", root, new Vector3(-W / 2f, H / 2f, 0f), new Vector3(0.1f, H, W), wall);
            Build.Prim(PrimitiveType.Cylinder, "Turntable", root, new Vector3(0f, 0.015f, 0f), new Vector3(2.4f, 0.015f, 2.4f), Build.Mat(new Color(0.2f, 0.2f, 0.21f), 0f, 0.3f), false);
            // Three-point lighting, plus a soft top light so nothing falls into black.
            Build.Lamp("Key light", root, new Vector3(2.3f, 3.2f, -2.2f), new Color(1f, 0.95f, 0.88f), 2.4f, 11f, true);
            Build.Lamp("Fill light", root, new Vector3(-2.7f, 2.0f, -1.6f), new Color(0.82f, 0.88f, 1f), 1.0f, 9f, false);
            Build.Lamp("Rim light", root, new Vector3(0.4f, 3.0f, 2.8f), new Color(1f, 1f, 1f), 1.8f, 8f, false);
            Build.Lamp("Top light", root, new Vector3(0f, 3.8f, 0f), new Color(1f, 1f, 1f), 0.7f, 7f, false);
            return root;
        }

        // A mid-grey floor with a line every metre and a fainter one every 25 cm.
        static Material FloorGrid()
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, name = "Studio grid" };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    bool major = x < 3 || y < 3;
                    bool minor = (x % (N / 4)) < 1 || (y % (N / 4)) < 1;
                    px[y * N + x] = major ? new Color(0.2f, 0.2f, 0.21f) : minor ? new Color(0.3f, 0.3f, 0.31f) : new Color(0.36f, 0.36f, 0.37f);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            var m = Build.Mat(Color.white, 0f, 0.15f);
            m.mainTexture = tex;
            m.mainTextureScale = new Vector2(10f, 10f);
            return m;
        }

        // Put the camera at `from` looking at `at`.
        void AimCamera(Vector3 from, Vector3 at)
        {
            player.Teleport(from, YawTo(from, at));
            Vector3 eye = player.cam.transform.position;
            Vector3 d = at - eye;
            float flat = new Vector2(d.x, d.z).magnitude;
            player.LookPitch(Mathf.Atan2(-d.y, Mathf.Max(0.01f, flat)) * Mathf.Rad2Deg);
        }

        // One frame of a circling shot: `angle` degrees round the centre, `dist` out, the camera standing on the floor
        // (`lift` raises it), looking at `lookY` above the floor.
        void Orbit(float angle, float dist, float lookY, float lift = 0f)
        {
            float r = angle * Mathf.Deg2Rad;
            Vector3 from = StudioAt + new Vector3(Mathf.Sin(r) * dist, 0.02f + lift, -Mathf.Cos(r) * dist);
            AimCamera(from, StudioAt + Vector3.up * lookY);
        }

        // It walks a circle round the turntable (at `speed` m/s), then back to the middle; the camera stands and watches.
        IEnumerator StudioWalk(Monster it, float speed)
        {
            Log("studio: walking at " + speed.ToString("0.0") + " m/s, from frame " + filmFrames);
            const float R = 1.7f;
            float a = 0f;
            AimCamera(StudioAt + new Vector3(0f, 0.02f, -4.4f), StudioAt + Vector3.up * 0.9f);
            for (float t = 0f; t < 2f * Mathf.PI * R / speed; t += Time.deltaTime)
            {
                a += speed / R * Time.deltaTime;
                Vector3 p = StudioAt + new Vector3(Mathf.Sin(a) * R, 0f, -Mathf.Cos(a) * R + 0.3f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                it.transform.SetPositionAndRotation(p, Quaternion.LookRotation(dir));
                Frame();
                yield return null;
            }
            for (float t = 0f; t < 1f; t += Time.deltaTime) { Frame(); yield return null; }
            it.transform.SetPositionAndRotation(StudioAt, Quaternion.identity);
            yield return null;
        }

        IEnumerator StudioFilm(StringBuilder summary)
        {
            yield return StartRunForTest();
            haunt.paused = true;
            var root = BuildStudio();
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.32f);
            Look.Fear(0f);
            player.frozen = true;
            hud.fade = hud.fadeTarget = 0f;
            hud.ClearSubtitles();
            hud.objectives.Clear();
            Time.captureFramerate = 30;

            int[] Pick(int legs)
            {
                var l = fear.Compose(2);
                l[Monster.Shift] = 0;
                l[Monster.Legs] = legs;
                return l;
            }
            var looks = new[] { Pick(Monster.TwoLegs), Pick(Monster.SpiderLegs), Pick(Monster.FourLegs), Pick(Monster.OnHands) };
            for (int i = 0; i < looks.Length; i++) Log("studio: form " + (i + 1) + ": " + fear.DescribeLook(looks[i]));
            var it = Monster.Create(null, looks[0]);
            it.transform.position = StudioAt;
            it.transform.rotation = Quaternion.identity;

            float angle = 0f;
            // Form 1, all the way round.
            for (float t = 0f; t < 6f; t += Time.deltaTime) { angle += 60f * Time.deltaTime; Orbit(angle, 3.4f, 1.0f, 0.1f); Frame(); yield return null; }
            Orbit(0f, 3.4f, 1.0f, 0.1f); yield return null; Shot("studio form 1");
            yield return StudioWalk(it, 1.2f);

            for (int k = 1; k < looks.Length; k++)
            {
                // Each change filmed circling slowly, so every side of the flesh shows as it moves.
                StartCoroutine(it.TransformInto(looks[k], 1f));
                float guard = 0f;
                while ((it.Transforming || guard < 0.2f) && guard < 60f)
                {
                    guard += Time.deltaTime;
                    angle += 14f * Time.deltaTime;
                    Orbit(angle, 3.0f, 0.9f, 0.1f);
                    Frame();
                    yield return null;
                }
                for (float t = 0f; t < 3f; t += Time.deltaTime) { angle += 40f * Time.deltaTime; Orbit(angle, 3.2f, 0.9f, 0.1f); Frame(); yield return null; }
                Orbit(0f, 3.2f, 0.9f, 0.1f); yield return null; Shot("studio form " + (k + 1));
                yield return StudioWalk(it, 1.2f + 0.5f * k);
            }

            // Close: back to the first form, the camera near its head the whole time.
            StartCoroutine(it.TransformInto(looks[0], 1.2f));
            for (float g = 0f; (it.Transforming || g < 0.2f) && g < 60f; g += Time.deltaTime)
            {
                angle += 8f * Time.deltaTime;
                Vector3 head = it.HeadPos;
                float r = angle * Mathf.Deg2Rad;
                Vector3 from = new Vector3(head.x + Mathf.Sin(r) * 1.6f, StudioAt.y + 0.02f, head.z - Mathf.Cos(r) * 1.6f);
                AimCamera(from, head);
                Frame();
                yield return null;
            }
            Shot("studio close");
            Destroy(it.gameObject);

            // The house's furniture, one at a time on the turntable.
            // A sample of the furniture and things the house and the dream draw on, a few of each kind.
            var models = new List<KeyValuePair<string, float>>();
            foreach (var id in new[] { "GothicBed_01", "GothicCabinet_01", "GothicCommode_01", "chinese_cabinet", "painted_wooden_sofa", "Sofa_01",
                                       "mid_century_lounge_chair", "chinese_armchair", "gallinera_chair", "painted_wooden_chair_02", "BarberShopChair_01",
                                       "gothic_coffee_table", "painted_wooden_table", "SchoolDesk_01", "wooden_display_shelves_01", "scandinavian_masonry_heater",
                                       "Chandelier_01", "bull_head", "lion_head", "brass_goblets", "horse_statue_01", "carrot_cake", "metal_tool_chest", "standing_chalkboard_01" })
                models.Add(new KeyValuePair<string, float>(id, Mathf.Min(2.3f, ModelSizes.Height(id, 0.9f))));
            int shown = 0;
            foreach (var kv in models)
            {
                GameObject hit;
                var go = Models.Place(kv.Key, root, Vector3.zero, kv.Value, 0f, out hit, false);
                if (go == null) { Log("studio: model " + kv.Key + " isn't installed"); continue; }
                shown++;
                float look = kv.Value * 0.5f, dist = Mathf.Max(2.2f, kv.Value * 1.7f);
                for (float t = 0f; t < 2.4f; t += Time.deltaTime)
                {
                    go.transform.localRotation = Quaternion.Euler(0f, t * 150f, 0f);
                    Orbit(20f, dist, look, 0f);
                    Frame();
                    yield return null;
                }
                go.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
                yield return null;
                Shot("studio model " + kv.Key);
                Destroy(go);
                yield return null;
            }

            Time.captureFramerate = 0;
            summary.Append("  \"mode\": \"studio\",\n  \"frames\": ").Append(filmFrames).Append(",\n  \"fps\": 30,\n  \"shots\": ").Append(shots.Count)
                   .Append(",\n  \"models_shown\": ").Append(shown).Append(",\n");
        }
    }
}
