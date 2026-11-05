"""
Re-generate the prompt embeddings baked into index.html.

Edit SCENES, STYLES or STYLE below, then run:
    pip install onnxruntime transformers numpy
    python tools/embed_moods.py

The script downloads the SDXS text encoder (246 MB, cached in ./.models), encodes every prompt
with CLIP, and rewrites the <script id="moodData"> block in index.html. The phone never needs
the text encoder.

What gets embedded:
  moods   E[m]      = enc("<scene m>, <STYLE>")                   fp16      [6, 77, 768]
  styles  D[m][k]   = enc("<scene m>, <style k>, <STYLE>") - E[m]  int8 + one fp16 scale per token
                                                                             [6, 6, 77, 768]
The app paints with  sum_m w_m * (E[m] + sum_k theta[m][k] * D[m][k]), where w comes from your
face and theta is the per-mood style taste it learns from your reactions.
"""
import base64, json, pathlib, re, urllib.request
import numpy as np, onnxruntime as ort
from transformers import CLIPTokenizer

ROOT = pathlib.Path(__file__).resolve().parent.parent
REPO = "https://huggingface.co/skillsafeai/sdxs-512-dreamshaper-onnx-webgpu/resolve/main/"
CACHE = ROOT / ".models"

STYLE = "wide panoramic landscape, painterly digital matte painting, soft volumetric light, rich color, highly detailed, no people"
# Order matters: index.html reads it from the embedded JSON. "night" is driven by closed eyes.
SCENES = {
    "calm": "a still tidal lake at dusk, teal and peach sky, low mist drifting over calm water, distant blue mountains, fireflies over reeds",
    "joy": "a sunlit golden meadow full of wildflowers, bright blue sky with fluffy white clouds, warm afternoon sun, drifting pink petals",
    "surprise": "a vast snowy valley at night under a vivid green and violet aurora, glittering starry sky, a shooting star",
    "anger": "jagged black volcanic mountains under a blood red storm sky, glowing lava rivers, flying embers, forked lightning",
    "sadness": "a grey rainy valley under heavy overcast clouds, rain falling over a misty pine forest, muted cold blue tones",
    "night": "a quiet moonlit landscape at midnight, full moon, deep indigo sky full of stars, silver light on rolling hills",
}
# Style directions the app learns your taste along. Keep them short: scene + style + STYLE must fit 77 tokens.
STYLES = {
    "warm": "golden hour, warm glowing sunlight",
    "cool": "cool blue hour, soft moody light",
    "vivid": "vivid saturated colors, dramatic high contrast",
    "pastel": "soft pastel watercolor painting, dreamy",
    "photo": "photorealistic cinematic photograph, sharp detail",
    "epic": "epic towering scale, vast sweeping vista",
}


def fetch(name):
    path = CACHE / name
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        print("downloading", name)
        urllib.request.urlretrieve(REPO + name, path)
    return path


def b64(a):
    return base64.b64encode(np.ascontiguousarray(a).tobytes()).decode()


def main():
    tok = CLIPTokenizer(str(fetch("tokenizer/vocab.json")), str(fetch("tokenizer/merges.txt")))
    te = ort.InferenceSession(str(fetch("text_encoder.onnx")), providers=["CPUExecutionProvider"])

    def enc(prompt, label):
        ids = tok(prompt, padding="max_length", max_length=77, truncation=True, return_tensors="np").input_ids.astype(np.int64)
        used = int((ids[0] != tok.pad_token_id).sum()) + 1
        if used >= 77:
            print(f"  WARNING {label}: prompt hit the 77-token limit and was cut off")
        return te.run(None, {"input_ids": ids})[0][0], used

    base, deltas = [], []
    for m, scene in SCENES.items():
        e, used = enc(f"{scene}, {STYLE}", m)
        base.append(e)
        row = []
        for k, style in STYLES.items():
            s, used_k = enc(f"{scene}, {style}, {STYLE}", f"{m}+{k}")
            row.append(s - e)
        deltas.append(row)
        print(f"{m:9} {used:2}/77 tokens, with longest style {max(len(tok(f'{scene}, {s}, {STYLE}').input_ids) for s in STYLES.values()):2}/77")

    E = np.stack(base).astype(np.float16)                 # [6, 77, 768]
    D = np.stack([np.stack(r) for r in deltas])           # [6, 6, 77, 768] float32
    scale = np.abs(D).max(axis=-1, keepdims=True) / 127.0  # one scale per (mood, style, token)
    scale[scale == 0] = 1e-8
    Q = np.clip(np.round(D / scale), -127, 127).astype(np.int8)
    err = np.abs(Q * scale - D).mean() / (np.abs(D).mean() + 1e-12)
    print(f"style deltas: mean |delta| {np.abs(D).mean():.4f}, int8 relative error {err * 100:.2f}%")

    payload = json.dumps({
        "order": list(SCENES), "shape": list(E.shape), "dtype": "float16",
        "prompts": {k: f"{v}, {STYLE}" for k, v in SCENES.items()},
        "b64": b64(E),
        "styles": {
            "order": list(STYLES), "prompts": STYLES,
            "shape": list(D.shape), "q_b64": b64(Q), "scale_b64": b64(scale[..., 0].astype(np.float16)),
        },
    })
    html_path = ROOT / "index.html"
    html = html_path.read_text(encoding="utf-8")
    pattern = r'(<script type="application/json" id="moodData">)(.*?)(</script>)'
    if not re.search(pattern, html, flags=re.S):
        raise SystemExit("moodData block not found in index.html")
    html = re.sub(pattern, lambda mt: mt.group(1) + payload + mt.group(3), html, count=1, flags=re.S)
    html_path.write_text(html, encoding="utf-8")
    print(f"wrote {(E.nbytes + Q.nbytes + scale.size * 2) / 1048576:.2f} MB of embeddings into {html_path.name}")


if __name__ == "__main__":
    main()
