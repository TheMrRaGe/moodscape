"""
FEAR Engine learner bench. Mirrors FearCore/Learners.cs (same constants) so the numbers here are the C#'s numbers.

Simulated players: each tag (kind of fear, sense, place) has a hidden pull on this player; some pairs of tags
are worth more together than apart (the "any combination" premise); every incident has its own quirk; tags wear
out with repetition and recover with rest. Each step the storyteller can only choose among the incidents whose
requirements happen to be met (a random subset), like in a real game.

Learners: random, per-item Thompson (TS), Thompson with a tag prior (what FearEngine.cs does today), the
mushroom-body instinct alone, and the hybrid (TS + instinct, with the instinct's say earned by its prediction
error). Oracle knows the player.
"""
import json, os, sys, numpy as np

D = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../Assets/NightPages/Resources/NPFly/mushroom_body.json")))
NG, NK, NM = len(D["glom"]), len(D["kcType"]), len(D["mbonType"])
G2K = np.zeros((NG, NK)); np.add.at(G2K, (D["g2kG"], D["g2kK"]), D["g2kW"])
K2M = np.zeros((NK, NM)); np.add.at(K2M, (D["k2mK"], D["k2mM"]), D["k2mW"])
DAN = {n: np.array(d["mbon"]) for n, d in zip(D["danNames"], D["dan"])}

# ---- constants shared with Learners.cs
GLOMS_PER_TAG = 4
SPARSE = 0.05          # k-winners-take-all: 5% of Kenyon cells fire
ETA = 0.5              # plasticity per unit prediction error
FLOOR = 0.2            # synapses never depress below 20% of baseline
RECOVER = 0.15         # the opposite side recovers by this share of the error (lets a value move both ways)
STD_U, STD_REC = 0.0, 0.02   # short-term depression of glomerular input (habituation), recovery per step
TS_SD, PRIOR_K = 0.45, 1.5
SIGNED, SIGNED_ETA = True, 0.6   # (False is the earlier depression-only rule, kept for comparison)   # signed readout: approach minus avoid as one weight, moved both ways by the error


def fnv(s):
    h = 2166136261
    for ch in s.encode():
        h = ((h ^ ch) * 16777619) & 0xFFFFFFFF
    return h


def tag_gloms(tag):
    h = fnv(tag); out = []
    for i in range(GLOMS_PER_TAG):
        h = ((h ^ (i + 1)) * 16777619) & 0xFFFFFFFF
        out.append(h % NG)
    return out


class Instinct:
    """Mushroom body: real glomerulus->KC wiring (or the FlyHash control), one compartment per domain."""
    def __init__(self, wiring="real", pair=("PAM08", "PPL105"), seed=0):
        if wiring == "real":
            self.g2k = G2K
        else:   # FlyHash control: same fan-in and weights per KC, random glomeruli
            rng = np.random.default_rng(seed); M = np.zeros_like(G2K)
            for k in range(NK):
                nz = G2K[:, k][G2K[:, k] > 0]
                if len(nz): M[rng.choice(NG, len(nz), replace=False), k] = rng.permutation(nz)
            self.g2k = M
        rew, pun = pair
        self.base_avoid = K2M[:, DAN[rew]].sum(1)     # reward depresses these (avoid MBONs)
        self.base_appr = K2M[:, DAN[pun]].sum(1)      # punishment depresses these (approach MBONs)
        self.fa = np.ones(NK); self.fv = np.ones(NK); self.w = np.zeros(NK)
        self.res = np.ones(NG)
        self.k = int(SPARSE * NK)

    def code(self, tags):
        # The pattern is never habituated (k-WTA normalises drive, so identity is kept); habituation is a gain.
        r = np.zeros(NG)
        for t in tags:
            for g in tag_gloms(t): r[g] += 1.0
        x = r @ self.g2k
        thr = np.partition(x, -self.k)[-self.k]
        return (x >= thr) & (x > 0), r

    def raw(self, a):
        if not a.any(): return 0.0
        if SIGNED: return float(self.w[a].mean())
        ba, bv = self.base_appr[a], self.base_avoid[a]
        appr = (ba * self.fa[a]).sum() / (ba.sum() + 1e-9)
        avoid = (bv * self.fv[a]).sum() / (bv.sum() + 1e-9)
        return appr - avoid

    def gain(self, r):
        on = r > 0
        return float((self.res[on] * r[on]).sum() / r[on].sum()) if on.any() else 1.0

    def value(self, tags):
        a, r = self.code(tags)
        v = self.raw(a)
        return v * self.gain(r) if (v > 0 and STD_U > 0) else v

    def learn(self, tags, y):
        a, r = self.code(tags)
        v = self.value(tags)
        d = float(np.clip(y - v, -1, 1))           # prediction error (Bennett et al. 2021)
        if SIGNED:
            self.w[a] += SIGNED_ETA * d
            return v
        if d > 0:
            self.fv[a] = np.maximum(FLOOR, self.fv[a] * (1 - ETA * d))
            self.fa[a] = np.minimum(1.0, self.fa[a] + RECOVER * d * (1 - self.fa[a]) * 4)
        elif d < 0:
            self.fa[a] = np.maximum(FLOOR, self.fa[a] * (1 + ETA * d))
            self.fv[a] = np.minimum(1.0, self.fv[a] - RECOVER * d * (1 - self.fv[a]) * 4)
        # short-term depression: what was just smelled is weaker next time
        rn = r / (r.max() + 1e-9)
        self.res = np.maximum(0.3, self.res - STD_U * rn * 3)
        return v

    def rest(self):
        self.res = np.minimum(1.0, self.res + STD_REC)


