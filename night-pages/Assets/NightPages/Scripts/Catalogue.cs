using System.Collections.Generic;
using UnityEngine;

namespace NightPages
{
    /// <summary>
    /// What the house is furnished with, so that no two runs look the same. Every downloaded model has a role (a chair,
    /// a small table, a cabinet, a vase, something on a shelf...). Each run, most pieces of furniture the house asks
    /// for by name are swapped for another of the same role and a similar size (so the layout still works), and every
    /// table, cabinet and shelf top gets a different scatter of small things. The dream maze furnishes its rooms from
    /// the same roles.
    /// Things the story uses (the clock, the wardrobe, the bed, the TV, the phone...) are never swapped.
    /// </summary>
    public static class Catalogue
    {
        public const string Chair = "chair", Armchair = "armchair", Sofa = "sofa", Table = "table", SmallTable = "smalltable",
            Cabinet = "cabinet", Tall = "tall", Shelves = "shelves", Lamp = "lamp", Vase = "vase", Decor = "decor",
            Kitchen = "kitchen", Food = "food", Clutter = "clutter", Stool = "stool", Odd = "odd", Plant = "plant", Head = "head";

        static readonly Dictionary<string, string[]> Roles = new Dictionary<string, string[]>
        {
            { Chair, new[] { "WoodenChair_01", "np_dinette_chair", "dining_chair_02", "painted_wooden_chair_01", "painted_wooden_chair_02", "gallinera_chair", "GreenChair_01", "SchoolChair_01" } },
            { Armchair, new[] { "ArmChair_01", "Rockingchair_01", "chinese_armchair", "mid_century_lounge_chair", "Ottoman_01" } },
            { Sofa, new[] { "sofa_03", "Sofa_01", "sofa_02", "painted_wooden_sofa" } },
            { Table, new[] { "WoodenTable_01", "WoodenTable_03", "wooden_table_02", "painted_wooden_table", "round_wooden_table_01", "round_wooden_table_02", "gallinera_table", "SchoolDesk_01", "dining_table", "np_dinette_table" } },
            { SmallTable, new[] { "CoffeeTable_01", "coffee_table_round_01", "gothic_coffee_table", "chinese_tea_table", "small_wooden_table_01", "side_table_01", "side_table_tall_01", "WoodenTable_02", "ClassicNightstand_01", "painted_wooden_nightstand" } },
            { Cabinet, new[] { "drawer_cabinet", "vintage_wooden_drawer_01", "vintage_cabinet_01", "GothicCommode_01", "painted_wooden_cabinet", "ClassicConsole_01", "metal_tool_chest" } },
            { Tall, new[] { "GothicCabinet_01", "chinese_cabinet", "wooden_display_shelves_01", "Shelf_01", "wooden_bookshelf_worn", "painted_wooden_shelves", "steel_frame_shelves_02" } },
            { Stool, new[] { "painted_wooden_stool", "wooden_stool_01", "folding_wooden_stool", "chinese_stool", "wooden_stool_02" } },
            { Lamp, new[] { "np_table_lamp", "wooden_lantern_01", "vintage_oil_lamp", "Lantern_01", "desk_lamp_arm_01", "brass_candleholders", "wooden_candlestick" } },
            { Vase, new[] { "ceramic_vase_01", "ceramic_vase_02", "ceramic_vase_03", "ceramic_vase_04", "antique_ceramic_vase_01", "brass_vase_01", "brass_vase_02", "brass_vase_03", "metal_jug", "jug_01" } },
            { Decor, new[] { "carved_wooden_elephant", "horse_statue_01", "marble_bust_01", "mantel_clock_01", "standing_picture_frame_01", "standing_picture_frame_02", "brass_goblets", "Camera_01", "vintage_video_camera", "decorative_book_set_01", "vintage_radio_transceiver", "pocket_watch", "magnifying_glass_01", "round_spectacles", "chess_set", "Ukulele_01", "vintage_pocket_watch", "vintage_binocular", "brass_vase_04", "vintage_microscope", "np_rotary_phone" } },
            { Kitchen, new[] { "wooden_bowl_01", "wooden_bowl_02", "pot_enamel_01", "brass_pot_01", "brass_pot_02", "brass_pan_01", "wooden_cutting_board", "jug_01", "tea_set_01", "metal_jug", "can_rusted", "russian_food_cans_01", "CheeseBox_01", "np_percolator", "np_toaster", "np_canisters", "ceramic_pot", "wooden_bucket_02" } },
            { Food, new[] { "food_apple_01", "bananas", "lemon", "yellow_onion", "food_pears_asian_01", "food_pomegranate_01", "croissant", "carrot_cake" } },
            { Clutter, new[] { "cardboard_box_01", "wicker_basket_01", "wicker_basket_02", "vintage_suitcase", "wooden_crate_01", "planter_pot_clay", "bleach_bottle", "all_purpose_cleaner", "multi_cleaner_bottle", "plastic_bottle_gallon", "cleaner_tin_01", "seeding_tray_01", "rubber_boots" } },
            { Plant, new[] { "potted_plant_01", "potted_plant_02", "potted_plant_04", "planter_pot_clay" } },
            { Head, new[] { "bull_head", "horse_head", "lion_head", "dartboard", "fancy_picture_frame_01", "fancy_picture_frame_02" } },
            { Odd, new[] { "BarberShopChair_01", "WetFloorSign_01", "standing_chalkboard_01", "chinese_screen_panels", "Barrel_01", "barrel_03", "scandinavian_masonry_heater", "spinning_wheel_01", "covered_car" } },
        };

