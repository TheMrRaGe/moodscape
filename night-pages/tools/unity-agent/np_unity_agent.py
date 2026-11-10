"""
Night Pages Unity agent: runs Unity jobs for this project in the background, so Claude can build and
play-test the game while you use the PC for other things. Nothing takes over your screen: Unity and the
game both run with no window, at low priority, and never take focus from what you're using.

    Once: double-click "Install Night Pages agent (background).bat" in the night-pages folder. From then on
    it runs hidden whenever you're logged in (a Windows scheduled task starts it at log-on and checks every
    minute that it's still running), so Claude can use it without you starting anything.
    Or, for a visible console instead: "Start Night Pages agent.bat".

It watches agent/request.txt for one of these, and nothing else:

    compile        open the project headless and report script errors
    build          build the Windows game into Build/NightPages.exe
    tour           run the built game headless: screenshots of the house, every monster form, every dream
    bot [runs]     run the built game headless: whole playthroughs at speed (default 3), endings and errors
    watch          the same bot playthrough, but in a normal window with sound, so you can watch it play
    check          build, then tour, then bot
    status         just rewrite agent/status.json
    restart        the agent exits; the scheduled task starts a fresh one within a minute (it also does this by
                   itself when this file changes, so a new version takes over without anyone touching the PC)

Switches, as files in agent/ (create or delete them; the agent notices within a second):
    PAUSED           take no jobs until it's deleted (queued requests wait)
    SHOW-WINDOWS     allow the "watch" job, which opens the game in a window with sound. Without this file,
                     nothing the agent runs ever opens a window, so it can't change your active app.
    WAIT-FOR-IDLE    start a job only after this many minutes with no mouse or keyboard input (the file holds
                     the number; empty means 5). For when even background CPU use would get in the way.

Anything else is refused, so the file can't be used to run other commands. Each job writes to its own folder,
runs/<date>-<time>-<job>/, with Unity's log, the game's log.txt, errors.txt, summary.json and the screenshots.
agent/status.json always says what it's doing and how the last job went.
Only one agent runs at a time. Uninstall with "Uninstall Night Pages agent (background).bat".
"""
import json, os, platform, re, socket, subprocess, sys, time
from pathlib import Path

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parent.parent                     # the night-pages folder
AGENT, RUNS = PROJECT / "agent", PROJECT / "runs"
REQUEST, STATUS = AGENT / "request.txt", AGENT / "status.json"
EXE = PROJECT / "Build" / "NightPages.exe"
WINDOWS = platform.system() == "Windows"
LOCK_PORT = 47825
JOB = re.compile(r"^(compile|build|tour|video|status|check|watch|restart|bot(?: ([1-9]|10))?)$")
LIMITS = {"compile": 1800, "build": 3600, "tour": 900, "bot": 1800, "watch": 1800, "video": 900}
REQUESTS = AGENT / "requests"              # or drop a file in here: agent/requests/<anything>.txt     # seconds; the first open imports models and is slow

SELF = Path(__file__).resolve()
BACKGROUND = sys.stdout is None or Path(sys.executable).name.lower() == "pythonw.exe" or "--background" in sys.argv
PAUSED, SHOW_WINDOWS, WAIT_FOR_IDLE = AGENT / "PAUSED", AGENT / "SHOW-WINDOWS", AGENT / "WAIT-FOR-IDLE"

state = {"agent": "running", "mode": "background (hidden)" if BACKGROUND else "console window", "pid": os.getpid(), "since": time.strftime("%Y-%m-%d %H:%M:%S"), "busy": False, "job": None,
         "last": None, "unity": None, "project": str(PROJECT)}


