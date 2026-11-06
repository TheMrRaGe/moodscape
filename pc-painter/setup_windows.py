"""
One-time setup of the Moodscape Mirror on Windows (setup-mirror.bat runs this; you don't need to run it yourself).

    python setup_windows.py                 desktop icon, start at login, local Wi-Fi access for the app
    python setup_windows.py --no-autostart  everything except starting at login
    python setup_windows.py --remove        undo all of it (your people and captures are kept)

What it sets up:
  * A "Moodscape Mirror" icon on the desktop. Double-click it to start the Mirror; it opens the live dashboard,
    which shows the QR code for your phone. If the Mirror is already running it just opens the dashboard.
  * The same, started minimized whenever you log in to Windows (without opening the dashboard).
  * A Windows Firewall rule (asks for administrator approval once) that lets phones on your home Wi-Fi reach the
    painter directly on port 8765. Only for networks Windows calls Private, and only from your own local network.
    The Android app then finds the laptop by itself whenever both are on the same Wi-Fi; no QR code needed.
"""
import argparse, base64, subprocess, sys, tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
NAME = "Moodscape Mirror"
RULE = "Moodscape Mirror (phones on this Wi-Fi)"
ICON = HERE / "status" / "mirror.ico"
AGENT_BAT = HERE / "mirror-agent.bat"


def ps(script, check=False):
    """Run a PowerShell script (passed encoded, so nothing in it needs escaping for cmd)."""
    enc = base64.b64encode(script.encode("utf-16-le")).decode()
    r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", enc],
                       capture_output=True, text=True)
    if check and r.returncode:
        raise RuntimeError((r.stderr or r.stdout).strip())
    return r


def q(s):
    return "'" + str(s).replace("'", "''") + "'"   # a PowerShell single-quoted string


def make_icon():
    try:
        from PIL import Image
        ICON.parent.mkdir(exist_ok=True)
        Image.open(HERE.parent / "icon-512.png").convert("RGBA").save(ICON, sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
        return ICON
    except Exception as e:
        print(f"  (no custom icon: {e})")
        return None


def shortcut(folder_expr, args, style, icon):
    return f"""
$dir = [Environment]::GetFolderPath({q(folder_expr)})
$s = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $dir {q(NAME + '.lnk')}))
$s.TargetPath = {q(AGENT_BAT)}
$s.Arguments = {q(args)}
$s.WorkingDirectory = {q(HERE)}
$s.WindowStyle = {style}
$s.Description = 'Start the Moodscape Mirror painter and open its dashboard'
{f'$s.IconLocation = {q(str(icon) + ",0")}' if icon else ''}
$s.Save()
Write-Output (Join-Path $dir {q(NAME + '.lnk')})
"""


def firewall():
    have = subprocess.run(["netsh", "advfirewall", "firewall", "show", "rule", f"name={RULE}"], capture_output=True, text=True)
    if have.returncode == 0 and "8765" in have.stdout:
        print("  Firewall: phones on your Wi-Fi can already reach the painter.")
    else:
        print("  Firewall: Windows will ask for permission to let phones on your home Wi-Fi reach the painter (port 8765).")
        add = (f"advfirewall firewall add rule name=\"{RULE}\" dir=in action=allow protocol=TCP localport=8765 "
               "profile=private,domain remoteip=localsubnet")
        r = ps(f"try {{ $p = Start-Process netsh -ArgumentList {q(add)} -Verb RunAs -Wait -PassThru -WindowStyle Hidden; exit $p.ExitCode }} catch {{ exit 5 }}")
        if r.returncode == 0:
            print("  Firewall: done.")
        else:
            print("  Firewall: skipped (permission wasn't given). The app still works through the QR code / internet link;")
            print("            run setup-mirror.bat again any time to allow the faster local connection.")
    nets = ps("Get-NetConnectionProfile | ForEach-Object { $_.Name + '|' + $_.NetworkCategory }").stdout.split()
    for line in nets:
        name, _, cat = line.rpartition("|")
        if cat.strip() == "Public":
            print(f"  Note: Windows treats the network '{name}' as Public, so phones on it can't connect directly.")
            print("        If it's your home Wi-Fi: Settings > Network & internet > Wi-Fi > (your network) > Private network.")


def remove():
    for folder in ("Desktop", "Startup"):
        r = ps(f"$f = Join-Path ([Environment]::GetFolderPath({q(folder)})) {q(NAME + '.lnk')}; if (Test-Path $f) {{ Remove-Item $f; 'removed ' + $f }}")
        print(" ", r.stdout.strip() or f"no {folder} shortcut")
    ps(f"$f = Join-Path ([Environment]::GetFolderPath('Startup')) 'Moodscape Mirror agent.lnk'; if (Test-Path $f) {{ Remove-Item $f }}")
    have = subprocess.run(["netsh", "advfirewall", "firewall", "show", "rule", f"name={RULE}"], capture_output=True)
    if have.returncode == 0:
        delete = f'advfirewall firewall delete rule name="{RULE}"'
        r = ps(f"try {{ $p = Start-Process netsh -ArgumentList {q(delete)} -Verb RunAs -Wait -PassThru -WindowStyle Hidden; exit $p.ExitCode }} catch {{ exit 5 }}")
        print("  firewall rule removed" if r.returncode == 0 else "  firewall rule kept (permission wasn't given)")
    print("Done. Your people, their tastes and your captures are untouched.")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--no-autostart", action="store_true", help="don't start the Mirror when you log in")
    ap.add_argument("--no-firewall", action="store_true", help="don't add the firewall rule for phones on your Wi-Fi")
    ap.add_argument("--remove", action="store_true", help="undo the setup")
    a = ap.parse_args()
    if sys.platform != "win32":
        raise SystemExit("This sets up Windows shortcuts; on other systems run mirror_agent.py directly.")
    if a.remove:
        return remove()
    print("Setting up the Moodscape Mirror on this PC:")
    icon = make_icon()
    print("  Desktop icon:", ps(shortcut("Desktop", "open", 7, icon), check=True).stdout.strip())
    # the older name, from mirror-agent.bat autostart: replaced by the one below, so it can't start twice
    ps(f"$f = Join-Path ([Environment]::GetFolderPath('Startup')) 'Moodscape Mirror agent.lnk'; if (Test-Path $f) {{ Remove-Item $f }}")
    if a.no_autostart:
        ps(f"$f = Join-Path ([Environment]::GetFolderPath('Startup')) {q(NAME + '.lnk')}; if (Test-Path $f) {{ Remove-Item $f }}")
        print("  Start at login: off")
    else:
        ps(shortcut("Startup", "", 7, icon), check=True)
        print("  Start at login: on (minimized; it doesn't open anything, it just has the painter ready)")
    if not a.no_firewall:
        firewall()
    print(f"\nAll set. Undo any time with:  setup-mirror.bat remove")


if __name__ == "__main__":
    main()
