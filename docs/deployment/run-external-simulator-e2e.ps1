param(
    [string]$NatsUrl = "nats://127.0.0.1:4222",
    [int]$FEnetPort = 2004,
    [string]$ConfigPath = "HeatingCameraSystem.Simulator\simulator.json",
    [switch]$StartNatsDocker
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$configFullPath = if ([System.IO.Path]::IsPathRooted($ConfigPath)) { $ConfigPath } else { Join-Path $repoRoot $ConfigPath }
$examplePath = Join-Path $repoRoot "HeatingCameraSystem.Simulator\simulator.example.json"
$simOut = Join-Path $repoRoot ".omo\evidence\task-12-simulator-stdout.txt"
$simErr = Join-Path $repoRoot ".omo\evidence\task-12-simulator-stderr.txt"
$startedContainer = $false
$simulator = $null

function Test-TcpPort([int]$Port) {
    return Test-NetConnection -ComputerName 127.0.0.1 -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue
}

try {
    if (-not (Test-Path -LiteralPath $configFullPath)) {
        $dir = Split-Path -Parent $configFullPath
        if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        Copy-Item -LiteralPath $examplePath -Destination $configFullPath
        Write-Host "[E2E] created config: $configFullPath"
    }

    if ($StartNatsDocker -and -not (Test-TcpPort 4222)) {
        docker compose -f (Join-Path $repoRoot "docs\deployment\docker-compose.yml") up -d nats | Out-Null
        $startedContainer = $true
    }

    if (-not (Test-TcpPort 4222)) { throw "NATS is not reachable at 127.0.0.1:4222" }
    if (Test-TcpPort $FEnetPort) { throw "FEnet port $FEnetPort is already in use" }

    dotnet build (Join-Path $repoRoot "HeatingCameraSystem.slnx") --nologo

    Remove-Item -LiteralPath $simOut,$simErr -ErrorAction SilentlyContinue
    $psi = [System.Diagnostics.ProcessStartInfo]::new("dotnet", "run --no-build --project `"$repoRoot\HeatingCameraSystem.Simulator`" -- `"$configFullPath`"")
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $false
    $psi.RedirectStandardError = $false
    $psi.CreateNoWindow = $true
    $simulator = [System.Diagnostics.Process]::Start($psi)

    $ready = $false
    for ($i = 0; $i -lt 80; $i++) {
        Start-Sleep -Milliseconds 250
        if ($simulator.HasExited) { break }
        if (Test-TcpPort $FEnetPort) { $ready = $true; break }
    }
    if (-not $ready) { throw "Simulator did not open FEnet port $FEnetPort" }
    "SIMULATOR READY port=$FEnetPort pid=$($simulator.Id)" | Tee-Object -FilePath $simOut

    dotnet run --no-build --project (Join-Path $repoRoot "HeatingCameraSystem.E2EDriver") -- --external-simulator $NatsUrl 127.0.0.1 $FEnetPort 30
    exit $LASTEXITCODE
}
finally {
    if ($simulator -ne $null) {
        try {
            $simulator.StandardInput.WriteLine("quit")
            $simulator.StandardInput.Flush()
        }
        catch [System.Exception] {
            Write-Warning "Simulator quit signal failed: $($_.Exception.Message)"
        }
        if (-not $simulator.WaitForExit(5000)) { $simulator.Kill() }
        $simulator.Dispose()
    }

    if ($startedContainer) {
        docker compose -f (Join-Path $repoRoot "docs\deployment\docker-compose.yml") stop nats | Out-Null
    }
}
