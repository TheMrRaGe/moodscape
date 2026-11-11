"""
Night Pages: paint the game's library of pictures with the Moodscape painter, on this PC's graphics card.

The same one-step painter (SDXS) and text encoder the Mirror uses, run over a list of prompts: portraits for the
walls, old family photographs, wall writing, children's drawings, stains and mould, wallpapers, faces at a window,
paintings for the dream's gallery. Each prompt is painted several times with different seeds, so the game has many
to pick from and no two runs hang the same pictures. Wallpapers and stains are made to repeat without a seam.

    python paint_library.py <out dir>             # e.g. ...\\night-pages\\Assets\\NightPages\\Resources\\NPPainted
    python paint_library.py <out dir> --per 6     # more of each
    python paint_library.py <out dir> --only portraits,writing
    python paint_library.py <out dir> --wishes <np_paint_wishes.json>   # also paint "learned" pictures of what scares this player

The game learns which pictures get to the player (Painted.Watch) and writes np_paint_wishes.json: how much each motif
(eyes, figure, child, face...) is expected to frighten. With --wishes, the strongest motifs are painted together into
a "learned" set the game hangs in preference to the rest.

Every picture is a 512x512 JPEG in <out dir>/<kind>/<name>_<n>.jpg; library.json there lists them with their prompts.
Nothing here is shown or announced: no window, no tunnel, no network beyond fetching a model file it's missing.
"""
import argparse, json, sys, time
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import painter_server as ps          # the painter itself, its model files and the graphics card choice
import mirror as mirror_mod          # the CLIP tokenizer

# kind -> list of (name, prompt). Kept short: CLIP reads 77 tokens at most.
LIBRARY = {
    "portraits": [
        ("pale_woman", "victorian oil portrait of a pale woman, hollow dark eyes, black dress, dark background, cracked varnish, unsettling"),
        ("old_man", "old oil portrait of a gaunt old man staring, sunken cheeks, dim candlelight, dark brown background, cracked paint"),
        ("child", "antique oil portrait of a pale child with a blank expression, faded, dark background, eerie"),
        ("couple", "faded oil painting of a stern married couple, 1900s, dark room, their eyes too dark, eerie"),
        ("faceless", "classical oil portrait of a woman whose face is smeared away, dark background, cracked varnish"),
        ("grandmother", "oil portrait of an elderly woman in a cardigan, thin smile, dim parlour, old varnish, uncanny"),
    ],
    "photos": [
        ("family", "faded 1950s black and white family photograph on a porch, scratched, one face blurred"),
        ("house", "old black and white photograph of a lonely farmhouse at dusk, grainy, scratched"),
        ("children", "faded 1960s photograph of two children in a garden, grainy, a dark figure in the background"),
        ("wedding", "old sepia wedding photograph, stiff couple, faded, water stained"),
        ("woods", "grainy black and white photograph of a dark forest edge at night, flash, something between the trees"),
        ("hall", "old flash photograph of an empty hallway in a house at night, grainy"),
    ],
    "paintings": [
        ("house_night", "dark oil painting of an old house at night with one lit window, ominous, muted colours"),
        ("hallway", "oil painting of a long dark hallway with an open door at the end, gloomy, dim"),
        ("sea", "dark seascape oil painting, grey storm, a small figure on the shore, bleak"),
        ("forest", "oil painting of a black forest at dusk, a pale shape among the trees, muted"),
        ("room", "oil painting of an empty bedroom at night, moonlight, the bed unmade, unsettling"),
        ("field", "oil painting of a grey field with a tall thin figure in the distance, overcast, bleak"),
    ],
    "writing": [
        ("let_me_in", "scrawled black handwriting on dirty cream wallpaper, smeared charcoal letters, frantic"),
        ("count", "rows of tally marks scratched into old plaster wall, hundreds of them, dirty"),
        ("names", "handwritten names scrawled over and over on a stained wall, pencil, frantic"),
        ("red", "red handwriting smeared on a dirty white wall, dripping, frantic scrawl"),
        ("child_writing", "a child's large crooked handwriting in crayon on wallpaper, smudged"),
    ],
    "drawings": [
        ("tall_man", "child's crayon drawing of a family and a tall black figure standing behind them, on paper"),
        ("house", "child's crayon drawing of a house with a face in the window, on lined paper"),
        ("bed", "child's crayon drawing of a bed with long arms reaching from under it, on paper"),
        ("eyes", "child's crayon drawing of many eyes in a dark scribbled room, on paper"),
        ("family", "child's crayon drawing of a family where one person is scribbled out in black, on paper"),
    ],
    "stains": [
        ("mould", "black mould stain on old cream wallpaper, damp, texture, flat"),
        ("water", "brown water stain spreading on a plaster ceiling, texture, flat"),
        ("handprints", "many dirty handprints on a pale wall, smudged, texture, flat"),
        ("seep", "dark wet stain seeping down a wall, streaks, grime, texture, flat"),
    ],
    "wallpapers": [
        ("damask", "faded victorian damask wallpaper pattern, flat texture, muted green"),
        ("floral", "old floral wallpaper pattern, faded roses, yellowed, flat texture"),
        ("stripes", "faded striped wallpaper with small flowers, 1940s, flat texture"),
        ("birds", "vintage wallpaper with small birds and branches, faded, flat texture"),
        ("eyes", "victorian wallpaper pattern made of tiny eyes, faded, flat texture"),
        ("teeth", "ornate damask wallpaper pattern that is made of teeth, faded, flat texture"),
    ],
    "faces": [
        ("window", "pale face pressed against a dark window at night, wide eyes, condensation"),
        ("doorway", "a pale face peering around a dark doorframe, only one eye visible, dark"),
        ("under", "pale eyes looking out from the darkness under a bed, dark"),
    ],
    "flesh": [
        ("rot", "wet black organic tissue texture, veins, glistening, flat"),
        ("skin", "pale wrinkled skin texture, close up, blemishes, flat"),
    ],
}

