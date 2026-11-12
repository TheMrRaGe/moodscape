#!/usr/bin/env python3
"""The rest of each Poly Haven model's surface: its normal map and its roughness/metal/AO, so models look
real under the game's lights rather than flat-coloured.

    python tools/fetch_pbr.py [model ids...]

For every model in fetch_assets.MODELS (or the ids given) it saves, beside the colour texture already there:
  textures/<set>_nor_gl_1k.jpg   the normal map (OpenGL convention, as Unity wants)
  textures/<set>_ms_1k.png       metallic in R, ambient occlusion in G, smoothness (1 - roughness) in A,
                                 packed the way Unity's Standard shader reads them
Models.Place picks them up at run time. Missing maps are skipped; the model just stays as it was.
"""
import io, json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import fetch_assets as fa
from PIL import Image


def urls(node, out):
    if isinstance(node, dict):
        if "url" in node and isinstance(node["url"], str):
            out.append(node["url"])
        for v in node.values():
            urls(v, out)
    return out


def one(mid):
    files = json.loads(fa.get(f"https://api.polyhaven.com/files/{mid}"))
    tex = fa.ROOT / "NPModels" / mid / "textures"
    if not tex.is_dir():
        return "no model folder"
    done = []
    for key in ("nor_gl", "arm"):
        if key not in files:
            continue
        for u in urls(files[key], []):
            if "/1k/" not in u or not u.lower().endswith(".jpg"):
                continue
            name = u.split("/")[-1]                    # e.g. tea_set_01_arm_1k.jpg
            if key == "nor_gl":
                dst = tex / name
                if not dst.exists():
                    dst.write_bytes(fa.get(u))
                done.append(name)
            else:
                dst = tex / name.replace("_arm_", "_ms_").replace(".jpg", ".png")
                if not dst.exists():
                    arm = Image.open(io.BytesIO(fa.get(u))).convert("RGB")
                    ao, rough, metal = arm.split()
                    smooth = rough.point(lambda v: 255 - v)
                    zero = Image.new("L", arm.size, 0)
                    Image.merge("RGBA", (metal, ao, zero, smooth)).save(dst, optimize=True)
                done.append(dst.name)
    return ", ".join(done) if done else "no maps"


def main():
    ids = sys.argv[1:] or fa.MODELS
    for mid in ids:
        try:
            print(mid, ":", one(mid), flush=True)
        except Exception as e:
            print(mid, ": FAILED", e, flush=True)


if __name__ == "__main__":
    main()
