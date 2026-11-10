"""Checks the Bluetooth packet decoding, the variability maths and the local web endpoint. Run: python test_heart_bridge.py"""
import json
import urllib.request

from heart_bridge import Heart, parse_hr_measurement, rmssd, serve

# 8-bit heart rate, no extras: flags 0x00, 72 bpm
assert parse_hr_measurement(bytes([0x00, 72])) == (72, [])
# 8-bit heart rate with two beat-to-beat intervals (1024 units = 1 second each)
bpm, rr = parse_hr_measurement(bytes([0x10, 60, 0x00, 0x04, 0x00, 0x04]))
assert bpm == 60 and rr == [1000.0, 1000.0], (bpm, rr)
# 16-bit heart rate, energy field present (skipped), one interval of 0.5 s
bpm, rr = parse_hr_measurement(bytes([0x19, 0x2C, 0x01, 0xAA, 0xBB, 0x00, 0x02]))
assert bpm == 300 and rr == [500.0], (bpm, rr)
assert rmssd([800, 800, 800, 800]) == 0.0
assert abs(rmssd([800, 850, 800, 850]) - 50.0) < 1e-6

heart = Heart()
server = serve(heart, 8799)
got = json.load(urllib.request.urlopen("http://127.0.0.1:8799/heart"))
assert got["ok"] is False, got
heart.update(91, [660, 655, 670], "test")
got = json.load(urllib.request.urlopen("http://127.0.0.1:8799/heart"))
assert got["ok"] and got["bpm"] == 91.0 and got["source"] == "test" and got["rmssd"] > 0, got
server.shutdown()
print("PASS")
