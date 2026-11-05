"""
Moodscape Mirror: a painting that evolves, in real time, toward what the person looking at it reacts to.

Two editions: Bliss keeps what you light up at; Unease keeps what makes you tense up while you keep watching.

The phone is only the eye. It sends face numbers (smile, brows, eyes, head turn, how close you are) about
14 times a second, and every ~1.3-3 s a 128-number face fingerprint so this laptop knows WHO is looking.
Everything else happens here:

    the space       every painting is one choice per section of mirror-bliss.txt / mirror-unease.txt
                    (subject, place, detail, time, weather, lighting, palette, style, framing): billions
                    of paintings, not a fixed list
    who is it?      fingerprint -> nearest known person, or a new guest after a few unknown reads.
                    Or, if the viewer didn't agree to face recognition, one profile per device and no
                    fingerprint at all
    what do they    an affinity per option, learned by Bayesian linear regression from "what was on
    desire?         screen 0.7 s ago" -> "how their face responded". Every reaction teaches it about all
                    seven parts of the painting at once
    real time       every ~2.5 s it changes one part (guided by Thompson sampling), watches the next
                    ~1.5 s of your face, and keeps the change if you warmed to it or undoes it if you
                    cooled. It slows down to let you enjoy what you light up at, and jumps further
                    when nothing is landing
    memory          your strongest moments (exact painting + layout) are saved; when it recognizes
                    you, it opens on your best one
    how it looks    prompts are encoded on the GPU as needed and the painting melts between them;
                    layouts change by slerping the noise, so nothing ever cuts

Profiles (names, face fingerprints, learned affinities, best moments) are saved in profiles.json next to
this file. They never leave this laptop. Delete the file to forget everyone, or use "Forget" on the phone.
"""
import hashlib, html, json, math, os, random, re, threading, time
from collections import OrderedDict, deque
from pathlib import Path

import numpy as np

N_EMB = 77 * 768

# ------------------------------------------------------------------ tuning
# who is looking
MATCH_DIST = 0.50        # face fingerprints closer than this are the same person (face-api: same person ~0.3-0.45)
WEAK_DIST = 0.58         # ...and up to this, if it's the person already in front of the mirror
NEW_AFTER = 3            # unknown reads in a row before a new guest profile is made
SWITCH_AFTER = 2         # reads in a row naming someone else before the mirror switches person
LOST_AFTER_S = 4.0       # no face for this long = nobody is looking
WARMUP_SAMPLES = 25      # a new face's resting expression is averaged over this many samples first
# learning
BEAT_S = 1.0             # the affinity model learns once a second
LAG_S = 0.7              # stream latency + human reaction time: a reaction belongs to what was on screen this long ago
PRIOR_SD = 0.25          # prior spread of a person's affinity for one option (face reactions are small numbers)
NOISE_SD = 0.45          # how noisy one beat of face reaction is
PAIR_SD = 0.35           # how noisy a before/after comparison of one change is (cleaner: slow drift cancels out)
FORGET = 0.9997          # per-beat decay, so tastes can change over weeks
# the real-time loop
MORPH_S = 0.9            # how long the painting takes to melt into a change
SEED_MORPH_S = 1.8       # how long a layout change takes
JUDGE_FROM_S, JUDGE_TO_S = 0.7, 2.2   # after a change lands, the face is judged over this window...
BEFORE_S = 1.2           # ...against this much time just before it
STEP_S = 2.6             # time between changes
SAVOR_S = 6.0            # ...while the person is lighting up, it holds the painting this long instead
KEEP, UNDO = 0.35, -0.45 # a change is a clear win / loss when the face moves this many "typical reactions"
JUMP_AFTER = 5           # changes in a row without a win before it jumps further (three parts at once)
SEED_P = 0.12            # chance a change is a new layout instead of a new part
EXPLOIT_AT = 150         # after this many beats, about a third of the choices go straight to the best-known option
# reading the face: what counts as a reaction in each edition
EDITIONS = {
    "bliss": {   # lighting up: smiles that reach the eyes, wonder, leaning in. Frowns, disgust, looking away count against
        "score": {"smile": 2.2, "cheek": 0.6, "wonder": 1.2, "jaw": 0.4, "frown": -1.6, "browDown": -1.0, "sneer": -2.0, "tension": -0.6},
        "lean": 1.5, "turned": -0.5, "eyes_closed": -0.4,
    },
    "unease": {  # tensing up while still watching: furrowed brows, a frown, a wrinkled nose, pressed lips, a wince,
                 # wide alarmed eyes, pulling back, a flinch. A smile means it isn't working. Leaving means it lost you
        "score": {"frown": 1.2, "browDown": 1.0, "sneer": 1.6, "tension": 1.4, "squint": 0.6, "wonder": 0.6, "smile": -1.5, "cheek": -0.5},
        "lean": -1.0, "turned": 0.3, "eyes_closed": 0.2,
    },
}
AWAY = -0.8              # the face leaving the frame: in either edition, the mirror lost them
TURN_YAW, TURN_PITCH = 0.3, 0.35   # head turned / tilted this far from the person's own resting pose = looking elsewhere
SIGNALS = ["smile", "cheek", "wonder", "jaw", "frown", "browDown", "sneer", "blink", "yaw", "pitch", "size", "tension", "squint"]
CAPTION_SLOTS = ("subject", "place", "detail", "time", "weather", "style")


