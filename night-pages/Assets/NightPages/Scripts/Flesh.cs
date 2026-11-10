using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// Turns the monster's skeleton of simple shapes into one continuous body. Every capsule, sphere, box and
    /// cylinder that shares the body's material becomes a signed-distance field; the fields are melted together
    /// with a smooth union (so a limb grows out of the torso instead of being stuck on it, ribs push up under the
    /// skin, joints swell), roughened with a little noise, and meshed with marching tetrahedra. The mesh is then
    /// skinned to the shapes it came from, so when an arm swings or the snake coils, the flesh bends with it.
    /// The shapes stay as the bones; only their renderers are hidden.
    /// </summary>
    public static class Flesh
    {
        enum Kind { Capsule, Sphere, Box, Cylinder }

        class Prim
        {
            public Transform bone;
            public Kind kind;
            public Matrix4x4 toLocal;       // rig space -> the primitive's unit space
            public float scale;             // unit-space distance -> metres (smallest axis)
            public Bounds box;              // in rig space, grown by the blend distance
            public float inflate;           // metres added all round (thin parts, so the grid doesn't break them up)
        }

        /// <summary>Small parts that are never melted into the body (eyes, teeth, mouths...).</summary>
        public static bool IsDetail(string name) { return Detail.Contains(name); }

        static readonly HashSet<string> Detail = new HashSet<string>
        {
            "Eye", "Tooth", "Needle", "Socket", "Mouth", "Grin", "Nose", "Pompom", "Ruff", "Hole", "Rim", "Crawler", "Collar"
        };

        /// <summary>
        /// Give `rig` a body. Every renderer under it that uses `mat` and is big enough is melted into one skinned
        /// mesh with that material; those renderers are switched off. Returns the new renderer, or null if there
        /// was nothing to melt.
        /// </summary>
        public static SkinnedMeshRenderer Skin(Transform rig, Material mat, float cell, float blend, float rough, string name)
        {
            var prims = new List<Prim>();
            var hidden = new List<Renderer>();
            var rigFromWorld = rig.worldToLocalMatrix;
            foreach (var r in rig.GetComponentsInChildren<Renderer>())
            {
                if (r.sharedMaterial != mat || r is SkinnedMeshRenderer || Detail.Contains(r.gameObject.name)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Kind k;
                string mesh = mf.sharedMesh.name;
                if (mesh.StartsWith("Capsule")) k = Kind.Capsule;
                else if (mesh.StartsWith("Sphere")) k = Kind.Sphere;
                else if (mesh.StartsWith("Cube")) k = Kind.Box;
                else if (mesh.StartsWith("Cylinder")) k = Kind.Cylinder;
                else continue;
                var t = r.transform;
                Vector3 ls = t.lossyScale;
                float smallest = Mathf.Min(Mathf.Abs(ls.x), Mathf.Min(Mathf.Abs(ls.y), Mathf.Abs(ls.z))) / Mathf.Max(0.0001f, rig.lossyScale.y);
                if (smallest < cell * 1.3f) continue;          // too thin for the grid: fingers stay as they are
                var p = new Prim
                {
                    bone = t,
                    kind = k,
                    toLocal = t.worldToLocalMatrix * rig.localToWorldMatrix,
                    scale = smallest,
                };
                var b = r.bounds;                              // world space
                var c = rigFromWorld.MultiplyPoint3x4(b.center);
                var e = rigFromWorld.MultiplyVector(b.extents);
                p.box = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);
                p.box.Expand(blend * 2.5f + cell * 2f);
                prims.Add(p);
                hidden.Add(r);
            }
            if (prims.Count == 0) return null;

            // The grid covers every primitive.
            Bounds all = prims[0].box;
            foreach (var p in prims) all.Encapsulate(p.box);
            Vector3 origin = all.min;
            int nx = Mathf.CeilToInt(all.size.x / cell) + 1, ny = Mathf.CeilToInt(all.size.y / cell) + 1, nz = Mathf.CeilToInt(all.size.z / cell) + 1;
            if ((long)nx * ny * nz > 900000)
            {
                cell *= Mathf.Pow((long)nx * ny * nz / 900000f, 1f / 3f);
                nx = Mathf.CeilToInt(all.size.x / cell) + 1; ny = Mathf.CeilToInt(all.size.y / cell) + 1; nz = Mathf.CeilToInt(all.size.z / cell) + 1;
            }
            var field = new float[nx * ny * nz];
            for (int i = 0; i < field.Length; i++) field[i] = 1f;

            // Melt each primitive into the field, only near itself.
            foreach (var p in prims)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt((p.box.min.x - origin.x) / cell)), x1 = Mathf.Min(nx - 1, Mathf.CeilToInt((p.box.max.x - origin.x) / cell));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((p.box.min.y - origin.y) / cell)), y1 = Mathf.Min(ny - 1, Mathf.CeilToInt((p.box.max.y - origin.y) / cell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((p.box.min.z - origin.z) / cell)), z1 = Mathf.Min(nz - 1, Mathf.CeilToInt((p.box.max.z - origin.z) / cell));
                for (int z = z0; z <= z1; z++)
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var q = origin + new Vector3(x * cell, y * cell, z * cell);
                            int i = (z * ny + y) * nx + x;
                            field[i] = SMin(field[i], Dist(p, q), blend);
                        }
            }
            // Flesh isn't smooth: a little lumpiness, the same pattern every time for the same body.
            if (rough > 0f)
                for (int z = 0; z < nz; z++)
                    for (int y = 0; y < ny; y++)
                        for (int x = 0; x < nx; x++)
                        {
                            int i = (z * ny + y) * nx + x;
                            if (field[i] > cell * 3f || field[i] < -cell * 3f) continue;
                            float px = origin.x + x * cell, py = origin.y + y * cell, pz = origin.z + z * cell;
                            float n = Mathf.PerlinNoise(px * 11f + pz * 3.1f, py * 11f) + Mathf.PerlinNoise(pz * 13f + 7f, py * 9f + px * 2f) - 1f;
                            field[i] += n * rough;
                        }

            List<Vector3> verts; List<int> tris; Vector3[] normals; Vector2[] uvs;
            if (!Surface(field, origin, cell, nx, ny, nz, Matrix4x4.identity, out verts, out tris, out normals, out uvs)) return null;

            // Skin: each vertex follows the one or two shapes it is closest to.
            var bones = new Transform[prims.Count];
            var bind = new Matrix4x4[prims.Count];
            for (int b = 0; b < prims.Count; b++)
            {
                bones[b] = prims[b].bone;
                bind[b] = prims[b].toLocal;              // mesh (rig space) -> bone space
            }
            var weights = new BoneWeight[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                int b1 = 0, b2 = 0;
                float d1 = float.MaxValue, d2 = float.MaxValue;
                for (int b = 0; b < prims.Count; b++)
                {
                    if (!prims[b].box.Contains(verts[i])) continue;
                    float d = Dist(prims[b], verts[i]);
                    if (d < d1) { d2 = d1; b2 = b1; d1 = d; b1 = b; }
                    else if (d < d2) { d2 = d; b2 = b; }
                }
                if (d1 == float.MaxValue)
                    for (int b = 0; b < prims.Count; b++)
                    {
                        float d = Dist(prims[b], verts[i]);
                        if (d < d1) { d1 = d; b1 = b; }
                    }
                float w2 = d2 == float.MaxValue ? 0f : 0.5f - 0.5f * Mathf.Clamp01((d2 - d1) / Mathf.Max(0.001f, blend * 1.5f));
                weights[i] = new BoneWeight { boneIndex0 = b1, weight0 = 1f - w2, boneIndex1 = b2, weight1 = w2 };
            }

            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.normals = normals;
            m.uv = uvs;
            m.SetTriangles(tris, 0);
            m.boneWeights = weights;
            m.bindposes = bind;
            m.RecalculateBounds();
            m.RecalculateTangents();

            var go = new GameObject(name);
            go.transform.SetParent(rig, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = m;
            smr.bones = bones;
            smr.rootBone = rig;
            smr.sharedMaterial = mat;
            smr.updateWhenOffscreen = true;
            smr.quality = SkinQuality.Bone2;
            foreach (var r in hidden) r.enabled = false;
            return smr;
        }

        // The surface where the field crosses zero (marching tetrahedra, vertices shared along grid edges), with
        // normals from the field and every triangle wound to face out. No Unity objects: safe off the main thread.
        static bool Surface(float[] field, Vector3 origin, float cell, int nx, int ny, int nz, Matrix4x4 uvSpace,
                            out List<Vector3> verts, out List<int> tris, out Vector3[] normals, out Vector2[] uvs)
        {
            verts = new List<Vector3>();
            tris = new List<int>();
            var edgeVert = new Dictionary<long, int>();
            int[][] tets = { new[] { 0, 5, 1, 6 }, new[] { 0, 1, 2, 6 }, new[] { 0, 2, 3, 6 }, new[] { 0, 3, 7, 6 }, new[] { 0, 7, 4, 6 }, new[] { 0, 4, 5, 6 } };
            int[] cx = { 0, 1, 1, 0, 0, 1, 1, 0 }, cy = { 0, 0, 1, 1, 0, 0, 1, 1 }, cz = { 0, 0, 0, 0, 1, 1, 1, 1 };
            var idx = new int[8];
            var val = new float[8];
            for (int z = 0; z < nz - 1; z++)
                for (int y = 0; y < ny - 1; y++)
                    for (int x = 0; x < nx - 1; x++)
                    {
                        bool inside = false, outside = false;
                        for (int c = 0; c < 8; c++)
                        {
                            idx[c] = ((z + cz[c]) * ny + (y + cy[c])) * nx + (x + cx[c]);
                            val[c] = field[idx[c]];
                            if (val[c] < 0f) inside = true; else outside = true;
                        }
                        if (!inside || !outside) continue;
                        foreach (var tet in tets) Tet(tet, idx, val, field, origin, cell, nx, ny, verts, tris, edgeVert);
                    }
            normals = null; uvs = null;
            if (tris.Count == 0) return false;
            normals = new Vector3[verts.Count];
            uvs = new Vector2[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                normals[i] = Gradient(field, verts[i], origin, cell, nx, ny, nz);
                Vector3 v = uvSpace.MultiplyPoint3x4(verts[i]);         // the body's own space, so the skin lands where it does on the body
                uvs[i] = new Vector2((float)System.Math.Atan2(v.x, v.z) / (2f * Mathf.PI) * 2f + 0.5f, v.y * 1.4f);
            }
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(n, normals[tris[i]] + normals[tris[i + 1]] + normals[tris[i + 2]]) < 0f)
                {
                    int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t;
                }
            }
            return true;
        }

        // ---------------------------------------------------------------- one body becoming another

        /// <summary>A body's primitives, measured from `space` (taken on the main thread).</summary>
        static List<Prim> Gather(Transform rig, Transform space, float cell, float blend)
        {
            var prims = new List<Prim>();
            var spaceFromWorld = space.worldToLocalMatrix;
            float sy = Mathf.Max(0.0001f, space.lossyScale.y);
            foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer || Detail.Contains(r.gameObject.name)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Kind k;
                string mesh = mf.sharedMesh.name;
                if (mesh.StartsWith("Capsule")) k = Kind.Capsule;
                else if (mesh.StartsWith("Sphere")) k = Kind.Sphere;
                else if (mesh.StartsWith("Cube")) k = Kind.Box;
                else if (mesh.StartsWith("Cylinder")) k = Kind.Cylinder;
                else continue;
                var t = r.transform;
                Vector3 ls = t.lossyScale;
                float smallest = Mathf.Min(Mathf.Abs(ls.x), Mathf.Min(Mathf.Abs(ls.y), Mathf.Abs(ls.z))) / sy;
                // Thin parts (spider legs, long fingers) are thinner than the grid: thicken them just enough to come
                // through whole, or they'd show up as broken dashes while it changes.
                float inflate = smallest < cell * 1.6f ? Mathf.Max(0f, cell * 0.8f - smallest * 0.25f) : 0f;
                var p = new Prim { bone = null, kind = k, toLocal = t.worldToLocalMatrix * space.localToWorldMatrix, scale = smallest, inflate = inflate };
                // Bounds from the primitive's own corners (renderer bounds aren't updated while it's switched off).
                var corners = new Bounds(spaceFromWorld.MultiplyPoint3x4(t.position), Vector3.zero);
                for (int c = 0; c < 8; c++)
                {
                    var lc = new Vector3((c & 1) == 0 ? -0.5f : 0.5f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -0.5f : 0.5f);
                    corners.Encapsulate(spaceFromWorld.MultiplyPoint3x4(t.TransformPoint(lc)));
                }
                p.box = corners;
                p.box.Expand(blend * 2.5f + cell * 2f + p.inflate * 2f);
                prims.Add(p);
            }
            return prims;
        }

        static float[] Field(List<Prim> prims, Vector3 origin, float cell, int nx, int ny, int nz, float blend)
        {
            var field = new float[nx * ny * nz];
            for (int i = 0; i < field.Length; i++) field[i] = 1f;
            foreach (var p in prims)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt((p.box.min.x - origin.x) / cell)), x1 = Mathf.Min(nx - 1, Mathf.CeilToInt((p.box.max.x - origin.x) / cell));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((p.box.min.y - origin.y) / cell)), y1 = Mathf.Min(ny - 1, Mathf.CeilToInt((p.box.max.y - origin.y) / cell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((p.box.min.z - origin.z) / cell)), z1 = Mathf.Min(nz - 1, Mathf.CeilToInt((p.box.max.z - origin.z) / cell));
                for (int z = z0; z <= z1; z++)
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var q = origin + new Vector3(x * cell, y * cell, z * cell);
                            int i = (z * ny + y) * nx + x;
                            field[i] = SMin(field[i], Dist(p, q), blend);
                        }
            }
            return field;
        }

        /// <summary>
        /// The shapes of a transformation, worked out on a worker thread one after another while it plays: poll Ready
        /// and take each shape with MeshAt(). Every shape also knows where each of its points lies on the next shape
        /// (in its third UV set), so the Becoming shader can slide the flesh smoothly from one to the next.
        /// </summary>
        public class Morphing
        {
            public System.Threading.Tasks.Task task;
            public readonly int steps;
            public Vector3 core;            // where the new body grows out from (in the space it was measured in)
            public float radius;            // how far from there the bodies reach
            public float seconds;           // how long the worker took
            volatile int ready;
            internal readonly List<Vector3>[] verts;
            internal readonly List<int>[] tris;
            internal readonly Vector3[][] normals;
            internal readonly Vector2[][] uvs;
            internal readonly List<Vector3>[] next;
            readonly Mesh[] meshes;

            public Morphing(int steps)
            {
                this.steps = steps;
                verts = new List<Vector3>[steps]; tris = new List<int>[steps]; normals = new Vector3[steps][];
                uvs = new Vector2[steps][]; next = new List<Vector3>[steps]; meshes = new Mesh[steps];
            }

            /// <summary>How many shapes are finished, from the first.</summary>
            public int Ready { get { return ready; } internal set { ready = value; } }
            public bool Done { get { return task == null || task.IsCompleted; } }
            public bool Failed { get { return task == null || task.IsFaulted || (task.IsCompleted && ready == 0); } }

            /// <summary>Shape i as a mesh (made the first time it's asked for), or null if it isn't ready or is empty.</summary>
            public Mesh MeshAt(int i)
            {
                if (i < 0 || i >= steps || i >= ready || verts[i] == null) return null;
                if (meshes[i] != null) return meshes[i];
                var m = new Mesh { name = "Becoming " + i };
                if (verts[i].Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(verts[i]);
                m.normals = normals[i];
                m.uv = uvs[i];
                m.SetUVs(2, next[i] ?? verts[i]);
                m.SetTriangles(tris[i], 0);
                m.RecalculateBounds();
                var bb = m.bounds;
                bb.Expand(0.3f);                                         // it writhes and slides past its own bounds
                m.bounds = bb;
                m.RecalculateTangents();
                meshes[i] = m;
                return m;
            }

            public void Free()
            {
                for (int i = 0; i < meshes.Length; i++) if (meshes[i] != null) { Object.Destroy(meshes[i]); meshes[i] = null; }
            }
        }

        static float SMax(float a, float b, float k) { return -SMin(-a, -b, k); }

        static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>
        /// One body becoming another, the way a shapeshifter in a film does it: the new body grows OUT of the old one,
        /// limbs budding from the core and stretching out (not all at once, each in its own time), while the old body's
        /// limbs draw back into it. For most of the change both are there at once, melted into one thing. The flesh
        /// heaves with slow lumps that rise and sink and never jump between shapes. Shapes come out in order on a worker
        /// thread (see Morphing); the first ones are ready almost at once.
        /// </summary>
        public static Morphing Morph(Transform rigA, Transform rigB, Transform space, float cell, float blend, int steps, float seed)
        {
            var a = Gather(rigA, space, cell, blend);
            var b = Gather(rigB, space, cell, blend);
            var job = new Morphing(steps);
            if (a.Count == 0 || b.Count == 0) return job;
            Bounds all = a[0].box;
            foreach (var p in a) all.Encapsulate(p.box);
            foreach (var p in b) all.Encapsulate(p.box);
            Vector3 origin = all.min;
            int nx = Mathf.CeilToInt(all.size.x / cell) + 1, ny = Mathf.CeilToInt(all.size.y / cell) + 1, nz = Mathf.CeilToInt(all.size.z / cell) + 1;
            if ((long)nx * ny * nz > 360000)
            {
                cell *= Mathf.Pow((long)nx * ny * nz / 360000f, 1f / 3f);
                nx = Mathf.CeilToInt(all.size.x / cell) + 1; ny = Mathf.CeilToInt(all.size.y / cell) + 1; nz = Mathf.CeilToInt(all.size.z / cell) + 1;
            }
            // The core: the middle of the bodies' mass, weighted toward the big shapes (the torso, not a finger).
            Vector3 core = Vector3.zero;
            float wsum = 0f;
            foreach (var list in new[] { a, b })
                foreach (var p in list)
                {
                    float w = p.box.size.x * p.box.size.y * p.box.size.z;
                    core += p.box.center * w;
                    wsum += w;
                }
            core = wsum > 0f ? core / wsum : all.center;
            float R = 0.2f;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? all.min.x : all.max.x, (c & 2) == 0 ? all.min.y : all.max.y, (c & 4) == 0 ? all.min.z : all.max.z);
                R = Mathf.Max(R, (corner - core).magnitude);
            }
            job.core = core;
            job.radius = R;
            Matrix4x4 uvSpace = rigA.worldToLocalMatrix * space.localToWorldMatrix;
            float sd = (seed % 37f) * 0.61f;
            job.task = System.Threading.Tasks.Task.Run(() =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var fa = Field(a, origin, cell, nx, ny, nz, blend);
                var fb = Field(b, origin, cell, nx, ny, nz, blend);
                var f = new float[fa.Length];
                float far = cell * 6f;
                for (int s = 0; s < steps; s++)
                {
                    float t = steps == 1 ? 1f : s / (float)(steps - 1);
                    // The new body grows out early; the old one only starts to draw back once the new is well under way.
                    float rNew = Mathf.Lerp(-0.35f * R, 1.4f * R, Smooth01(t / 0.78f));
                    float rOld = Mathf.Lerp(1.4f * R, -0.35f * R, Smooth01((t - 0.22f) / 0.78f));
                    float boil = Mathf.Sin(Mathf.PI * t);
                    float k = blend * (1f + 1.8f * boil);
                    float ph = t * 4f + sd;                               // lumps drift continuously, never re-rolled
                    for (int z = 0; z < nz; z++)
                        for (int y = 0; y < ny; y++)
                            for (int x = 0; x < nx; x++)
                            {
                                int i = (z * ny + y) * nx + x;
                                float va = fa[i], vb = fb[i];
                                if (s == 0) { f[i] = va; continue; }
                                if (s == steps - 1) { f[i] = vb; continue; }
                                if (va > far && vb > far) { f[i] = Mathf.Min(va, vb); continue; }
                                float px = origin.x + x * cell, py = origin.y + y * cell, pz = origin.z + z * cell;
                                float dx = px - core.x, dy = py - core.y, dz = pz - core.z;
                                float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                                float inv = 1f / Mathf.Max(d, 0.0001f);
                                float ux = dx * inv, uy = dy * inv, uz = dz * inv;
                                // Each direction grows (and withdraws) at its own pace, so limbs come out one by one.
                                float jit = R * 0.22f * (Mathf.Sin(ux * 3.1f + sd) * Mathf.Sin(uy * 2.3f + 1.7f) + 0.6f * Mathf.Sin(uz * 2.7f - sd * 0.5f));
                                float oldF = SMax(va, d + jit - rOld, 0.08f);
                                float newF = SMax(vb, d - jit - rNew, 0.08f);
                                float v = SMin(oldF, newF, k);
                                if (v > -cell * 5f && v < cell * 5f)
                                {
                                    float lump = Mathf.Sin(px * 8.5f + ph) * Mathf.Sin(py * 7.3f - ph * 0.8f) * Mathf.Sin(pz * 9.1f + ph * 1.2f);
                                    v -= lump * 0.032f * boil;
                                }
                                f[i] = v;
                            }
                    List<Vector3> vs; List<int> ts; Vector3[] ns; Vector2[] us;
                    bool any = Surface(f, origin, cell, nx, ny, nz, uvSpace, out vs, out ts, out ns, out us);
                    // Where the last shape's flesh lies on this one: it slides there instead of jumping.
                    if (s > 0 && job.verts[s - 1] != null)
                    {
                        var prev = job.verts[s - 1];
                        var to = new List<Vector3>(prev.Count);
                        float limit = cell * 4f + R * 1.8f / steps;
                        foreach (var p0 in prev)
                        {
                            Vector3 q = p0;
                            if (any)
                                for (int it = 0; it < 3; it++)
                                {
                                    float val = Sample(f, q, origin, cell, nx, ny, nz);
                                    if (Mathf.Abs(val) < cell * 0.05f) break;
                                    q -= Gradient(f, q, origin, cell, nx, ny, nz) * val;
                                }
                            Vector3 move = q - p0;
                            if (move.magnitude > limit) move = move.normalized * limit;
                            to.Add(p0 + move);
                        }
                        job.next[s - 1] = to;
                    }
                    if (any) { job.verts[s] = vs; job.tris[s] = ts; job.normals[s] = ns; job.uvs[s] = us; }
                    job.Ready = s;                                    // shapes before this one are complete, with where they go next
                }
                job.Ready = steps;
                job.seconds = (float)watch.Elapsed.TotalSeconds;
            });
            return job;
        }

        static float SMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        // Distance in metres from a rig-space point to one primitive's surface (negative inside). Unity's
        // primitives in their own space: capsule radius 0.5 between y = +/-0.5; sphere radius 0.5; cube side 1;
        // cylinder radius 0.5, height 2.
        static float Dist(Prim p, Vector3 q)
        {
            Vector3 l = p.toLocal.MultiplyPoint3x4(q);
            float d;
            switch (p.kind)
            {
                case Kind.Capsule:
                    l.y -= Mathf.Clamp(l.y, -0.5f, 0.5f);
                    d = l.magnitude - 0.5f;
                    break;
                case Kind.Sphere:
                    d = l.magnitude - 0.5f;
                    break;
                case Kind.Box:
                {
                    Vector3 a = new Vector3(Mathf.Abs(l.x) - 0.5f, Mathf.Abs(l.y) - 0.5f, Mathf.Abs(l.z) - 0.5f);
                    Vector3 o = new Vector3(Mathf.Max(a.x, 0f), Mathf.Max(a.y, 0f), Mathf.Max(a.z, 0f));
                    d = o.magnitude + Mathf.Min(Mathf.Max(a.x, Mathf.Max(a.y, a.z)), 0f);
                    break;
                }
                default:
                {
                    float r = new Vector2(l.x, l.z).magnitude - 0.5f, h = Mathf.Abs(l.y) - 1f;
                    d = Mathf.Min(Mathf.Max(r, h), 0f) + new Vector2(Mathf.Max(r, 0f), Mathf.Max(h, 0f)).magnitude;
                    break;
                }
            }
            return d * p.scale - p.inflate;
        }

        static Vector3 Gradient(float[] f, Vector3 p, Vector3 origin, float cell, int nx, int ny, int nz)
        {
            float gx = Sample(f, p + Vector3.right * cell, origin, cell, nx, ny, nz) - Sample(f, p - Vector3.right * cell, origin, cell, nx, ny, nz);
            float gy = Sample(f, p + Vector3.up * cell, origin, cell, nx, ny, nz) - Sample(f, p - Vector3.up * cell, origin, cell, nx, ny, nz);
            float gz = Sample(f, p + Vector3.forward * cell, origin, cell, nx, ny, nz) - Sample(f, p - Vector3.forward * cell, origin, cell, nx, ny, nz);
            var g = new Vector3(gx, gy, gz);
            return g.sqrMagnitude > 1e-12f ? g.normalized : Vector3.up;
        }

        // Trilinear sample of the field.
        static float Sample(float[] f, Vector3 p, Vector3 origin, float cell, int nx, int ny, int nz)
        {
            Vector3 g = (p - origin) / cell;
            int x = Mathf.Clamp((int)g.x, 0, nx - 2), y = Mathf.Clamp((int)g.y, 0, ny - 2), z = Mathf.Clamp((int)g.z, 0, nz - 2);
            float fx = Mathf.Clamp01(g.x - x), fy = Mathf.Clamp01(g.y - y), fz = Mathf.Clamp01(g.z - z);
            float c000 = f[(z * ny + y) * nx + x], c100 = f[(z * ny + y) * nx + x + 1];
            float c010 = f[(z * ny + y + 1) * nx + x], c110 = f[(z * ny + y + 1) * nx + x + 1];
            float c001 = f[((z + 1) * ny + y) * nx + x], c101 = f[((z + 1) * ny + y) * nx + x + 1];
            float c011 = f[((z + 1) * ny + y + 1) * nx + x], c111 = f[((z + 1) * ny + y + 1) * nx + x + 1];
            float a = Mathf.Lerp(Mathf.Lerp(c000, c100, fx), Mathf.Lerp(c010, c110, fx), fy);
            float b = Mathf.Lerp(Mathf.Lerp(c001, c101, fx), Mathf.Lerp(c011, c111, fx), fy);
            return Mathf.Lerp(a, b, fz);
        }

        static Vector3 Pos(int i, Vector3 origin, float cell, int nx, int ny)
        {
            int x = i % nx, y = (i / nx) % ny, z = i / (nx * ny);
            return origin + new Vector3(x * cell, y * cell, z * cell);
        }

        static int EdgeVertex(int a, int b, float[] field, Vector3 origin, float cell, int nx, int ny, List<Vector3> verts, Dictionary<long, int> cache)
        {
            if (a > b) { int t = a; a = b; b = t; }
            long key = ((long)a << 32) | (uint)b;
            int v;
            if (cache.TryGetValue(key, out v)) return v;
            float fa = field[a], fb = field[b];
            float t01 = Mathf.Clamp01(fa / (fa - fb));
            verts.Add(Vector3.Lerp(Pos(a, origin, cell, nx, ny), Pos(b, origin, cell, nx, ny), t01));
            v = verts.Count - 1;
            cache[key] = v;
            return v;
        }

        // One tetrahedron of a cell: 0, 1 or 2 triangles where the surface crosses it, wound outward.
        static void Tet(int[] t, int[] idx, float[] val, float[] field, Vector3 origin, float cell, int nx, int ny,
                        List<Vector3> verts, List<int> tris, Dictionary<long, int> cache)
        {
            int mask = 0;
            for (int i = 0; i < 4; i++) if (val[t[i]] < 0f) mask |= 1 << i;
            if (mask == 0 || mask == 15) return;
            int[] g = { idx[t[0]], idx[t[1]], idx[t[2]], idx[t[3]] };
            System.Func<int, int, int> E = (i, j) => EdgeVertex(g[i], g[j], field, origin, cell, nx, ny, verts, cache);
            int inCount = 0;
            for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) inCount++;
            bool flip = inCount > 2;
            if (flip || mask == 9 || mask == 10 || mask == 12) mask = 15 - mask;     // same crossing edges; winding is fixed afterwards
            int a, b, c, d;
            switch (mask)
            {
                case 1: a = 0; b = 1; c = 2; d = 3; break;
                case 2: a = 1; b = 0; c = 3; d = 2; break;
                case 4: a = 2; b = 0; c = 1; d = 3; break;
                case 8: a = 3; b = 0; c = 2; d = 1; break;
                case 3: Quad(E(0, 2), E(0, 3), E(1, 3), E(1, 2), flip, tris); return;
                case 5: Quad(E(0, 1), E(0, 3), E(2, 3), E(2, 1), flip, tris); return;
                case 6: Quad(E(1, 0), E(1, 3), E(2, 3), E(2, 0), flip, tris); return;
                default: return;
            }
            Tri(E(a, b), E(a, c), E(a, d), flip, tris);
        }

        static void Tri(int i, int j, int k, bool flip, List<int> tris)
        {
            if (flip) { tris.Add(i); tris.Add(k); tris.Add(j); }
            else { tris.Add(i); tris.Add(j); tris.Add(k); }
        }

        static void Quad(int i, int j, int k, int l, bool flip, List<int> tris)
        {
            Tri(i, j, k, flip, tris);
            Tri(i, k, l, flip, tris);
        }
    }
}
