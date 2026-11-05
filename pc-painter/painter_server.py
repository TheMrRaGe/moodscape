"""
Moodscape PC painter.

Runs the SDXS painter on this computer's graphics card and serves the Moodscape app, so a phone
that can't run the model itself (e.g. a budget Android) gets full-quality paintings. The phone keeps
doing the camera, face reading and learning; it sends only the prompt embedding and the noise (numbers,
never video) and gets a 512x512 JPEG back.

    python painter_server.py             # serves on port 8765 and opens a Cloudflare tunnel
    python painter_server.py --no-tunnel # local network / localhost only
    python painter_server.py --cpu       # force CPU (slow; for testing)

Every decision is printed: which GPU backend was picked, where the model came from, each painting's timing.
"""
import argparse, io, json, os, platform, re, shutil, subprocess, sys, threading, time, urllib.request
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
from pathlib import Path

import numpy as np
import onnxruntime as ort
from PIL import Image

HERE = Path(__file__).resolve().parent
APP_DIR = HERE.parent                     # the repo root holds index.html, manifest.json, icons
MODEL_DIR = HERE / "models"
BIN_DIR = HERE / "bin"
REPO = "https://huggingface.co/skillsafeai/sdxs-512-dreamshaper-onnx-webgpu/resolve/main/"
APP_URL = "https://raw.githack.com/TheMrRaGe/moodscape/main/index.html"  # the app's permanent address
FILES = {"unet": ("unet.onnx", 631766742), "vae": ("vae_decoder.onnx", 2467568)}
SIGMA = 14.614646911621094
N_EMB, N_LAT = 77 * 768, 4 * 64 * 64
MAX_BODY = (N_EMB + N_LAT) * 4 + 1024


def log(msg):
    print(time.strftime("%H:%M:%S"), msg, flush=True)


def download(name, size):
    MODEL_DIR.mkdir(exist_ok=True)
    path = MODEL_DIR / name
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
    log("gpu: no GPU backend found, falling back to CPU (each painting will take minutes). "
        "On Windows run: pip install onnxruntime-directml   On Linux with NVIDIA: pip install onnxruntime-gpu")
    return ["CPUExecutionProvider"]


class Painter:
    def __init__(self, providers):
        opt = ort.SessionOptions(); opt.log_severity_level = 3
        t0 = time.time()
        self.unet = ort.InferenceSession(str(download(*FILES["unet"])), opt, providers=providers)
        self.vae = ort.InferenceSession(str(download(*FILES["vae"])), opt, providers=providers)
        self.provider = self.unet.get_providers()[0]
        self.lock = threading.Lock()  # one painting at a time on the GPU
        log(f"painter: sessions ready on {self.provider} in {time.time() - t0:.1f} s")
        self.count = 0

    def paint(self, emb, x):
        """emb: float32 [77*768] conditioning, x: float32 [4*64*64] noise already scaled by sigma (as the phone does it)."""
        with self.lock:
            t0 = time.time()
            sample = (x / np.sqrt(SIGMA ** 2 + 1)).reshape(1, 4, 64, 64).astype(np.float32)
            eps = self.unet.run(None, {"sample": sample, "timestep": np.array([999], np.float32),
                                       "encoder_hidden_states": emb.reshape(1, 77, 768)})[0]
            t1 = time.time()
            latent = (x.reshape(1, 4, 64, 64) - SIGMA * eps).astype(np.float32)
            img = self.vae.run(None, {"latent": latent})[0][0]
            t2 = time.time()
        rgb = (np.clip(img / 2 + 0.5, 0, 1).transpose(1, 2, 0) * 255).astype(np.uint8)
        buf = io.BytesIO(); Image.fromarray(rgb).save(buf, format="JPEG", quality=90)
        self.count += 1
        return buf.getvalue(), (t1 - t0) * 1000, (t2 - t1) * 1000