def _log_default(msg):
    print(time.strftime("%H:%M:%S"), msg, flush=True)


def ease(s):
    s = max(0.0, min(1.0, s))
    return s * s * (3 - 2 * s)


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


# ------------------------------------------------------------------ the space of paintings

class Space:
    """mirror-space.txt: sections of options. A painting ("choice") is one option index per section."""

    def __init__(self, path, log=_log_default, edition=None):
        self.path, self.log = Path(path), log
        self.edition = edition or self.path.stem.replace("mirror-", "")
        self.slots, self.labels, self.phrases, self.ending = [], [], [], ""
        cur = None
        for raw in self.path.read_text(encoding="utf-8").splitlines():
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            m = re.match(r"^\[\s*([\w -]+?)\s*\]$", line)
            if m:
                cur = m.group(1).lower()
                if cur not in self.slots:
                    self.slots.append(cur); self.labels.append([]); self.phrases.append([])
                continue
            if line.lower().startswith("ending:"):
                self.ending = line.split(":", 1)[1].strip(); continue
            if cur is None or "|" not in line:
                continue
            label, phrase = (s.strip() for s in line.split("|", 1))
            j = self.slots.index(cur)
            if label and phrase and label not in self.labels[j]:
                self.labels[j].append(label); self.phrases[j].append("" if phrase.lower() == "(none)" else phrase)
        keep = [j for j, l in enumerate(self.labels) if l]
        self.slots = [self.slots[j] for j in keep]; self.labels = [self.labels[j] for j in keep]; self.phrases = [self.phrases[j] for j in keep]
        if not self.slots:
            raise SystemExit(f"mirror: {self.path} has no sections; it needs [place] and lines like 'Label | words'")
        self.sizes = [len(l) for l in self.labels]
        self.offsets = np.cumsum([0] + self.sizes[:-1]).tolist()
        self.K = int(sum(self.sizes))
        self.keys = [f"{s}:{l}" for s, labels in zip(self.slots, self.labels) for l in labels]
        self.cache, self.lock = OrderedDict(), threading.Lock()
        self.tok = self.te = None
        self.encode_ms = 0.0
        log(f"mirror: {self.edition} has {' x '.join(f'{n} {s}' for s, n in zip(self.slots, self.sizes))} = {math.prod(self.sizes):,} paintings")

    # --- text
    def prompt(self, c):
        words = [self.phrases[j][c[j]] for j in range(len(self.slots))]
        first = words[0]
        if len(self.slots) > 1 and self.slots[1] == "detail" and words[1]:
            first, words = f"{first} {words[1]}", [first] + [""] + words[2:]
        parts = [first] + [w for w in words[1:] if w] + ([self.ending] if self.ending else [])
        return ", ".join(p for p in parts if p)

    def caption(self, c):
        out = []
        for j, s in enumerate(self.slots):
            if s in CAPTION_SLOTS and self.phrases[j][c[j]]:
                out.append(self.labels[j][c[j]])
        return out[:5] or [self.labels[0][c[0]]]

    def describe(self, c):
        return " / ".join(f"{s} {self.labels[j][c[j]]}" for j, s in enumerate(self.slots))

    def features(self, c):
        x = np.zeros(self.K)
        for j, o in enumerate(c):
            x[self.offsets[j] + o] = 1.0
        return x

    def to_labels(self, c):
        return {s: self.labels[j][c[j]] for j, s in enumerate(self.slots)}

    def from_labels(self, d):
        try:
            return tuple(self.labels[j].index(d[s]) for j, s in enumerate(self.slots))
        except (KeyError, ValueError):
            return None

    def random(self, rng):
        return tuple(int(rng.integers(n)) for n in self.sizes)

    # --- embeddings
    def attach_encoder(self, model_dir, fetch, providers=("CPUExecutionProvider",), share=None):
        """Load the CLIP text encoder (or share another Space's: both editions use one copy on the GPU)."""
        if share is not None:
            self.tok, self.te = share.tok, share.te
        else:
            import onnxruntime as ort
            self.tok = ClipTokenizer(fetch("tokenizer/vocab.json", 1059962), fetch("tokenizer/merges.txt", 524619))
            opt = ort.SessionOptions(); opt.log_severity_level = 3
            # an fp16 export: on a CPU one prompt can take most of a minute, on the graphics card a few milliseconds
            self.te = ort.InferenceSession(str(fetch("text_encoder.onnx", 246275486)), opt, providers=list(providers))
        on = self.te.get_providers()[0].replace("ExecutionProvider", "")
        worst = max(((self.tok(self.prompt(c))[1], c) for c in self._longest()), key=lambda t: t[0])
        if worst[0] >= 77:
            self.log(f"mirror: WARNING the longest combination is cut off at 77 tokens: {self.prompt(worst[1])!r}. Shorten some lines.")
        t0 = time.perf_counter(); self.encode(tuple(0 for _ in self.slots)); first = (time.perf_counter() - t0) * 1000
        t0 = time.perf_counter(); self.encode(tuple(min(1, n - 1) for n in self.sizes)); self.encode_ms = (time.perf_counter() - t0) * 1000
        self.log(f"mirror: {self.edition}: text encoder on {on}: {self.encode_ms:.0f} ms per new painting (first {first:.0f} ms); longest prompt {worst[0]}/77 tokens")
        if self.encode_ms > 1500:
            self.log("mirror: WARNING encoding is slow (no graphics card?); the mirror will change much more slowly than intended")

    def _longest(self):
        idx = []
        for j in range(len(self.slots)):
            idx.append(max(range(self.sizes[j]), key=lambda o: len(self.tok.encode(self.phrases[j][o])) if self.tok else len(self.phrases[j][o])))
        return [tuple(idx)]

    def encode(self, c):
        p = self.prompt(c)
        with self.lock:
            hit = self.cache.get(p)
            if hit is not None:
                self.cache.move_to_end(p)
                return hit.astype(np.float32).ravel()
        ids, _, _ = self.tok(p)
        e = self.te.run(None, {"input_ids": ids})[0][0].astype(np.float16)
        with self.lock:
            self.cache[p] = e
            while len(self.cache) > 600:      # ~70 MB of recent paintings
                self.cache.popitem(last=False)
        return e.astype(np.float32).ravel()


