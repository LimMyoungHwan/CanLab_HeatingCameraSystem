param(
    [string]$ConfigPath = "HeatingCameraSystem.Simulator\simulator.json"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$configFullPath = if ([System.IO.Path]::IsPathRooted($ConfigPath)) { $ConfigPath } else { Join-Path $repoRoot $ConfigPath }
$examplePath = Join-Path $repoRoot "HeatingCameraSystem.Simulator\simulator.example.json"

if (-not (Test-Path -LiteralPath $configFullPath)) {
    $dir = Split-Path -Parent $configFullPath
    if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Copy-Item -LiteralPath $examplePath -Destination $configFullPath
    Write-Host "[Simulator] created config: $configFullPath"
}

dotnet run --project (Join-Path $repoRoot "HeatingCameraSystem.Simulator") -- $configFullPath
