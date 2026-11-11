#!/usr/bin/env python3
"""Download the free 3D models and photo textures Night Pages uses, from Poly Haven (polyhaven.com).
Everything there is CC0: free for any use, including commercial, with no credit required.

    python tools/fetch_assets.py

Models land in Assets/NightPages/Resources/NPModels/<id>/ (an FBX plus its colour texture); textures in
Assets/NightPages/Resources/NPTex/<id>/ (colour + normal map, 1k). Unity imports them by itself; the game finds
them by id and falls back to plain shapes and generated textures for anything missing, so it always runs.
Files already downloaded (same checksum) are skipped.
"""
import hashlib, json, sys, urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "Assets" / "NightPages" / "Resources"
UA = {"User-Agent": "night-pages-asset-fetch"}

MODELS = """
vintage_grandfather_clock_01 old_bed_frame ClassicNightstand_01 painted_wooden_cabinet_02 wooden_bookshelf_worn
WoodenTable_01 dining_chair_02 round_wooden_table_02 ArmChair_01 Rockingchair_01 sofa_03 potted_plant_02
vintage_electric_kettle drawer_cabinet vintage_wooden_drawer_01 CoffeeTable_01 painted_wooden_stool
vintage_oil_lamp mantel_clock_01 standing_picture_frame_01 standing_picture_frame_02 vintage_radio_transceiver
book_encyclopedia_set_01 ClassicConsole_01 wooden_candlestick electric_stove alarm_clock_01 ceramic_vase_01
antique_ceramic_vase_01
tea_set_01 wooden_bowl_01 wooden_bowl_02 jug_01 pot_enamel_01 brass_pot_01 wooden_cutting_board wooden_spoon
food_apple_01 bananas lemon yellow_onion wicker_basket_01 wicker_basket_02 decorative_book_set_01 throw_pillows_01
vintage_telephone_wall_clock wall_clock vintage_suitcase round_spectacles pocket_watch postcard_set_01
magnifying_glass_01 vintage_lighter office_notepads chess_set spinning_wheel_01 painted_wooden_shelves
hanging_picture_frame_01 hanging_picture_frame_02 hanging_picture_frame_03 carved_wooden_elephant ceramic_vase_02
brass_candleholders vintage_flashlight rubber_boots fishermans_hat wooden_broom dustpan metal_trash_can
cardboard_box_01 mousetrap wine_bottles_01 painted_wooden_chair_01 side_table_01 vintage_cabinet_01 Ottoman_01
wooden_axe tree_stump_01 tree_stump_02 wooden_picnic_table painted_wooden_bench watering_can_metal_01
garden_gloves_01 rusted_spade_01 wooden_bucket_01 wine_barrel_01 garden_gnome planter_box_01 old_tyre
covered_car wooden_ladder propane_tank stone_fire_pit wooden_crate_01 hatchet trashbag
dead_tree_trunk dead_tree_trunk_02 fern_02 dry_branches_medium_01 rock_moss_set_01 pine_roots
Television_01 ornate_mirror_01 fancy_picture_frame_02 side_table_tall_01 potted_plant_01 potted_plant_04
cassette_player vintage_day_bed ceramic_vase_03 rubber_duck_toy vintage_crutches_01
wheelchair_01 filmstrip_projector_8mm binder_notebook vintage_stapler cigarette_pack small_wooden_table_01
marble_bust_01 carved_wooden_plate brass_vase_01
# (added for variety: more of an old house, and things a dream would put in one)
GothicBed_01 GothicCabinet_01 GothicCommode_01 GreenChair_01 Shelf_01 Sofa_01 WoodenTable_02 WoodenTable_03
Chandelier_01 Chandelier_03 Lantern_01 brass_goblets brass_pan_01 brass_pot_02 brass_vase_02 brass_vase_03
bull_head horse_head lion_head ceramic_vase_04 chinese_armchair chinese_cabinet chinese_screen_panels chinese_stool
chinese_tea_table coffee_table_round_01 dartboard desk_lamp_arm_01 fancy_picture_frame_01 folding_wooden_stool
gallinera_chair gallinera_table gothic_coffee_table lantern_chandelier_01 lightbulb_01 metal_jug painted_wooden_cabinet
painted_wooden_chair_02 painted_wooden_nightstand painted_wooden_sofa painted_wooden_table planter_pot_clay
round_wooden_table_01 scandinavian_masonry_heater sofa_02 standing_chalkboard_01 wooden_display_shelves_01
wooden_stool_01 wooden_stool_02 wooden_table_02 carrot_cake croissant CheeseBox_01 food_pears_asian_01
food_pomegranate_01 can_rusted all_purpose_cleaner bleach_bottle multi_cleaner_bottle Camera_01 Ukulele_01
vintage_video_camera horse_statue_01 metal_tool_chest steel_frame_shelves_02 barrel_03 Barrel_01 BarberShopChair_01
SchoolChair_01 SchoolDesk_01 ceiling_fan mid_century_lounge_chair WetFloorSign_01 plastic_bottle_gallon
russian_food_cans_01 seeding_tray_01 cleaner_tin_01
""".split()