# ------------------------------------------------------------------ people

class Taste:
    """What one person reacts to in one edition: an affinity per option (Bayesian linear regression,
    reaction ≈ Σ options on screen · affinity), their best moments, and their favourite layouts."""

    def __init__(self, space, d=None):
        self.space, K = space, space.K
        self.beats, self.seeds, self.moments = 0, {}, []
        self.yscale = 0.15                                  # typical size of this person's reactions in this edition
        self.P0 = np.eye(K) / PRIOR_SD ** 2
        self.A, self.b, self.seen = self.P0.copy(), np.zeros(K), np.zeros(K)
        if d:
            self._load(d)
        self._mu = self._L = None

    def _load(self, d):
        self.beats, self.yscale = int(d.get("beats", 0)), float(d.get("yscale", self.yscale))
        self.seeds = {int(k): v for k, v in d.get("seeds", {}).items()}
        self.moments = [m for m in d.get("moments", []) if isinstance(m, dict) and "choice" in m]
        old = d.get("keys") or []
        if not old:
            return
        A, b, seen = np.array(d["A"]), np.array(d["b"]), np.array(d.get("seen", [0] * len(old)))
        pos = {k: i for i, k in enumerate(old)}
        keep = [(j, pos[k]) for j, k in enumerate(self.space.keys) if k in pos]   # learned scores are kept by section:label
        if keep:
            jn, jo = map(list, zip(*keep))
            self.A[np.ix_(jn, jn)], self.b[jn], self.seen[jn] = A[np.ix_(jo, jo)], b[jo], seen[jo]

    def to_json(self):
        return {"beats": self.beats, "yscale": round(self.yscale, 4), "seeds": {str(k): v for k, v in self.seeds.items()},
                "moments": self.moments, "keys": self.space.keys,
                "A": np.round(self.A, 4).tolist(), "b": np.round(self.b, 5).tolist(), "seen": np.round(self.seen, 2).tolist()}

    def mean(self):
        if self._mu is None:
            self._mu = np.linalg.solve(self.A, self.b)
        return self._mu

    def sample(self, rng):
        if self._L is None:
            cov = np.linalg.inv(self.A)
            self._L = np.linalg.cholesky((cov + cov.T) / 2 + 1e-9 * np.eye(len(cov)))
        return self.mean() + self._L @ rng.standard_normal(len(self.b))

    def learn(self, x, y):
        self.A = FORGET * (self.A - self.P0) + self.P0 + np.outer(x, x) / NOISE_SD ** 2
        self.b = FORGET * self.b + x * y / NOISE_SD ** 2
        self.seen += x
        self.beats += 1
        self.yscale += (abs(y) - self.yscale) * 0.02
        self._mu = self._L = None

    def learn_pair(self, dx, d):
        """A judged change: the face moved by d when the painting changed by dx. Drift in the resting face cancels."""
        self.A = self.A + np.outer(dx, dx) / PAIR_SD ** 2
        self.b = self.b + dx * d / PAIR_SD ** 2
        self._mu = self._L = None

    def best_choice(self):
        mu, sp = self.mean(), self.space
        return tuple(int(np.argmax(mu[o:o + n])) for o, n in zip(sp.offsets, sp.sizes))

    def remember(self, c, seed, y):
        """Keep the strongest moments: exact painting + layout, with a running average of reactions."""
        labels = self.space.to_labels(c)
        for m in self.moments:
            if m["choice"] == labels and m["seed"] == seed:
                m["n"] += 1; m["score"] += (y - m["score"]) / m["n"]; break
        else:
            self.moments.append({"choice": labels, "seed": int(seed), "score": round(float(y), 4), "n": 1, "at": int(time.time())})
        self.moments.sort(key=lambda m: -m["score"])
        del self.moments[12:]

    def best_moment(self):
        """The moment to open on: what the model now predicts for it, plus what was actually seen (one lucky
        reaction alone shouldn't win). Returns (choice, seed, moment, value) or None."""
        mu, best = self.mean(), None
        for m in self.moments:
            c = self.space.from_labels(m["choice"])
            if c is not None:
                v = float(self.space.features(c) @ mu) + m["score"] * m["n"] / (m["n"] + 2)
                if best is None or v > best[3]:
                    best = (c, m["seed"], m, v)
        return best

    def top(self, k=6):
        mu, sd, sp = self.mean(), 1 / np.sqrt(np.diag(self.A)), self.space
        out = []
        for i in np.argsort(-mu):
            slot, label = sp.keys[i].split(":", 1)
            j = sp.slots.index(slot)
            if sp.phrases[j][sp.labels[j].index(label)] == "":
                continue  # "nothing extra" isn't interesting to list
            out.append({"slot": slot, "item": label, "score": round(float(mu[i]), 3), "sure": round(float(1 - sd[i] / PRIOR_SD), 2)})
            if len(out) >= k:
                break
        return out


