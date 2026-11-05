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

def fake_space(name):
    sp = M.Space(HERE / f"mirror-{name}.txt", log=lambda m: None)
    sp.encode = lambda c: np.random.default_rng(zlib.crc32(sp.prompt(c).encode())).standard_normal(8).astype(np.float32)
    return sp


SPACES = {"bliss": fake_space("bliss"), "unease": fake_space("unease")}
SPACE = SPACES["bliss"]


class Person:
    def __init__(self, rng, react=0.6, noise=0.12, love=0.35, space=None, unease=None):
        self.rng = rng
        self.space = space or SPACE
        SP = self.space
        self.unease = unease   # another Person-like taste over the unease space: what unsettles them
        self.p = rng.normal(0, 0.04, SP.K)
        for o, n in zip(SP.offsets, SP.sizes):        # in every part: a couple of loves, a couple of dislikes
            idx = rng.permutation(n)
            self.p[o + idx[0]] = love
            if n > 4:
                self.p[o + idx[1]] = love * 0.6
                self.p[o + idx[2]] = self.p[o + idx[3]] = -0.3
        self.best = sum(self.p[o:o + n].max() for o, n in zip(SP.offsets, SP.sizes))
        self.rand = sum(self.p[o:o + n].mean() for o, n in zip(SP.offsets, SP.sizes))
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
        s.setdefault("tension", 0.05); s.setdefault("squint", 0.1)
        return [float(s[k]) for k in M.SIGNALS]

    def uneasy_signals(self, unsettled):
        """In the Unease edition: unsettled -> tense lips, furrowed brow, wrinkled nose, pulling back; bored -> a little smile."""
        r = unsettled * self.react + self.rng.normal(0, self.noise)
        s = dict(self.base); s.setdefault("tension", 0.05); s.setdefault("squint", 0.1)
        s["tension"] += max(0, r) * 0.2 + self.rng.normal(0, 0.03)
        s["browDown"] += max(0, r) * 0.15
        s["frown"] += max(0, r) * 0.1 + self.rng.normal(0, 0.02)
        s["sneer"] += max(0, r) * 0.06
        s["smile"] += max(0, -r) * 0.12 + self.rng.normal(0, 0.03)
        s["size"] *= 1 - 0.05 * r
        s["yaw"] += self.rng.normal(0, 0.08)
        return [float(s[k]) for k in M.SIGNALS]


def run(sess, person, t, minutes, hz=14, look_away=0.04, lag=0.45, encode_s=0.05, identify=True):
    end, next_id, ready_at, scores, firsts = t + minutes * 60, t, None, [], []
    while t < end:
        if sess.want() is not None:      # the server encodes on the GPU, then hands it back
            ready_at = ready_at or t + encode_s
            if t >= ready_at:
                c = sess.want(); sess.arrive(c, sess.space.encode(c), t); ready_at = None
        sess.tick(t)
        seen = next((x for ts, x in reversed(sess.shown) if ts <= t - lag), None)
        if identify and t >= next_id:
            sess.on_identity(person.descriptor(), t); next_id = t + 1.4
        if seen is None or person.rng.random() < look_away:
            sess.on_sample(None, t)
        elif sess.edition == "unease":
            u = person.unease
            sess.on_sample(person.uneasy_signals(float(seen @ u.p)), t)
            scores.append(u.score(seen))
        else:
            liking = float(seen @ person.p)
            sess.on_sample(person.signals(liking), t)
            scores.append(person.score(seen))
        t += 1 / hz
    return t, scores


def away(sess, t, secs):
    for _ in range(int(secs * 20)):
        if sess.want() is not None:
            c = sess.want(); sess.arrive(c, sess.space.encode(c), t)
        sess.tick(t); sess.on_sample(None, t); t += 0.05
    return t