SEAMLESS = {"wallpapers", "stains", "flesh"}

# Motifs: what a picture is of, for the game's fear learner. A word in the prompt -> a motif tag.
MOTIFS = {
    "eyes": "eyes", "eye": "eyes", "staring": "eyes", "peering": "eyes",
    "figure": "figure", "tall": "figure", "shape": "figure", "something": "figure",
    "child": "child", "children": "child", "child's": "child",
    "face": "face", "faces": "face", "faceless": "faceless", "smeared": "faceless", "blurred": "faceless", "scribbled": "faceless",
    "family": "family", "couple": "family", "wedding": "family", "married": "family",
    "house": "house", "farmhouse": "house", "hallway": "house", "bedroom": "house", "doorway": "house", "window": "house", "door": "house",
    "forest": "woods", "woods": "woods", "trees": "woods", "field": "woods",
    "sea": "water", "seascape": "water", "water": "water", "wet": "water",
    "handwriting": "writing", "names": "writing", "tally": "writing", "letters": "writing",
    "arms": "hands", "handprints": "hands", "reaching": "hands",
    "teeth": "teeth", "skin": "flesh", "tissue": "flesh", "veins": "flesh",
    "old": "old", "victorian": "old", "antique": "old", "1900s": "old", "1950s": "old", "1960s": "old",
}

# The pieces the learned set is made from, per motif: a subject for each kind it can hang as.
WISH_SUBJECTS = {
    "eyes": "too many eyes, all looking out at the viewer",
    "figure": "a tall thin dark figure standing too close",
    "child": "a pale child with a blank stare",
    "face": "a pale face filling the frame, wide eyes",
    "faceless": "a person whose face is smeared away",
    "family": "a stern family, one of them wrong",
    "house": "the inside of an old dark house, a door open",
    "woods": "black trees at night",
    "water": "dark still water",
    "writing": "frantic scrawled writing",
    "hands": "long arms reaching out of the dark",
    "teeth": "a wide smile with too many teeth",
    "flesh": "wet pale skin",
    "old": "faded, cracked and old",
}
WISH_FORMS = {
    "portraits": "victorian oil portrait of {a} and {b}, dark background, cracked varnish, unsettling",
    "photos": "faded old black and white photograph of {a} and {b}, grainy, scratched",
    "paintings": "dark oil painting of {a}, {b}, ominous, muted colours",
    "drawings": "child's crayon drawing of {a} and {b}, on paper",
}


def motifs(prompt):
    words = prompt.lower().replace(",", " ").split()
    return sorted(set(MOTIFS[w] for w in words if w in MOTIFS))


