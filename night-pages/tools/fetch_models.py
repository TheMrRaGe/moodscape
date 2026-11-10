#!/usr/bin/env python3
"""Download the free 3D models Night Pages uses, from Poly Haven (polyhaven.com). All of them are CC0:
free for any use, including commercial, with no credit required.

    python tools/fetch_models.py

Each model lands in Assets/NightPages/Resources/NPModels/<id>/ as an FBX plus its colour texture. Unity
imports them by itself; the game picks them up by id (see House.cs) and falls back to plain boxes for
any model that isn't there. Only the 1k colour texture is fetched: the normal and roughness maps are
EXR files that Unity would need hand-set import settings for.
"""
import hashlib, json, sys, urllib.request
from pathlib import Path

MODELS = """vintage_grandfather_clock_01 old_bed_frame ClassicNightstand_01 painted_wooden_cabinet_02 wooden_bookshelf_worn
WoodenTable_01 dining_chair_02 round_wooden_table_02 ArmChair_01 Rockingchair_01 sofa_03 potted_plant_02
vintage_electric_kettle drawer_cabinet vintage_wooden_drawer_01 CoffeeTable_01 painted_wooden_stool
vintage_oil_lamp mantel_clock_01 standing_picture_frame_01 standing_picture_frame_02 vintage_radio_transceiver
book_encyclopedia_set_01 ClassicConsole_01 wooden_candlestick electric_stove alarm_clock_01 ceramic_vase_01
antique_ceramic_vase_01""".split()

OUT = Path(__file__).resolve().parent.parent / "Assets" / "NightPages" / "Resources" / "NPModels"
UA = {"User-Agent": "night-pages-model-fetch"}


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r:
        return r.read()


def save(url, path, md5):
    if path.is_file() and hashlib.md5(path.read_bytes()).hexdigest() == md5:
        return 0
    data = get(url)
    if md5 and hashlib.md5(data).hexdigest() != md5:
        raise RuntimeError(f"checksum mismatch for {url}")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return len(data)


def main():
    only = sys.argv[1:] or MODELS
    total = 0
    for mid in only:
        try:
            files = json.loads(get(f"https://api.polyhaven.com/files/{mid}"))
            fbx = files["fbx"]["1k"]["fbx"]
            n = save(fbx["url"], OUT / mid / f"{mid}_1k.fbx", fbx.get("md5"))
            for rel, inc in fbx.get("include", {}).items():
                name = rel.split("/")[-1]
                if "_diff_" in name and name.lower().endswith((".jpg", ".png")):
                    n += save(inc["url"], OUT / mid / "textures" / name, inc.get("md5"))
            total += n
            print(f"ok    {mid}  ({n / 1e6:.1f} MB new)")
        except Exception as e:
            print(f"FAIL  {mid}: {e}")
    print(f"done: {total / 1e6:.1f} MB downloaded into {OUT}")


if __name__ == "__main__":
    main()
