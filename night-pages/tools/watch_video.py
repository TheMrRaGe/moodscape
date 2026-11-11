#!/usr/bin/env python3
"""
Watch a playtest recording properly (not just a few stills).

  python3 watch_video.py <video.mp4> <out_dir> [--game-log session.log]

Writes into out_dir:
  speech.txt      what was said, with times (faster-whisper, if any narration)
  screen.txt      every change of on-screen text (prompts, subtitles), read twice a second (tesseract)
  events.txt      both of those merged with the game's own log (if given), in time order
  strip_MMSS.jpg  a motion strip around each event: 8 frames over 2 seconds, so movement can be seen
  sheet_NN.jpg    an overview, one frame every 5 seconds

Needs ffmpeg, tesseract, Pillow; faster-whisper for speech (pip install faster-whisper).
"""
import glob, os, re, subprocess, sys, wave


def run(cmd):
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def duration(video):
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", video],
                         capture_output=True, text=True).stdout.strip()
    return float(out or 0)


def speech(video, out):
    wav = os.path.join(out, "audio.wav")
    run(["ffmpeg", "-y", "-v", "error", "-i", video, "-vn", "-ac", "1", "-ar", "16000", wav])
    lines = []
    try:
        import numpy as np
        from faster_whisper import WhisperModel
        w = wave.open(wav)
        a = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
        model = WhisperModel("base.en", device="cpu", compute_type="int8")
        segs, _ = model.transcribe(a, vad_filter=True)
        for s in segs:
            lines.append((s.start, "SAID: " + s.text.strip()))
    except Exception as e:
        lines.append((0, "(no transcription: %s)" % e))
    with open(os.path.join(out, "speech.txt"), "w") as f:
        for t, l in lines:
            f.write("%d:%02d %s\n" % (t // 60, t % 60, l))
    return lines


WORDS = re.compile(r"[A-Za-z]{3,}")


def screen(video, out, fps=2):
    d = os.path.join(out, "ocr")
    os.makedirs(d, exist_ok=True)
    # The middle band (prompts) and the bottom band (subtitles), enlarged and grey for the OCR.
    run(["ffmpeg", "-y", "-v", "error", "-i", video, "-vf",
         "fps=%d,crop=iw*0.6:ih*0.5:iw*0.2:ih*0.4,scale=iw*2:-1,format=gray" % fps, os.path.join(d, "p%05d.png")])
    files = sorted(glob.glob(os.path.join(d, "p*.png")))
    subprocess.run("ls %s/p*.png | xargs -P 8 -I{} sh -c 'tesseract {} {}.t --psm 6 -l eng >/dev/null 2>&1'" % d, shell=True)
    lines, prev = [], None
    for f in files:
        n = int(re.findall(r"(\d+)\.png$", f)[0]) - 1
        t = n / fps
        try:
            raw = open(f + ".t.txt").read().splitlines()
        except OSError:
            continue
        keep = [l.strip() for l in raw if len(WORDS.findall(l)) >= 2]
        txt = " | ".join(keep)
        if txt and txt != prev:
            lines.append((t, "SCREEN: " + txt))
        prev = txt
    with open(os.path.join(out, "screen.txt"), "w") as fo:
        for t, l in lines:
            fo.write("%d:%02d %s\n" % (t // 60, t % 60, l))
    return lines


def strips(video, out, times):
    done = set()
    for t in times:
        key = int(t)
        if key in done:
            continue
        done.add(key)
        pat = os.path.join(out, "_s%05d_%%02d.jpg" % key)
        run(["ffmpeg", "-y", "-v", "error", "-ss", str(max(0, t - 0.5)), "-t", "2", "-i", video, "-vf", "fps=4,scale=480:-2", pat])
        from PIL import Image, ImageDraw
        fs = sorted(glob.glob(os.path.join(out, "_s%05d_*.jpg" % key)))[:8]
        if not fs:
            continue
        ims = [Image.open(f) for f in fs]
        w, h = ims[0].size
        sheet = Image.new("RGB", (w * 4, h * 2))
        for i, im in enumerate(ims):
            ImageDraw.Draw(im).text((4, 4), "%d:%02d +%.2fs" % (key // 60, key % 60, i * 0.25), fill="yellow")
            sheet.paste(im, ((i % 4) * w, (i // 4) * h))
        sheet.save(os.path.join(out, "strip_%02d%02d.jpg" % (key // 60, key % 60)), quality=80)
        for f in fs:
            os.remove(f)


def overview(video, out):
    from PIL import Image, ImageDraw
    d = os.path.join(out, "ov")
    os.makedirs(d, exist_ok=True)
    run(["ffmpeg", "-y", "-v", "error", "-i", video, "-vf", "fps=1/5,scale=480:-2", os.path.join(d, "f%04d.jpg")])
    fs = sorted(glob.glob(os.path.join(d, "f*.jpg")))
    for k in range(0, len(fs), 12):
        part = fs[k:k + 12]
        w, h = Image.open(part[0]).size
        s = Image.new("RGB", (w * 3, h * 4))
        for i, f in enumerate(part):
            im = Image.open(f)
            n = (k + i) * 5
            ImageDraw.Draw(im).text((4, 4), "%d:%02d" % (n // 60, n % 60), fill="yellow")
            s.paste(im, ((i % 3) * w, (i // 3) * h))
        s.save(os.path.join(out, "sheet_%02d.jpg" % (k // 12)), quality=80)


def main():
    video, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    game = []
    if "--game-log" in sys.argv:
        path = sys.argv[sys.argv.index("--game-log") + 1]
        for l in open(path, errors="ignore"):
            m = re.match(r"(\d+):(\d\d) (.*)", l.strip())
            if m:
                game.append((int(m.group(1)) * 60 + int(m.group(2)), "GAME: " + m.group(3)))
    said = speech(video, out)
    seen = screen(video, out)
    overview(video, out)
    events = sorted(said + seen + game, key=lambda e: e[0])
    with open(os.path.join(out, "events.txt"), "w") as f:
        for t, l in events:
            f.write("%d:%02d %s\n" % (t // 60, t % 60, l))
    # Motion strips around everything said, and around marks and new on-screen messages.
    times = [t for t, l in said if not l.startswith("(no")]
    times += [t for t, l in game if "MARK" in l]
    times += [t for t, l in seen][::6]
    strips(video, out, sorted(set(int(t) for t in times))[:80])
    print("duration %.0f s: %d said, %d screen changes, %d game lines" % (duration(video), len(said), len(seen), len(game)))


if __name__ == "__main__":
    main()
