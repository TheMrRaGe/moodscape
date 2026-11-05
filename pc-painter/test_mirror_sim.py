"""
Simulated viewers for the Mirror's brain (no GPU, no phone). Run:  python test_mirror_sim.py

A simulated person has hidden tastes: a few options they love or dislike in each part of the painting
(place, time, palette...). Their face reacts noisily, with a delay, and not always, to whatever is on
screen. The test checks that the mirror recognizes who is looking, learns their taste from those reactions,
and that what it shows climbs toward what they love, measured on a 0-1 scale where 0 is a random painting
and 1 is their perfect one.
"""
import json, sys, tempfile, zlib
from pathlib import Path
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import mirror as M

SPACE = M.Space(HERE / "mirror-space.txt", log=lambda m: None)
SPACE.encode = lambda c: np.random.default_rng(zlib.crc32(SPACE.prompt(c).encode())).standard_normal(8).astype(np.float32)


class Person:
    def __init__(self, rng, react=0.6, noise=0.12, love=0.35):
        self.rng = rng
        self.p = rng.normal(0, 0.04, SPACE.K)
        for o, n in zip(SPACE.offsets, SPACE.sizes):        # in every part: a couple of loves, a couple of dislikes
            idx = rng.permutation(n)
            self.p[o + idx[0]] = love
            if n > 4:
                self.p[o + idx[1]] = love * 0.6
                self.p[o + idx[2]] = self.p[o + idx[3]] = -0.3
        self.best = sum(self.p[o:o + n].max() for o, n in zip(SPACE.offsets, SPACE.sizes))
        self.rand = sum(self.p[o:o + n].mean() for o, n in zip(SPACE.offsets, SPACE.sizes))
        self.face = rng.normal(0, 0.055, 128)
        self.base = dict(smile=0.08, cheek=0.1, wonder=0.5, jaw=0.1, frown=0.05, browDown=0.06, sneer=0.03, blink=0.15, yaw=0.44, pitch=-0.14, size=0.4)
        self.react, self.noise = react, noise   # react: how expressive this face is; yaw 0.44: phone propped at an angle

    def score(self, x):
        return (x @ self.p - self.rand) / (self.best - self.rand)

    def descriptor(self):
        return self.face + self.rng.normal(0, 0.025, 128)

    def signals(self, liking):
        r = liking * self.react + self.rng.normal(0, self.noise)
        s = dict(self.base)
        s["smile"] += max(0, r) * 0.22 + self.rng.normal(0, 0.03)
        s["cheek"] += max(0, r) * 0.1
        s["frown"] += max(0, -r) * 0.15 + self.rng.normal(0, 0.02)
        s["browDown"] += max(0, -r) * 0.12
        s["size"] *= 1 + 0.06 * r
        s["yaw"] += self.rng.normal(0, 0.08)
        return [float(s[k]) for k in M.SIGNALS]


def run(sess, person, t, minutes, hz=14, look_away=0.04, lag=0.45, encode_s=0.05):
    end, next_id, ready_at, scores, firsts = t + minutes * 60, t, None, [], []
    while t < end:
        if sess.want() is not None:      # the server encodes on the GPU, then hands it back
            ready_at = ready_at or t + encode_s
            if t >= ready_at:
                c = sess.want(); sess.arrive(c, SPACE.encode(c), t); ready_at = None
        sess.tick(t)
        seen = next((x for ts, x in reversed(sess.shown) if ts <= t - lag), None)
        if t >= next_id:
            sess.on_identity(person.descriptor(), t); next_id = t + 1.4
        if seen is None or person.rng.random() < look_away:
            sess.on_sample(None, t)
        else:
            liking = float(seen @ person.p)
            sess.on_sample(person.signals(liking), t)
            scores.append(person.score(seen))
        t += 1 / hz
    return t, scores


def away(sess, t, secs):
    for _ in range(int(secs * 20)):
        if sess.want() is not None:
            c = sess.want(); sess.arrive(c, SPACE.encode(c), t)
        sess.tick(t); sess.on_sample(None, t); t += 0.05
    return t