def main():
    rng = np.random.default_rng(int(sys.argv[1]) if len(sys.argv) > 1 else 11)
    ok = True
    with tempfile.TemporaryDirectory() as tmp:
        prof = M.Profiles(Path(tmp) / "profiles.json", log=lambda m: None)
        events = []
        sess = M.MirrorSession(SPACES, prof, log=lambda m: events.append(m), seed=2)
        heath = Person(rng, unease=Person(rng, space=SPACES["unease"]))
        guest = Person(rng)
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
              ", ".join(f"{d['slot']} {d['item']}" for d in v.taste(SPACE).top(5)))
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
              + f" ({len(v.taste(SPACE).moments)} moments saved)")
        ok &= sess.viewer is v and first > 0.3

        sess.command({"cmd": "edition", "edition": "unease"}, t)
        uc = []
        for block in range(5):
            t, sc = run(sess, heath, t, 2); uc.append(np.mean(sc))
        print("Heath switches to Unease (0 = random, 1 = the most unsettling painting for him), 2-minute blocks:", " ".join(f"{u:.2f}" for u in uc))
        ok &= sess.viewer is v and np.mean(uc[-2:]) > uc[0] + 0.1
        ok &= v.taste(SPACE).beats > 0 and v.taste(SPACES["unease"]).beats > 0
        sess.command({"cmd": "edition", "edition": "bliss"}, t)

        stoic = Person(rng, react=0.3, noise=0.15)
        t = away(sess, t, 6)
        sc_all = []
        for block in range(5):
            t, sc = run(sess, stoic, t, 2); sc_all.append(np.mean(sc))
        print("A barely-expressive face, 2-minute blocks:", " ".join(f"{s:.2f}" for s in sc_all))

        # face recognition declined: one profile per phone, no fingerprints
        dsess = M.MirrorSession(SPACES, prof, log=lambda m: None, seed=5)
        dsess.command({"cmd": "device", "device": "phone-1234abcd"}, 0.0)
        owner = Person(rng)
        _, dsc = run(dsess, owner, 0.0, 6, identify=False)
        dp = dsess.viewer
        print(f"Phone-only mode: viewer '{dp.name}', {len(dp.exemplars)} fingerprints stored, minutes 4-6 score {np.mean(dsc[len(dsc) * 2 // 3:]):.2f}")
        ok &= dp is not None and dp.device == "phone-1234abcd" and not dp.exemplars

        prof.save(force=True)
        text = (HERE / "mirror-bliss.txt").read_text(encoding="utf-8").replace("[detail]\n", "[detail]\nSnowmen | with snowmen\n")
        edited = Path(tmp) / "space2.txt"; edited.write_text(text, encoding="utf-8")
        again = M.Profiles(Path(tmp) / "profiles.json", log=lambda m: None)
        hp = again.people[v.pid]
        ht = hp.taste(M.Space(edited, log=lambda m: None, edition="bliss"))
        same = [d["item"] for d in ht.top(4)] == [d["item"] for d in v.taste(SPACE).top(4)]
        print(f"Reloaded with an edited space: {len(again.people)} people, Heath's top 4 unchanged: {same}, moments kept: {len(ht.moments)}, "
              f"unease taste kept: {hp.taste(SPACES['unease']).beats} reactions")
        ok &= same and hp.taste(SPACES["unease"]).beats > 0

        old = {"people": [{"pid": "abc", "name": "Heath", "exemplars": [list(heath.face)], "beats": 134, "base": heath.base,
                           "worlds": ["Beach"], "A": [[16.0]], "b": [0.1], "seen": [3]}]}
        (Path(tmp) / "old.json").write_text(json.dumps(old))
        mig = M.Profiles(Path(tmp) / "old.json", log=lambda m: None)
        ok &= mig.people["abc"].name == "Heath" and mig.people["abc"].taste(SPACE).beats == 0 and len(mig.people["abc"].exemplars) == 1
        v2 = {"people": [{"version": 2, "pid": "def", "name": "Kayla", "exemplars": [list(guest.face)], "base": guest.base,
                          **v.taste(SPACE).to_json()}]}
        (Path(tmp) / "v2.json").write_text(json.dumps(v2))
        k = M.Profiles(Path(tmp) / "v2.json", log=lambda m: None).people["def"]
        print(f"Profiles from the last version load: Kayla's Bliss taste kept ({k.taste(SPACE).beats} reactions), Unease starts fresh ({k.taste(SPACES['unease']).beats})")
        ok &= k.taste(SPACE).beats == v.taste(SPACE).beats and k.taste(SPACES["unease"]).beats == 0
        print("sample decisions:", *events[30:38], sep="\n  ")
        print("PASS" if ok else "FAIL")
        return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