def wished(path, per):
    """The 'learned' prompts: the player's two or three strongest motifs, combined, in each hanging kind."""
    try:
        w = json.loads(Path(path).read_text(encoding="utf-8")).get("motifs", {})
    except Exception as e:
        ps.log(f"paint: no wishes read from {path}: {e}")
        return []
    top = [m for m, v in sorted(w.items(), key=lambda kv: -kv[1]) if m in WISH_SUBJECTS][:3]
    if len(top) < 2:
        return []
    ps.log("paint: learned: this player is most afraid of " + ", ".join(f"{m} ({w[m]:.2f})" for m in top))
    out = []
    for kind, form in WISH_FORMS.items():
        for i, (a, b) in enumerate([(top[0], top[1]), (top[0], top[-1]), (top[1], top[-1])][:2]):
            out.append((kind, f"learned_{a}_{b}", form.format(a=WISH_SUBJECTS[a], b=WISH_SUBJECTS[b]), [a, b, "learned"]))
    return out


def seamless(img):
    """Make a picture repeat without a seam: blend it with itself shifted by half, the seam hidden in the middle."""
    a = np.asarray(img).astype(np.float32)
    h, w = a.shape[:2]
    b = np.roll(np.roll(a, h // 2, axis=0), w // 2, axis=1)
    y = np.abs(np.linspace(-1, 1, h))[:, None]
    x = np.abs(np.linspace(-1, 1, w))[None, :]
    m = np.clip(np.maximum(x, y) * 1.6 - 0.6, 0, 1)[..., None]      # near the edges: the shifted copy (seamless there)
    return Image.fromarray(np.clip(a * (1 - m) + b * m, 0, 255).astype(np.uint8))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--per", type=int, default=4)
    ap.add_argument("--only", default="")
    ap.add_argument("--cpu", action="store_true")
    ap.add_argument("--wishes", default="")
    a = ap.parse_args()
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    only = set(s for s in a.only.split(",") if s)

    providers, gpu = ps.pick_providers(a.cpu)
    painter = ps.Painter(providers, motion=0.0, gpu=gpu)
    tok = mirror_mod.ClipTokenizer(ps.download("tokenizer/vocab.json", 1059962), ps.download("tokenizer/merges.txt", 524619))
    opt = ps.session_options()
    te = ps.ort.InferenceSession(str(ps.download("text_encoder.onnx", 246275486)), opt, providers=providers)
    ps.log(f"paint: painter on {painter.provider} ({painter.gpu}); text encoder on {te.get_providers()[0]}")

    jobs = []
    for kind, items in LIBRARY.items():
        if only and kind not in only:
            continue
        for name, prompt in items:
            jobs.append((kind, name, prompt, motifs(prompt)))
    if a.wishes:
        jobs += [(k, n, p, m) for k, n, p, m in wished(a.wishes, a.per) if not only or k in only]

    listing, made, t0 = [], 0, time.time()
    for kind, name, prompt, tags in jobs:
            (out / kind).mkdir(exist_ok=True)
            ids, n, cut = tok(prompt)
            if cut:
                ps.log(f"paint: WARNING the prompt for {kind}/{name} is cut off at 77 tokens")
            emb = te.run(None, {"input_ids": ids})[0][0].astype(np.float32).ravel()
            for k in range(a.per):
                seed = abs(hash((kind, name, k))) % (2 ** 31)
                x = np.random.default_rng(seed).standard_normal(ps.N_LAT).astype(np.float32)
                img = Image.fromarray(painter._image(emb, x))
                if kind in SEAMLESS:
                    img = seamless(img)
                if kind == "photos":
                    img = img.convert("L").convert("RGB").filter(ImageFilter.GaussianBlur(0.6))     # old film: grey and soft
                path = out / kind / f"{name}_{k}.jpg"
                img.save(path, quality=88)
                listing.append({"kind": kind, "name": f"{name}_{k}", "n": k, "file": f"{kind}/{name}_{k}.jpg", "prompt": prompt, "seed": seed, "tags": tags})
                made += 1
            ps.log(f"paint: {kind}/{name}: {a.per} painted")
    (out / "library.json").write_text(json.dumps({"made": time.strftime("%Y-%m-%d %H:%M"), "pictures": listing}, indent=1), encoding="utf-8")
    ps.log(f"paint: done: {made} pictures in {time.time() - t0:.0f} s -> {out}")


if __name__ == "__main__":
    main()
