"""
Simulated viewers for the mirror's brain (no GPU, no phone). Run:  python test_mirror_sim.py

A simulated person has hidden favourite worlds. Their face reacts (noisily, with a delay, and not always)
to whatever is on screen. The test checks that the mirror recognizes who is looking, learns each person's
favourites from those reactions, and spends more and more of its time showing them.
"""
import sys, tempfile
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import mirror as M

NAMES = [l.split("|")[0].strip() for l in (Path(__file__).parent / "desires.txt").read_text(encoding="utf-8").splitlines()
         if "|" in l and not l.startswith("#")]


class FakeDesires:
    names = NAMES


class Person:
    def __init__(self, rng, likes, dislikes, react=0.6, noise=0.12):
        self.rng = rng
        self.p = rng.normal(0, 0.05, len(NAMES))
        for i, v in likes.items():
            self.p[i] = v
        for i in dislikes:
            self.p[i] = -0.5
        self.face = rng.normal(0, 0.055, 128)
        self.base = dict(smile=0.08, cheek=0.1, wonder=0.1, jaw=0.05, frown=0.05, browDown=0.06, sneer=0.03, blink=0.1, yaw=0.0, pitch=0.0, size=0.42)
        self.react, self.noise = react, noise   # react: how expressive this face is

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


def run(sess, person, t0, minutes, hz=12, look_away=0.04, history=None):
    dt, t, lag = 1 / hz, t0, 0.5
    shown = []  # (t, D) as the phone sees it
    on_fav = []
    next_id = t0
    end = t0 + minutes * 60
    while t < end:
        sess.tick(t)
        D, *_ = sess.frame_params(t)
        shown.append((t, D))
        while shown and shown[0][0] < t - lag - 0.2:
            shown.pop(0)
        seen = shown[0][1]
        if t >= next_id:
            sess.on_identity(person.descriptor(), t); next_id = t + 1.5
        if person.rng.random() < look_away:
            sess.on_sample(None, t)
        else:
            sess.on_sample(person.signals(float(seen @ person.p)), t)
        if history is not None:
            history.append((t, D @ person.p, D))
        t += dt
    return t


def main():
    rng = np.random.default_rng(7)
    with tempfile.TemporaryDirectory() as tmp:
        quiet = lambda m: None
        prof = M.Profiles(Path(tmp) / "profiles.json", NAMES, log=quiet)
        events = []
        sess = M.MirrorSession(FakeDesires, prof, log=lambda m: events.append(m), seed=1)
        heath = Person(rng, likes={3: 0.7, 11: 0.5}, dislikes=[21, 17])     # forest, northern lights; hates volcano, lighthouse
        guest = Person(rng, likes={0: 0.7, 24: 0.5}, dislikes=[1])           # beach, villa; dislikes the cabin
        fav = lambda p: set(np.argsort(-p.p)[:2])

        ok = True
        t, hist = 0.0, []
        for block in range(6):   # 6 × 5 min of Heath watching
            h = []
            t = run(sess, heath, t, 5, history=h)
            share = np.mean([D[list(fav(heath))].sum() for _, _, D in h])
            liking = np.mean([l for _, l, _ in h])
            print(f"Heath minutes {block * 5:2}-{block * 5 + 5:2}: favourites on screen {share:5.0%} (chance {2 / len(NAMES):.0%}), "
                  f"avg liking of what's shown {liking:+.2f} (best possible {heath.p.max():+.2f})")
        v = sess.viewer
        top = [w["world"] for w in v.top(4)]
        print("Heath's learned top 4:", top, "| true favourites:", [NAMES[i] for i in fav(heath)])
        ok &= v.name == "Guest 1" and set(top[:2]) == {NAMES[i] for i in fav(heath)}
        ok &= share > 0.45

        # Heath walks away; nobody for 10 s; a new person steps in
        for k in range(200):
            sess.tick(t); sess.on_sample(None, t); t += 1 / 20
        ok &= sess.viewer is None
        h = []
        t = run(sess, guest, t, 6, history=h)
        g = sess.viewer
        share_g = np.mean([D[list(fav(guest))].sum() for _, _, D in h[len(h) // 2:]])
        print(f"New person: became {g.name}; their favourites on screen in minutes 3-6: {share_g:.0%}; learned top 2: {[w['world'] for w in g.top(2)]}")
        ok &= g is not v and g.name == "Guest 2" and share_g > 0.3

        # Heath comes back: should be recognized within a few seconds and shown his favourite straight away
        for k in range(100):
            sess.tick(t); sess.on_sample(None, t); t += 1 / 20
        h = []
        t = run(sess, heath, t, 0.25, history=h)
        back = next((ti - h[0][0] for ti, _, D in h if D[list(fav(heath))].max() > 0.6), None)
        print(f"Heath returns: recognized as {sess.viewer.name}; a favourite of his filled the mirror after {back:.1f} s")
        ok &= sess.viewer is v and back is not None and back < 5

        # a person whose face barely moves: learning should be slower but not wrong
        stoic = Person(rng, likes={6: 0.7, 9: 0.5}, dislikes=[], react=0.3, noise=0.15)
        for k in range(120):
            sess.tick(t); sess.on_sample(None, t); t += 1 / 20
        h = []
        t = run(sess, stoic, t, 20, history=h)
        s3 = np.mean([D[list(fav(stoic))].sum() for _, _, D in h[-len(h) // 4:]])
        print(f"A barely-expressive face ({sess.viewer.name}): favourites on screen in the last 5 of 20 min: {s3:.0%}")

        prof.save(force=True)
        again = M.Profiles(Path(tmp) / "profiles.json", NAMES[:5] + ["A new world"] + NAMES[5:], log=quiet)
        hp = again.people[v.pid]
        print(f"Saved and reloaded with an edited world list: {len(again.people)} people; Heath's top 2 still {[w['world'] for w in hp.top(2)]}")
        ok &= [w["world"] for w in hp.top(2)] == top[:2]
        print("decisions logged:", len(events), "| sample:", *events[5:9], sep="\n  ")
        print("PASS" if ok else "FAIL")
        return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
