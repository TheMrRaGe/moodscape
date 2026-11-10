#!/usr/bin/env python3
"""
extract_mb.py - pull the mushroom body out of the MaleCNS connectome for the FEAR engine.

    python3 extract_mb.py <brain_mirrored.npz> <out.json>

Reads the mirrored whole-brain graph built by flybrain/scripts (MaleCNS v1.0, HHMI Janelia FlyEM + Google
Research, CC-BY 4.0; Kenyon-cell wiring is left as traced by mirror_connectome.py) and writes only what a
rate-based mushroom body needs:

  glom      the 53 olfactory receptor types (the glomeruli), as the "senses" the engine smells situations with
  g2k       glomerulus -> Kenyon cell effective drive, two hops through projection neurons:
            g2k[g, k] = sum over PN p of  share of p's ORN input that comes from g  x  synapses p -> k
            (only PN types: the antennal-lobe local neurons are left out, because with their transmitter labels
            as published they broadcast every glomerulus to every PN - FINDINGS.md, decision 13)
  k2m       Kenyon cell -> MBON synapse counts (the plastic synapses, where memory lives)
  mbon      the 97 MBONs: type, side, transmitter sign
  dan       for every dopamine type (PAM01-15 reward, PPL101-108 punishment) the MBONs it innervates
            (DAN -> MBON synapses: a compartment is a DAN type's target set, FINDINGS.md "Qualification")

Nothing here is tuned. The engine (FearEngine/Instinct.cs) does the rest at run time.
"""
import json, re, sys
import numpy as np

src, out = sys.argv[1], sys.argv[2]
z = np.load(src, allow_pickle=True)
t, side, sign = z["type"], z["side"], z["sign"]
pre, post, w = z["pre"], z["post"], z["w"].astype(np.float64)
N = len(t)
ts = np.array([str(x) for x in t])

is_orn = np.char.startswith(ts, "ORN_")
is_kc = np.char.startswith(ts, "KC")
is_mbon = np.char.startswith(ts, "MBON")
is_pn = np.array([bool(re.search(r"PN", x)) and not x.startswith("ORN") for x in ts])
glom_types = sorted(set(ts[is_orn]))
gi = {g: i for i, g in enumerate(glom_types)}
kc_idx = np.flatnonzero(is_kc); kc_pos = -np.ones(N, int); kc_pos[kc_idx] = np.arange(len(kc_idx))
mb_idx = np.flatnonzero(is_mbon); mb_pos = -np.ones(N, int); mb_pos[mb_idx] = np.arange(len(mb_idx))
pn_idx = np.flatnonzero(is_pn); pn_pos = -np.ones(N, int); pn_pos[pn_idx] = np.arange(len(pn_idx))
print(f"glomeruli {len(glom_types)}  KC {len(kc_idx)}  MBON {len(mb_idx)}  PN {len(pn_idx)}")

# ORN -> PN: each PN's glomerular tuning (share of its ORN input from each glomerulus)
m = is_orn[pre] & is_pn[post] & (sign[pre] > 0)
tune = np.zeros((len(pn_idx), len(glom_types)))
np.add.at(tune, (pn_pos[post[m]], np.array([gi[x] for x in ts[pre[m]]])), w[m])
tot = tune.sum(1, keepdims=True); tot[tot == 0] = 1; tune /= tot

# PN -> KC (excitatory PNs only)
m = is_pn[pre] & is_kc[post] & (sign[pre] > 0)
p2k = np.zeros((len(pn_idx), len(kc_idx)))
np.add.at(p2k, (pn_pos[pre[m]], kc_pos[post[m]]), w[m])
g2k = tune.T @ p2k                                   # (53, 4064)
print(f"g2k: {np.count_nonzero(g2k)} nonzero; KCs with any glomerular drive {int((g2k.sum(0) > 0).sum())}")

# KC -> MBON
m = is_kc[pre] & is_mbon[post]
k2m_rows, k2m_cols, k2m_w = kc_pos[pre[m]], mb_pos[post[m]], w[m]
print(f"KC->MBON edges {m.sum()}  synapses {int(k2m_w.sum())}")

# DAN -> MBON compartments
dan = {}
for d in [f"PAM{i:02d}" for i in range(1, 16)] + [f"PPL10{i}" for i in range(1, 9)]:
    m = (ts[pre] == d) & is_mbon[post]
    if not m.any(): continue
    tgt = np.zeros(len(mb_idx)); np.add.at(tgt, mb_pos[post[m]], w[m])
    dan[d] = {"mbon": [int(i) for i in np.flatnonzero(tgt)], "w": [float(x) for x in tgt[tgt > 0]]}
    print(f"  {d}: {len(dan[d]['mbon'])} MBONs, {int(tgt.sum())} synapses")

# sparse g2k: keep entries carrying 99% of each KC's drive
rows, cols, vals = [], [], []
for k in range(g2k.shape[1]):
    col = g2k[:, k]; tot_k = col.sum()
    if tot_k <= 0: continue
    order = np.argsort(-col); acc = 0.0
    for g in order:
        if col[g] <= 0 or acc >= 0.99 * tot_k: break
        rows.append(int(g)); cols.append(int(k)); vals.append(round(float(col[g]), 3)); acc += col[g]
print(f"g2k kept {len(vals)} entries ({len(vals) / max(1, (g2k.sum(0) > 0).sum()):.1f} glomeruli per KC)")

res = {
    "source": "MaleCNS connectome v1.0 (HHMI Janelia FlyEM + Google Research), CC-BY 4.0; mirrored graph from flybrain",
    "glom": glom_types,
    "kcType": [str(x) for x in ts[kc_idx]],
    "mbonType": [str(x) for x in ts[mb_idx]], "mbonSide": [str(x) for x in side[mb_idx]], "mbonSign": [int(x) for x in sign[mb_idx]],
    "g2kG": rows, "g2kK": cols, "g2kW": vals,
    "k2mK": [int(x) for x in k2m_rows], "k2mM": [int(x) for x in k2m_cols], "k2mW": [float(x) for x in k2m_w],
    "danNames": list(dan.keys()), "dan": [dan[k] for k in dan],
}
with open(out, "w") as f: json.dump(res, f, separators=(",", ":"))
import os; print("wrote", out, os.path.getsize(out) // 1024, "KB")
