# 시뮬레이션 모드 — 하드웨어 없이 E2E 테스트

실제 PLC / Serial Shutter / 열화상 카메라가 없는 환경에서 전체 시스템을 검증한다.

## 시뮬레이션 범위

| 컴포넌트 | 실제 | 시뮬 구현 |
|---|---|---|
| PLC (Modbus TCP) | `PlcModbusClient` (FluentModbus) | `FakePlcController` — 메모리 상태, SetTarget 호출 즉시 Current 일치 |
| Serial Shutter | `SerialShutterController` (COM 포트) | `FakeSerialShutterController` — `_isOpen` Dictionary |
| Camera | `CameraCaptureService` (OpenCvSharp `VideoCapture`) | 둘 중 선택:<br>① 일반 USB 웹캠 그대로 사용 (`SimulationMode=false`, `CameraIndex=0`)<br>② `FakeCameraCaptureService` — 합성 JPEG (타임스탬프 + 카메라 인덱스 텍스트 오버레이) |
| NATS | `NatsCommunicationService` | 시뮬 없음. 실제 NATS 서버(Docker) 사용 |

## 사전 준비

- .NET 8 SDK
- NATS 서버 실행 중 (`nats://127.0.0.1:4222`)
  - 이미 띄워뒀다면 그대로 사용
  - 새로 띄우려면: `docker compose -f docs/deployment/docker-compose.yml up -d`
- 웹캠 1개 (선택) — 없으면 두 Agent 모두 `SimulationMode=true`

## 1단계 — Master 시뮬 모드 활성화

설정 파일 위치: `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json`

| 방법 | 내용 |
|---|---|
| 신규 | 샘플 복사: `docs/samples/hardware.simulation.json` → `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` |
| 기존 | 최상위에 `"SimulationMode": true` 한 줄 추가 |

```json
{
  "SimulationMode": true,
  "Plc": { ... },
  "Nats": { "Url": "nats://127.0.0.1:4222" },
  ...
}
```

Master 실행:
```powershell
dotnet run --project HeatingCameraSystem.Master
```

확인 포인트 (Debug Output 또는 콘솔):
- `[AppServices] SimulationMode=true -> using Fake PLC + Fake Shutter`
- `[FakePlc] ConnectAsync(...) -> OK (simulated)`
- Dashboard 좌측 상단 온도/습도 = `0` → 곧 `25.0 / 50.0` 로 표시

`ConnectionMonitorService` 는 시뮬 모드에서 시작하지 않음 (불필요한 재연결 시도 차단).

## 2단계 — Agent 멀티 인스턴스 실행

같은 PC에서 2~3개 Agent 인스턴스를 띄운다. 같은 폴더의 `agent.json` 한 파일을 공유하므로 **CLI 인수로 오버라이드**한다.

```
사용법: Agent.exe <AgentId> <NatsUrl> [<CameraIndex>] [<StoragePath>] [<SimulationMode>]
```

PowerShell 터미널 3개:

```powershell
# 터미널 1 — 웹캠 사용 (index 0)
dotnet run --project HeatingCameraSystem.Agent -- Agent_0 nats://127.0.0.1:4222 0 ImageStorage_0 false

# 터미널 2 — 합성 카메라 (Fake)
dotnet run --project HeatingCameraSystem.Agent -- Agent_1 nats://127.0.0.1:4222 1 ImageStorage_1 true

# 터미널 3 — 합성 카메라 (Fake)
dotnet run --project HeatingCameraSystem.Agent -- Agent_2 nats://127.0.0.1:4222 2 ImageStorage_2 true
```

각 Agent 콘솔 로그:
```
[Agent_0] Camera idx=0 ready (REAL)
[Agent_0] Connected to NATS (nats://127.0.0.1:4222)
[Agent_0] Agent running. Press Ctrl+C to stop.
```

Master Dashboard → Agents 패널:
- `Agent_0` (녹색 점), `Agent_1` (녹색 점), `Agent_2` (녹색 점)
- 각 Agent 아래 `CAM-00`, `CAM-01`, `CAM-02` (cyan/green 점)

