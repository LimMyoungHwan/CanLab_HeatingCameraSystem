<#
HeatingCameraSystem - Agent Manager 승인 루프 E2E Runner (SC-12)

전제:
  - NATS 서버가 nats://127.0.0.1:4222 에서 실행 중이어야 함
  - .NET 8 SDK 설치
  - 저장소 루트에서 실행

동작:
  1. ManagerE2EDriver 를 임시 폴더에 publish
  2. 실행 — AgentManager(SimulationMode) in-process 호스팅
     FakeEnumerator 카메라 2대 발견 → inventory 발행 → driver Approve
     → AgentId 부여 + 승인 재발행 → manager-state.json 영속 검증
  3. 종료 코드 반환 (0 PASS / 1 FAIL / 2 NATS 연결 실패 / 3 timeout)

  ※ 캡처 roundtrip 은 run-e2e-simulation.ps1 (E2EDriver) 가 담당한다.

사용:
  PS> ./docs/deployment/run-manager-e2e.ps1
#>

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$pubDir   = Join-Path $env:TEMP "HCS_MgrE2E_pub"

Write-Host "[MGR-E2E] repo = $repoRoot"
Write-Host "[MGR-E2E] pub  = $pubDir"

if (Test-Path $pubDir) { Remove-Item -Recurse -Force $pubDir }

Write-Host "[MGR-E2E] publish ManagerE2EDriver ..."
dotnet publish (Join-Path $repoRoot "HeatingCameraSystem.ManagerE2EDriver/HeatingCameraSystem.ManagerE2EDriver.csproj") `
    -c Debug -o $pubDir --nologo | Out-Null

$driverExe = Join-Path $pubDir "HeatingCameraSystem.ManagerE2EDriver.exe"

Write-Host "[MGR-E2E] run driver"
& $driverExe "nats://127.0.0.1:4222" "20"
$exitCode = $LASTEXITCODE

Write-Host "[MGR-E2E] exit code = $exitCode"
exit $exitCode
