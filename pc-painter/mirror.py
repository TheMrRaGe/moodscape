"""
Moodscape Mirror: a painting that shows each person what they light up at.

The phone is only the eye. It sends face numbers (smile, brows, eyes, head turn, how close you are) about
12 times a second, and every ~1.5 s a 128-number face fingerprint so this laptop knows WHO is looking.
Everything else happens here:

    who is it?      fingerprint -> nearest known person (or a new guest after a few unknown reads)
    what do they    each person has an affinity score per world (desires.txt), learned by Bayesian
    desire?         linear regression from "what was on screen" -> "how their face responded"
    what to show?   linger while they light up, wander when they go flat, pick the next world by
                    Thompson sampling (likely favourites, with honest exploration of the unknown)
    how to show it  the painting melts continuously toward the chosen mix of worlds; layouts
                    change by slerping the noise, so nothing ever cuts

Profiles (names, face fingerprints, learned affinities) are saved in profiles.json next to this file.
They never leave this laptop. Delete the file to forget everyone, or use "Forget" on the phone.
"""
import hashlib, html, json, math, os, random, re, time
from collections import deque
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
N_EMB = 77 * 768

# ------------------------------------------------------------------ tuning
MATCH_DIST = 0.50        # face fingerprints closer than this are the same person (face-api: same person ~0.3-0.45)
WEAK_DIST = 0.58         # ...and up to this, if it's the person already in front of the mirror
NEW_AFTER = 3            # unknown reads in a row before a new guest profile is made
SWITCH_AFTER = 2         # reads in a row naming someone else before the mirror switches person
LOST_AFTER_S = 4.0       # no face for this long = nobody is looking
BEAT_S = 1.5             # learning and decisions happen once per beat
LAG_S = 0.8              # stream latency + human reaction time: a reaction belongs to what was shown this long ago
WARMUP_SAMPLES = 25      # a new face's resting expression is averaged over this many samples first
MIN_DWELL_S = 5.0        # a world stays at least this long
WANDER_S = 12.0          # ...and moves on after this if the face stays flat
LINGER_MAX_S = 45.0      # ...but lingers up to this while the face keeps lighting up
ENGAGED = 0.6            # lighting up = smoothed reaction above this many 'typical reactions' for this person
DISLIKED = -0.6          # below this many = move on early
BLEND_P = 0.35           # chance a new pick blends in a second world
MORPH_S = 1.1            # how fast the painting melts toward a new mix (time constant)
SEED_MORPH_S = 2.2       # how long a layout change takes
PRIOR_SD = 0.25          # prior spread of a person's affinity for any world (face reactions are small numbers)
NOISE_SD = 0.4           # how noisy one beat of face reaction is
EXPLOIT_AT = 300         # after this many reactions, about half the picks go straight to the best-known world
FORGET = 0.9995          # per-beat decay, so tastes can change over weeks
SCORE = {"smile": 2.2, "cheek": 0.6, "wonder": 1.2, "jaw": 0.4, "frown": -1.6, "browDown": -1.0, "sneer": -2.0}
LEAN_GAIN, AWAY, NOT_FACING, EYES_CLOSED = 1.5, -0.8, -0.5, -0.4
SIGNALS = ["smile", "cheek", "wonder", "jaw", "frown", "browDown", "sneer", "blink", "yaw", "pitch", "size"]


def _log_default(msg):
    print(time.strftime("%H:%M:%S"), msg, flush=True)


# ------------------------------------------------------------------ CLIP tokenizer (pure Python, no torch/transformers)

def _bytes_to_unicode():
    bs = list(range(ord("!"), ord("~") + 1)) + list(range(ord("¡"), ord("¬") + 1)) + list(range(ord("®"), ord("ÿ") + 1))
    cs, n = bs[:], 0
    for b in range(256):
        if b not in bs:
            bs.append(b); cs.append(256 + n); n += 1
    return dict(zip(bs, map(chr, cs)))