        // What the story needs: never swapped.
        static readonly HashSet<string> Keep = new HashSet<string>
        {
            "vintage_grandfather_clock_01", "painted_wooden_cabinet_02", "old_bed_frame", "Television_01", "vintage_telephone_wall_clock",
            "filmstrip_projector_8mm", "wheelchair_01", "vintage_day_bed", "electric_stove", "potted_plant_02", "vintage_electric_kettle",
            "ornate_mirror_01", "standing_picture_frame_01",
        };

        // Too modern, or too strange, for the house itself: only the dream uses these.
        static readonly HashSet<string> DreamOnly = new HashSet<string>
        {
            "SchoolChair_01", "SchoolDesk_01", "metal_tool_chest", "mid_century_lounge_chair", "BarberShopChair_01", "steel_frame_shelves_02", "GreenChair_01",
            // Not an American farmhouse of the 50s to 70s (the era pass): only the dream borrows them.
            "chinese_armchair", "chinese_tea_table", "chinese_stool", "chinese_cabinet", "chinese_screen_panels", "GothicCommode_01", "GothicCabinet_01",
            "GothicBed_01", "gothic_coffee_table", "lion_head", "bull_head", "WetFloorSign_01", "standing_chalkboard_01", "scandinavian_masonry_heater",
        };

        static readonly Dictionary<string, string> roleOf = new Dictionary<string, string>();
        // One choice per piece per run: four kitchen chairs are the same four chairs.
        static readonly Dictionary<string, string> chosen = new Dictionary<string, string>();
        static System.Random rng = new System.Random(1);
        public static int Swapped { get; private set; }

        /// <summary>A new run: a new set of choices.</summary>
        public static void NewRun(int seed)
        {
            rng = new System.Random(seed);
            Swapped = 0;
            chosen.Clear();
            if (roleOf.Count == 0)
                foreach (var kv in Roles) foreach (var id in kv.Value) if (!roleOf.ContainsKey(id)) roleOf[id] = kv.Key;
        }

        static bool Installed(string id) { return Models.Load(id) != null; }

        static Vector3 Size(string id) { Vector3 v; return ModelSizes.All.TryGetValue(id, out v) ? v : Vector3.zero; }

        /// <summary>
        /// The piece to use where the house asks for `id`: usually another of the same role and about the same size;
        /// sometimes the one asked for. Anything the story needs, or without a role, comes back unchanged.
        /// </summary>
        public static string Swap(string id)
        {
            if (id == null || Keep.Contains(id)) return id;
            if (roleOf.Count == 0) NewRun(Random.Range(1, int.MaxValue));
            string role;
            string done;
            if (chosen.TryGetValue(id, out done)) return done;
            if (!roleOf.TryGetValue(id, out role) || rng.NextDouble() < 0.25) { chosen[id] = id; return id; }
            Vector3 want = Size(id);
            var fits = new List<string>();
            foreach (var other in Roles[role])
            {
                if (other == id || DreamOnly.Contains(other) || !Installed(other)) continue;
                Vector3 s = Size(other);
                if (want != Vector3.zero && s != Vector3.zero)
                {
                    // About the same footprint and height (the layout around it was planned for the original).
                    float fw = Mathf.Max(s.x, s.z) / Mathf.Max(0.01f, Mathf.Max(want.x, want.z)), fh = s.y / Mathf.Max(0.01f, want.y);
                    if (fw < 0.6f || fw > 1.25f || fh < 0.6f || fh > 1.6f) continue;
                    if (s.x > want.x * 1.1f || s.z > want.z * 1.1f) continue;          // no deeper or wider than the space it goes in
                }
                fits.Add(other);
            }
            if (fits.Count == 0) { chosen[id] = id; return id; }
            Swapped++;
            var pick = fits[rng.Next(fits.Count)];
            chosen[id] = pick;
            return pick;
        }

        /// <summary>Any installed model of a role (for scatters and the dream's rooms), or null.</summary>
        public static string Any(string role, System.Random r = null)
        {
            string[] list;
            if (!Roles.TryGetValue(role, out list)) return null;
            r = r ?? rng;
            for (int tries = 0; tries < 8; tries++)
            {
                var id = list[r.Next(list.Length)];
                if (Installed(id)) return id;
            }
            return null;
        }

        public static float Height(string id, float fallback) { return ModelSizes.Height(id, fallback); }
        public static Vector3 SizeOf(string id) { var s = Size(id); return s == Vector3.zero ? new Vector3(0.5f, 0.8f, 0.5f) : s; }
        public static int Next(int n) { return rng.Next(n); }
        public static bool WasSwapped(string asked, string got) { return asked != got; }
        public static double Roll() { return rng.NextDouble(); }
    }
}
