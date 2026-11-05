"""
Moodscape PC painter — live stream.

Runs the SDXS painter on this computer's graphics card and streams a continuously repainted landscape to
your phone. The phone keeps doing the camera, face reading and learning; it sends only its mood numbers
(the prompt mix, the learned style taste and the layout seed) a few times a second, never video. This PC
turns those into frames as fast as its GPU allows and streams them back.

    python painter_server.py              # serve on port 8765 and open a free Cloudflare tunnel
    python painter_server.py --no-tunnel  # local network / localhost only
    python painter_server.py --cpu        # force CPU (very slow; for testing)
    python painter_server.py --max-fps 15 --motion 0.5
    python painter_server.py --qr mirror  # the QR code opens the Mirror instead of Moodscape

Mirror mode (mirror.html + mirror.py): the phone sends face numbers (and, if the viewer agreed, a face
fingerprint); this PC works out who is looking and evolves a painting in real time toward what that person
reacts to. Two editions: Bliss (mirror-bliss.txt) keeps what you light up at, Unease (mirror-unease.txt) keeps
what makes you tense up. "Keep" saves the moment as a still, a seamless living loop and NFT-style metadata in
captures/.

Every decision is printed: GPU backend and name, where the model came from, measured frame rate, each
phone that connects, and per-connection stream stats.
"""
import argparse, asyncio, base64, io, json, math, os, platform, re, shutil, struct, subprocess, sys, threading, time, urllib.request
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from urllib.parse import urlparse

import numpy as np
import onnxruntime as ort
from aiohttp import web, WSMsgType
from PIL import Image

import mirror as mirror_mod

CAPTURES = Path(__file__).resolve().parent / "captures"
KEEP_FILES = {"still.png": "image/png", "loop.webp": "image/webp", "metadata.json": "application/json"}

HERE = Path(__file__).resolve().parent
APP_DIR = HERE.parent                     # the repo root holds index.html, manifest.json, icons
MODEL_DIR = HERE / "models"
BIN_DIR = HERE / "bin"
REPO = "https://huggingface.co/skillsafeai/sdxs-512-dreamshaper-onnx-webgpu/resolve/main/"
APP_URL = "https://raw.githack.com/TheMrRaGe/moodscape/main/index.html"  # the app's permanent address
FILES = {"unet": ("unet.onnx", 631766742), "vae": ("vae_decoder.onnx", 2467568)}
SIGMA = 14.614646911621094
N_EMB, N_LAT = 77 * 768, 4 * 64 * 64
BLOCKED = {"pc-painter", ".git", "tools", ".github", ".models"}
ALLOWED_ORIGIN_SUFFIXES = (".githack.com", ".trycloudflare.com")


def log(msg):
    print(time.strftime("%H:%M:%S"), msg, flush=True)


# ---------------------------------------------------------------- model + GPU

def download(name, size):
    path = MODEL_DIR / name
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and path.stat().st_size == size:
        log(f"model: {name} already downloaded")
        return path
    log(f"model: downloading {name} ({size / 2**20:.0f} MB) from Hugging Face…")
    tmp = path.with_suffix(".part")
    with urllib.request.urlopen(REPO + name) as r, open(tmp, "wb") as f:
        got, last = 0, 0
        while chunk := r.read(1 << 20):
            f.write(chunk); got += len(chunk)
            if got - last > 50 * 2**20 or got == size:
                last = got; log(f"model:   {got / 2**20:.0f} / {size / 2**20:.0f} MB")
    if tmp.stat().st_size != size:
        raise SystemExit(f"model: {name} download was incomplete ({tmp.stat().st_size} of {size} bytes); run again")
    tmp.replace(path)
    return path


