<#
HeatingCameraSystem - Agent Manager 승인 루프 E2E Runner (SC-12)

전제:
  - NATS 서버가 nats://127.0.0.1:4222 에서 실행 중이어야 함
  - .NET 8 SDK 설치
  - 저장소 루트에서 실행

동작:
  1. ManagerE2EDriver 를 임시 폴더에 publish
  2. 실행 — AgentManager in-process 호스팅 (WPF/콘솔 Agent 없이):
     [범위 1] FakeEnumerator 카메라 2대 발견 → inventory → driver Approve
              → AgentId 부여 + 승인 재발행 → manager-state.json 영속 검증
     [범위 2] FakeAgentUiRuntime 로 S7 런타임 IPC 검증: 2대 로드+하트비트 →
              1대 Disable → 해당 카메라만 runtimeUnload/not-running, 나머지는 유지
  3. 종료 코드 반환 (0 PASS / 1 FAIL / 2 NATS 연결 실패 / 3 timeout)

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