class Profile:
    """A person: name, face fingerprints (or a device), resting face, and one Taste per edition."""

    def __init__(self, pid, name, d=None):
        self.pid, self.name = pid, name
        self.exemplars, self.base, self.device = [], None, None
        self.created = self.last_seen = time.time()
        self.tastes, self._raw = {}, {}
        if d:
            self.exemplars = [list(map(float, e)) for e in d.get("exemplars", [])]
            self.base, self.device = d.get("base"), d.get("device")
            if self.base is not None:   # profiles from before tension/squint were read: start those at rest
                for k in SIGNALS:
                    self.base.setdefault(k, 0.0)
            self.created, self.last_seen = d.get("created", self.created), d.get("last_seen", self.last_seen)
            if "tastes" in d:
                self._raw = dict(d["tastes"])
            elif d.get("version", 1) >= 2 and "keys" in d:   # the second Mirror had one taste: it was Bliss
                self._raw = {"bliss": d}
            # the first Mirror (fixed worlds): keep who they are; taste starts fresh

    def taste(self, space):
        t = self.tastes.get(space.edition)
        if t is None:
            t = self.tastes[space.edition] = Taste(space, self._raw.pop(space.edition, None))
        return t

    def reactions(self):
        return sum(t.beats for t in self.tastes.values()) + sum(int(r.get("beats", 0)) for r in self._raw.values())

    def to_json(self):
        tastes = dict(self._raw)
        tastes.update({e: t.to_json() for e, t in self.tastes.items()})
        return {"version": 3, "pid": self.pid, "name": self.name, "exemplars": [[round(v, 5) for v in e] for e in self.exemplars],
                "device": self.device, "base": self.base, "created": self.created, "last_seen": self.last_seen, "tastes": tastes}

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
    def __init__(self, path, log=_log_default):
        self.path, self.log = Path(path), log
        self.people, self.dirty, self.saved_at = {}, False, time.time()
        if self.path.exists():
            try:
                data = json.loads(self.path.read_text(encoding="utf-8"))
                for d in data.get("people", []):
                    p = Profile(d["pid"], d["name"], d)
                    self.people[p.pid] = p
                log(f"mirror: {len(self.people)} known people loaded from {self.path.name}: "
                    + (", ".join(f"{p.name} ({p.reactions()} reactions)" for p in self.people.values()) or "none"))
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

    def _new(self, name):
        pid = hashlib.sha1(f"{time.time()}{random.random()}".encode()).hexdigest()[:10]
        p = self.people[pid] = Profile(pid, name)
        self.dirty = True
        return p

    def create(self, descs):
        n = 1
        while any(p.name == f"Guest {n}" for p in self.people.values()):
            n += 1
        p = self._new(f"Guest {n}")
        for d in descs:
            p.add_exemplar(d)
        return p

    def for_device(self, device):
        """No face recognition: one profile per phone, keyed by a random id the phone made up. No biometrics stored."""
        for p in self.people.values():
            if p.device == device:
                return p
        p = self._new("You")
        p.device = device
        return p

    def forget(self, pid):
        p = self.people.pop(pid, None)
        self.dirty = True
        return p

    def room_mean(self, space):
        """What the room as a whole reacts to in this edition: used when nobody is in front of the mirror."""
        ts = [p.taste(space) for p in self.people.values()]
        if not ts:
            return np.zeros(space.K)
        w = np.array([math.sqrt(t.beats + 1) for t in ts])
        return sum(wi * t.mean() for wi, t in zip(w, ts)) / w.sum()

    def save(self, force=False):
        if not (self.dirty or force):
            return
        tmp = self.path.with_suffix(".tmp")
        tmp.write_text(json.dumps({"version": 3, "people": [p.to_json() for p in self.people.values()]}), encoding="utf-8")
        os.replace(tmp, self.path)
        self.dirty, self.saved_at = False, time.time()

    def save_if_due(self, every=15):
        if self.dirty and time.time() - self.saved_at > every:
            self.save()


