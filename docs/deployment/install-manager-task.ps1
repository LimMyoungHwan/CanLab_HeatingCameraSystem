#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Register a logon Scheduled Task that autostarts the HCS-Manager in the operator's interactive session.
.DESCRIPTION
    Why a Scheduled Task (not a Windows Service): the Manager (HeatingCameraSystem.AgentManager) is a plain
    .NET Generic Host console app — the session-0 Windows Service host was removed. It now runs in the
    operator's login session, started at logon alongside AgentUI, and only supervises per-camera runtimes
    over NATS (S7 runtimeLoad/runtimeUnload). It does NOT spawn AgentUI — AgentUI keeps its own logon task
    so it holds the single UVC handle in the interactive session.

    UNATTENDED OPERATION (auto-login):
      For a PC with no operator present, Windows must auto-login so this logon task fires.
      Configure ONE of:
        * Sysinternals Autologon (recommended) — stores the password as an LSA secret, not plaintext.
        * netplwiz -> uncheck "Users must enter a user name and password".
      Do NOT hard-code the password in a script. This installer intentionally does not store it.

    Restart-on-failure: restarts the Manager up to 3 times at 1-minute intervals.
.PARAMETER InstallRoot
    Root where the Manager was deployed (expects <InstallRoot>\Manager\HeatingCameraSystem.AgentManager.exe).
    Also passed to the exe as its installRoot argument (args[0]).
.PARAMETER User
    The interactive account that logs on at the PC (default: current user).
#>
param(
    [string]$InstallRoot = "C:\HeatingCameraSystem",
    [string]$User = "$env:USERDOMAIN\$env:USERNAME"
)

$ErrorActionPreference = "Stop"
$taskName = "HCS-Manager"
$exePath  = Join-Path $InstallRoot "Manager\HeatingCameraSystem.AgentManager.exe"

if (-not (Test-Path $exePath)) {
    Write-Warning "Manager not found at $exePath — copy the Manager build there before it will launch."
}

$action    = New-ScheduledTaskAction -Execute $exePath -Argument ('"{0}"' -f $InstallRoot)
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $User
$principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Limited
$settings  = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -MultipleInstances IgnoreNew

if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings `
    -Description "HeatingCameraSystem Manager autostart at logon (camera approval/inventory + AgentUI runtime supervisor over NATS, S7)." | Out-Null

Write-Host "Scheduled task '$taskName' registered for '$User'." -ForegroundColor Green
Write-Host "  Exe:  $exePath $InstallRoot"
Write-Host "  Runs: at logon; restart 3x / 1min on failure; single instance."
Write-Host ""
Write-Host "Unattended PC? Configure auto-login (Sysinternals Autologon or netplwiz) so this task fires." -ForegroundColor Yellow
