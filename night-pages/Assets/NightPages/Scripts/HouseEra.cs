using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// The era pass: what a farmhouse lived in from the 1950s to the 70s actually had, room by room, from period
    /// photographs and time-capsule houses (a black rotary phone on the hall table, a chrome-and-Formica dinette,
    /// a percolator and a canister set on the worktop, cast-iron radiators under the windows, a cedar chest at the
    /// foot of the bed, a floor lamp with a pleated shade by the armchair, a magazine pile, the TV cabinet).
    ///
    /// It's a generator, not a fixed layout: each recipe line is a chance, and where each thing goes is found in this
    /// house as it was built: a clear stretch of wall away from the doors, the top of the piece it belongs on. So
    /// no two houses are dressed the same, but every one reads as the same family's. All decisions are logged.
    /// </summary>
    public partial class House
    {
        public int eraPlaced;
        readonly List<string> eraLog = new List<string>();

        /// <summary>One line of a room's recipe.</summary>
        struct Era
        {
            public string model, where, on;     // where: "wall" (stands against a wall), "corner", "on" (on top of furniture named `on`)
            public float chance, mass;
            public int count;
            public Era(string model, string where, float chance, string on = null, float mass = 0.5f, int count = 1)
            { this.model = model; this.where = where; this.chance = chance; this.on = on; this.mass = mass; this.count = count; }
        }

        static readonly Dictionary<string, Era[]> Recipes = new Dictionary<string, Era[]>
        {
            { "hall", new[] {
                new Era("np_rotary_phone", "on", 0.95f, "Hall table", 1.2f),
                new Era("np_radiator", "wall", 0.6f),
                new Era("vintage_pocket_watch", "on", 0.25f, "Hall table", 0.1f),
            } },
            { "kitchen", new[] {
                new Era("np_canisters", "on", 0.9f, "Cabinets", 1.5f),
                new Era("np_percolator", "on", 0.85f, "Cabinets", 1.0f),
                new Era("np_toaster", "on", 0.7f, "Cabinets", 1.2f),
                new Era("np_radiator", "wall", 0.5f),
                new Era("metal_stool_01", "wall", 0.4f, null, 4f),
                new Era("wooden_bucket_02", "corner", 0.35f, null, 2f),
                new Era("ceramic_pot", "on", 0.4f, "Kitchen table", 0.8f),
            } },
            { "living room", new[] {
                new Era("np_floor_lamp", "corner", 0.95f, null, 6f),
                new Era("np_table_lamp", "on", 0.7f, "side_table", 2f),
                new Era("np_radiator", "wall", 0.6f),
                new Era("vintage_binocular", "on", 0.3f, "Coffee table", 0.6f),
                new Era("brass_vase_04", "on", 0.35f, "Coffee table", 0.6f),
            } },
            { "bedroom", new[] {
                new Era("np_cedar_chest", "wall", 0.9f, null, 18f),
                new Era("np_table_lamp", "on", 0.85f, "Bedside table", 2f),
                new Era("np_radiator", "wall", 0.5f),
                new Era("vintage_pocket_watch", "on", 0.3f, "Bedside table", 0.1f),
                new Era("treasure_chest", "corner", 0.15f, null, 12f),
            } },
            { "study", new[] {
                new Era("np_table_lamp", "on", 0.6f, "Radio stand", 2f),
                new Era("vintage_microscope", "on", 0.25f, "Radio stand", 2f),
                new Era("np_radiator", "wall", 0.5f),
                new Era("WoodenChair_01", "wall", 0.4f, null, 5f),
            } },
            { "bathroom", new[] {
                new Era("plunger", "corner", 0.6f, null, 0.6f),
                new Era("drain_cleaner", "corner", 0.4f, null, 0.8f),
            } },
        };

        /// <summary>Dress every room from its recipe (seeded like the rest of the house, so Continue builds the same).</summary>
        void BuildEra()
        {
            eraPlaced = 0;
            eraLog.Clear();
            foreach (var r in rooms)
            {
                Era[] recipe;
                if (!Recipes.TryGetValue(r.name, out recipe)) continue;
                foreach (var e in recipe)
                    for (int i = 0; i < e.count; i++)
                    {
                        if (Random.value > e.chance) continue;
                        if (Models.Load(e.model) == null) { eraLog.Add(r.name + ": " + e.model + " isn't installed"); continue; }
                        bool ok = e.where == "on" ? PlaceOn(r, e) : PlaceAgainstWall(r, e, e.where == "corner");
                        if (ok) eraPlaced++;
                    }
            }
            GameDirector.I.Log("house: era pass: " + eraPlaced + " period pieces placed" + (eraLog.Count > 0 ? " (" + string.Join("; ", eraLog.ToArray()) + ")" : ""));
        }

        Vector3 SizeOf(string model) { return ModelSizes.All.ContainsKey(model) ? ModelSizes.All[model] : new Vector3(0.4f, 0.4f, 0.4f); }

        /// <summary>
        /// Stand it against a clear stretch of this room's wall (or tucked into a corner), facing the room, away from
        /// the doors. The spot is found by casting from the middle of the room to the walls and testing the footprint.
        /// </summary>
        bool PlaceAgainstWall(Room r, Era e, bool corner)
        {
            Vector3 size = SizeOf(e.model);
            float w = Mathf.Max(size.x, 0.1f), d = Mathf.Max(size.z, 0.1f), h = Mathf.Max(size.y, 0.1f);
            for (int tries = 0; tries < 60; tries++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (corner) dir = new Vector3(Mathf.Sign(dir.x), 0f, Mathf.Sign(dir.z)).normalized;
                Vector3 from = new Vector3(r.center.x, 0.55f, r.center.z);
                RaycastHit hit;
                if (!Physics.Raycast(from, dir, out hit, 9f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.normal.y) > 0.2f || hit.collider.GetComponentInParent<Movable>() != null) continue;
                if (RoomAt(hit.point - dir * 0.3f) != r) continue;
                // Only real walls (long, tall things), not the side of a cupboard.
                var hb = hit.collider.bounds;
                if (hb.size.y < 2f) continue;
                Vector3 n = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                Vector3 along = Vector3.Cross(Vector3.up, n);
                Vector3 pos = hit.point + n * (d / 2f + 0.04f);
                if (corner)
                {
                    // Slide along the wall into the corner the cast was heading for.
                    float side = Vector3.Dot(dir, along) >= 0f ? 1f : -1f;
                    RaycastHit side2;
                    if (Physics.Raycast(pos + Vector3.up * 0.5f, along * side, out side2, 6f, ~0, QueryTriggerInteraction.Ignore))
                        pos = side2.point - along * side * (w / 2f + 0.06f) - Vector3.up * 0.5f;
                }
                pos.y = 0f;
                var rot = Quaternion.LookRotation(n);
                if (Physics.CheckBox(pos + Vector3.up * (h / 2f + 0.03f), new Vector3(w / 2f, h / 2f, d / 2f) * 0.96f, rot, ~0, QueryTriggerInteraction.Ignore)) continue;
                bool nearDoor = false;
                foreach (var door in doors) if (door != null && Vis.Level(door.Middle - pos).magnitude < 1.05f + w / 2f) nearDoor = true;
                if (nearDoor) continue;
                // Keep a way through the room: not on its own walking line.
                if (Vis.Level(pos - r.center).magnitude < 0.6f) continue;
                GameObject hitbox;
                var made = Models.Place(e.model, transform, transform.InverseTransformPoint(pos), ModelSizes.Height(e.model, h), Models.Face(n.x, n.z) + Lived(1.5f), out hitbox, true, Mathf.Max(w, d) * 1.05f);
                if (made == null) return false;
                hitbox.name = Physical.Label(e.model).Replace("np ", "");
                furniture.Add(hitbox);
                eraLog.Add(r.name + ": " + e.model + (corner ? " in a corner" : " against the wall"));
                return true;
            }
            eraLog.Add(r.name + ": no room for " + e.model);
            return false;
        }

        /// <summary>Put it on the piece of furniture it belongs on (found by name in this room), somewhere clear on top.</summary>
        bool PlaceOn(Room r, Era e)
        {
            GameObject host = null;
            foreach (var f in furniture)
                if (f != null && f.name.Contains(e.on) && RoomAt(f.transform.position) == r) { host = f; break; }
            if (host == null) { eraLog.Add(r.name + ": no " + e.on.ToLowerInvariant() + " for " + e.model); return false; }
            var col = host.GetComponent<Collider>();
            Bounds hb = col != null ? col.bounds : new Bounds(host.transform.position, Vector3.one * 0.5f);
            Vector3 size = SizeOf(e.model);
            for (int tries = 0; tries < 30; tries++)
            {
                float ix = Mathf.Max(0f, hb.extents.x - size.x / 2f - 0.03f), iz = Mathf.Max(0f, hb.extents.z - size.z / 2f - 0.03f);
                Vector3 top = new Vector3(hb.center.x + Random.Range(-ix, ix), hb.max.y + 0.6f, hb.center.z + Random.Range(-iz, iz));
                RaycastHit hit;
                if (!Physics.Raycast(top, Vector3.down, out hit, 1.5f, ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.85f) continue;
                if (hit.point.y < hb.max.y - 0.25f) continue;           // fell past the edge or into a gap
                float yaw = Models.Face(r.center.x - hit.point.x, r.center.z - hit.point.z) + Lived(20f);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                if (Physics.CheckBox(hit.point + Vector3.up * (size.y / 2f + 0.01f), size * 0.45f, rot, ~0, QueryTriggerInteraction.Ignore)) continue;
                var m = ModelsLoose.Place(e.model, transform, transform.InverseTransformPoint(hit.point), ModelSizes.Height(e.model, size.y), yaw, e.mass, Mathf.Max(size.x, size.z) * 1.1f);
                if (m == null) return false;
                m.gameObject.name = Physical.Label(e.model).Replace("np ", "");
                movables.Add(m);
                foreach (var piece in ModelsLoose.LastPieces) movables.Add(piece);
                eraLog.Add(r.name + ": " + e.model + " on the " + e.on.ToLowerInvariant());
                return true;
            }
            eraLog.Add(r.name + ": no clear spot on the " + e.on.ToLowerInvariant() + " for " + e.model);
            return false;
        }
    }
}