TEXTURES = """
weathered_plank_siding grey_roof_01 dark_brick_wall wood_floor_deck old_wood_floor wood_floor_worn
white_plaster_rough_01 floor_tiles_06 forest_leaves_02 leafy_grass stony_dirt_path brown_mud_leaves_01
pine_bark mossy_rock dirty_carpet rough_pine_door concrete_floor_worn_001
dark_wooden_planks diagonal_parquet laminate_floor_02 plank_flooring plank_flooring_02 marble_01 floor_tiles_02
floor_tiles_08 plastered_wall_04 grey_plaster_03 painted_concrete dark_wood brown_planks_05 castle_brick_02_white
beige_wall_001 broken_wall
""".split()
# Wallpapers from ambientCG (ambientcg.com, also CC0): colour and normal map, saved like the Poly Haven textures.
AMBIENTCG = "Wallpaper001A Wallpaper001B Wallpaper001C Wallpaper002A Wallpaper002B Wallpaper002C".split()


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=180) as r:
        return r.read()


def save(url, path, md5):
    if path.is_file() and (not md5 or hashlib.md5(path.read_bytes()).hexdigest() == md5):
        return 0
    data = get(url)
    if md5 and hashlib.md5(data).hexdigest() != md5:
        raise RuntimeError(f"checksum mismatch for {url}")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return len(data)


def model(mid):
    files = json.loads(get(f"https://api.polyhaven.com/files/{mid}"))
    fbx = files["fbx"]["1k"]["fbx"]
    n = save(fbx["url"], ROOT / "NPModels" / mid / f"{mid}_1k.fbx", fbx.get("md5"))
    for rel, inc in fbx.get("include", {}).items():
        name = rel.split("/")[-1]
        if "_diff_" in name and name.lower().endswith((".jpg", ".png")):
            n += save(inc["url"], ROOT / "NPModels" / mid / "textures" / name, inc.get("md5"))
    return n


def texture(tid):
    files = json.loads(get(f"https://api.polyhaven.com/files/{tid}"))
    n = 0
    for kind, suffix in (("Diffuse", "diff"), ("nor_gl", "nor_gl")):
        f = files[kind]["1k"]["jpg"]
        n += save(f["url"], ROOT / "NPTex" / tid / f"{tid}_{suffix}.jpg", f.get("md5"))
    return n


def ambientcg(aid):
    import io, zipfile
    out = ROOT / "NPTex" / aid
    if (out / f"{aid}_diff.jpg").is_file() and (out / f"{aid}_nor_gl.jpg").is_file():
        return 0
    data = get(f"https://ambientcg.com/get?file={aid}_1K-JPG.zip")
    z = zipfile.ZipFile(io.BytesIO(data))
    out.mkdir(parents=True, exist_ok=True)
    for name in z.namelist():
        if name.endswith("_Color.jpg"):
            (out / f"{aid}_diff.jpg").write_bytes(z.read(name))
        elif name.endswith("_NormalGL.jpg"):
            (out / f"{aid}_nor_gl.jpg").write_bytes(z.read(name))
    return len(data)


def sizes_cs():
    """The real sizes of every model, for Scripts/ModelSizes.cs (width, height, depth in metres)."""
    info = json.loads(get("https://api.polyhaven.com/assets?t=models"))
    lines = []
    for mid in MODELS:
        d = info.get(mid, {}).get("dimensions")
        if d:
            lines.append(f'            {{ "{mid}", new Vector3({d[0] / 1000:.3f}f, {d[2] / 1000:.3f}f, {d[1] / 1000:.3f}f) }},')
    return "\n".join(lines)


def main():
    want = set(sys.argv[1:])
    total, failed = 0, []
    if want == {"--sizes"}:
        print(sizes_cs())
        return
    for kind, ids, fn in (("model", MODELS, model), ("texture", TEXTURES, texture), ("texture", AMBIENTCG, ambientcg)):
        for i in ids:
            if want and i not in want:
                continue
            try:
                n = fn(i)
                total += n
                print(f"ok    {kind:7s} {i}" + (f"  ({n / 1e6:.1f} MB)" if n else ""))
            except Exception as e:
                failed.append(i)
                print(f"FAIL  {kind:7s} {i}: {e}")
    print(f"done: {total / 1e6:.0f} MB downloaded, {len(failed)} failed {failed if failed else ''}")


if __name__ == "__main__":
    main()