class Stats:
    def __init__(self): self.n = {}; self.m = {}; self.tn = {}; self.tm = {}
    def item(self, i): return self.n.get(i, 0.0), self.m.get(i, 0.0)
    def prior(self, tags):
        vals = [self.tm.get(t, 0.0) for t in tags]
        return sum(vals) / len(vals)
    def learn(self, i, tags, y):
        n = self.n.get(i, 0.0) + 1; self.n[i] = n
        self.m[i] = self.m.get(i, 0.0) + (y - self.m.get(i, 0.0)) / min(n, 8)
        for t in tags:
            tn = self.tn.get(t, 0.0) + 1; self.tn[t] = tn
            self.tm[t] = self.tm.get(t, 0.0) + (y - self.tm.get(t, 0.0)) / min(tn, 15)


WEAR_K, WEAR_REST = 0.3, 0.9    # the engine's own guess at how fast a kind of fright wears out and recovers
USE_WEAR = True


class Learner:
    def __init__(self, kind, rng, wiring="real"):
        self.kind = kind; self.rng = rng; self.st = Stats(); self.h = {}
        self.mb = Instinct("flyhash" if "flyhash" in kind else wiring) if kind in ("instinct", "hybrid", "ts+inst", "ts+inst-flyhash") else None
        self.err_ts = 0.25; self.err_mb = 0.25        # running squared prediction errors
        self.n_err = 0

    def ts_guess(self, i, tags, prior=True):
        n, m = self.st.item(i)
        if not prior: return m
        return (n * m + PRIOR_K * self.st.prior(tags)) / (n + PRIOR_K)

    def say(self):   # how much the instinct is trusted, earned by its error vs TS's
        w = self.err_ts / (self.err_ts + self.err_mb)
        return float(np.clip(w, 0.1, 0.9)) if self.n_err >= 5 else 0.1

    def novelty(self, tags):
        if not USE_WEAR: return 1.0
        return float(np.exp(-WEAR_K * np.mean([self.h.get(t, 0.0) for t in tags])))

    def draw(self, i, tags):
        v = self.draw_fresh(i, tags)
        return v * self.novelty(tags) if v > 0 else v

    def draw_fresh(self, i, tags):
        n, _ = self.st.item(i)
        noise = self.rng.normal() * TS_SD / np.sqrt(1 + n)
        if self.kind == "random": return self.rng.random()
        if self.kind == "ts": return self.ts_guess(i, tags, prior=False) + noise
        if self.kind == "ts+prior": return self.ts_guess(i, tags) + noise
        if self.kind == "instinct": return self.mb.value(tags) + noise
        if self.kind.startswith("ts+inst"):   # first impression from the instinct, then the item's own evidence takes over
            n, m = self.st.item(i)
            return (n * m + PRIOR_K * self.mb.value(tags)) / (n + PRIOR_K) + noise
        w = self.say()
        return (1 - w) * self.ts_guess(i, tags) + w * self.mb.value(tags) + noise

    def learn(self, i, tags, y):
        nov = self.novelty(tags)
        if y > 0: y = min(1.0, y / max(nov, 0.35))    # learn what it would do fresh; wear is applied when choosing
        for t in tags: self.h[t] = self.h.get(t, 0.0) + 1.0
        if self.mb is not None:
            pm = self.mb.value(tags); pt = self.ts_guess(i, tags)
            self.err_mb = 0.9 * self.err_mb + 0.1 * (y - pm) ** 2
            self.err_ts = 0.9 * self.err_ts + 0.1 * (y - pt) ** 2
            self.n_err += 1
            self.mb.learn(tags, y)
        self.st.learn(i, tags, y)

    def tick(self):
        for t in self.h: self.h[t] *= WEAR_REST
        if self.mb is not None: self.mb.rest()


