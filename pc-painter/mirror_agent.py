"""
Moodscape Mirror agent: keeps the Mirror painter running, and lets anyone with access to this folder (for example
Claude, through the Claude desktop app) start, stop or restart it and pick up its QR code and log, without anyone
at the keyboard.

    python mirror_agent.py      (or double-click mirror-agent.bat)

It starts the painter in Mirror mode right away, then watches control/command.txt for one of:

    start | stop | restart | status

optionally followed by painter options, e.g.  restart --gpu 1  or  restart --max-fps 15 --motion 0.5
Only these options are accepted: --gpu N, --max-fps N, --motion X, --cpu, --no-tunnel. Anything else is refused,
so the file can't be used to run other commands.

It writes:
    status/agent.json     whether the painter is running, its pid and options, the last command and its result
    status/painter.log    everything the painter prints, fresh on each start (GPU choice, frame rate, viewers)
and the painter itself writes status/link.json and status/qr.png when its public link is up.
Closing the agent's window stops the painter too.
"""
import json, os, platform, re, signal, subprocess, sys, time
from pathlib import Path

HERE = Path(__file__).resolve().parent
CONTROL, STATUS = HERE / "control", HERE / "status"
COMMAND = CONTROL / "command.txt"
ALLOWED = re.compile(r"^(--gpu \d{1,2}|--max-fps \d{1,2}(\.\d+)?|--motion [01](\.\d+)?|--cpu|--no-tunnel)$")
WINDOWS = platform.system() == "Windows"

proc, log_file, state = None, None, {"args": [], "started": None, "last_command": None, "result": "", "exit_code": None}


def say(msg):
    print(time.strftime("%H:%M:%S"), "agent:", msg, flush=True)


def parse_args(words):
    args, i = [], 0
    while i < len(words):
        w = words[i]
        pair = f"{w} {words[i + 1]}" if i + 1 < len(words) else w
        if ALLOWED.match(pair) and w in ("--gpu", "--max-fps", "--motion"):
            args += [w, words[i + 1]]; i += 2
        elif ALLOWED.match(w):
            args.append(w); i += 1
        else:
            raise ValueError(f"option not allowed: {w!r}")
    return args


def running():
    return proc is not None and proc.poll() is None


def start(args):
    global proc, log_file
    if running():
        return "already running"
    for f in ("link.json", "qr.png"):
        (STATUS / f).unlink(missing_ok=True)
    log_file = open(STATUS / "painter.log", "w", encoding="utf-8", errors="replace")
    cmd = [sys.executable, "-u", str(HERE / "painter_server.py"), "--qr", "mirror", *args]
    kw = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if WINDOWS else {"start_new_session": True}
    proc = subprocess.Popen(cmd, cwd=HERE, stdout=log_file, stderr=subprocess.STDOUT, **kw)
    state.update(args=args, started=time.strftime("%Y-%m-%d %H:%M:%S"), exit_code=None)
    say(f"started the painter (pid {proc.pid}) {' '.join(args)}; its output goes to status/painter.log")
    return f"started (pid {proc.pid})"


def stop():
    global proc, log_file
    if not running():
        return "not running"
    pid = proc.pid
    if WINDOWS:   # the whole tree, so the painter's Cloudflare tunnel stops too
        subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True)
    else:
        try:
            os.killpg(pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
    try:
        proc.wait(timeout=15)
    except subprocess.TimeoutExpired:
        proc.kill()
    state["exit_code"] = proc.returncode
    proc = None
    if log_file:
        log_file.close(); log_file = None
    (STATUS / "link.json").unlink(missing_ok=True)
    say(f"stopped the painter (pid {pid})")
    return f"stopped (pid {pid})"


def write_status():
    out = {"running": running(), "pid": proc.pid if running() else None, **state,
           "updated": time.strftime("%Y-%m-%d %H:%M:%S")}
    tmp = STATUS / "agent.tmp"
    tmp.write_text(json.dumps(out, indent=2), encoding="utf-8")
    os.replace(tmp, STATUS / "agent.json")


def handle(line):
    words = line.split()
    if not words:
        return
    verb, rest = words[0].lower(), words[1:]
    state["last_command"] = line
    try:
        args = parse_args(rest) if rest else state["args"]
        if verb == "start":
            state["result"] = start(args)
        elif verb == "stop":
            state["result"] = stop()
        elif verb == "restart":
            stop(); time.sleep(1)
            state["result"] = start(args)
        elif verb == "status":
            state["result"] = "running" if running() else "not running"
        else:
            state["result"] = f"unknown command {verb!r} (use start, stop, restart or status)"
    except ValueError as e:
        state["result"] = f"refused: {e}"
    say(f"{line!r} -> {state['result']}")


def main():
    CONTROL.mkdir(exist_ok=True); STATUS.mkdir(exist_ok=True)
    # being closed (the window's X, logging off, a kill) stops the painter and its tunnel first
    for name in ("SIGTERM", "SIGBREAK", "SIGHUP"):
        if hasattr(signal, name):
            signal.signal(getattr(signal, name), lambda *_: sys.exit(0))
    say(f"watching {COMMAND} (start | stop | restart | status). Close this window to stop the painter and the agent.")
    state["result"] = start(parse_args(sys.argv[1:]))
    try:
        while True:
            if COMMAND.exists():
                try:
                    text = COMMAND.read_text(encoding="utf-8-sig").strip()
                    COMMAND.unlink()
                except OSError:
                    text = ""
                for line in text.splitlines()[:3]:
                    handle(line.strip())
            if proc is not None and proc.poll() is not None and state["exit_code"] is None:
                state["exit_code"] = proc.returncode
                state["result"] = f"the painter exited with code {proc.returncode}; see status/painter.log"
                say(state["result"])
            write_status()
            time.sleep(1.5)
    except KeyboardInterrupt:
        pass
    finally:
        stop(); write_status()


if __name__ == "__main__":
    main()