class ClipTokenizer:
    """OpenAI CLIP BPE. Matches transformers' CLIPTokenizer on plain prompts (checked in tests)."""
    PAT = re.compile(r"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[^\W\d_]+|\d|(?:[^\s\w]|_)+", re.IGNORECASE)

    def __init__(self, vocab_path, merges_path):
        self.encoder = json.loads(Path(vocab_path).read_text(encoding="utf-8"))
        lines = Path(merges_path).read_text(encoding="utf-8").split("\n")[1:]
        merges = [tuple(l.split()) for l in lines if len(l.split()) == 2]
        self.ranks = {m: i for i, m in enumerate(merges)}
        self.byte_enc = _bytes_to_unicode()
        self.sot, self.eot = self.encoder["<|startoftext|>"], self.encoder["<|endoftext|>"]
        self.cache = {}

    def _bpe(self, token):
        if token in self.cache:
            return self.cache[token]
        word = tuple(token[:-1]) + (token[-1] + "</w>",)
        while len(word) > 1:
            pairs = {(word[i], word[i + 1]) for i in range(len(word) - 1)}
            best = min(pairs, key=lambda p: self.ranks.get(p, math.inf))
            if best not in self.ranks:
                break
            a, b = best
            out, i = [], 0
            while i < len(word):
                if i < len(word) - 1 and word[i] == a and word[i + 1] == b:
                    out.append(a + b); i += 2
                else:
                    out.append(word[i]); i += 1
            word = tuple(out)
        self.cache[token] = word
        return word

    def encode(self, text):
        text = re.sub(r"\s+", " ", html.unescape(html.unescape(text))).strip().lower()
        ids = []
        for tok in self.PAT.findall(text):
            tok = "".join(self.byte_enc[b] for b in tok.encode("utf-8"))
            ids.extend(self.encoder[p] for p in self._bpe(tok))
        return ids

    def __call__(self, text, length=77):
        """Returns (ids [1, 77] int64, tokens used including start/end, whether it was cut off)."""
        ids = [self.sot] + self.encode(text)
        cut = len(ids) + 1 > length
        ids = ids[: length - 1] + [self.eot]
        used = len(ids)
        return np.array([ids + [self.eot] * (length - used)], np.int64), used, cut


# ------------------------------------------------------------------ the worlds

def parse_desires(path):
    style, names, scenes = "", [], []
    for raw in Path(path).read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if line.lower().startswith("style:"):
            style = line.split(":", 1)[1].strip()
        elif "|" in line:
            name, scene = (s.strip() for s in line.split("|", 1))
            if name and scene and name not in names:
                names.append(name); scenes.append(scene)
    if len(names) < 2:
        raise SystemExit(f"mirror: {path} needs at least two worlds (lines like 'Name | what to paint')")
    return style, names, [f"{s}, {style}" if style else s for s in scenes]


class Desires:
    """The worlds and their CLIP embeddings. Encoded once on this laptop and cached by content hash."""

    def __init__(self, path, model_dir, fetch, log=_log_default, providers=("CPUExecutionProvider",)):
        self.path = Path(path)
        style, self.names, self.prompts = parse_desires(self.path)
        key = hashlib.sha1(json.dumps(self.prompts).encode()).hexdigest()[:12]
        cache = Path(model_dir) / f"desires-{key}.npy"
        if cache.exists():
            E = np.load(cache)
            log(f"mirror: {len(self.names)} worlds loaded from {cache.name}")
        else:
            log(f"mirror: encoding {len(self.names)} worlds from {self.path.name} (first time, or the list changed)…")
            import onnxruntime as ort
            tok = ClipTokenizer(fetch("tokenizer/vocab.json", 1059962), fetch("tokenizer/merges.txt", 524619))
            opt = ort.SessionOptions(); opt.log_severity_level = 3
            # the encoder is an fp16 export: on a CPU each prompt can take a minute, on the graphics card a fraction of a second
            te = ort.InferenceSession(str(fetch("text_encoder.onnx", 246275486)), opt, providers=list(providers))
            log(f"mirror:   text encoder on {te.get_providers()[0].replace('ExecutionProvider', '')}")
            out = []
            for name, prompt in zip(self.names, self.prompts):
                ids, used, cut = tok(prompt)
                if cut:
                    log(f"mirror:   WARNING '{name}' is longer than 77 tokens and was cut off; shorten it in {self.path.name}")
                out.append(te.run(None, {"input_ids": ids})[0][0])
                log(f"mirror:   {name:20} {used:2}/77 tokens")
            E = np.stack(out).astype(np.float16)
            np.save(cache, E)
            for old in Path(model_dir).glob("desires-*.npy"):
                if old != cache:
                    old.unlink(missing_ok=True)
            log(f"mirror: worlds encoded and saved as {cache.name}")
        self.E = E.astype(np.float32).reshape(len(self.names), N_EMB)

    def embed(self, weights):
        idx = np.nonzero(weights > 0.004)[0]
        return weights[idx] @ self.E[idx]