# ---- the world
FAMS = ["unknown", "hunted", "body", "animal", "disgust", "uncanny", "trapped", "nature", "medical", "loss", "self"]
SENSE = ["sound", "sight", "moved", "light", "near", "voice"]
PLACE = ["far", "close", "behind", "refuge", "dark"]


def make_items(rng, n, prefix):
    items = []
    for j in range(n):
        tags = list(rng.choice(FAMS, rng.integers(1, 3), replace=False)) + [rng.choice(SENSE), rng.choice(PLACE)]
        items.append((prefix + str(j), tags))
    return items


class Player:
    def __init__(self, rng, interactions=True):
        vocab = FAMS + SENSE + PLACE
        self.w = {t: rng.normal(0, 0.3) for t in vocab}
        self.pair = {}
        if interactions:
            for _ in range(25):
                a, b = rng.choice(vocab, 2, replace=False)
                self.pair[(a, b)] = self.pair[(b, a)] = rng.normal(0, 0.5)
        self.quirk = {}
        self.h = {t: 0.0 for t in vocab}
        self.rng = rng

    def expect(self, i, tags):
        if i not in self.quirk: self.quirk[i] = self.rng.normal(0, 0.12)
        base = sum(self.w[t] for t in tags) / np.sqrt(len(tags))
        inter = sum(self.pair.get((a, b), 0) for x, a in enumerate(tags) for b in tags[x + 1:])
        raw = base + inter + self.quirk[i]
        wear = np.exp(-0.25 * np.mean([self.h[t] for t in tags]))
        return float(np.tanh(raw if raw < 0 else raw * wear))

    def react(self, i, tags):
        y = self.expect(i, tags) + self.rng.normal(0, 0.25)
        for t in tags: self.h[t] += 1.0
        return float(np.clip(y, -1, 1))

    def rest(self):
        for t in self.h: self.h[t] *= 0.88


def run(kind, seed, steps=150, avail=8, interactions=True, new_at=None, transfer=None, wiring="real", player=None, items=None):
    rng = np.random.default_rng(seed)
    player = player or Player(np.random.default_rng(seed + 1000), interactions)
    items = items or make_items(np.random.default_rng(seed + 2000), 40, "a")
    L = transfer if transfer is not None else Learner(kind, np.random.default_rng(seed + 3000), wiring)
    got, best = [], []
    for s in range(steps):
        if new_at is not None and s == new_at: items = items + make_items(np.random.default_rng(seed + 4000), 20, "b")
        cand = [items[k] for k in rng.choice(len(items), avail, replace=False)]
        ex = [player.expect(i, t) for i, t in cand]
        if kind == "oracle": k = int(np.argmax(ex))
        else: k = int(np.argmax([L.draw(i, t) for i, t in cand]))
        got.append(ex[k]); best.append(max(ex))
        i, t = cand[k]
        y = player.react(i, t)
        if kind != "oracle": L.learn(i, t, y); L.tick()
        player.rest()
    return np.array(got), np.array(best), L, player


def bench(seeds=60, **kw):
    kinds = ["random", "ts", "ts+prior", "instinct", "hybrid", "oracle"]
    res = {}
    for kind in kinds:
        g = np.array([run(kind, s, **kw)[0] for s in range(seeds)])
        res[kind] = g
    return res


def show(title, res, windows=((0, 20), (0, 50), (50, 150))):
    print("\n" + title)
    print("%-10s" % "" + "".join("  steps %3d-%-3d" % w for w in windows))
    for k, g in res.items():
        row = "".join("   %+.3f ±%.3f" % (g[:, a:b].mean(), g[:, a:b].mean(1).std() / np.sqrt(len(g))) for a, b in windows)
        print("%-10s%s" % (k, row))


if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else "all"
    if which in ("all", "linear"):
        show("Player whose fears add up (no combinations)", bench(interactions=False))
    if which in ("all", "combo"):
        show("Player with combinations (some pairs of tags worth more together)", bench(interactions=True))
