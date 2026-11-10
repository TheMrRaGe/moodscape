using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Surfaces for the monster, painted in code: a colour texture and a bump map for each kind of skin, cloth
    /// or shell. Everything tiles, so it wraps around the primitive shapes without seams you'd notice in the dark.
    /// Generated once each, the first time that kind appears.
    /// </summary>
    public static class MonsterSkin
    {
        const int N = 256;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public static void ResetStatics() { cache.Clear(); }

        // ---------------------------------------------------------------- tileable noise

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + s * 982451653;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        static float Noise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int xa = ((x0 % period) + period) % period, ya = ((y0 % period) + period) % period;
            int xb = (xa + 1) % period, yb = (ya + 1) % period;
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int period, int seed, int octaves = 4)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(u, v, period, seed + o * 17) * amp;
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }

        /// <summary>Thin branching lines: 1 on a vein, 0 away from one.</summary>
        static float Veins(float u, float v, int period, int seed, float sharp)
        {
            float r = 1f - Mathf.Abs(2f * Fbm(u, v, period, seed, 3) - 1f);
            return Mathf.Pow(r, sharp);
        }

        /// <summary>Cellular noise, tiling: distance to the nearest and second-nearest cell point, and the nearest cell's id.</summary>
        static void Cells(float u, float v, int period, int seed, out float f1, out float f2, out float id)
        {
            float x = u * period, y = v * period;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            f1 = f2 = 9f; id = 0f;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j;
                    int wx = ((gx % period) + period) % period, wy = ((gy % period) + period) % period;
                    float px = gx + Hash(wx, wy, seed), py = gy + Hash(wx, wy, seed + 1);
                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < f1) { f2 = f1; f1 = d; id = Hash(wx, wy, seed + 2); }
                    else if (d < f2) f2 = d;
                }
        }

        // ---------------------------------------------------------------- building a material

        delegate void Painter(float u, float v, out Color colour, out float height);

        static Material Make(string key, Painter paint, float smooth, float bump, float tiles, float normalStrength = 6f)
        {
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            var px = new Color[N * N];
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Color c; float hh;
                    paint(x / (float)N, y / (float)N, out c, out hh);
                    px[y * N + x] = c;
                    h[y * N + x] = hh;
                }
            var albedo = new Texture2D(N, N, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, name = "Monster " + key };
            albedo.SetPixels(px);
            albedo.Apply(true);
            var normal = Tex.NormalFrom("monster-" + key, N, (u, v) => h[Mathf.Clamp((int)(v * N), 0, N - 1) * N + Mathf.Clamp((int)(u * N), 0, N - 1)], normalStrength);
            m = Build.Mat(Color.white, 0f, smooth);
            m.name = "Monster " + key;
            m.mainTexture = albedo;
            var scale = new Vector2(tiles, tiles);
            m.mainTextureScale = scale;
            Tex.Bump(m, normal, scale, bump);
            cache[key] = m;
            return m;
        }

        static Color Mix(Color a, Color b, float t) { return Color.Lerp(a, b, Mathf.Clamp01(t)); }

        // ---------------------------------------------------------------- the surfaces

        /// <summary>The default: dark, tight, slightly wet skin with veins under it. Not black: you see it's skin.</summary>
        public static Material Hide()
        {
            return Make("hide", (float u, float v, out Color c, out float h) =>
            {
                float mott = Fbm(u, v, 6, 11);
                float vein = Veins(u, v, 4, 21, 9f);
                float fine = Veins(u * 1f, v, 10, 31, 14f);
                float pores = Fbm(u, v, 48, 41, 2);
                Color baseC = Mix(new Color(0.07f, 0.055f, 0.055f), new Color(0.16f, 0.12f, 0.11f), mott * 1.4f - 0.2f);
                c = Mix(baseC, new Color(0.22f, 0.07f, 0.09f), vein * 0.8f + fine * 0.35f);
                c *= 0.85f + pores * 0.3f;
                h = mott * 0.3f + vein * 0.5f + fine * 0.2f - pores * 0.15f;
            }, 0.55f, 1.0f, 2f);
        }

        /// <summary>Pale, mottled skin; for the thing with holes and for faces that should be faces.</summary>
        public static Material Pale(string key, Color tint)
        {
            return Make(key, (float u, float v, out Color c, out float h) =>
            {
                float mott = Fbm(u, v, 5, 51);
                float blotch = Mathf.Clamp01(Fbm(u, v, 3, 61) * 2.2f - 1.1f);
                float vein = Veins(u, v, 5, 71, 22f);
                float pores = Fbm(u, v, 64, 81, 2);
                c = tint * (0.86f + mott * 0.28f);
                c = Mix(c, new Color(tint.r * 0.85f, tint.g * 0.62f, tint.b * 0.62f), blotch * 0.6f);
                c = Mix(c, new Color(0.45f, 0.4f, 0.55f), vein * 0.16f);
                c *= 0.93f + pores * 0.12f;
                h = mott * 0.25f + vein * 0.3f - pores * 0.25f;
            }, 0.32f, 0.6f, 2f);
        }

        /// <summary>Waterlogged: grey-blue-green, marbled with dark veins, glossy.</summary>
        public static Material Drowned()
        {
            return Make("drowned", (float u, float v, out Color c, out float h) =>
            {
                float mott = Fbm(u, v, 4, 91);
                float marble = Veins(u, v, 3, 101, 6f);
                float fine = Veins(u, v, 9, 111, 16f);
                float wrinkle = Fbm(u * 1f, v, 24, 121, 3);
                c = Mix(new Color(0.26f, 0.33f, 0.33f), new Color(0.42f, 0.47f, 0.44f), mott);
                c = Mix(c, new Color(0.09f, 0.13f, 0.2f), marble * 0.75f + fine * 0.4f);
                c = Mix(c, new Color(0.3f, 0.32f, 0.2f), Mathf.Clamp01(Fbm(u, v, 2, 131) * 2.5f - 1.5f));
                h = wrinkle * 0.6f + marble * 0.3f;
            }, 0.86f, 0.9f, 2f, 9f);
        }

        /// <summary>Porcelain, crazed all over with fine cracks; the cracks have dirt in them.</summary>
        public static Material Porcelain()
        {
            return Make("porcelain", (float u, float v, out Color c, out float h) =>
            {
                float f1, f2, id, g1, g2, gid;
                Cells(u, v, 7, 141, out f1, out f2, out id);
                Cells(u, v, 19, 151, out g1, out g2, out gid);
                float wob = (Fbm(u, v, 16, 147, 2) - 0.5f) * 0.12f;          // cracks wander instead of running ruler-straight
                float crack = 1f - Mathf.Clamp01((f2 - f1 + wob) * 55f);
                float fineCrack = (1f - Mathf.Clamp01((g2 - g1 + wob) * 90f)) * 0.35f * Mathf.Clamp01(Fbm(u, v, 3, 157) * 3f - 1.2f);
                float grime = Fbm(u, v, 4, 161);
                c = Mix(new Color(0.9f, 0.86f, 0.8f), new Color(0.78f, 0.72f, 0.64f), grime * 0.8f);
                c = Mix(c, new Color(0.24f, 0.18f, 0.14f), Mathf.Max(crack, fineCrack));
                h = 1f - Mathf.Max(crack, fineCrack) * 0.8f;
            }, 0.78f, 0.7f, 1.5f, 4f);
        }

        /// <summary>White greasepaint over skin: thick in places, rubbed thin in others.</summary>
        public static Material Greasepaint()
        {
            return Make("greasepaint", (float u, float v, out Color c, out float h) =>
            {
                float thin = Mathf.Clamp01(Fbm(u, v, 4, 171) * 2.4f - 1.0f);
                float streak = Fbm(u * 1f, v, 16, 181, 2);
                c = Mix(new Color(0.93f, 0.92f, 0.88f), new Color(0.72f, 0.58f, 0.52f), thin * 0.75f);
                c *= 0.92f + streak * 0.12f;
                h = (1f - thin) * 0.4f + streak * 0.3f;
            }, 0.6f, 0.5f, 1.5f);
        }

        /// <summary>A clown suit gone grey: big faded diamonds, stained.</summary>
        public static Material Harlequin()
        {
            return Make("harlequin", (float u, float v, out Color c, out float h) =>
            {
                float du = Mathf.Repeat(u * 4f, 1f) - 0.5f, dv = Mathf.Repeat(v * 3f, 1f) - 0.5f;
                bool diamond = Mathf.Abs(du) + Mathf.Abs(dv) < 0.5f;
                float weave = (Mathf.Sin(u * N * 1.1f) * Mathf.Sin(v * N * 1.1f)) * 0.5f + 0.5f;
                float stain = Mathf.Clamp01(Fbm(u, v, 4, 191) * 2.6f - 1.4f);
                c = diamond ? new Color(0.52f, 0.12f, 0.11f) : new Color(0.74f, 0.7f, 0.6f);
                c = Mix(c, new Color(0.28f, 0.24f, 0.18f), stain * 0.7f);
                c *= 0.9f + weave * 0.1f;
                h = weave * 0.4f;
            }, 0.08f, 0.5f, 1f, 3f);
        }

        /// <summary>Woven cloth: hospital gown, coat, doll's dress. Stains spread through it.</summary>
        public static Material Cloth(string key, Color colour, float stains, bool tweed = false, bool flowers = false)
        {
            return Make(key, (float u, float v, out Color c, out float h) =>
            {
                float wu = Mathf.Sin(u * N * 1.57f), wv = Mathf.Sin(v * N * 1.57f);
                float weave = (wu > 0f) ^ (wv > 0f) ? 1f : 0f;
                float slub = Fbm(u, v, 32, 201, 2);
                c = colour * (0.85f + weave * 0.1f + slub * 0.1f);
                if (tweed)
                {
                    float herring = Mathf.Repeat(u * 24f + Mathf.Abs(Mathf.Repeat(v * 24f, 2f) - 1f), 1f) < 0.5f ? 1f : 0f;
                    c *= 0.85f + herring * 0.2f;
                    if (Hash((int)(u * N), (int)(v * N), 211) > 0.94f) c = Mix(c, new Color(0.5f, 0.45f, 0.4f), 0.5f);
                }
                if (flowers)
                {
                    float f1, f2, id;
                    Cells(u, v, 10, 221, out f1, out f2, out id);
                    if (f1 < 0.12f) c = Mix(c, id > 0.5f ? new Color(0.75f, 0.62f, 0.55f) : new Color(0.85f, 0.82f, 0.7f), 0.8f);
                }
                float stain = Mathf.Clamp01(Fbm(u, v, 3, 231) * 2.8f - 1.9f + stains);
                c = Mix(c, new Color(c.r * 0.55f, c.g * 0.45f, c.b * 0.35f), stain);
                h = weave * 0.5f + slub * 0.3f;
            }, 0.06f, 0.6f, 3f, 3f);
        }

        /// <summary>Hand-knitted wool: rows of little V stitches. Grandma's cardigan.</summary>
        public static Material Knit()
        {
            return Make("cardigan", (float u, float v, out Color c, out float h) =>
            {
                float col = Mathf.Repeat(u * 32f, 1f), row = Mathf.Repeat(v * 40f, 1f);
                float leg = Mathf.Abs(col - 0.5f) * 2f;                       // 0 in the middle of a column, 1 at its edges
                float vee = 1f - Mathf.Abs(leg - (1f - row)) * 2.5f;           // the slanted legs of a V
                float stitch = Mathf.Clamp01(vee) * (1f - leg * 0.3f);
                float pill = Fbm(u, v, 64, 241, 2);
                float fade = Fbm(u, v, 3, 251);
                c = Mix(new Color(0.4f, 0.24f, 0.27f), new Color(0.55f, 0.37f, 0.4f), stitch * 0.7f + fade * 0.3f);
                c *= 0.9f + pill * 0.15f;
                h = stitch * 0.8f + pill * 0.2f;
            }, 0.04f, 0.9f, 2f, 3f);
        }

        /// <summary>Hair: fine streaks running one way.</summary>
        public static Material Hair(string key, Color colour, float smooth)
        {
            return Make(key, (float u, float v, out Color c, out float h) =>
            {
                float strands = Fbm(u * 1f, v, 64, 261, 2);
                float s2 = Noise(u, v * 0.02f + u * 0f, 128, 271);
                float clump = Fbm(u, v, 4, 281);
                float k = strands * 0.5f + s2 * 0.5f;
                c = colour * (0.6f + k * 0.6f) * (0.85f + clump * 0.3f);
                h = k;
            }, smooth, 0.7f, 2f, 3f);
        }

        /// <summary>Shell: dark, glossy, in plates, with fine bristles. Spiders and things that crawl.</summary>
        public static Material Chitin()
        {
            return Make("chitin", (float u, float v, out Color c, out float h) =>
            {
                float band = Mathf.Repeat(v * 6f, 1f);
                float plate = Mathf.SmoothStep(0f, 1f, band) * (1f - Mathf.SmoothStep(0.85f, 1f, band));
                float bristle = Hash((int)(u * N), (int)(v * N), 291) > 0.9f ? 1f : 0f;
                float mott = Fbm(u, v, 8, 301);
                c = Mix(new Color(0.05f, 0.035f, 0.03f), new Color(0.24f, 0.14f, 0.07f), mott * 0.7f + plate * 0.3f);
                c = Mix(c, new Color(0.55f, 0.45f, 0.3f), bristle * 0.5f);
                h = plate * 0.6f + bristle * 0.4f + mott * 0.2f;
            }, 0.7f, 0.8f, 2f, 5f);
        }

        /// <summary>Overlapping scales, banded, olive and dark brown.</summary>
        public static Material Scales()
        {
            return Make("scales", (float u, float v, out Color c, out float h) =>
            {
                float x = u * 24f, y = v * 32f;
                int row = Mathf.FloorToInt(y);
                float sx = Mathf.Repeat(x + (row % 2) * 0.5f, 1f) - 0.5f, sy = Mathf.Repeat(y, 1f);
                float r = Mathf.Sqrt(sx * sx * 3.2f + (1f - sy) * (1f - sy));    // each scale rounded at its tip
                float scale = Mathf.Clamp01(1f - r);
                float edge = 1f - Mathf.Clamp01(Mathf.Abs(r - 0.95f) * 12f);
                float band = Mathf.Sin(v * Mathf.PI * 2f * 4f + Fbm(u, v, 4, 311) * 4f) * 0.5f + 0.5f;
                c = Mix(new Color(0.12f, 0.12f, 0.06f), new Color(0.34f, 0.31f, 0.16f), band);
                c = Mix(c, new Color(0.88f, 0.8f, 0.55f), Mathf.Clamp01(Fbm(u, v, 6, 321) * 3f - 2.1f));
                c *= 0.7f + scale * 0.45f;
                c = Mix(c, new Color(0.04f, 0.04f, 0.02f), edge * 0.6f);
                h = scale;
            }, 0.62f, 1f, 2f, 5f);
        }

        /// <summary>Mangy fur: streaks of coarse hair, with patches where it has come away to grey skin.</summary>
        public static Material Mange()
        {
            return Make("mange", (float u, float v, out Color c, out float h) =>
            {
                float streak = Fbm(u, v, 96, 331, 2) * 0.6f + Noise(u, v, 160, 341) * 0.4f;
                float bare = Mathf.Clamp01(Fbm(u, v, 4, 351) * 3.2f - 1.9f);
                Color fur = Mix(new Color(0.05f, 0.045f, 0.04f), new Color(0.2f, 0.17f, 0.13f), streak);
                Color skin = new Color(0.32f, 0.27f, 0.26f) * (0.85f + Fbm(u, v, 16, 361) * 0.3f);
                c = Mix(fur, skin, bare);
                h = Mathf.Lerp(streak, 0.2f, bare);
            }, 0.18f, 1f, 3f, 5f);
        }

        /// <summary>The material for a kind of monster's body and head (null where the old flat colour stays).</summary>
        public static void For(int kind, out Material body, out Material head)
        {
            switch (kind)
            {
                case Monster.Holes: body = head = Pale("pale", new Color(0.66f, 0.56f, 0.5f)); break;
                case Monster.Drowned: body = head = Drowned(); break;
                case Monster.Doctor: body = Cloth("gown", new Color(0.55f, 0.66f, 0.62f), 0.35f); head = Pale("pallid", new Color(0.74f, 0.7f, 0.66f)); break;
                case Monster.Her: body = Knit(); head = Hair("grey hair", new Color(0.66f, 0.66f, 0.64f), 0.25f); break;
                case Monster.You: body = Cloth("your coat", new Color(0.2f, 0.22f, 0.3f), 0.1f, true); head = Pale("flesh", new Color(0.66f, 0.52f, 0.44f)); break;
                case Monster.Spider: case Monster.Swarm: body = head = Chitin(); break;
                case Monster.Snake: body = head = Scales(); break;
                case Monster.Hound: body = head = Mange(); break;
                case Monster.Doll: body = Cloth("doll dress", new Color(0.46f, 0.56f, 0.66f), 0.3f, false, true); head = Porcelain(); break;
                case Monster.Clown: body = Harlequin(); head = Greasepaint(); break;
                case Monster.Shadow: body = head = null; break;
                default: body = head = Hide(); break;
            }
        }
    }
}
