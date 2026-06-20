<#
HeatingCameraSystem - End-to-End Simulation Runner

전제:
  - NATS 서버가 nats://127.0.0.1:4222 에서 실행 중이어야 함
  - .NET 8 SDK 설치
  - 저장소 루트에서 실행

동작:
  1. Agent + E2EDriver 를 임시 폴더에 publish
  2. Agent_0 / Agent_1 (둘 다 SimulationMode, 카메라 idx 0/1) 백그라운드 실행
  3. E2EDriver 실행 (FakePlc + RecipeEngine 로직, 4-step recipe)
  4. 캡처 결과 4건 + JPEG 파일 4건 검증
  5. Agent 종료, 종료 코드 반환

사용:
  PS> ./docs/deployment/run-e2e-simulation.ps1
#>

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$pubDir   = Join-Path $env:TEMP "HCS_E2E"

Write-Host "[E2E] repo  = $repoRoot"
Write-Host "[E2E] pub   = $pubDir"

if (Test-Path $pubDir) { Remove-Item -Recurse -Force $pubDir }

Write-Host "[E2E] publish Agent + Driver ..."
dotnet publish (Join-Path $repoRoot "HeatingCameraSystem.Agent/HeatingCameraSystem.Agent.csproj") `
    -c Debug -o (Join-Path $pubDir "Agent") --nologo | Out-Null
dotnet publish (Join-Path $repoRoot "HeatingCameraSystem.E2EDriver/HeatingCameraSystem.E2EDriver.csproj") `
    -c Debug -o (Join-Path $pubDir "Driver") --nologo | Out-Null

$agentExe  = Join-Path $pubDir "Agent\HeatingCameraSystem.Agent.exe"
$driverExe = Join-Path $pubDir "Driver\HeatingCameraSystem.E2EDriver.exe"

Write-Host "[E2E] launch Agent_0 (cam idx 0)"
$a0 = Start-Process -FilePath $agentExe `
    -ArgumentList "Agent_0","nats://127.0.0.1:4222","0","ImageStorage_0","true" `
    -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput "$pubDir\agent0.log" -RedirectStandardError "$pubDir\agent0.err"
Start-Sleep -Seconds 3

Write-Host "[E2E] launch Agent_1 (cam idx 1)"
$a1 = Start-Process -FilePath $agentExe `
    -ArgumentList "Agent_1","nats://127.0.0.1:4222","1","ImageStorage_1","true" `
    -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput "$pubDir\agent1.log" -RedirectStandardError "$pubDir\agent1.err"
Start-Sleep -Seconds 3

try {
    if ($a0.HasExited -or $a1.HasExited) {
        Write-Error "Agent process died before driver started. Check $pubDir\agent*.log / .err"
        exit 99
    }

    Write-Host "[E2E] run driver"
    & $driverExe "nats://127.0.0.1:4222" "20"
    $exitCode = $LASTEXITCODE
}
finally {
    Stop-Process -Id $a0.Id -Force -ErrorAction SilentlyContinue
    Stop-Process -Id $a1.Id -Force -ErrorAction SilentlyContinue
}

Write-Host "[E2E] exit code = $exitCode"
exit $exitCode