> **주의**: `CameraIndex` 는 Recipe 의 `RecipeStep.CameraIndex` 와 일치해야 NATS 토픽 `master.cmd.capture.Agent_{CameraIndex}` 로 라우팅된다. AgentId 와 CameraIndex 의 숫자 부분이 같도록 맞춘다.
>
> v2.1+ 부터 CLI 인수를 모두 넘기는 다중 인스턴스 기동은 `agent.json` 을 쓰지 않으므로 **동시 기동 race 없음**. 순차 기동 / 3초 대기 불필요.

## 3단계 — Recipe 실행

Master UI 에서:

1. **Recipe Editor** 탭 → 새 레시피 생성 또는 샘플 가져오기
   ```
   Name: SimTest
   GlobalTargetTemperature: 30
   GlobalTargetHumidity: 55
   Steps:
     - CameraIndex=0, TargetPositionIndex=1, TargetBlackBodyTemperature=35
     - CameraIndex=1, TargetPositionIndex=2, TargetBlackBodyTemperature=40
     - CameraIndex=2, TargetPositionIndex=3, TargetBlackBodyTemperature=45
   ```
2. **Dashboard** 탭 → SelectedRecipe = `SimTest` → `START`
3. 진행률 바 단계별 표시: `챔버 안정화` → `서보 이동 (1/3)` → `BB 안정화 (1/3)` → `캡처 (1/3)` → … → `완료`

## 검증 체크리스트

| 항목 | 확인 방법 |
|---|---|
| Dashboard 온도/습도 갱신 | 좌상단 숫자가 `30.0 / 55.0` 으로 변경 |
| Agent 하트비트 | Agents 패널의 점이 5초 간격으로 살아있음 |
| 캡처 명령 도달 | Agent 콘솔: `[Agent_N] Step <stepId>: OK -> <path>` |
| 캡처 결과 수신 | Master 의 RecipeEngine 콘솔: `Recipe 'SimTest' completed.` |
| 이력 저장 | History 탭에 단계 수만큼 행 추가 (`Temperature=30.0`, `Humidity=55.0` 기록) |
| 이미지 파일 | Agent 실행 폴더의 `ImageStorage_N\capture_*.jpg` 생성 — 웹캠은 실제 프레임, Fake 는 합성 이미지 |

## 자동 테스트

xUnit 단위 테스트로 시뮬 컴포넌트 + RecipeEngine 통합 시나리오를 검증한다.

```powershell
dotnet test --no-build
```

추가된 케이스 (`HeatingCameraSystem.Tests/SimulationTests.cs`, 11건):
- `FakePlcControllerTests` — 연결 / 온도·습도 snap / 서보 즉시 도착 / BB 인덱스 분리 / 미연결 시 throw (6건)
- `FakeSerialShutterControllerTests` — 카메라별 open/close 상태 / 미연결 throw / Disconnect 리셋 (3건)
- `FakeCameraCaptureServiceTests` — 합성 JPEG 생성 + 매직 바이트 검증 (1건)
- `RecipeEngineWithFakePlcTests` — FakePlc + mocked NATS 풀 사이클 실행 (1건)

총 38개 테스트 (기존 27 + 신규 11) 모두 통과해야 한다.

## 열화상 카메라로 교체

`SimulationMode=false` 로 두고 `CameraCaptureService` 의 `VideoCapture(cameraIndex)` 가 실제 열화상 SDK 와 호환되도록만 교체하면 된다. 인터페이스(`ICameraCaptureService`) 는 동일하게 유지되므로 호출부 변경 없음.

## 시뮬 모드 끄기

- `hardware.json` → `"SimulationMode": false`
- `agent.json` → `"SimulationMode": false` (또는 CLI 5번째 인수 `false`)
- Master / Agent 재시작 → 실제 PLC / Serial / 웹캠 사용 모드로 동작
