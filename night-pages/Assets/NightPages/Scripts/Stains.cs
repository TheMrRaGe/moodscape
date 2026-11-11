using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Marks on things: made as small textures here (no image files), then laid on a surface as a thin quad.
    /// Dark marks (coffee rings, water stains, grime, finger smudges, mildew, soot, rust streaks, scuffs, mud, shoe
    /// prints) are multiplied into what's under them, so they darken the actual wallpaper or wood rather than
    /// sitting on top as a sticker. Light marks (dust, cobwebs, limescale) are blended over.
    /// Anything laid with a "clean" job fades as it's wiped, scrubbed or swept.
    /// </summary>
    public static class Stains
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        static System.Random R = new System.Random(4242);
        static Material wetSheen;

        public static void ResetStatics() { cache.Clear(); tinted.Clear(); R = new System.Random(4242); wetSheen = null; }

        // ---------------------------------------------------------------- textures

        /// <summary>A mark texture by kind and variant (each variant is a different random draw).</summary>
        public static Texture2D Tex(string kind, int variant = 0)
        {
            string key = kind + variant;
            Texture2D t;
            if (cache.TryGetValue(key, out t) && t != null) return t;
            var r = new System.Random(kind.GetHashCode() * 31 + variant * 977);
            if (kind == "wet")
            {
                // The shape of a puddle as see-through: the blot's dark parts become how solid the shine is.
                var src = Tex("blot", variant).GetPixels();
                var a = new Color[src.Length];
                for (int i = 0; i < src.Length; i++) a[i] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - src[i].r) * 2.2f));
                t = new Texture2D(128, 128, TextureFormat.RGBA32, true) { name = "stain " + key, wrapMode = TextureWrapMode.Clamp };
                t.SetPixels(a);
                t.Apply(true);
                cache[key] = t;
                return t;
            }
            int n = kind == "web" || kind == "dust" ? 256 : 128;
            var px = new Color[n * n];
            bool alpha = kind == "web" || kind == "dust" || kind == "lime";
            for (int i = 0; i < px.Length; i++) px[i] = alpha ? new Color(1f, 1f, 1f, 0f) : Color.white;
            float ox = (float)r.NextDouble() * 50f, oy = (float)r.NextDouble() * 50f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    float ang = Mathf.Atan2(v, u);
                    float nz = Mathf.PerlinNoise(ox + x * 0.06f, oy + y * 0.06f);
                    float fine = Mathf.PerlinNoise(ox + x * 0.25f, oy + y * 0.25f);
                    float dark = 0f;
                    switch (kind)
                    {
                        case "ring":    // a cup's ring: a thin dark rim, a little broken, the inside barely tinted
                        {
                            float rad = 0.62f + (nz - 0.5f) * 0.06f;
                            float rim = Mathf.Exp(-Mathf.Pow((d - rad) / 0.035f, 2f)) * (0.55f + 0.45f * Mathf.PerlinNoise(ox + ang * 3f, oy));
                            dark = rim * 0.85f + (d < rad ? 0.12f * fine : 0f);
                            break;
                        }
                        case "blot":    // a soft irregular stain (water, grease, something spilt and dried)
                        {
                            float edge = 0.62f + (nz - 0.5f) * 0.55f;
                            dark = Mathf.Clamp01((edge - d) / 0.25f) * (0.45f + 0.35f * fine);
                            dark += Mathf.Exp(-Mathf.Pow((d - edge) / 0.04f, 2f)) * 0.35f;      // tide line where it dried
                            break;
                        }
                        case "grime":   // hand-height dirt: diffuse, patchy
                            dark = Mathf.Clamp01(1f - d) * Mathf.Clamp01(nz * 1.6f - 0.35f) * (0.6f + 0.4f * fine);
                            break;
                        case "smudge":  // finger and palm marks around a switch or a handle
                        {
                            for (int k = 0; k < 7; k++)
                            {
                                float cx = (float)(r.NextDouble() * 1.2 - 0.6), cy = (float)(r.NextDouble() * 1.2 - 0.6);
                                float rx = 0.07f + (float)r.NextDouble() * 0.09f, ry = rx * (1.3f + (float)r.NextDouble() * 0.6f);
                                float e = Mathf.Pow((u - cx) / rx, 2f) + Mathf.Pow((v - cy) / ry, 2f);
                                dark = Mathf.Max(dark, Mathf.Clamp01(1f - e) * (0.35f + 0.3f * fine));
                            }
                            r = new System.Random(kind.GetHashCode() * 31 + variant * 977);    // same marks every row
                            break;
                        }
                        case "mildew":  // black speckles, clustered, thicker toward one edge (a corner)
                        {
                            float cluster = Mathf.Clamp01((1.3f - (u + 1f) * 0.5f - (v + 1f) * 0.3f)) * Mathf.Clamp01(nz * 1.8f - 0.4f);
                            float speck = Mathf.PerlinNoise(ox + x * 0.9f, oy + y * 0.9f);
                            dark = speck > 0.62f ? cluster * (speck - 0.5f) * 3f : cluster * 0.15f;
                            break;
                        }
                        case "soot":    // a plume: dark at the bottom, fanning out and fading upward
                        {
                            // Darkest a fifth of the way up (the edges of a mark fade out, so the bottom edge can't hold it).
                            float up = (v + 1f) * 0.5f;
                            float width = 0.2f + up * 0.75f;
                            float rise = up < 0.18f ? up / 0.18f : Mathf.Pow(1f - (up - 0.18f) / 0.82f, 1.2f);
                            dark = Mathf.Clamp01(1f - Mathf.Abs(u) / width) * rise * (0.7f + 0.5f * nz);
                            break;
                        }
                        case "streak":  // drips running down: rust under a tap, damp down a wall
                        {
                            float col = Mathf.PerlinNoise(ox + x * 0.35f, 3.3f);
                            float run = col > 0.55f ? (col - 0.55f) * 2.5f : 0f;
                            float top = (v + 1f) * 0.5f;
                            float fade = top > 0.8f ? (1f - top) / 0.2f : Mathf.Clamp01(top * 1.1f + 0.1f);   // strongest just below the top
                            dark = run * fade * (0.6f + 0.4f * fine) * Mathf.Clamp01(1.4f - Mathf.Abs(u));
                            break;
                        }
                        case "speckle": // ash, crumbs of soot, fly spots: small dark dots, denser in the middle
                        {
                            float dot = Mathf.PerlinNoise(ox + x * 0.7f, oy + y * 0.7f);
                            float dens = Mathf.Clamp01(1f - d) * (0.5f + nz);
                            dark = dot > 0.7f - dens * 0.12f ? dens * 0.9f : dens * 0.12f * fine;
                            break;
                        }
                        case "scuff":   // shoe and chair-back scuffs: short horizontal strokes
                        {
                            float row = Mathf.PerlinNoise(ox + y * 0.5f, oy + x * 0.02f);
                            dark = row > 0.6f ? (row - 0.6f) * 2.2f * Mathf.Clamp01(1f - Mathf.Abs(u) * 0.9f) : 0f;
                            break;
                        }
                        case "shoe":    // a shoe print: sole and heel, treaded
                        {
                            float sole = Mathf.Pow(u / 0.42f, 2f) + Mathf.Pow((v - 0.25f) / 0.62f, 2f);
                            float heel = Mathf.Pow(u / 0.36f, 2f) + Mathf.Pow((v + 0.62f) / 0.3f, 2f);
                            bool inside = sole < 1f || heel < 1f;
                            float tread = Mathf.Sin(y * 0.9f) > 0.2f ? 1f : 0.45f;
                            dark = inside ? tread * (0.5f + 0.5f * fine) : 0f;
                            break;
                        }
                        case "web":     // a cobweb across a corner: spokes and a sagging spiral, white on clear
                        {
                            // The corner the spokes fan out from: variant 0 bottom-left, 1 bottom-right, 2 top-left, 3 top-right.
                            float cu = ((variant & 1) != 0 ? -u : u) + 1f, cv = ((variant & 2) != 0 ? -v : v) + 1f;
                            float rr = Mathf.Sqrt(cu * cu + cv * cv);
                            float a = Mathf.Atan2(cv, cu);
                            float spoke = Mathf.Abs(Mathf.Sin(a * 9f + 0.3f)) < 0.04f / Mathf.Max(0.2f, rr) ? 1f : 0f;
                            float ring = Mathf.Abs(Mathf.Sin(rr * 26f - Mathf.Sin(a * 9f) * 0.6f)) < 0.07f ? 1f : 0f;
                            float alive = Mathf.Clamp01(1.9f - rr) * (0.55f + 0.45f * nz);
                            px[y * n + x] = new Color(0.92f, 0.92f, 0.9f, Mathf.Clamp01((spoke + ring * 0.7f) * alive * 0.8f));
                            continue;
                        }
                        case "dust":    // a grey film, thicker in patches, finger-trails where someone touched it
                        {
                            float a = Mathf.Clamp01(0.35f + (nz - 0.5f) * 0.6f + (fine - 0.5f) * 0.25f);
                            a *= Mathf.Clamp01((1f - Mathf.Max(Mathf.Abs(u), Mathf.Abs(v))) * 6f);
                            px[y * n + x] = new Color(0.62f, 0.6f, 0.56f, a * 0.7f);
                            continue;
                        }
                        case "lime":    // limescale: chalky white crust
                        {
                            float a = Mathf.Clamp01((0.7f - d) * 3f) * Mathf.Clamp01(nz * 1.5f - 0.3f);
                            px[y * n + x] = new Color(0.92f, 0.92f, 0.86f, a * 0.6f);
                            continue;
                        }
                    }
                    float g = 1f - Mathf.Clamp01(dark);
                    px[y * n + x] = new Color(g, g, g, 1f);
                }
            t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "stain " + key, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px);
            t.Apply(true);
            cache[key] = t;
            return t;
        }

        // ---------------------------------------------------------------- laying marks

        /// <summary>
        /// A dark mark on a surface. `normal` points out of the surface; the mark is `w` by `h` metres, turned
        /// `spin` degrees about the normal, tinted (coffee is brown, rust orange, mildew near black).
        /// </summary>
        public static Renderer Mark(Transform parent, Vector3 at, Vector3 normal, float w, float h, string kind, Color tint, float strength = 1f, float spin = 0f, int variant = -1)
        {
            if (variant < 0) variant = R.Next(6);
            var tex = Tex(kind, variant);
            var m = Painted.Decal(Tinted(tex, tint, kind + variant + ColorUtility.ToHtmlStringRGB(tint)), strength, 0.05f);
            if (m == null) return null;
            return Quad("Mark (" + kind + ")", parent, at, normal, w, h, m, spin, 0.0025f);
        }

        /// <summary>A light mark (dust, a cobweb, limescale) blended over the surface.</summary>
        public static Renderer Film(Transform parent, Vector3 at, Vector3 normal, float w, float h, string kind, float alpha = 1f, float spin = 0f, int variant = -1)
        {
            if (variant < 0) variant = R.Next(4);
            // Lit like everything else (a cobweb is grey in the dark, not glowing).
            var m = Build.Mat(Color.white, 0f, 0.05f);
            Work.Fade(m);
            m.mainTexture = Tex(kind, variant);
            m.color = new Color(1f, 1f, 1f, alpha);
            m.renderQueue = 3001;
            return Quad("Film (" + kind + ")", parent, at, normal, w, h, m, spin, 0.003f);
        }

        // Coloured versions of a grey mark: white stays white, the dark parts take the tint.
        static readonly Dictionary<string, Texture2D> tinted = new Dictionary<string, Texture2D>();
        static Texture2D Tinted(Texture2D grey, Color tint, string key)
        {
            if (tint == Color.black || tint == default(Color)) return grey;
            Texture2D t;
            if (tinted.TryGetValue(key, out t) && t != null) return t;
            var px = grey.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                float k = 1f - px[i].r;                                      // how dark
                px[i] = Color.Lerp(Color.white, tint, Mathf.Clamp01(k * 1.6f)) * (1f - k * 0.3f);
                px[i].a = 1f;
            }
            t = new Texture2D(grey.width, grey.height, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px);
            t.Apply(true);
            tinted[key] = t;
            return t;
        }

        static Renderer Quad(string name, Transform parent, Vector3 at, Vector3 normal, float w, float h, Material m, float spin, float lift)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, true);
            normal = normal.normalized;
            // A quad faces -z: look along -normal so its front faces out of the surface.
            Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            q.transform.rotation = Quaternion.AngleAxis(spin, normal) * Quaternion.LookRotation(-normal, up);
            q.transform.position = at + normal * lift;
            q.transform.localScale = new Vector3(w, h, 1f);
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        /// <summary>A wet sheen: a transparent, very glossy film that catches the lamps (puddles, a wet floor).</summary>
        public static Material Sheen()
        {
            if (wetSheen != null) return wetSheen;
            // Not metallic and not mirror-smooth: indoors there's nothing for a mirror to reflect but black.
            wetSheen = Build.Mat(new Color(0.75f, 0.78f, 0.82f, 0.22f), 0f, 0.88f);
            Work.Fade(wetSheen);
            wetSheen.SetFloat("_Metallic", 0f);
            wetSheen.color = new Color(0.75f, 0.78f, 0.82f, 0.22f);
            return wetSheen;
        }

        /// <summary>Make a mark cleanable: a job that fades it (a cloth for most, the broom for floors of grit).</summary>
        public static Work Cleanable(Renderer mark, string verb, string what, float need, params string[] tools)
        {
            if (mark == null) return null;
            var area = mark.gameObject;
            var col = area.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(1f, 1f, 0.15f);
            var w = Work.Make(area, verb, what, WorkMode.Rub, need, tools);
            var mat = mark.material;
            bool decal = mat.HasProperty("_Strength");
            float s0 = decal ? mat.GetFloat("_Strength") : mat.color.a;
            w.progress = p =>
            {
                if (decal) mat.SetFloat("_Strength", s0 * (1f - p));
                else { var c = mat.color; c.a = s0 * (1f - p); mat.color = c; }
            };
            w.removeWhenDone = true;
            return w;
        }
    }
}
