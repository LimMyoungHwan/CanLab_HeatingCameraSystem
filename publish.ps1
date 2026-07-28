#Requires -Version 5.1
# Gathers deployment output into:
#   master_bin/        Master PC (WPF operator console)
#   agent_bin/         camera PC - AgentUI (WPF, live view + serial control)
#   agent_console_bin/ camera PC - Agent (headless console). Deploy AgentUI OR Agent per PC, not both.
# Usage:
#   .\publish.ps1                     # framework-dependent (needs .NET 8 Desktop Runtime on target)
#   .\publish.ps1 -SelfContained      # portable, bundles runtime (no .NET install needed on target)
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$sc = if ($SelfContained) { "true" } else { "false" }

$targets = @(
    @{ Name = "Master";  Proj = "HeatingCameraSystem.Master\HeatingCameraSystem.Master.csproj";   Out = "master_bin";        Exe = "HeatingCameraSystem.Master.exe" },
    @{ Name = "AgentUI"; Proj = "HeatingCameraSystem.AgentUI\HeatingCameraSystem.AgentUI.csproj";  Out = "agent_bin";         Exe = "HeatingCameraSystem.AgentUI.exe" },
    @{ Name = "Agent";   Proj = "HeatingCameraSystem.Agent\HeatingCameraSystem.Agent.csproj";      Out = "agent_console_bin"; Exe = "HeatingCameraSystem.Agent.exe" }
)

foreach ($t in $targets) {
    $outDir = Join-Path $root $t.Out
    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
    Write-Host "==> Publishing $($t.Name) -> $($t.Out)\  (self-contained=$sc)" -ForegroundColor Cyan
    dotnet publish (Join-Path $root $t.Proj) -c $Configuration -r $Runtime --self-contained $sc -o $outDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "$($t.Name) publish failed (exit $LASTEXITCODE)" }
}

Write-Host "`nDone." -ForegroundColor Green
foreach ($t in $targets) { Write-Host "  $($t.Out)\$($t.Exe)" }