def make_handler(painter):
    class Handler(SimpleHTTPRequestHandler):
        def __init__(self, *a, **kw):
            super().__init__(*a, directory=str(APP_DIR), **kw)

        def log_message(self, fmt, *args):  # keep the console for decisions, not every icon request
            if not self.path.startswith("/api/paint"):
                return
            super().log_message(fmt, *args)

        def end_headers(self):
            self.send_header("Cache-Control", "no-store")
            if self.path.startswith("/api/"):  # the app at its permanent address calls this from another origin
                self.send_header("Access-Control-Allow-Origin", "*")
                self.send_header("Access-Control-Allow-Headers", "content-type")
                self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
                self.send_header("Access-Control-Expose-Headers", "X-Paint-Ms")
                self.send_header("Access-Control-Max-Age", "600")
            super().end_headers()

        def do_OPTIONS(self):
            if not self.path.startswith("/api/"):
                return self.send_error(404)
            self.send_response(204); self.send_header("Content-Length", "0"); self.end_headers()

        def send_json(self, code, obj):
            body = json.dumps(obj).encode()
            self.send_response(code); self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)

        def do_GET(self):
            if self.path.startswith("/api/health"):
                return self.send_json(200, {"ok": True, "painter": "pc", "provider": painter.provider,
                                            "host": platform.node(), "paintings": painter.count})
            if self.path.startswith(("/pc-painter", "/.git", "/tools")):
                return self.send_error(404)
            return super().do_GET()

        def do_POST(self):
            if not self.path.startswith("/api/paint"):
                return self.send_error(404)
            n = int(self.headers.get("Content-Length") or 0)
            if n != (N_EMB + N_LAT) * 4:
                return self.send_json(400, {"error": f"expected {(N_EMB + N_LAT) * 4} bytes, got {n}"})
            raw = np.frombuffer(self.rfile.read(n), dtype="<f4")
            if not np.isfinite(raw).all():
                return self.send_json(400, {"error": "non-finite values in request"})
            jpeg, unet_ms, vae_ms = painter.paint(raw[:N_EMB].copy(), raw[N_EMB:].copy())
            log(f"paint #{painter.count}: unet {unet_ms:.0f} ms, decode {vae_ms:.0f} ms")
            self.send_response(200); self.send_header("Content-Type", "image/jpeg")
            self.send_header("Content-Length", str(len(jpeg)))
            self.send_header("X-Paint-Ms", f"{unet_ms:.0f},{vae_ms:.0f}"); self.end_headers(); self.wfile.write(jpeg)
    return Handler


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


def start_tunnel(port):
    exe = cloudflared_path()
    if not exe:
        return
    log("tunnel: starting a free Cloudflare quick tunnel (no account needed)…")
    proc = subprocess.Popen([exe, "tunnel", "--no-autoupdate", "--url", f"http://localhost:{port}"],
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, bufsize=1)

    def watch():
        for line in proc.stdout:
            m = re.search(r"https://[a-z0-9-]+\.trycloudflare\.com", line)
            if m:
                show_link(m.group(0))
        log("tunnel: cloudflared exited; the phone link has stopped working. Restart this script.")
    threading.Thread(target=watch, daemon=True).start()
    return proc


def show_link(url):
    pair = f"{APP_URL}#pc={url}"
    print("\n" + "=" * 72, flush=True)
    print("  On your phone, scan this QR code (or open the link) in Chrome:\n", flush=True)
    try:
        import qrcode  # optional: pip install qrcode
        q = qrcode.QRCode(border=1, error_correction=qrcode.constants.ERROR_CORRECT_L); q.add_data(pair); q.make()
        q.print_ascii(invert=True)
    except ImportError:
        print("  (pip install qrcode to show a scannable QR code here)", flush=True)
    print(f"\n    {pair}\n", flush=True)
    print("  It opens Moodscape at its usual address and pairs it with this PC, so your", flush=True)
    print("  learned taste is kept. The tunnel address changes each time you restart this;", flush=True)
    print("  scan the new code then. Direct link to this PC, if you prefer:", flush=True)
    print(f"    {url}/", flush=True)
    print("=" * 72 + "\n", flush=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--no-tunnel", action="store_true", help="don't open a Cloudflare tunnel")
    ap.add_argument("--cpu", action="store_true", help="force CPU instead of the graphics card")
    a = ap.parse_args()
    if not (APP_DIR / "index.html").exists():
        raise SystemExit(f"index.html not found in {APP_DIR}; keep this folder inside the moodscape repo")
    painter = Painter(pick_providers(a.cpu))
    log("painter: warming up (first run compiles GPU kernels)…")
    rng = np.random.default_rng(0)
    _, u, v = painter.paint(np.zeros(N_EMB, np.float32), (rng.standard_normal(N_LAT) * SIGMA).astype(np.float32))
    painter.count = 0
    log(f"painter: warm-up painting took {u + v:.0f} ms")
    server = ThreadingHTTPServer(("0.0.0.0", a.port), make_handler(painter))
    log(f"server: app and painter on http://localhost:{a.port}/")
    tunnel = None if a.no_tunnel else start_tunnel(a.port)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        log("server: stopping")
    finally:
        if tunnel: tunnel.terminate()


if __name__ == "__main__":
    main()
