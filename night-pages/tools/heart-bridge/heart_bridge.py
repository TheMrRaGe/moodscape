"""Heart-rate bridge for Night Pages.

Gets your heart rate from a watch or strap and hands it to the game on this PC at
http://127.0.0.1:8766/heart. The game works without it; with it, your pulse counts toward how
frightened the game thinks you are.

Three ways to feed it:

  python heart_bridge.py                 Bluetooth: any device that broadcasts standard heart rate
                                         (Polar, Garmin with "broadcast heart rate" on, Coospo/Wahoo
                                         straps and armbands, many others). Finds it by itself.
  python heart_bridge.py --pulsoid TOKEN Relay app: Pulsoid running on an Apple Watch, Galaxy Watch or
                                         Wear OS watch. Get the token from pulsoid.net.
  python heart_bridge.py --simulate      No device: a made-up pulse, for testing the game.

Nothing is stored and nothing leaves this PC (apart from Pulsoid's own connection, if you use it).
"""
import argparse
import asyncio
import json
import math
import random
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HR_SERVICE = "0000180d-0000-1000-8000-00805f9b34fb"
HR_MEASUREMENT = "00002a37-0000-1000-8000-00805f9b34fb"


class Heart:
    """The latest reading, shared between whichever source is running and the little web server."""

    def __init__(self):
        self.lock = threading.Lock()
        self.bpm = 0.0
        self.rr = []            # recent beat-to-beat intervals, in milliseconds
        self.at = 0.0
        self.source = "none"

    def update(self, bpm, rr_ms=(), source=None):
        with self.lock:
            self.bpm = float(bpm)
            self.rr = (self.rr + [float(x) for x in rr_ms if 250 < x < 2000])[-30:]
            self.at = time.time()
            if source:
                self.source = source

    def snapshot(self):
        with self.lock:
            age = time.time() - self.at if self.at else 999.0
            return {"ok": self.at > 0 and age < 10, "bpm": round(self.bpm, 1), "rmssd": round(rmssd(self.rr), 1),
                    "age_s": round(age, 1), "source": self.source}


def rmssd(rr):
    """Beat-to-beat variability. It drops when you're stressed, so the game reads a fall as tension."""
    if len(rr) < 3:
        return 0.0
    diffs = [(b - a) ** 2 for a, b in zip(rr, rr[1:])]
    return math.sqrt(sum(diffs) / len(diffs))


def parse_hr_measurement(data):
    """Decode a standard Bluetooth Heart Rate Measurement packet -> (bpm, [rr intervals in ms])."""
    flags = data[0]
    i = 1
    if flags & 0x01:
        bpm = int.from_bytes(data[i:i + 2], "little"); i += 2
    else:
        bpm = data[i]; i += 1
    if flags & 0x08:        # energy expended, not needed
        i += 2
    rr = []
    if flags & 0x10:
        while i + 1 < len(data):
            rr.append(int.from_bytes(data[i:i + 2], "little") * 1000.0 / 1024.0)
            i += 2
    return bpm, rr


def serve(heart, port):
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path.split("?")[0] != "/heart":
                self.send_error(404)
                return
            body = json.dumps(heart.snapshot()).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *args):
            pass

    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)     # this PC only
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server


async def run_bluetooth(heart, name_filter):
    try:
        from bleak import BleakClient, BleakScanner
    except ImportError:
        print("Bluetooth needs the 'bleak' package:  pip install bleak")
        return
    while True:
        print("Looking for a Bluetooth heart-rate device (wear it, and turn on heart-rate broadcast if it has that setting)...")
        found = None
        devices = await BleakScanner.discover(timeout=8.0, return_adv=True)
        for device, adv in devices.values():
            uuids = [u.lower() for u in (adv.service_uuids or [])]
            name = device.name or adv.local_name or ""
            if HR_SERVICE in uuids and (not name_filter or name_filter.lower() in name.lower()):
                found = (device, name or device.address)
                break
        if not found:
            print("  none found; trying again in a few seconds")
            await asyncio.sleep(4)
            continue
        device, name = found
        print(f"  found {name}; connecting")
        try:
            async with BleakClient(device) as client:
                def on_data(_, data):
                    bpm, rr = parse_hr_measurement(bytes(data))
                    heart.update(bpm, rr, f"Bluetooth ({name})")
                await client.start_notify(HR_MEASUREMENT, on_data)
                print(f"  connected to {name}. Leave this window open while you play.")
                while client.is_connected:
                    await asyncio.sleep(1)
                    snap = heart.snapshot()
                    print(f"\r  {snap['bpm']:.0f} bpm   ", end="", flush=True)
        except Exception as e:
            print(f"\n  lost {name} ({e!r}); reconnecting")
        await asyncio.sleep(2)


async def run_pulsoid(heart, token):
    try:
        import websockets
    except ImportError:
        print("Pulsoid needs the 'websockets' package:  pip install websockets")
        return
    url = "wss://dev.pulsoid.net/api/v1/data/real_time?access_token=" + token
    while True:
        try:
            async with websockets.connect(url) as ws:
                print("Connected to Pulsoid. Start the Pulsoid app on your watch.")
                async for msg in ws:
                    bpm = json.loads(msg).get("data", {}).get("heart_rate")
                    if bpm:
                        heart.update(bpm, (), "Pulsoid")
                        print(f"\r  {bpm:.0f} bpm   ", end="", flush=True)
        except Exception as e:
            print(f"\nPulsoid connection dropped ({e!r}); retrying")
            await asyncio.sleep(5)


async def run_simulated(heart):
    print("Simulating a pulse: about 68 bpm, with a scare every half minute or so.")
    bpm, target, t = 68.0, 68.0, 0.0
    while True:
        if random.random() < 0.03:
            target = random.choice([68, 68, 95, 110])
        target += (68 - target) * 0.02
        bpm += (target - bpm) * 0.15 + random.uniform(-0.6, 0.6)
        rr = 60000.0 / bpm + random.uniform(-1, 1) * (45 if bpm < 80 else 12)
        heart.update(bpm, [rr], "simulated")
        print(f"\r  {bpm:.0f} bpm   ", end="", flush=True)
        await asyncio.sleep(1.0)
        t += 1


def main():
    ap = argparse.ArgumentParser(description="Heart-rate bridge for Night Pages")
    ap.add_argument("--pulsoid", metavar="TOKEN", help="read from Pulsoid (Apple Watch, Galaxy Watch, Wear OS)")
    ap.add_argument("--simulate", action="store_true", help="no device: a made-up pulse for testing")
    ap.add_argument("--name", default="", help="Bluetooth: only connect to a device whose name contains this")
    ap.add_argument("--port", type=int, default=8766)
    args = ap.parse_args()
    heart = Heart()
    serve(heart, args.port)
    print(f"Heart-rate bridge: the game reads http://127.0.0.1:{args.port}/heart")
    try:
        if args.simulate:
            asyncio.run(run_simulated(heart))
        elif args.pulsoid:
            asyncio.run(run_pulsoid(heart, args.pulsoid))
        else:
            asyncio.run(run_bluetooth(heart, args.name))
    except KeyboardInterrupt:
        print("\nStopped.")


if __name__ == "__main__":
    main()
