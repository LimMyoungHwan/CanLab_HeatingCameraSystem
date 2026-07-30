#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Register a logon Scheduled Task that autostarts AgentUI in the operator's interactive session.
.DESCRIPTION
    Why a Scheduled Task (not the Manager service): a Windows Service runs in Session 0 and cannot
    put a WPF window on the operator's desktop, and opening the camera there would fight the
    interactive AgentUI for the single UVC handle. So AgentUI is launched by THIS logon task, in the
    user session, while the Manager (HCS-Manager) only supervises per-camera runtimes over NATS (S7).

    UNATTENDED OPERATION (auto-login):
      For a camera PC with no operator present, Windows must auto-login so this logon task fires.
      Configure ONE of:
        * Sysinternals Autologon (recommended) — stores the password as an LSA secret, not plaintext.
        * netplwiz -> uncheck "Users must enter a user name and password".
      Do NOT hard-code the password in a script. This installer intentionally does not store it.

    Restart-on-failure: restarts AgentUI up to 3 times at 1-minute intervals; AgentUI's own
    single-instance mutex prevents duplicates.
.PARAMETER InstallRoot
    Root where AgentUI was deployed (expects <InstallRoot>\AgentUI\HeatingCameraSystem.AgentUI.exe).
.PARAMETER User
    The interactive account that logs on at the camera PC (default: current user).
.PARAMETER Headless
    Launch AgentUI with --headless (no window; NATS + camera runtimes only).
#>
param(
    [string]$InstallRoot = "C:\HeatingCameraSystem",
    [string]$User = "$env:USERDOMAIN\$env:USERNAME",
    [switch]$Headless
)

$ErrorActionPreference = "Stop"
$taskName = "HCS-AgentUI"
$exePath  = Join-Path $InstallRoot "AgentUI\HeatingCameraSystem.AgentUI.exe"

if (-not (Test-Path $exePath)) {
    Write-Warning "AgentUI not found at $exePath — copy the AgentUI build there before it will launch."
}

$action = if ($Headless) {
    New-ScheduledTaskAction -Execute $exePath -Argument "--headless"
} else {
    New-ScheduledTaskAction -Execute $exePath
}
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
    -Description "HeatingCameraSystem AgentUI autostart at logon (camera runtimes + operator UI)." | Out-Null

Write-Host "Scheduled task '$taskName' registered for '$User'." -ForegroundColor Green
Write-Host "  Exe:  $exePath $(if ($Headless) { '--headless' } else { '(no args)' })"
Write-Host "  Runs: at logon; restart 3x / 1min on failure; single instance."
Write-Host ""
Write-Host "Unattended camera PC? Configure auto-login (Sysinternals Autologon or netplwiz) so this task fires." -ForegroundColor Yellow