# ------------------------------------------------------------------ one phone looking into the mirror

class MirrorSession:
    """All the state for one connected phone. Every method takes `now` (seconds) so it can be simulated.

    The server asks `want()` for a painting that needs encoding, encodes it on the GPU, and hands it back with
    `arrive()`; it renders with `frame_params()`."""

    def __init__(self, spaces, profiles, log=_log_default, seed=None, who="a phone", edition="bliss"):
        self.spaces, self.profiles, self.log, self.who = spaces, profiles, log, who
        self.edition = edition if edition in spaces else next(iter(spaces))
        self.space = spaces[self.edition]
        self.device_profile = None         # set when the viewer declined face recognition: one profile per phone
        self.rng = np.random.default_rng(seed)
        self.viewer = None
        self.face_at = -1e9
        self.id_reads = deque(maxlen=6)    # recent identity reads: (pid or None, dist, desc)
        self.samples = deque()             # (t, engagement or None for "away") for the current viewer
        self.warm = []
        self.shown = deque()               # (t, features on screen)
        self.learning = True
        self.events = deque(maxlen=12)
        self.reason = ""
        # what's on screen: melting from `src` to `dst`
        self.src = self.dst = None         # (choice, embedding)
        self.m_t0 = -1e9
        self.seed_a = self.seed_b = int(self.rng.integers(1, 2 ** 31))
        self.seed_t = -1e9
        # the loop
        self.pending = None                # (choice, seed or None, kind, why) waiting for its embedding
        self.change = None                 # the last change, while its reaction is judged
        self.next_step = 0.0
        self.misses = 0                    # changes in a row without a win
        self.recent = 0.0                  # engagement, smoothed over beats
        self.glow = 0.0
        self.next_beat = 0.0
        self.last_tick = None
        self.room_mode(0.0, "waiting for someone to look")

    def taste(self):
        return self.viewer.taste(self.space) if self.viewer is not None else None

    # ---------- for the server
    def want(self):
        return self.pending[0] if self.pending else None

    def arrive(self, choice, emb, now):
        if not self.pending or self.pending[0] != choice:
            return
        _, seed, kind, why = self.pending
        self.pending = None
        if self.dst is None:                          # the very first painting
            self.src = self.dst = (choice, emb)
        else:
            self.src = (self.dst[0], self._current_emb(now))
            self.dst = (choice, emb)
        self.m_t0 = now
        if seed is not None and seed != self.seed_b:
            if now - self.seed_t < SEED_MORPH_S:      # still melting from the last layout: finish from the nearer end
                self.seed_a = self.seed_b if now - self.seed_t > SEED_MORPH_S / 2 else self.seed_a
            else:
                self.seed_a = self.seed_b
            self.seed_b, self.seed_t = int(seed), now
        if kind in ("step", "revert") and self.change is not None:
            self.change["t"] = now
        self.event(why)

    def _current_emb(self, now):
        a, b = self.src[1], self.dst[1]
        m = ease((now - self.m_t0) / MORPH_S)
        return b if m >= 1 else a * (1 - m) + b * m

    def frame_params(self, now):
        if self.dst is None:
            return None
        m = ease((now - self.m_t0) / MORPH_S)
        s = ease((now - self.seed_t) / SEED_MORPH_S) if self.seed_a != self.seed_b else 1.0
        return self.src[1], self.dst[1], m, self.seed_a, self.seed_b, s

    # ---------- inputs
    def event(self, msg):
        self.reason = msg
        self.events.append(msg)
        self.log(f"mirror[{self.who}]: {msg}")

    def on_sample(self, s, now):
        """s: list of SIGNALS values, or None when the camera sees no face."""
        if s is not None:
            if len(s) == len(SIGNALS) - 2:      # an older phone page without tension/squint
                s = list(s) + [0.0, 0.0]
            if len(s) != len(SIGNALS) or not all(isinstance(v, (int, float)) and math.isfinite(v) for v in s):
                return
            self.face_at = now
            if self.viewer is None and self.device_profile is not None:
                self.set_viewer(self.device_profile, now, "welcome back" if self.device_profile.base else "hello: getting to know you")
        v = self.viewer
        if v is None:
            return
        if s is None:
            self.samples.append((now, None)); self.glow *= 0.9
            return
        sig = dict(zip(SIGNALS, s))
        if v.base is None:  # learn this new face's resting expression (and how the phone sees their head) first
            self.warm.append(sig)
            if len(self.warm) >= WARMUP_SAMPLES:
                v.base = {k: float(np.median([w[k] for w in self.warm])) for k in SIGNALS}
                self.warm = []
                self.event(f"learned {v.name}'s resting face; now watching reactions")
            return
        e = self.engagement(sig, v.base, self.edition)
        self.samples.append((now, e))
        self.glow += (e - self.glow) * 0.2
        a = 0.0005  # resting face drifts over minutes (lighting, tiredness, how the phone is propped); faster would cancel real reactions
        for k in SIGNALS:
            v.base[k] += (sig[k] - v.base[k]) * a

    @staticmethod
    def engagement(sig, base, edition="bliss"):
        ed = EDITIONS[edition]
        e = sum(w * (sig[k] - base[k]) for k, w in ed["score"].items())
        e += ed["lean"] * max(-0.3, min(0.3, sig["size"] / max(base["size"], 1e-3) - 1))   # leaning in (or pulling back)
        if abs(sig["yaw"] - base["yaw"]) > TURN_YAW or abs(sig["pitch"] - base["pitch"]) > TURN_PITCH:
            e += ed["turned"]   # measured from this person's own resting pose, so a phone propped at an angle doesn't count
        if sig["blink"] > max(0.6, base["blink"] + 0.35):
            e += ed["eyes_closed"]
        return e

    def on_identity(self, desc, now):
        desc = np.asarray(desc, np.float32)
        if self.device_profile is not None or desc.shape != (128,) or not np.isfinite(desc).all():
            return
        best, dist = self.profiles.match(desc)
        v = self.viewer
        pid = best.pid if best is not None and (dist < MATCH_DIST or (best is v and dist < WEAK_DIST)) else None
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
        self.samples.clear(); self.warm = []; self.recent = 0.0; self.misses = 0
        self.id_reads.clear(); self.change = None
        self.event(why)
        if p is None:
            self.room_mode(now, "showing what the room likes")
            return
        p.last_seen = time.time()
        self.profiles.dirty = True
        self.open_for(p, now)

    def open_for(self, p, now):
        t, ed = p.taste(self.space), self.edition
        if t.beats >= 30:   # someone it knows: open on their best moment, or on their favourite parts if it predicts those higher
            bm, c = t.best_moment(), t.best_choice()
            if bm and bm[3] >= float(self.space.features(c) @ t.mean()):
                c, seed, m, _ = bm
                self.request(c, seed, "open", f"{p.name}'s best {ed} moment so far (reaction {m['score']:+.2f}): {self.space.describe(c)}")
            else:
                self.request(c, self.pick_seed(t, True), "open", f"{p.name}'s strongest {ed} parts so far: {self.space.describe(c)}")
        else:
            c = tuple(self.thompson_slot(t.sample(self.rng), j) for j in range(len(self.space.slots)))
            self.request(c, None, "open", f"getting to know {p.name} in {ed}: starting with {self.space.describe(c)}")
        self.next_step = now + STEP_S + 1.0

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
                    self.viewer = None; self.room_mode(now, "showing what the room likes")
        elif c == "learn":
            self.learning = bool(d.get("on"))
            self.event("learning " + ("on" if self.learning else "paused"))
        elif c == "next":
            t = self.taste()
            if t is not None and self.learning and self.dst is not None:
                t.learn(self.space.features(self.dst[0]), -max(0.04, t.yscale)); self.profiles.dirty = True
            self.change = None
            self.jump(now, "you asked for something else (counts as a no for this painting)")
        elif c == "edition":
            ed = str(d.get("edition"))
            if ed in self.spaces and ed != self.edition:
                self.edition, self.space = ed, self.spaces[ed]
                self.change, self.pending, self.misses, self.recent = None, None, 0, 0.0
                self.samples.clear(); self.shown.clear()   # what was on screen was described in the other edition's terms
                self.dst = None if self.dst is None else (None, self.dst[1])   # melt from what's on screen into the new edition
                self.event(f"switched to {ed}")
                if self.viewer is not None:
                    self.open_for(self.viewer, now)
                else:
                    self.room_mode(now, f"{ed}: waiting for someone to look")
        elif c == "device":
            dev = re.sub(r"[^A-Za-z0-9-]", "", str(d.get("device", "")))[:40]
            if len(dev) >= 8:
                self.device_profile = self.profiles.for_device(dev)
                self.event("face recognition is off: this mirror belongs to this phone, no face fingerprint is made or stored")

    # ---------- the loop
    def tick(self, now):
        self.last_tick = now
        if self.viewer is not None and now - self.face_at > LOST_AFTER_S:
            self.set_viewer(None, now, f"{self.viewer.name} stepped away")
        if self.dst is not None and self.dst[0] is not None:
            m = ease((now - self.m_t0) / MORPH_S)
            src = self.src[0] if self.src[0] is not None else self.dst[0]
            x = self.space.features(self.dst[0]) if m >= 1 else (1 - m) * self.space.features(src) + m * self.space.features(self.dst[0])
            self.shown.append((now, x))
        while self.shown and self.shown[0][0] < now - 8:
            self.shown.popleft()
        while self.samples and self.samples[0][0] < now - 12:
            self.samples.popleft()
        if now >= self.next_beat:
            self.next_beat = now + BEAT_S
            self.beat(now)
        if self.pending is None and self.dst is not None and self.dst[0] is not None:
            if self.viewer is None:
                if now >= self.next_step:
                    self.room_step(now)
            else:
                self.step(now)

    def level(self, t0, t1):
        """Mean engagement between t0 and t1 (looking away counts against), or None without enough face."""
        win = [e for t, e in self.samples if t0 <= t <= t1]
        if len(win) < 4:
            return None
        present = [e for e in win if e is not None]
        away = 1 - len(present) / len(win)
        return (float(np.mean(present)) if present else 0.0) + AWAY * away

    def beat(self, now):
        v, t = self.viewer, self.taste()
        if v is None or v.base is None:
            return
        y = self.level(now - BEAT_S, now)
        if y is None:
            return
        self.recent = 0.6 * self.recent + 0.4 * y
        xs = [x for t, x in self.shown if now - BEAT_S - LAG_S <= t <= now - LAG_S]
        if self.learning and xs:
            t.learn(np.mean(xs, axis=0), y)
            self.profiles.dirty = True
            settled = now - self.m_t0 > MORPH_S + LAG_S and now - self.seed_t > SEED_MORPH_S + LAG_S
            if settled:
                s = t.seeds.setdefault(self.seed_b, [0.0, 0]); s[0] += y; s[1] += 1
                if y > 1.2 * max(0.04, t.yscale):
                    t.remember(self.dst[0], self.seed_b, y)

    def step(self, now):
        v, t = self.viewer, self.taste()
        if v.base is None:
            return
        unit = max(0.04, t.yscale)
        ch = self.change
        if ch is not None and "t" in ch:      # judge the last change once its reaction window has passed
            if now < ch["t"] + JUDGE_TO_S:
                return
            after = self.level(ch["t"] + JUDGE_FROM_S, ch["t"] + JUDGE_TO_S)
            d = None if after is None or ch["before"] is None else (after - ch["before"]) / unit
            self.change = None
            if d is None:
                self.next_step = now + 1.0
                return
            if self.learning and ch.get("dx") is not None and np.any(ch["dx"]):
                t.learn_pair(ch["dx"], after - ch["before"])
                self.profiles.dirty = True
            if ch["kind"] == "step" and d < UNDO:
                self.misses += 1
                self.request(ch["prev"], ch["prev_seed"], "revert", f"{v.name} {'cooled on' if self.edition == 'bliss' else 'relaxed at'} {ch['what']} ({d:+.1f}); undoing it")
                self.change = {"kind": "revert", "before": self.level(now - BEFORE_S, now), "what": f"going back from {ch['what']}",
                               "dx": -ch["dx"] if ch.get("dx") is not None else None}
                self.next_step = now + 0.8
                return
            if d > KEEP:
                self.misses = 0
                self.event(f"{v.name} {'warmed to' if self.edition == 'bliss' else 'tensed up at'} {ch['what']} ({d:+.1f}); keeping it")
            else:
                self.misses += 1
            savor = self.recent > 0.6 * unit
            self.next_step = now + (SAVOR_S if savor else STEP_S - JUDGE_TO_S)
            return
        if ch is not None or now < self.next_step:
            return
        if (self.misses >= JUMP_AFTER and self.recent < 0.3 * unit) or self.recent < -0.8 * unit:
            self.misses = 0
            self.jump(now, f"nothing's landing for {v.name} (reaction {self.recent / unit:+.1f}); jumping further", undoable=True)
            return
        self.mutate(now)

    def thompson_slot(self, a, j, exclude=None):
        o, n = self.space.offsets[j], self.space.sizes[j]
        seg = a[o:o + n].copy()
        if exclude is not None and n > 1:
            seg[exclude] = -np.inf
        return int(np.argmax(seg))

    def scores(self, t):
        if self.rng.random() < 0.6 * t.beats / (t.beats + EXPLOIT_AT):
            return t.mean(), True
        return t.sample(self.rng), False   # Thompson sampling: plausible favourites, including ones it isn't sure about yet

    def mutate(self, now):
        v, sp, t = self.viewer, self.space, self.taste()
        cur = self.dst[0]
        if self.rng.random() < SEED_P:
            seed = self.pick_seed(t)
            if seed != self.seed_b:
                self.begin_change(now, cur, seed, "a new layout", cur)
                self.request(cur, seed, "step", f"trying a new layout for {v.name}")
                return
        a, greedy = self.scores(t)
        gains = []
        for j in range(len(sp.slots)):
            if sp.sizes[j] < 2:
                gains.append(-np.inf); continue
            best = self.thompson_slot(a, j, exclude=cur[j])
            gains.append(a[sp.offsets[j] + best] - a[sp.offsets[j] + cur[j]])
        g = np.array(gains)
        if greedy and np.nanmax(g[np.isfinite(g)]) <= 0:   # every part is already its best guess: let them enjoy it
            self.next_step = now + SAVOR_S
            self.event(f"holding: this is its best guess for {v.name}")
            return
        p = np.exp((g - g.max()) / 0.05); p[~np.isfinite(g)] = 0; p /= p.sum()
        j = int(self.rng.choice(len(g), p=p))          # most likely the part with the most promising alternative
        o = self.thompson_slot(a, j, exclude=cur[j])
        new = cur[:j] + (o,) + cur[j + 1:]
        what = f"{sp.slots[j]} {sp.labels[j][cur[j]]} -> {sp.labels[j][o]}"
        self.begin_change(now, cur, None, what, new)
        self.request(new, None, "step", f"trying {what}" + (" (its best guess)" if greedy else ""))

    def jump(self, now, why, undoable=False):
        v, sp, t = self.viewer, self.space, self.taste()
        cur = self.dst[0] if self.dst and self.dst[0] is not None else sp.random(self.rng)
        a = t.sample(self.rng) if t is not None else self.profiles.room_mean(sp) + self.rng.normal(0, 0.1, sp.K)
        js = self.rng.choice(len(sp.slots), size=min(3, len(sp.slots)), replace=False)
        new = list(cur)
        for j in js:
            new[j] = self.thompson_slot(a, int(j), exclude=cur[j])
        new = tuple(new)
        seed = self.pick_seed(t) if t is not None else int(self.rng.integers(1, 2 ** 31))
        if undoable and self.dst is not None:   # judged like any change, and undone if it lands worse
            self.begin_change(now, cur, seed, "a jump", new)
            self.request(new, seed, "step", f"{why} -> {sp.describe(new)}")
            return
        self.change = None
        self.request(new, seed, "jump", f"{why} -> {sp.describe(new)}")
        self.next_step = now + STEP_S + MORPH_S

    def begin_change(self, now, prev, seed, what, new):
        dx = self.space.features(new) - self.space.features(prev)
        self.change = {"kind": "step", "before": self.level(now - BEFORE_S, now), "prev": prev,
                       "prev_seed": self.seed_b if seed is not None else None, "what": what, "dx": dx}

    def request(self, choice, seed, kind, why):
        self.pending = (tuple(int(c) for c in choice), seed, kind, why)

    def pick_seed(self, t, favourite=False):
        if t is not None and t.seeds:
            total = sum(n for _, n in t.seeds.values()) + 1
            scored = {s: sm / n + (0 if favourite else 0.3 * math.sqrt(math.log(total) / n)) for s, (sm, n) in t.seeds.items() if n}
            explore = 0.4 / (1 + t.beats / 200)
            if scored and (favourite or (len(scored) >= 3 and self.rng.random() > explore)):
                return int(max(scored, key=scored.get))
        if t is not None and len(t.seeds) > 16:   # keep the 16 best layouts
            t.seeds.pop(min(t.seeds, key=lambda s: t.seeds[s][0] / max(1, t.seeds[s][1])))
        return int(self.rng.integers(1, 2 ** 31))

    def room_mode(self, now, why):
        sp = self.space
        mu = self.profiles.room_mean(sp) + self.rng.normal(0, 0.05, sp.K)
        c = tuple(self.thompson_slot(mu, j) for j in range(len(sp.slots))) if self.profiles.people else sp.random(self.rng)
        self.change = None
        self.request(c, int(self.rng.integers(1, 2 ** 31)), "room", f"nobody's looking: {why} -> {sp.describe(c)}")
        self.next_step = now + 8.0

    def room_step(self, now):
        sp = self.space
        mu = self.profiles.room_mean(sp) + self.rng.normal(0, 0.08, sp.K)
        cur = self.dst[0]
        j = int(self.rng.integers(len(sp.slots)))
        new = cur[:j] + (self.thompson_slot(mu, j, exclude=cur[j]),) + cur[j + 1:]
        seed = int(self.rng.integers(1, 2 ** 31)) if self.rng.random() < 0.3 else None
        self.request(new, seed, "room", f"drifting: {sp.slots[j]} -> {sp.labels[j][new[j]]}")
        self.next_step = now + 8.0

    # ---------- what the phone shows
    def status(self, now):
        v = self.viewer
        looking = now - self.face_at < 1.5
        c = self.dst[0] if self.dst else None
        t = self.taste()
        unit = max(0.04, t.yscale) if t else 0.15
        out = {"edition": self.edition, "seeing": "person" if v else ("identifying" if looking else "nobody"),
               "who": {"pid": v.pid, "name": v.name, "reactions": t.beats, "warming": v.base is None} if v else None,
               "showing": self.space.caption(c) if c is not None else [], "glow": round(max(-1.0, min(1.0, self.glow / (2 * unit))), 2),
               "recent": round(self.recent / unit, 2), "learning": self.learning, "why": self.reason,
               "judging": bool(self.change and "t" in self.change and now < self.change["t"] + JUDGE_TO_S)}
        if t is not None:
            out["top"] = t.top(6)
        return out

    def people(self):
        out = []
        for p in sorted(self.profiles.people.values(), key=lambda p: -p.last_seen):
            t = p.taste(self.space)
            out.append({"pid": p.pid, "name": p.name, "reactions": t.beats, "top": t.top(5), "faces": len(p.exemplars),
                        "device": p.device is not None, "moments": len(t.moments), "edition": self.edition})
        return out

    def keep(self):
        """What's on screen right now, for a keepsake capture (still + living loop + metadata). No face data."""
        if self.dst is None or self.dst[0] is None:
            return None
        c = self.dst[0]
        return {"edition": self.edition, "emb": self.dst[1], "seed": self.seed_b, "labels": self.space.to_labels(c),
                "caption": self.space.caption(c), "prompt": self.space.prompt(c)}
