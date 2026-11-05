// Checks the Mirror's camera heart-rate reader (posHeartRate in mirror.html) on synthetic skin colour.
//   node tools/test_heart_rate.js
// A pulse of known rate is added to forehead/cheek colour, with camera noise, uneven frame timing, slow lighting
// drift and head motion. It should read the rate within 5 bpm, and give low quality when there's no pulse at all.
const fs = require("fs"), path = require("path");
const html = fs.readFileSync(path.join(__dirname, "..", "mirror.html"), "utf8");
const posHeartRate = new Function(html.split("/* rppg-start */")[1].split("/* rppg-end */")[0] + "; return posHeartRate;")();

function synth(bpm, seconds, { fps = 14, jitter = 25, noise = 0.25, amp = 0.35, drift = 4, motion = 0 } = {}, seed = 1) {
  let s = seed; const rnd = () => (s = (s * 16807) % 2147483647) / 2147483647;
  const gauss = () => Math.sqrt(-2 * Math.log(rnd() + 1e-12)) * Math.cos(2 * Math.PI * rnd());
  const out = []; let t = 0;
  while (t < seconds * 1000) {
    const sec = t / 1000, p = Math.sin(2 * Math.PI * (bpm / 60) * sec), d = drift * Math.sin(2 * Math.PI * 0.05 * sec);
    const m = motion * Math.sin(2 * Math.PI * 0.4 * sec);
    // the pulse shows most in green, less in blue, a little in red; lighting and motion move all channels together
    out.push({ t, r: 170 + d + m + 0.33 * amp * p + noise * gauss(), g: 120 + d + m + amp * p + noise * gauss(), b: 100 + d + m + 0.5 * amp * p + noise * gauss() });
    t += 1000 / fps + (rnd() - 0.5) * jitter;
  }
  return out;
}
// What matters: a reading the Mirror uses (quality >= 0.4) must be right, and a face with no readable pulse must
// not produce a usable reading. Some correct readings fall under 0.4 and are ignored; that's the safe direction.
let used = 0, total = 0, wrongUsed = 0, falsePulse = 0;
for (const noise of [0.15, 0.25, 0.35])
  for (const motion of [0, 3])
    for (const bpm of [55, 65, 72, 80, 95, 110, 125])
      for (let seed = 1; seed <= 4; seed++) {
        const r = posHeartRate(synth(bpm, 10, { noise, motion }, seed)); total++;
        if (r.q >= 0.4) { used++; if (Math.abs(r.bpm - bpm) >= 5) { wrongUsed++; console.log(`confidently wrong: true ${bpm}, read ${r.bpm.toFixed(0)}, q ${r.q.toFixed(2)}, noise ${noise}`); } }
      }
for (const noise of [0.15, 0.25, 0.35, 0.5])
  for (let seed = 1; seed <= 10; seed++) if (posHeartRate(synth(70, 10, { noise, amp: 0 }, seed)).q >= 0.4) falsePulse++;
console.log(`pulse readings used: ${used}/${total} (${(100 * used / total).toFixed(0)}%), used but wrong by 5+ bpm: ${wrongUsed}`);
console.log(`no-pulse faces read as a usable pulse: ${falsePulse}/40`);
const ok = wrongUsed === 0 && falsePulse === 0 && used / total > 0.4;
console.log(ok ? "PASS" : "FAIL");
process.exit(ok ? 0 : 1);