def main():
    rng = np.random.default_rng(int(sys.argv[1]) if len(sys.argv) > 1 else 11)
    ok = True
    with tempfile.TemporaryDirectory() as tmp:
        prof = M.Profiles(Path(tmp) / "profiles.json", SPACE, log=lambda m: None)
        events = []
        sess = M.MirrorSession(SPACE, prof, log=lambda m: events.append(m), seed=2)
        heath, guest = Person(rng), Person(rng)
        t = 0.0
        print("0 = a random painting, 1 = this person's perfect painting")
        curve = []
        for block in range(8):
            t, sc = run(sess, heath, t, 2)
            curve.append(np.mean(sc))
            print(f"  Heath minutes {block * 2:2}-{block * 2 + 2:2}: {np.mean(sc):.2f}")
        v = sess.viewer
        ok &= curve[0] < curve[-1] and np.mean(curve[-3:]) > 0.5
        ups = sum("warmed" in e for e in events); downs = sum("undoing" in e for e in events); jumps = sum("jumping" in e for e in events)
        print(f"  {len(events)} decisions: {ups} kept as wins, {downs} undone, {jumps} jumps; learned top:",
              ", ".join(f"{d['slot']} {d['item']}" for d in v.top(5)))
        truth = [SPACE.keys[i] for i in np.argsort(-heath.p)[:7]]
        print("  true loves:", ", ".join(k.replace(":", " ") for k in truth))

        t = away(sess, t, 8)
        ok &= sess.viewer is None
        t, sc = run(sess, guest, t, 6)
        g = sess.viewer
        print(f"New person became {g.name}; minutes 4-6 score {np.mean(sc[len(sc) * 2 // 3:]):.2f}")
        ok &= g is not v

        t = away(sess, t, 6)
        t, sc = run(sess, heath, t, 0.1)    # 6 seconds: recognized, then the opening painting melts in
        first = np.mean(sc[-25:])
        print(f"Heath returns: recognized as {sess.viewer.name}; it opens on his best-known painting, scoring {first:.2f}"
              + (f" ({len(v.moments)} moments saved)" if v.moments else ""))
        ok &= sess.viewer is v and first > 0.3

        stoic = Person(rng, react=0.3, noise=0.15)
        t = away(sess, t, 6)
        sc_all = []
        for block in range(5):
            t, sc = run(sess, stoic, t, 2); sc_all.append(np.mean(sc))
        print("A barely-expressive face, 2-minute blocks:", " ".join(f"{s:.2f}" for s in sc_all))

        prof.save(force=True)
        text = (HERE / "mirror-space.txt").read_text(encoding="utf-8").replace("[detail]\n", "[detail]\nSnowmen | with snowmen\n")
        edited = Path(tmp) / "space2.txt"; edited.write_text(text, encoding="utf-8")
        again = M.Profiles(Path(tmp) / "profiles.json", M.Space(edited, log=lambda m: None), log=lambda m: None)
        hp = again.people[v.pid]
        same = [d["item"] for d in hp.top(4)] == [d["item"] for d in v.top(4)]
        print(f"Reloaded with an edited space: {len(again.people)} people, Heath's top 4 unchanged: {same}, moments kept: {len(hp.moments)}")
        ok &= same

        old = {"people": [{"pid": "abc", "name": "Heath", "exemplars": [list(heath.face)], "beats": 134, "base": heath.base,
                           "worlds": ["Beach"], "A": [[16.0]], "b": [0.1], "seen": [3]}]}
        (Path(tmp) / "old.json").write_text(json.dumps(old))
        mig = M.Profiles(Path(tmp) / "old.json", SPACE, log=lambda m: print("  " + m) if "first Mirror" in m else None)
        ok &= mig.people["abc"].name == "Heath" and mig.people["abc"].beats == 0 and len(mig.people["abc"].exemplars) == 1
        print("sample decisions:", *events[30:38], sep="\n  ")
        print("PASS" if ok else "FAIL")
        return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
