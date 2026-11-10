# Night Pages agent: install (or remove) the hidden background version.
#
# Registers a Windows scheduled task, for this user only, that runs the agent with pythonw.exe (no console, no
# window). It starts when you log on and is checked every minute; if the agent is already running, that check does
# nothing at all. Nothing it runs ever takes focus, so your active app never changes.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File install_background.ps1            install / reinstall
#   powershell -NoProfile -ExecutionPolicy Bypass -File install_background.ps1 -Remove    uninstall
param([switch]$Remove)
$ErrorActionPreference = "Stop"
$TaskName = "Night Pages Unity agent"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = (Resolve-Path (Join-Path $Here "..\..")).Path
$Script = Join-Path $Here "np_unity_agent.py"

if ($Remove) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Removed the background Night Pages agent."
    } else { Write-Host "The background Night Pages agent wasn't installed." }
    exit 0
}

# A windowless Python: the one the Moodscape painter uses on this PC first, then the py launcher's, then usual installs.
$candidates = @(
    (Join-Path $Project "..\pc-painter\.venv\Scripts\pythonw.exe"),
    "$env:LOCALAPPDATA\Programs\Python\Python313\pythonw.exe",
    "$env:LOCALAPPDATA\Programs\Python\Python312\pythonw.exe",
    "$env:LOCALAPPDATA\Programs\Python\Python311\pythonw.exe",
    "$env:LOCALAPPDATA\Programs\Python\Python310\pythonw.exe",
    "$env:USERPROFILE\anaconda3\pythonw.exe"
)
$Pyw = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Pyw) {
    $launcher = Get-Command pyw.exe -ErrorAction SilentlyContinue
    if ($launcher) { $Pyw = $launcher.Source }
}
if (-not $Pyw) { throw "Couldn't find pythonw.exe. Install Python 3 from https://www.python.org/downloads/ and run this again." }
$Pyw = (Resolve-Path $Pyw).Path
Write-Host "Python (windowless): $Pyw"

$argLine = "`"$Script`" --background"
if ((Split-Path -Leaf $Pyw) -ieq "pyw.exe") { $argLine = "-3 $argLine" }
$action = New-ScheduledTaskAction -Execute $Pyw -Argument $argLine -WorkingDirectory $Project
$atLogon = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
function Every-Minute([bool]$withDuration) {
    if ($withDuration) {
        return New-ScheduledTaskTrigger -Once -At (Get-Date).AddSeconds(5) -RepetitionInterval (New-TimeSpan -Minutes 1) -RepetitionDuration (New-TimeSpan -Days 3650)
    }
    return New-ScheduledTaskTrigger -Once -At (Get-Date).AddSeconds(5) -RepetitionInterval (New-TimeSpan -Minutes 1)     # newer Windows: repeats indefinitely
}
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -Hidden -Priority 7
# Interactive: runs in your session (so the game can use the graphics card for screenshots), only while you're logged on.
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
$desc = "Lets Claude build and test Night Pages in the background. Hidden, low priority, never opens a window. Folder: $Project"
try {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger @($atLogon, (Every-Minute $true)) -Settings $settings `
        -Principal $principal -Description $desc -Force | Out-Null
} catch {
    Write-Host "(retrying with an open-ended repeat: $($_.Exception.Message))"
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger @($atLogon, (Every-Minute $false)) -Settings $settings `
        -Principal $principal -Description $desc -Force | Out-Null
}
Start-ScheduledTask -TaskName $TaskName
Write-Host ""
Write-Host "Installed. The Night Pages agent now runs hidden whenever you're logged in."
Write-Host "If the old agent window is open, close it: the background one takes over within a minute."