def gpu_name():
    try:
        if shutil.which("nvidia-smi"):
            out = subprocess.run(["nvidia-smi", "--query-gpu=name,memory.total", "--format=csv,noheader"], capture_output=True, text=True, timeout=10).stdout.strip()
            if out: return out.splitlines()[0]
        if platform.system() == "Windows":
            out = subprocess.run(["powershell", "-NoProfile", "-Command", "(Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name) -join ', '"],
                                 capture_output=True, text=True, timeout=15).stdout.strip()
            if out: return out
        if platform.system() == "Darwin":
            out = subprocess.run(["system_profiler", "SPDisplaysDataType"], capture_output=True, text=True, timeout=15).stdout
            m = re.search(r"Chipset Model: (.+)", out)
            if m: return m.group(1).strip()
        if shutil.which("lspci"):
            out = subprocess.run(["lspci"], capture_output=True, text=True, timeout=10).stdout
            m = re.findall(r"(?:VGA|3D|Display)[^:]*: (.+)", out)
            if m: return ", ".join(m)
    except Exception:
        pass
    return "unknown GPU"


def pick_providers(force_cpu):
    avail = ort.get_available_providers()
    log(f"gpu: onnxruntime {ort.__version__} offers {', '.join(avail)}")
    if force_cpu:
        log("gpu: --cpu given, using CPU")
        return ["CPUExecutionProvider"]
    for p in ["CUDAExecutionProvider", "DmlExecutionProvider", "CoreMLExecutionProvider", "ROCMExecutionProvider"]:
        if p in avail:
            log(f"gpu: using {p}")
            return [p, "CPUExecutionProvider"]
    log("gpu: no GPU backend found, falling back to CPU (each frame will take minutes). "
        "On Windows run: pip install onnxruntime-directml   On Linux with NVIDIA: pip install onnxruntime-gpu")
    return ["CPUExecutionProvider"]


def load_embeddings():
    """The mood and style embeddings baked into index.html, so the PC and phone always share one copy."""
    html = (APP_DIR / "index.html").read_text(encoding="utf-8")
    data = json.loads(re.search(r'<script type="application/json" id="moodData">(.*?)</script>', html, re.S).group(1))
    E = np.frombuffer(base64.b64decode(data["b64"]), np.float16).reshape(6, N_EMB).astype(np.float32)
    st = data["styles"]
    q = np.frombuffer(base64.b64decode(st["q_b64"]), np.int8).reshape(6, 6, 77, 768).astype(np.float32)
    sc = np.frombuffer(base64.b64decode(st["scale_b64"]), np.float16).reshape(6, 6, 77, 1).astype(np.float32)
    D = (q * sc).reshape(36, N_EMB)
    log(f"prompts: {len(data['order'])} moods × {len(st['order'])} styles loaded from index.html ({', '.join(data['order'])})")
    return E, D, data["order"], st["order"]