# ------------------------------------------------------------------ people

class Profile:
    def __init__(self, pid, name, worlds, d=None):
        n = len(worlds)
        self.pid, self.name, self.worlds = pid, name, list(worlds)
        self.exemplars, self.beats, self.seeds = [], 0, {}
        self.base = None
        self.yscale = 0.15                                  # typical size of this person's reactions                                    # resting face, per signal
        self.P0 = np.eye(n) / PRIOR_SD ** 2
        self.A, self.b = self.P0.copy(), np.zeros(n)
        self.created = self.last_seen = time.time()
        self.seen = np.zeros(n)                             # beats each world was on screen (weighted)
        if d:
            self._load(d)
        self._mu = None

    def _load(self, d):
        self.exemplars = [list(map(float, e)) for e in d.get("exemplars", [])]
        self.beats, self.base = int(d.get("beats", 0)), d.get("base")
        self.yscale = float(d.get("yscale", self.yscale))
        self.seeds = {int(k): v for k, v in d.get("seeds", {}).items()}
        self.created, self.last_seen = d.get("created", self.created), d.get("last_seen", self.last_seen)
        old = d.get("worlds", [])
        if old:  # learned scores are kept by world name, so editing desires.txt keeps what still applies
            A, b, seen = np.array(d["A"]), np.array(d["b"]), np.array(d.get("seen", [0] * len(old)))
            pos = {w: i for i, w in enumerate(old)}
            keep = [(j, pos[w]) for j, w in enumerate(self.worlds) if w in pos]
            if keep:
                jn, jo = zip(*keep)
                self.A[np.ix_(jn, jn)], self.b[list(jn)], self.seen[list(jn)] = A[np.ix_(jo, jo)], b[list(jo)], seen[list(jo)]

    def to_json(self):
        return {"pid": self.pid, "name": self.name, "exemplars": [[round(v, 5) for v in e] for e in self.exemplars],
                "beats": self.beats, "base": self.base, "yscale": round(self.yscale, 4), "seeds": {str(k): v for k, v in self.seeds.items()},
                "created": self.created, "last_seen": self.last_seen, "worlds": self.worlds,
                "A": np.round(self.A, 5).tolist(), "b": np.round(self.b, 5).tolist(), "seen": np.round(self.seen, 2).tolist()}

    # affinity model: Bayesian linear regression, reaction ≈ Σ_i weight_shown_i · affinity_i
    def mean(self):
        if self._mu is None:
            self._mu = np.linalg.solve(self.A, self.b)
        return self._mu

    def sample(self, rng):
        cov = np.linalg.inv(self.A)
        L = np.linalg.cholesky((cov + cov.T) / 2 + 1e-9 * np.eye(len(cov)))
        return self.mean() + L @ rng.standard_normal(len(cov))

    def learn(self, x, y):
        self.A = FORGET * (self.A - self.P0) + self.P0 + np.outer(x, x) / NOISE_SD ** 2
        self.b = FORGET * self.b + x * y / NOISE_SD ** 2
        self.seen += x
        self.beats += 1
        self.yscale += (abs(y) - self.yscale) * 0.02
        self._mu = None

    def confidence(self):
        return 1 / np.sqrt(np.diag(self.A))   # posterior sd per world

    def top(self, k=5):
        mu, sd = self.mean(), self.confidence()
        order = np.argsort(-mu)[:k]
        return [{"world": self.worlds[i], "score": round(float(mu[i]), 3), "sure": round(float(1 - sd[i] / PRIOR_SD), 2),
                 "seen": round(float(self.seen[i]), 1)} for i in order]

    def distance(self, desc):
        if not self.exemplars:
            return math.inf
        return float(np.min(np.linalg.norm(np.asarray(self.exemplars) - desc, axis=1)))

    def add_exemplar(self, desc):
        """Keep up to 8 varied fingerprints (lighting, angle, glasses) so recognition gets steadier."""
        desc = [float(v) for v in desc]
        if not self.exemplars:
            self.exemplars.append(desc); return True
        d = np.linalg.norm(np.asarray(self.exemplars) - np.asarray(desc), axis=1)
        if d.min() < 0.22:
            return False
        if len(self.exemplars) >= 8:
            self.exemplars.pop(int(np.argmin(d)))   # replace the most redundant one
        self.exemplars.append(desc)
        return True