def say(msg):
    line = time.strftime("%H:%M:%S") + " " + str(msg)
    if sys.stdout is not None:
        print(line, flush=True)
    try:
        AGENT.mkdir(exist_ok=True)
        with open(AGENT / "agent.log", "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except OSError:
        pass


_last_beat = 0.0


def idle_seconds():
    """How long since the last mouse or keyboard input on this PC (Windows only; 0 elsewhere)."""
    if not WINDOWS:
        return 0.0
    try:
        import ctypes

        class LASTINPUTINFO(ctypes.Structure):
            _fields_ = [("cbSize", ctypes.c_uint), ("dwTime", ctypes.c_uint)]
        li = LASTINPUTINFO(ctypes.sizeof(LASTINPUTINFO), 0)
        if not ctypes.windll.user32.GetLastInputInfo(ctypes.byref(li)):
            return 0.0
        return ((ctypes.windll.kernel32.GetTickCount() - li.dwTime) & 0xFFFFFFFF) / 1000.0
    except Exception:
        return 0.0


def idle_minutes_wanted():
    if not WAIT_FOR_IDLE.is_file():
        return 0.0
    try:
        txt = WAIT_FOR_IDLE.read_text(encoding="utf-8", errors="ignore").strip()
        return float(txt) if txt else 5.0
    except (OSError, ValueError):
        return 5.0


def beat():
    """Every few seconds, note that the agent is alive (status.json "alive"), so a stuck agent is easy to spot."""
    global _last_beat
    if time.time() - _last_beat >= 5:
        _last_beat = time.time()
        state["alive"] = time.strftime("%Y-%m-%d %H:%M:%S")
        try:
            save()
        except OSError:
            pass


def take_request():
    """The next request, or None. Reads agent/request.txt or any agent/requests/*.txt, ignoring a file that is
    still being written (empty and less than 5 s old). A file that can't be removed (OneDrive or another program
    holding it) is logged once and remembered, never retried silently."""
    candidates = [REQUEST] + (sorted(REQUESTS.glob("*.txt")) if REQUESTS.is_dir() else [])
    for f in candidates:
        try:
            if not f.is_file():
                continue
            st = f.stat()
            if st.st_size == 0 and time.time() - st.st_mtime < 5:
                continue
            key = (str(f), st.st_mtime_ns)
            if key in _seen:
                continue
            text = f.read_text(encoding="utf-8", errors="ignore").strip()
            line = text.splitlines()[0].strip().lower() if text else ""
            _seen.add(key)
            try:
                f.unlink()
            except OSError as e:
                say(f"note: couldn't remove {f.name} after reading it ({e}); it won't be run twice")
            (AGENT / "last-request.txt").write_text(line + "\n", encoding="utf-8")
            return line or None
        except OSError as e:
            say(f"note: couldn't read {f.name} yet ({e}); will try again")
    return None


_seen = set()


def save():
    AGENT.mkdir(exist_ok=True)
    tmp = STATUS.with_suffix(".tmp")
    tmp.write_text(json.dumps(state, indent=2), encoding="utf-8")
    os.replace(tmp, STATUS)


def find_unity():
    if os.environ.get("NP_UNITY") and Path(os.environ["NP_UNITY"]).is_file():
        return Path(os.environ["NP_UNITY"])
    want = None
    pv = PROJECT / "ProjectSettings" / "ProjectVersion.txt"
    if pv.is_file():
        m = re.search(r"m_EditorVersion:\s*(\S+)", pv.read_text(encoding="utf-8", errors="ignore"))
        want = m.group(1) if m else None
    hub = Path(r"C:\Program Files\Unity\Hub\Editor")
    if want and (hub / want / "Editor" / "Unity.exe").is_file():
        return hub / want / "Editor" / "Unity.exe"
    found = sorted(hub.glob("*/Editor/Unity.exe"), reverse=True) if hub.is_dir() else []
    return found[0] if found else None


def editor_has_project_open():
    lock = PROJECT / "Temp" / "UnityLockfile"
    if not lock.exists():
        return False
    try:
        with open(lock, "a"):
            return False                          # left over from a closed editor: not held
    except OSError:
        return True


_job = None


def kill_children_with_agent(proc):
    """Windows: put the child in a job object that closes with this agent, so closing the agent's window also
    ends any Unity or game process it started (nothing is left running hidden)."""
    global _job
    if not WINDOWS:
        return
    try:
        import ctypes
        from ctypes import wintypes
        k32 = ctypes.WinDLL("kernel32", use_last_error=True)
        if _job is None:
            _job = k32.CreateJobObjectW(None, None)

            class BASIC(ctypes.Structure):
                _fields_ = [("PerProcessUserTimeLimit", ctypes.c_int64), ("PerJobUserTimeLimit", ctypes.c_int64),
                            ("LimitFlags", wintypes.DWORD), ("MinimumWorkingSetSize", ctypes.c_size_t),
                            ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", wintypes.DWORD),
                            ("Affinity", ctypes.c_size_t), ("PriorityClass", wintypes.DWORD), ("SchedulingClass", wintypes.DWORD)]

            class IOC(ctypes.Structure):
                _fields_ = [(n, ctypes.c_uint64) for n in ("r", "w", "o", "rb", "wb", "ob")]

            class EXT(ctypes.Structure):
                _fields_ = [("Basic", BASIC), ("Io", IOC), ("ProcessMemoryLimit", ctypes.c_size_t), ("JobMemoryLimit", ctypes.c_size_t),
                            ("PeakProcessMemoryUsed", ctypes.c_size_t), ("PeakJobMemoryUsed", ctypes.c_size_t)]
            info = EXT()
            info.Basic.LimitFlags = 0x2000          # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            k32.SetInformationJobObject(_job, 9, ctypes.byref(info), ctypes.sizeof(info))
        k32.AssignProcessToJobObject(_job, wintypes.HANDLE(int(proc._handle)))
    except Exception as e:
        say(f"  (couldn't tie the game to the agent's window: {e!r})")


def run(cmd, log_path, limit, stall=None, hang_file=None, hidden=True):
    """Run one program with no window, at low priority, and wait for it. Gives up after `limit` seconds, or, with
    `stall`, as soon as its log stops growing for that long or the game's watchdog writes `hang_file`."""
    flags = 0
    if WINDOWS:
        flags = subprocess.BELOW_NORMAL_PRIORITY_CLASS | (subprocess.CREATE_NO_WINDOW if hidden else 0)
    say("  " + " ".join(f'"{c}"' if " " in str(c) else str(c) for c in cmd))
    with open(log_path.with_suffix(".stdout.txt"), "w", encoding="utf-8", errors="ignore") as out:
        p = subprocess.Popen([str(c) for c in cmd], stdout=out, stderr=subprocess.STDOUT, creationflags=flags)
        kill_children_with_agent(p)
        start = last_change = time.time()
        last_size = -1
        while True:
            code = p.poll()
            if code is not None:
                return code
            now = time.time()
            if now - start > limit:
                p.kill()
                return "timed out after %d s" % limit
            if hang_file is not None and hang_file.is_file():
                time.sleep(1)
                p.kill()
                return "hung (the game's watchdog saw frames stop; see hang.txt)"
            if stall:
                size = log_path.stat().st_size if log_path.is_file() else 0
                if size != last_size:
                    last_size, last_change = size, now
                elif now - last_change > stall:
                    p.kill()
                    return "hung (no log output for %d s)" % stall
            beat()
            time.sleep(2)


def unity_errors(log):
    if not log.is_file():
        return []
    text = log.read_text(encoding="utf-8", errors="ignore")
    errs = sorted(set(re.findall(r"^.*error CS\d+:.*$", text, re.M)))
    if "Scripts have compiler errors" in text and not errs:
        errs = ["Scripts have compiler errors (see unity.log)"]
    for line in re.findall(r"^.*\[NightPages\] BUILD .*$", text, re.M):
        say("  " + line.strip())
    return errs


def license_problem(log):
    """Unity's free licence only works while Unity Hub is running and signed in. Say so plainly."""
    try:
        text = log.read_text(encoding="utf-8", errors="ignore") if log.is_file() else ""
    except OSError:
        return None
    if "No valid Unity Editor license found" in text or "Access token is unavailable" in text and "return code 198" in text:
        return ("Unity has no licence right now: open Unity Hub and make sure you're signed in (it can stay in the tray), "
                "then ask again")
    return None


LICENCE_RETRY = 120          # seconds between tries while waiting for a licence
LICENCE_GIVE_UP = 3 * 3600   # stop waiting after this long


def job_dir(name):
    d = RUNS / (time.strftime("%Y%m%d-%H%M%S") + "-" + name)
    d.mkdir(parents=True, exist_ok=True)
    return d


def do_compile():
    d = job_dir("compile")
    code = run([state["unity"], "-batchmode", "-quit", "-nographics", "-projectPath", PROJECT, "-logFile", d / "unity.log"], d / "unity.log", LIMITS["compile"])
    errs = unity_errors(d / "unity.log")
    (d / "compile-errors.txt").write_text("\n".join(errs) or "no script errors\n", encoding="utf-8")
    ok = code == 0 and not errs
    why = license_problem(d / "unity.log")
    if why:
        return False, d, "compile FAILED: " + why
    return ok, d, f"compile {'OK' if ok else 'FAILED'} (exit {code}, {len(errs)} script errors)"


def do_build():
    d = job_dir("build")
    code = run([state["unity"], "-batchmode", "-quit", "-projectPath", PROJECT, "-executeMethod", "NightPages.EditorTools.NightPagesBuild.Run",
                "-npexe", EXE, "-logFile", d / "unity.log"], d / "unity.log", LIMITS["build"])
    errs = unity_errors(d / "unity.log")
    (d / "compile-errors.txt").write_text("\n".join(errs) or "no script errors\n", encoding="utf-8")
    ok = code == 0 and EXE.is_file()
    why = license_problem(d / "unity.log")
    if why:
        say("  " + why)
        return False, d, "build FAILED: " + why
    return ok, d, f"build {'OK' if ok else 'FAILED'} (exit {code}, {len(errs)} script errors)"


def do_play(kind, runs=3):
    if not EXE.is_file():
        return False, None, "no build yet: run 'build' first"
    d = job_dir(kind)
    if kind == "watch":
        args = [EXE, "-npbot", d, "-npruns", "1", "-npsound", "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720", "-logFile", d / "player.log"]
    else:
        flag = {"tour": "-nptour", "video": "-npvideo"}.get(kind, "-npbot")
        args = [EXE, "-batchmode", flag, d, "-logFile", d / "player.log"]
        if kind == "bot":
            args += ["-npruns", str(runs)]
    code = run(args, d / "player.log", LIMITS[kind], stall=90, hang_file=d / "hang.txt", hidden=kind != "watch")
    summary = d / "summary.json"
    got = json.loads(summary.read_text(encoding="utf-8")) if summary.is_file() else None
    ok = code == 0 and got is not None
    detail = "" if got is None else ", ".join(f"{k} {v}" for k, v in got.items() if k in ("shots", "frames", "errors", "endings", "fell_through_floor", "top_kind_of_fear", "layout_problems", "playthrough_problems"))
    return ok, d, f"{kind} {'OK' if ok else 'FAILED'} (exit {code}{', ' + detail if detail else ''})"


def handle(line):
    m = JOB.match(line)
    if not m:
        return {"job": line[:60], "ok": False, "result": "refused: not one of compile, build, tour, video, bot [runs], check, watch, status, restart"}
    job = line.split()[0]
    if job == "status":
        return {"job": "status", "ok": True, "result": "agent running"}
    if job == "watch" and not SHOW_WINDOWS.exists():
        return {"job": "watch", "ok": False, "result": "refused: 'watch' opens a window, and windows are off. "
                "Create agent/SHOW-WINDOWS to allow it (only when a window popping up is OK)."}
    if job in ("compile", "build", "check") and editor_has_project_open():
        return {"job": line, "ok": False, "result": "Night Pages is open in the Unity editor. Close it there so the agent can open the project."}
    steps = {"compile": [do_compile], "build": [do_build], "tour": [lambda: do_play("tour")], "watch": [lambda: do_play("watch")],
             "bot": [lambda: do_play("bot", int(m.group(2) or 3))], "video": [lambda: do_play("video")],
             "check": [do_build, lambda: do_play("tour"), lambda: do_play("bot", 2)]}[job]
    results, dirs, ok = [], [], True
    for step in steps:
        state["job"] = f"{line} (step {len(results) + 1} of {len(steps)})"
        save()
        good, d, msg = step()
        say("  " + msg)
        results.append(msg)
        if d:
            dirs.append(str(d))
        if not good:
            ok = False
            break
    return {"job": line, "ok": ok, "result": "; ".join(results), "run_dirs": dirs}


def main():
    try:
        lock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        lock.bind(("127.0.0.1", LOCK_PORT))
    except OSError:
        if not BACKGROUND:                         # the scheduled task tries every minute; stay quiet about it
            say("The Night Pages agent is already running. This window can be closed.")
            time.sleep(4)
        return
    AGENT.mkdir(exist_ok=True)
    RUNS.mkdir(exist_ok=True)
    unity = find_unity()
    state["unity"] = str(unity) if unity else None
    save()
    if not unity:
        say("Couldn't find Unity.exe under C:\\Program Files\\Unity\\Hub\\Editor. Set NP_UNITY to its path and start again.")
    say(f"Night Pages agent ({state['mode']}, pid {os.getpid()}): watching {REQUEST}")
    say(f"Unity: {unity}")
    REQUESTS.mkdir(exist_ok=True)
    born = SELF.stat().st_mtime
    waiting_note = ""
    retry = None             # (request, next try, give up at): a job that failed only for want of a licence
    while True:
        # A new version of this file: let the scheduled task start it (between jobs, never during one).
        try:
            if SELF.stat().st_mtime != born:
                say("the agent's code changed: exiting so the new version starts (within a minute)")
                state["agent"] = "restarting for an update"
                save()
                return
        except OSError:
            pass
        paused = PAUSED.exists()
        idle_need = idle_minutes_wanted() * 60.0
        idle_now = idle_seconds()
        note = "paused (agent/PAUSED exists)" if paused else (
            f"waiting for the PC to be idle {idle_need / 60:.0f} min (idle {idle_now / 60:.1f})" if idle_need and idle_now < idle_need else "")
        state["agent"] = note or ("waiting for a Unity licence (open Unity Hub and sign in)" if retry else "running")
        if note != waiting_note:
            waiting_note = note
            if note:
                say(note)
            save()
        beat()
        line = None if note else take_request()
        # No new request: is a job waiting for Unity's licence due another try? (Sooner if Unity Hub has just started.)
        if not line and not note and retry:
            if time.time() > retry[2]:
                say(f"gave up waiting for a Unity licence for '{retry[0]}' after {LICENCE_GIVE_UP // 3600} h; ask again once Unity Hub is open")
                retry = None
            elif time.time() >= retry[1]:
                line = retry[0]
                say(f"trying '{line}' again (waiting for a Unity licence)")
        if line == "restart":
            say("restart requested: exiting; the scheduled task starts a fresh agent within a minute")
            state.update(agent="restarting", last={"job": "restart", "ok": True, "result": "restarting", "finished": time.strftime("%Y-%m-%d %H:%M:%S")})
            save()
            return
        if line:
            say(f"job: {line}")
            state.update(busy=True, job=line, started=time.strftime("%Y-%m-%d %H:%M:%S"))
            save()
            if not state["unity"] and line != "status":
                result = {"job": line, "ok": False, "result": "Unity.exe not found"}
            else:
                try:
                    result = handle(line)
                except Exception as e:                     # keep the agent alive whatever happens
                    result = {"job": line, "ok": False, "result": f"agent error: {e!r}"}
            result["finished"] = time.strftime("%Y-%m-%d %H:%M:%S")
            if not result.get("ok") and "no licence" in result.get("result", ""):
                # Don't drop it: keep trying quietly until Unity Hub is open and signed in.
                first = retry[2] - LICENCE_GIVE_UP if retry and retry[0] == line else time.time()
                retry = (line, time.time() + LICENCE_RETRY, first + LICENCE_GIVE_UP)
                result["result"] += f" (waiting: it will run by itself once Unity Hub is open and signed in; tries every {LICENCE_RETRY // 60} min for up to {LICENCE_GIVE_UP // 3600} h)"
                state["agent"] = "waiting for a Unity licence (open Unity Hub)"
            elif retry and retry[0] == line:
                retry = None
            state.update(busy=False, job=None, last=result)
            save()
            say(f"done: {result['result']}")
        time.sleep(1)


if __name__ == "__main__":
    try:
        say(f"starting with Python {sys.version.split()[0]} at {sys.executable}")
        main()
    except KeyboardInterrupt:
        pass
    except Exception:
        import traceback
        say("the agent crashed:\n" + traceback.format_exc())
        raise