class Painter:
    def __init__(self, providers, motion):
        opt = ort.SessionOptions(); opt.log_severity_level = 3
        t0 = time.time()
        self.unet = ort.InferenceSession(str(download(*FILES["unet"])), opt, providers=providers)
        self.vae = ort.InferenceSession(str(download(*FILES["vae"])), opt, providers=providers)
        self.provider = self.unet.get_providers()[0]
        self.gpu = gpu_name() if self.provider != "CPUExecutionProvider" else "CPU"
        self.E, self.D, self.order, self.styles = load_embeddings()
        self.motion = motion
        self.noise_cache = OrderedDict()
        self.count, self.fps = 0, 0.0
        log(f"painter: sessions ready on {self.provider} ({self.gpu}) in {time.time() - t0:.1f} s")

    def _noise(self, seed):
        if seed not in self.noise_cache:
            rng = np.random.default_rng(seed)
            self.noise_cache[seed] = [rng.standard_normal(N_LAT).astype(np.float32) for _ in range(3)]
            if len(self.noise_cache) > 8: self.noise_cache.popitem(last=False)
        self.noise_cache.move_to_end(seed)
        return self.noise_cache[seed]

    def _run(self, emb, x):
        buf = io.BytesIO(); Image.fromarray(self._image(emb, x)).save(buf, format="JPEG", quality=82)
        return buf.getvalue()

    def _image(self, emb, x):
        sample = (x / np.sqrt(SIGMA ** 2 + 1)).reshape(1, 4, 64, 64).astype(np.float32)
        eps = self.unet.run(None, {"sample": sample, "timestep": np.array([999], np.float32),
                                   "encoder_hidden_states": emb.reshape(1, 77, 768).astype(np.float32)})[0]
        img = self.vae.run(None, {"latent": (x.reshape(1, 4, 64, 64) - SIGMA * eps).astype(np.float32)})[0][0]
        return (np.clip(img / 2 + 0.5, 0, 1).transpose(1, 2, 0) * 255).astype(np.uint8)

    def _living(self, seed, t, m):
        n0, n1, n2 = self._noise(int(seed) & 0x7FFFFFFF)
        a, b = m * 0.10 * np.sin(t * 0.35), m * 0.07 * np.sin(t * 0.23 + 1.3)  # slow rotation in noise space = gentle living motion
        return n0 * np.cos(a) * np.cos(b) + n1 * np.sin(a) + n2 * np.cos(a) * np.sin(b)

    def frame(self, ctl, t):
        """One live frame from a phone's control message. Returns (jpeg, ms)."""
        t0 = time.perf_counter()
        w = np.clip(np.asarray(ctl["w"], np.float32).reshape(6), 0, 1)
        th = np.clip(np.asarray(ctl["th"], np.float32).reshape(6, 6), -0.5, 1.0)
        emb = w @ self.E + (w[:, None] * th).reshape(36) @ self.D
        m = self.motion * float(np.clip(ctl.get("motion", 1.0), 0, 1))
        x = self._living(ctl["seed"], t, m) * SIGMA
        jpeg = self._run(emb, x)
        ms = (time.perf_counter() - t0) * 1000
        self.count += 1
        self.fps = 1000 / ms if self.fps == 0 else self.fps * 0.9 + (1000 / ms) * 0.1
        return jpeg, ms

    def frame_mirror(self, src, dst, m, seed_a, seed_b, s, t):
        """One Mirror frame: melting from painting src to dst (m = 0..1), and from layout seed_a to seed_b (s = 0..1)."""
        t0 = time.perf_counter()
        emb = dst if m >= 1 else src * (1 - m) + dst * m
        if seed_a == seed_b or s >= 1:
            x = self._living(seed_b, t, self.motion)
        else:  # spherical blend of two noise fields keeps the noise statistics right, so the layout morphs instead of cutting
            x = np.cos(s * np.pi / 2) * self._living(seed_a, t, self.motion) + np.sin(s * np.pi / 2) * self._living(seed_b, t, self.motion)
        jpeg = self._run(emb, x * SIGMA)
        ms = (time.perf_counter() - t0) * 1000
        self.count += 1
        self.fps = 1000 / ms if self.fps == 0 else self.fps * 0.9 + (1000 / ms) * 0.1
        return jpeg, ms

    def keepsake(self, emb, seed, frames=48):
        """A seamless living loop of one painting: the noise travels once round a closed path, so the last frame
        flows into the first. Returns (still RGB, [loop RGB frames], ms)."""
        t0 = time.perf_counter()
        n0, n1, n2 = self._noise(int(seed) & 0x7FFFFFFF)
        out = []
        for i in range(frames):
            th = 2 * np.pi * i / frames
            a, b = 0.10 * np.sin(th), 0.07 * np.sin(th + 1.3)
            out.append(self._image(emb, (n0 * np.cos(a) * np.cos(b) + n1 * np.sin(a) + n2 * np.cos(a) * np.sin(b)) * SIGMA))
        return out[0], out, (time.perf_counter() - t0) * 1000

    def paint_raw(self, emb, x):
        """Single painting from a phone that sends its own conditioning and noise (/api/paint)."""
        t0 = time.perf_counter()
        jpeg = self._run(emb, x)
        self.count += 1
        return jpeg, (time.perf_counter() - t0) * 1000

    def benchmark(self):
        log("painter: warming up (first run compiles GPU kernels)…")
        ctl = {"w": [1, 0, 0, 0, 0, 0], "th": [[0] * 6] * 6, "seed": 1}
        _, first = self.frame(ctl, 0)
        log(f"painter: first frame {first:.0f} ms")
        if first > 20000:
            log("painter: too slow to measure a frame rate; this will be a slideshow, not a live stream")
            return 1000 / first
        times = [self.frame(ctl, i * 0.2)[1] for i in range(5)]
        fps = 1000 / (sum(times) / len(times))
        self.fps = fps
        feel = "smooth" if fps >= 15 else "watchable" if fps >= 6 else "choppy (a few frames a second)" if fps >= 2 else "a slow slideshow"
        log(f"painter: about {fps:.1f} frames per second on {self.gpu}, which will look {feel}")
        return fps


