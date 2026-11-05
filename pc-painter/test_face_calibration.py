"""
Does the Mirror learn to read an unusual face from Love / Skip taps?  Run:  python test_face_calibration.py

The simulated person raises their eyebrows when they like something and smirks when they don't, which the default
reading gets backwards. Without taps the mirror drifts toward what they dislike; with occasional taps it should
learn their tells (eyebrows up = yes, smile = not a yes) and climb toward what they like.
"""
import sys, tempfile
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import numpy as np, mirror as M, test_mirror_sim as T

class Odd(T.Person):
    """Likes -> raises brows; dislikes -> smirks. The default weights read this backwards."""
    def signals(self, liking):
        r = liking * self.react + self.rng.normal(0, self.noise)
        s = dict(self.base)
        s["wonder"] += max(0, r) * 0.2 + self.rng.normal(0, 0.03)
        s["smile"] += max(0, -r) * 0.15 + self.rng.normal(0, 0.03)
        s["yaw"] += self.rng.normal(0, 0.08); s["motion"] = max(0.0, s["motion"] + self.rng.normal(0, 0.004))
        return [float(s[k]) for k in M.SIGNALS]

def trial(seed, taps):
    rng = np.random.default_rng(seed)
    with tempfile.TemporaryDirectory() as tmp:
        prof = M.Profiles(Path(tmp) / "p.json", log=lambda m: None)
        sess = M.MirrorSession(T.SPACES, prof, log=lambda m: None, seed=seed)
        a = Odd(rng); t, out, nxt = 0.0, [], 30.0
        for blk in range(6):
            end = t + 120
            while t < end:
                t2, sc = T.run(sess, a, t, 1 / 6)          # 10 s at a time
                t = t2; out.append(np.mean(sc) if sc else 0)
                if taps and sess.viewer is not None and sess.dst and sess.dst[0] is not None:
                    s = a.score(sess.space.features(sess.dst[0]))
                    if s > 0.45: sess.command({"cmd": "love"}, t)          # they tap Love on paintings they clearly like...
                    elif s < -0.1: sess.command({"cmd": "next"}, t)        # ...and Skip ones they clearly don't
        tl = sess.viewer.taste(T.SPACE)
        return np.array(out).reshape(6, -1).mean(axis=1), tl.labels, tl.tells(3)

ends = {}
for taps in (False, True):
    res = [trial(s, taps) for s in range(4)]
    curve = np.mean([r[0] for r in res], axis=0)
    ends[taps] = curve[-1]
    print(("with Love/Skip taps   " if taps else "no taps (defaults)    "), " ".join(f"{c:.2f}" for c in curve),
          "(2-minute blocks; 0 = random, 1 = their perfect painting) | taps:", [r[1] for r in res], "| learned tells:", res[0][2])
ok = ends[True] > ends[False] + 0.4 and ends[True] > 0.2
print("PASS" if ok else "FAIL")
sys.exit(0 if ok else 1)