class Profiles:
    def __init__(self, path, worlds, log=_log_default):
        self.path, self.worlds, self.log = Path(path), list(worlds), log
        self.people, self.dirty, self.saved_at = {}, False, time.time()
        if self.path.exists():
            try:
                data = json.loads(self.path.read_text(encoding="utf-8"))
                for d in data.get("people", []):
                    p = Profile(d["pid"], d["name"], self.worlds, d)
                    self.people[p.pid] = p
                log(f"mirror: {len(self.people)} known people loaded from {self.path.name}: "
                    + (", ".join(f"{p.name} ({p.beats} reactions)" for p in self.people.values()) or "none"))
            except Exception as e:
                bad = self.path.with_suffix(f".broken-{int(time.time())}.json")
                self.path.replace(bad)
                log(f"mirror: {self.path.name} couldn't be read ({e}); moved it to {bad.name} and starting fresh")
        else:
            log(f"mirror: no one known yet; people are added as they look into the mirror ({self.path.name})")

    def match(self, desc):
        best, dist = None, math.inf
        for p in self.people.values():
            d = p.distance(desc)
            if d < dist:
                best, dist = p, d
        return best, dist

    def create(self, descs):
        n = 1
        while any(p.name == f"Guest {n}" for p in self.people.values()):
            n += 1
        pid = hashlib.sha1(f"{time.time()}{random.random()}".encode()).hexdigest()[:10]
        p = Profile(pid, f"Guest {n}", self.worlds)
        for d in descs:
            p.add_exemplar(d)
        self.people[pid] = p
        self.dirty = True
        return p

    def forget(self, pid):
        p = self.people.pop(pid, None)
        self.dirty = True
        return p

    def room_mean(self):
        """What the room as a whole likes: used when nobody is in front of the mirror."""
        if not self.people:
            return np.zeros(len(self.worlds))
        w = np.array([math.sqrt(p.beats + 1) for p in self.people.values()])
        return sum(wi * p.mean() for wi, p in zip(w, self.people.values())) / w.sum()

    def save(self, force=False):
        if not (self.dirty or force):
            return
        tmp = self.path.with_suffix(".tmp")
        tmp.write_text(json.dumps({"version": 1, "people": [p.to_json() for p in self.people.values()]}), encoding="utf-8")
        os.replace(tmp, self.path)
        self.dirty, self.saved_at = False, time.time()

    def save_if_due(self, every=15):
        if self.dirty and time.time() - self.saved_at > every:
            self.save()


# ------------------------------------------------------------------ one phone looking into the mirror

def ease(s):
    return s * s * (3 - 2 * s)