# ---------------------------------------------------------------- web server

def origin_ok(request):
    origin = request.headers.get("Origin")
    if not origin:
        return True
    host = (urlparse(origin).hostname or "").lower()
    return (host in ("localhost", "127.0.0.1", request.host.split(":")[0].lower())
            or host.endswith(ALLOWED_ORIGIN_SUFFIXES) or host in ("raw.githack.com", "rawcdn.githack.com"))


@web.middleware
async def cors(request, handler):
    if request.method == "OPTIONS" and request.path.startswith("/api/"):
        resp = web.Response(status=204)
    else:
        resp = await handler(request)
    if request.path.startswith("/api/") and not isinstance(resp, web.WebSocketResponse):
        resp.headers.update({"Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "content-type",
                             "Access-Control-Allow-Methods": "GET, POST, OPTIONS", "Access-Control-Expose-Headers": "X-Paint-Ms",
                             "Access-Control-Max-Age": "600", "Cache-Control": "no-store"})
    return resp


def save_keepsake(info, still, frames):
    """captures/<edition>-<date>-<time>-<place>/: still.png, loop.webp (seamless, 12 fps) and metadata.json in the
    common NFT metadata shape (name, description, image, animation_url, attributes). The recipe (prompt + layout
    seed) is included so the painting can be re-rendered as proof of origin. Nothing about the viewer is saved."""
    slug = re.sub(r"[^a-z0-9]+", "-", " ".join(info["caption"][:2]).lower()).strip("-")[:40] or "moment"
    name = f"{info['edition']}-{time.strftime('%Y%m%d-%H%M%S')}-{slug}"
    d = CAPTURES / name
    d.mkdir(parents=True, exist_ok=True)
    Image.fromarray(still).save(d / "still.png")
    imgs = [Image.fromarray(f) for f in frames]
    imgs[0].save(d / "loop.webp", save_all=True, append_images=imgs[1:], duration=83, loop=0, quality=88, method=4)
    edition = info["edition"].title()
    meta = {
        "name": f"Moodscape Mirror · {edition} · {' · '.join(info['caption'][:2])}",
        "description": (f"A moment from a Moodscape Mirror ({edition} edition): a painting that changes as it watches "
                        f"the face of the person looking at it, evolving toward what {'they light up at' if info['edition'] == 'bliss' else 'unsettles them'}."),
        "image": "still.png",
        "animation_url": "loop.webp",
        "attributes": [{"trait_type": "Edition", "value": edition}]
                      + [{"trait_type": k.title(), "value": v} for k, v in info["labels"].items()]
                      + [{"trait_type": "Layout", "value": str(info["seed"])}],
        "properties": {"prompt": info["prompt"], "layout_seed": info["seed"], "loop_frames": len(frames), "loop_fps": 12,
                       "model": "SDXS-512-DreamShaper, one step (ONNX export skillsafeai/sdxs-512-dreamshaper-onnx-webgpu)",
                       "model_license": "openrail++", "created": time.strftime("%Y-%m-%dT%H:%M:%S%z")},
    }
    (d / "metadata.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    return name


def phone_name(request):
    ua = request.headers.get("User-Agent", "")
    m = re.search(r"\(([^;)]+;[^;)]+)", ua)
    return m.group(1) if m else "a phone"


def make_app(painter, gpu_pool, max_fps, profiles=None):
    async def health(request):
        return web.json_response({"ok": True, "painter": "pc", "stream": True, "mirror": profiles is not None,
                                  "provider": painter.provider, "gpu": painter.gpu,
                                  "host": platform.node(), "fps": round(painter.fps, 1), "paintings": painter.count})

    async def paint(request):
        body = await request.read()
        if len(body) != (N_EMB + N_LAT) * 4:
            return web.json_response({"error": f"expected {(N_EMB + N_LAT) * 4} bytes, got {len(body)}"}, status=400)
        raw = np.frombuffer(body, dtype="<f4")
        if not np.isfinite(raw).all():
            return web.json_response({"error": "non-finite values in request"}, status=400)
        jpeg, ms = await asyncio.get_running_loop().run_in_executor(gpu_pool, painter.paint_raw, raw[:N_EMB].copy(), raw[N_EMB:].copy())
        log(f"paint: single painting in {ms:.0f} ms")
        return web.Response(body=jpeg, content_type="image/jpeg", headers={"X-Paint-Ms": f"{ms:.0f},0"})

    async def stream(request):
        if not origin_ok(request):
            log(f"stream: refused a connection from {request.headers.get('Origin')}")
            return web.Response(status=403, text="origin not allowed")
        ws = web.WebSocketResponse(max_msg_size=1 << 16, heartbeat=20)
        await ws.prepare(request)
        who = phone_name(request)
        log(f"stream: {who} connected")
        st = {"ctl": None, "inflight": 0, "seq": 0, "frames": 0, "since": time.time(), "bytes": 0, "t0": time.time()}
        min_gap = 1.0 / max_fps

        async def render_loop():
            loop = asyncio.get_running_loop()
            last = 0.0
            while not ws.closed:
                ctl = st["ctl"]
                if ctl is None or st["inflight"] >= 2 or time.time() - last < min_gap:
                    await asyncio.sleep(0.004); continue
                last = time.time()
                try:
                    jpeg, ms = await loop.run_in_executor(gpu_pool, painter.frame, ctl, time.time() - st["t0"])
                except Exception as e:
                    log(f"stream: frame failed ({e}); check the control message"); await asyncio.sleep(0.5); continue
                if ws.closed: break
                st["seq"] += 1; st["inflight"] += 1; st["frames"] += 1; st["bytes"] += len(jpeg)
                await ws.send_bytes(struct.pack("<III", st["seq"], int(ctl.get("cid", 0)), int(ms)) + jpeg)
                if time.time() - st["since"] >= 10:
                    dt = time.time() - st["since"]
                    log(f"stream: {st['frames'] / dt:.1f} fps to {who}, {st['bytes'] / dt / 1024:.0f} KB/s, GPU {ms:.0f} ms/frame")
                    st.update(frames=0, bytes=0, since=time.time())

        task = asyncio.create_task(render_loop())
        try:
            async for msg in ws:
                if msg.type != WSMsgType.TEXT:
                    continue
                try:
                    d = json.loads(msg.data)
                except ValueError:
                    continue
                if "ack" in d:
                    st["inflight"] = max(0, st["inflight"] - 1)
                ctl = d.get("ctl")
                if isinstance(ctl, dict) and len(ctl.get("w", [])) == 6 and len(ctl.get("th", [])) == 6 and "seed" in ctl:
                    if st["ctl"] is None:
                        log(f"stream: first control from {who}, streaming")
                    st["ctl"] = ctl
        finally:
            task.cancel()
            log(f"stream: {who} disconnected")
        return ws

    async def mirror_stream(request):
        """The Mirror: face numbers in, a painting of what this person lights up at out."""
        if not origin_ok(request):
            log(f"mirror: refused a connection from {request.headers.get('Origin')}")
            return web.Response(status=403, text="origin not allowed")
        ws = web.WebSocketResponse(max_msg_size=1 << 16, heartbeat=20)
        await ws.prepare(request)
        who = phone_name(request)
        clock = time.monotonic
        sess = mirror_mod.MirrorSession(painter.spaces, profiles, log=log, who=who)
        st = {"inflight": 0, "seq": 0, "frames": 0, "since": time.time(), "bytes": 0, "t0": time.time(), "fps": 0.0, "last": 0.0}
        min_gap = 1.0 / max_fps
        log(f"mirror: {who} connected; {len(profiles.people)} known people")

        async def send_json(obj):
            if not ws.closed:
                await ws.send_str(json.dumps(obj, separators=(",", ":")))

        await send_json({"hello": {"host": platform.node(), "gpu": painter.gpu, "provider": painter.provider.replace("ExecutionProvider", ""),
                                   "fps": round(painter.fps, 1), "encode_ms": round(painter.spaces["bliss"].encode_ms),
                                   "editions": {e: math.prod(sp.sizes) for e, sp in painter.spaces.items()}}})
        await send_json({"people": sess.people()})

        async def render_loop():
            loop = asyncio.get_running_loop()
            last = 0.0
            while not ws.closed:
                # up to 4 frames in flight: over a tunnel each acknowledgement takes a round trip, and 2 capped the stream near 10 fps
                params = sess.frame_params(clock())
                if params is None or st["inflight"] >= 4 or time.time() - last < min_gap:
                    await asyncio.sleep(0.004); continue
                last = time.time()
                try:
                    jpeg, ms = await loop.run_in_executor(gpu_pool, painter.frame_mirror, *params, time.time() - st["t0"])
                except Exception as e:
                    log(f"mirror: frame failed ({e})"); await asyncio.sleep(0.5); continue
                if ws.closed: break
                now = time.time()
                if st["last"]:
                    st["fps"] += (1 / max(1e-3, now - st["last"]) - st["fps"]) * 0.1
                st["last"] = now
                st["seq"] += 1; st["inflight"] += 1; st["frames"] += 1; st["bytes"] += len(jpeg)
                await ws.send_bytes(struct.pack("<III", st["seq"], int(ms), 0) + jpeg)
                if now - st["since"] >= 15:
                    dt = now - st["since"]
                    v = sess.viewer
                    log(f"mirror: {st['frames'] / dt:.1f} fps to {who}, {st['bytes'] / dt / 1024:.0f} KB/s, GPU {ms:.0f} ms/frame; "
                        f"{sess.edition}, looking: {v.name + f' ({v.taste(sess.space).beats} reactions learned)' if v else 'nobody'}")
                    st.update(frames=0, bytes=0, since=now)

        encoding = {"busy": False}

        async def encode(choice):
            sp = sess.space
            try:
                t0 = time.perf_counter()
                emb = await asyncio.get_running_loop().run_in_executor(gpu_pool, sp.encode, choice)
                sp.encode_ms = sp.encode_ms * 0.8 + (time.perf_counter() - t0) * 200
                if sp is sess.space:
                    sess.arrive(choice, emb, clock())
            except Exception as e:
                log(f"mirror: encoding a painting failed ({e!r})")
                await asyncio.sleep(0.5)
            finally:
                encoding["busy"] = False

        async def control_loop():
            last_status = 0.0
            while not ws.closed:
                now = clock()
                try:
                    want = sess.want()
                    if want is not None and not encoding["busy"]:   # a new painting: encode its prompt on the GPU, between frames
                        encoding["busy"] = True
                        asyncio.create_task(encode(want))
                    sess.tick(now)
                    if now - last_status >= 0.25:
                        last_status = now
                        out = sess.status(now); out["fps"] = round(st["fps"], 1)
                        await send_json({"status": out})
                    profiles.save_if_due()
                except Exception as e:
                    log(f"mirror: control step failed ({e!r})")
                await asyncio.sleep(0.05)

        keeping = {"busy": False}

        async def keep():
            """Save what's on screen as a keepsake: still.png, a seamless loop.webp and metadata.json. No face data."""
            info = sess.keep()
            if info is None or keeping["busy"]:
                return
            keeping["busy"] = True
            await send_json({"keeping": True})
            try:
                loop = asyncio.get_running_loop()
                still, frames, ms = await loop.run_in_executor(gpu_pool, painter.keepsake, info["emb"], info["seed"])
                name = await loop.run_in_executor(None, save_keepsake, info, still, frames)
                log(f"mirror: kept '{' · '.join(info['caption'])}' ({info['edition']}, layout {info['seed']}): "
                    f"{len(frames)}-frame loop rendered in {ms / 1000:.1f} s, saved to captures/{name}")
                base = f"/api/capture/{name}/"
                await send_json({"kept": {"name": name, "caption": info["caption"], "edition": info["edition"],
                                          "still": base + "still.png", "loop": base + "loop.webp", "meta": base + "metadata.json"}})
            except Exception as e:
                log(f"mirror: keeping a moment failed ({e!r})")
                await send_json({"kept": {"error": str(e)}})
            finally:
                keeping["busy"] = False

        tasks = [asyncio.create_task(render_loop()), asyncio.create_task(control_loop())]
        try:
            async for msg in ws:
                if msg.type != WSMsgType.TEXT:
                    continue
                try:
                    d = json.loads(msg.data)
                    if not isinstance(d, dict):
                        continue
                    if "ack" in d:
                        st["inflight"] = max(0, st["inflight"] - 1)
                    if "s" in d:
                        sess.on_sample(d["s"], clock())
                    if "id" in d and "cmd" not in d:
                        sess.on_identity(d["id"], clock())
                    if d.get("cmd") == "keep":
                        asyncio.create_task(keep())
                    elif "cmd" in d:
                        if d["cmd"] != "people":
                            sess.command(d, clock())
                        await send_json({"people": sess.people()})
                except Exception as e:
                    log(f"mirror: ignored a bad message from {who} ({e!r})")
        finally:
            for t in tasks:
                t.cancel()
            if sess.viewer is not None:
                sess.viewer.last_seen = time.time()
            profiles.save(force=True)
            log(f"mirror: {who} disconnected; profiles saved")
        return ws

    async def capture_file(request):
        name, file = request.match_info["name"], request.match_info["file"]
        if not re.fullmatch(r"[a-z]+-\d{8}-\d{6}-[a-z0-9-]{1,40}", name) or file not in KEEP_FILES:
            raise web.HTTPNotFound()
        path = CAPTURES / name / file
        if not path.is_file():
            raise web.HTTPNotFound()
        return web.FileResponse(path, headers={"Content-Type": KEEP_FILES[file], "Cache-Control": "no-store"})

    async def static(request):
        rel = request.match_info.get("path", "") or "index.html"
        parts = [p for p in rel.split("/") if p]
        if not parts or parts[0] in BLOCKED or ".." in parts:
            raise web.HTTPNotFound()
        path = (APP_DIR / Path(*parts)).resolve()
        if not str(path).startswith(str(APP_DIR.resolve())) or not path.is_file():
            raise web.HTTPNotFound()
        return web.FileResponse(path, headers={"Cache-Control": "no-store"})

    app = web.Application(middlewares=[cors], client_max_size=(N_EMB + N_LAT) * 4 + 4096)
    app.router.add_get("/api/health", health)
    app.router.add_post("/api/paint", paint)
    app.router.add_get("/api/stream", stream)
    if profiles is not None:
        app.router.add_get("/api/mirror", mirror_stream)
        app.router.add_get("/api/capture/{name}/{file}", capture_file)
        async def save_profiles(_app):
            profiles.save(force=True)
        app.on_shutdown.append(save_profiles)
    app.router.add_get("/", static)
    app.router.add_get("/{path:.*}", static)
    return app


# ---------------------------------------------------------------- tunnel

def cloudflared_path():
    found = shutil.which("cloudflared")
    if found:
        return found
    BIN_DIR.mkdir(exist_ok=True)
    system, machine = platform.system().lower(), platform.machine().lower()
    arch = "arm64" if machine in ("arm64", "aarch64") else "amd64"
    if system == "windows":
        name, url = "cloudflared.exe", f"https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-{arch}.exe"
    elif system == "linux":
        name, url = "cloudflared", f"https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-{arch}"
    else:
        log("tunnel: on macOS install it with:  brew install cloudflared   then run this again")
        return None
    path = BIN_DIR / name
    if not path.exists():
        log(f"tunnel: downloading Cloudflare's tunnel tool ({url.rsplit('/', 1)[1]})…")
        urllib.request.urlretrieve(url, path)
        path.chmod(0o755)
    return str(path)


def start_tunnel(port, which):
    exe = cloudflared_path()
    if not exe:
        return None
    log("tunnel: starting a free Cloudflare quick tunnel (no account needed)…")
    proc = subprocess.Popen([exe, "tunnel", "--no-autoupdate", "--url", f"http://localhost:{port}"],
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, bufsize=1)

    def watch():
        for line in proc.stdout:
            m = re.search(r"https://[a-z0-9-]+\.trycloudflare\.com", line)
            if m:
                show_link(m.group(0), which)
        log("tunnel: cloudflared exited; the phone link has stopped working. Restart this script.")
    threading.Thread(target=watch, daemon=True).start()
    return proc


def show_link(url, which="moodscape"):
    pair = f"{APP_URL}#pc={url}"
    mirror_url = f"{url}/mirror.html"
    target = mirror_url if which == "mirror" else pair
    print("\n" + "=" * 72, flush=True)
    print(f"  On your phone, scan this QR code (or open the link) in Chrome to open the {'Mirror' if which == 'mirror' else 'Moodscape app'}:\n", flush=True)
    try:
        import qrcode
        q = qrcode.QRCode(border=1, error_correction=qrcode.constants.ERROR_CORRECT_L); q.add_data(target); q.make()
        q.print_ascii(invert=True)
    except ImportError:
        print("  (pip install qrcode to show a scannable QR code here)", flush=True)
    print(f"\n    {target}\n", flush=True)
    if which == "mirror":
        print("  The Mirror is served by this PC, so the link changes each time you restart", flush=True)
        print("  this script; scan the new code then. People it has learned stay on this PC.", flush=True)
        print(f"  Moodscape (mood app) link:  {pair}", flush=True)
    else:
        print("  It opens Moodscape at its usual address and pairs it with this PC, so your", flush=True)
        print("  learned taste is kept. The tunnel address changes each time you restart this;", flush=True)
        print("  scan the new code then. Direct link to this PC, if you prefer:", flush=True)
        print(f"    {url}/", flush=True)
        print(f"  Mirror link:  {mirror_url}", flush=True)
    print("=" * 72 + "\n", flush=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--no-tunnel", action="store_true", help="don't open a Cloudflare tunnel")
    ap.add_argument("--cpu", action="store_true", help="force CPU instead of the graphics card")
    ap.add_argument("--max-fps", type=float, default=24, help="cap the stream's frame rate (default 24)")
    ap.add_argument("--motion", type=float, default=1.0, help="how much the scene gently moves on its own, 0–1 (default 1)")
    ap.add_argument("--no-benchmark", action="store_true", help="skip the startup speed test")
    ap.add_argument("--qr", choices=["moodscape", "mirror"], default="moodscape", help="which app the phone QR code opens")
    ap.add_argument("--bliss", default=str(HERE / "mirror-bliss.txt"), help="the Bliss edition's sections of options")
    ap.add_argument("--unease", default=str(HERE / "mirror-unease.txt"), help="the Unease edition's sections of options")
    ap.add_argument("--profiles", default=str(HERE / "profiles.json"), help="where the Mirror keeps the people it knows")
    a = ap.parse_args()
    if not (APP_DIR / "index.html").exists():
        raise SystemExit(f"index.html not found in {APP_DIR}; keep this folder inside the moodscape repo")
    painter = Painter(pick_providers(a.cpu), a.motion)
    bliss, unease = mirror_mod.Space(a.bliss, log, "bliss"), mirror_mod.Space(a.unease, log, "unease")
    bliss.attach_encoder(MODEL_DIR, download, providers=painter.unet.get_providers())
    unease.attach_encoder(MODEL_DIR, download, share=bliss)   # one text encoder on the GPU for both editions
    painter.spaces = {"bliss": bliss, "unease": unease}
    profiles = mirror_mod.Profiles(a.profiles, log)
    if not a.no_benchmark:
        painter.benchmark()
    gpu_pool = ThreadPoolExecutor(max_workers=1)  # one GPU, one frame at a time
    log(f"server: app, live stream and painter on http://localhost:{a.port}/ (stream capped at {a.max_fps:g} fps)")
    log(f"server: Mirror on http://localhost:{a.port}/mirror.html (works with this PC's own webcam too)")
    tunnel = None if a.no_tunnel else start_tunnel(a.port, a.qr)
    try:
        web.run_app(make_app(painter, gpu_pool, a.max_fps, profiles), host="0.0.0.0", port=a.port, print=None)
    finally:
        profiles.save(force=True)
        if tunnel: tunnel.terminate()


if __name__ == "__main__":
    main()