class MirrorSession:
    """All the state for one connected phone. Every method takes `now` (seconds) so it can be simulated."""

    def __init__(self, desires, profiles, log=_log_default, seed=None, who="a phone"):
        self.desires, self.profiles, self.log, self.who = desires, profiles, log, who
        self.n = len(desires.names)
        self.rng = np.random.default_rng(seed)
        self.viewer = None
        self.face_at = -1e9            # last time a face was seen
        self.id_reads = deque(maxlen=6)  # recent identity reads: (pid or None, dist, desc)
        self.samples = deque()         # (t, engagement or None for "away") for the current viewer
        self.warm = []                 # resting-face samples for a brand-new person
        self.shown = deque()           # (t, weights on screen)
        self.D = np.zeros(self.n)      # weights on screen now
        self.T = np.zeros(self.n)      # where the painting is heading
        self.t_target, self.reason = 0.0, ""
        self.seed_a = self.seed_b = int(self.rng.integers(1, 2 ** 31))
        self.seed_t, self.seed_s = 0.0, 1.0
        self.next_beat = 0.0
        self.recent = 0.0              # engagement, smoothed over beats
        self.last_y = None
        self.glow = 0.0                # live engagement for the phone's glow
        self.learning = True
        self.morph = MORPH_S
        self.events = deque(maxlen=12)
        self.last_tick = None
        self.room_next = 0.0
        self.room_pick(0.0, first=True)

    # ---------- what the painter needs
    def frame_params(self, now):
        s = ease(min(1.0, (now - self.seed_t) / SEED_MORPH_S)) if self.seed_a != self.seed_b else 1.0
        return self.D.copy(), self.seed_a, self.seed_b, s

    # ---------- events
    def event(self, msg, now=None):
        self.events.append(msg)
        self.log(f"mirror[{self.who}]: {msg}")

    def on_sample(self, s, now):
        """s: list of SIGNALS values, or None when the camera sees no face."""
        if s is not None:
            if len(s) != len(SIGNALS) or not all(isinstance(v, (int, float)) and math.isfinite(v) for v in s):
                return
            self.face_at = now
        v = self.viewer
        if v is None:
            return
        if s is None:
            self.samples.append((now, None)); self.glow *= 0.9
            return
        sig = dict(zip(SIGNALS, s))
        if v.base is None:  # learn this new face's resting expression first
            self.warm.append(sig)
            if len(self.warm) >= WARMUP_SAMPLES:
                v.base = {k: float(np.median([w[k] for w in self.warm])) for k in SIGNALS}
                self.warm = []
                self.event(f"learned {v.name}'s resting face; now watching reactions")
            return
        e = self.engagement(sig, v.base)
        self.samples.append((now, e))
        self.glow += (e - self.glow) * 0.2
        a = 0.004  # resting face drifts slowly (lighting, tiredness), so reactions stay relative to today
        for k in SIGNALS:
            v.base[k] += (sig[k] - v.base[k]) * a

    @staticmethod
    def engagement(sig, base):
        e = sum(w * (sig[k] - base[k]) for k, w in SCORE.items())
        e += LEAN_GAIN * max(-0.3, min(0.3, sig["size"] / max(base["size"], 1e-3) - 1))   # leaning in
        if abs(sig["yaw"]) > 0.35 or abs(sig["pitch"]) > 0.45:
            e += NOT_FACING
        if sig["blink"] > 0.6:
            e += EYES_CLOSED
        return e

    def on_identity(self, desc, now):
        desc = np.asarray(desc, np.float32)
        if desc.shape != (128,) or not np.isfinite(desc).all():
            return
        best, dist = self.profiles.match(desc)
        v = self.viewer
        if best is not None and (dist < MATCH_DIST or (best is v and dist < WEAK_DIST)):
            pid = best.pid
        else:
            pid = None
        self.id_reads.append((pid, dist, desc))
        reads = list(self.id_reads)
        if pid is not None and v is not None and pid == v.pid:
            if dist < 0.42 and v.add_exemplar(desc):
                self.profiles.dirty = True
            return
        if pid is not None:
            need = 1 if (v is None and dist < 0.42) else SWITCH_AFTER
            if len(reads) >= need and all(r[0] == pid for r in reads[-need:]):
                self.set_viewer(best, now, f"recognized {best.name} (face match {dist:.2f})")
            return
        unknown = 0
        for r in reversed(reads):
            if r[0] is not None:
                break
            unknown += 1
        if unknown >= NEW_AFTER:
            descs = [r[2] for r in reads[-unknown:]]
            if np.mean([np.linalg.norm(descs[0] - d) for d in descs[1:]]) > MATCH_DIST:
                self.event("couldn't get a steady read of the new face; still looking")
                self.id_reads.clear()
                return
            p = self.profiles.create(descs)
            closest = f"; closest known face was {best.name} at {dist:.2f}" if best else ""
            self.set_viewer(p, now, f"a new face: met {p.name}{closest}")
            self.id_reads.clear()

    def set_viewer(self, p, now, why):
        if self.viewer is not None:
            self.viewer.last_seen = time.time()
        self.viewer = p
        self.samples.clear(); self.warm = []; self.recent = 0.0; self.last_y = None
        self.id_reads.clear()
        self.event(why)
        if p is None:
            self.room_pick(now, first=True)
            return
        p.last_seen = time.time()
        self.profiles.dirty = True
        if p.beats >= 20:   # someone it knows: go straight to what they love most
            i = int(np.argmax(p.mean()))
            self.set_target(np.eye(self.n)[i], now, f"showing {p.name}'s favourite so far: {self.desires.names[i]}", morph=0.6)
            self.change_seed(now, self.pick_seed(p, favourite=True))
        else:
            self.pick(now, f"getting to know {p.name}", morph=0.8)

    def command(self, d, now):
        c = d.get("cmd")
        if c == "rename":
            p = self.profiles.people.get(str(d.get("pid")))
            name = re.sub(r"\s+", " ", str(d.get("name", ""))).strip()[:32]
            if p and name:
                self.event(f"{p.name} is now called {name}")
                p.name = name; self.profiles.dirty = True
        elif c == "forget":
            p = self.profiles.forget(str(d.get("pid")))
            if p:
                self.event(f"forgot {p.name} and everything learned about them")
                if self.viewer is p:
                    self.viewer = None; self.room_pick(now, first=True)
        elif c == "learn":
            self.learning = bool(d.get("on"))
            self.event("learning " + ("on" if self.learning else "paused"))
        elif c == "next":
            if self.viewer is not None and self.learning:
                x = self.window_mean(now - 3.0 - LAG_S, now - LAG_S)
                if x is not None:
                    self.viewer.learn(x, -0.3); self.profiles.dirty = True
            self.pick(now, "you asked for something else (counts as a mild no)")

    # ---------- the loop
    def tick(self, now):
        dt = 0.05 if self.last_tick is None else max(0.0, min(0.5, now - self.last_tick))
        self.last_tick = now
        if self.viewer is not None and now - self.face_at > LOST_AFTER_S:
            self.set_viewer(None, now, f"{self.viewer.name} stepped away; showing what the room likes")
        # melt toward the target
        k = 1 - math.exp(-dt / self.morph)
        self.D += (self.T - self.D) * k
        self.D[self.D < 0.008] = 0
        tot = self.D.sum()
        self.D = self.D / tot if tot > 0 else self.T.copy()
        self.shown.append((now, self.D.copy()))
        while self.shown and self.shown[0][0] < now - 8:
            self.shown.popleft()
        if self.seed_a != self.seed_b and now - self.seed_t >= SEED_MORPH_S:
            self.seed_a = self.seed_b
        if now >= self.next_beat:
            self.next_beat = now + BEAT_S
            self.beat(now)

    def window_mean(self, t0, t1):
        xs = [w for t, w in self.shown if t0 <= t <= t1]
        return np.mean(xs, axis=0) if xs else None

    def beat(self, now):
        v = self.viewer
        if v is None:
            if now >= self.room_next:
                self.room_pick(now)
            return
        win = [(t, e) for t, e in self.samples if t > now - BEAT_S]
        while self.samples and self.samples[0][0] < now - 6:
            self.samples.popleft()
        y = None
        if v.base is not None and len(win) >= 5:
            present = [e for _, e in win if e is not None]
            away = 1 - len(present) / len(win)
            y = (float(np.mean(present)) if present else 0.0) + AWAY * away
            x = self.window_mean(now - BEAT_S - LAG_S, now - LAG_S)
            if self.learning and x is not None and away < 0.5:
                v.learn(x, y)
                self.profiles.dirty = True
                if self.seed_a == self.seed_b:   # credit the layout too, once it's fully on screen
                    s = v.seeds.setdefault(self.seed_b, [0.0, 0])
                    s[0] += y; s[1] += 1
            self.recent = 0.5 * self.recent + 0.5 * y
            self.last_y = y
        self.decide(now, y)

    def decide(self, now, y):
        v, dwell = self.viewer, now - self.t_target
        if dwell < MIN_DWELL_S or v.base is None:
            return
        unit = max(0.04, v.yscale)   # what counts as a reaction depends on how expressive this face is
        mu = v.mean()
        on_fav = int(np.argmax(self.T)) == int(np.argmax(mu)) and v.beats > 60
        if y is not None and self.recent > ENGAGED * unit and dwell < LINGER_MAX_S:
            comps = np.nonzero(self.T)[0]
            if len(comps) > 1:  # deepen: lean the mix toward the part they seem to like more
                best = comps[np.argmax(mu[comps])]
                if self.T[best] < 0.9:
                    self.T[comps] *= 0.9; self.T[best] += 1 - self.T.sum()
                    self.reason = f"lingering: {v.name} is lighting up, leaning into {self.desires.names[best]}"
                    self.event(self.reason)
            return
        if self.recent < DISLIKED * unit:
            self.pick(now, f"{v.name} didn't light up at this (reaction {self.recent:+.2f}); moving on")
        elif dwell > (WANDER_S * 2 if on_fav else WANDER_S):
            self.pick(now, f"wandering on after {dwell:.0f} s (reaction {self.recent:+.2f})")

    def pick(self, now, why, morph=MORPH_S):
        v = self.viewer
        if v is None:
            a = self.profiles.room_mean() + self.rng.normal(0, 0.08, self.n)
        elif self.rng.random() < 0.6 * v.beats / (v.beats + EXPLOIT_AT):
            a, why = v.mean(), why + " (going with what it knows)"
        else:
            a = v.sample(self.rng)   # Thompson sampling: plausible favourites, including ones it isn't sure about yet
        order = np.argsort(-a)
        T = np.zeros(self.n)
        if self.rng.random() < BLEND_P:
            T[order[0]], T[order[1]] = 0.65, 0.35
        else:
            T[order[0]] = 1.0
        same = int(order[0]) == int(np.argmax(self.T)) if self.T.any() else False
        self.set_target(T, now, why, morph)
        if same or v is None or self.rng.random() < 0.5:   # a fresh look at the same place, or a new view
            self.change_seed(now, self.pick_seed(v))

    def set_target(self, T, now, why, morph=MORPH_S):
        self.T, self.t_target, self.morph = T, now, morph
        self.recent = 0.0
        mix = " + ".join(f"{self.desires.names[i]}" + (f" {T[i]:.0%}" if T[i] < 1 else "") for i in np.argsort(-T) if T[i] > 0)
        self.reason = f"{why} -> {mix}"
        self.event(self.reason)

    def pick_seed(self, v, favourite=False):
        if v is not None and v.seeds:
            total = sum(n for _, n in v.seeds.values()) + 1
            scored = {s: sm / n + (0 if favourite else 0.3 * math.sqrt(math.log(total) / n)) for s, (sm, n) in v.seeds.items() if n}
            explore = 0.35 / (1 + v.beats / 150)
            if scored and (favourite or (len(scored) >= 3 and self.rng.random() > explore)):
                return int(max(scored, key=scored.get))
        return int(self.rng.integers(1, 2 ** 31))

    def change_seed(self, now, seed):
        if seed == self.seed_b:
            return
        if self.seed_a != self.seed_b:   # still melting from the last layout: finish it from where it is
            self.seed_a = self.seed_b if now - self.seed_t > SEED_MORPH_S / 2 else self.seed_a
        self.seed_b, self.seed_t = seed, now
        v = self.viewer
        if v is not None and len(v.seeds) > 12:   # keep the 12 best layouts
            worst = min(v.seeds, key=lambda s: v.seeds[s][0] / max(1, v.seeds[s][1]))
            if worst != seed:
                v.seeds.pop(worst)

    def room_pick(self, now, first=False):
        self.room_next = now + 18
        mu = self.profiles.room_mean()
        cur = int(np.argmax(self.T)) if self.T.any() else -1
        cands = [int(i) for i in np.argsort(-mu)[:6] if int(i) != cur] if self.profiles.people else [i for i in range(self.n) if i != cur]
        i = int(self.rng.choice(cands))
        T = np.zeros(self.n); T[i] = 1
        self.set_target(T, now, "nobody's looking: drifting through what the room likes" if not first else "waiting for someone to look", morph=2.5)
        if first and not self.D.any():
            self.D = T.copy()
        self.change_seed(now, int(self.rng.integers(1, 2 ** 31)))

    # ---------- what the phone shows
    def status(self, now):
        v = self.viewer
        looking = now - self.face_at < 1.5
        showing = [[self.desires.names[i], round(float(self.D[i]), 2)] for i in np.argsort(-self.D)[:3] if self.D[i] > 0.05]
        out = {"seeing": "person" if v else ("identifying" if looking else "nobody"),
               "who": {"pid": v.pid, "name": v.name, "reactions": v.beats, "warming": v.base is None} if v else None,
               "showing": showing, "glow": round(max(-1.0, min(1.0, self.glow)), 2), "recent": round(self.recent, 2),
               "learning": self.learning, "why": self.reason}
        if v is not None:
            out["top"] = v.top(5)
        return out

    def people(self):
        return [{"pid": p.pid, "name": p.name, "reactions": p.beats, "top": p.top(3), "faces": len(p.exemplars)}
                for p in sorted(self.profiles.people.values(), key=lambda p: -p.last_seen)]
