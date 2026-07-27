# 시뮬레이션과 외부 Simulator

하드웨어 없이 검증하는 방법은 두 가지다. 이름이 비슷하지만 목적이 다르다.

| 방식 | 설정 | 경계 | 용도 |
|---|---|---|---|
| 내부 `SimulationMode` | `hardware.json` / `agent.json` 의 `SimulationMode=true` | 프로세스 내부 Fake 구현 | 빠른 개발, UI/레시피 로직 확인 |
| 외부 Simulator | `HeatingCameraSystem.Simulator` 별도 실행, Master는 `SimulationMode=false` | 실제 XGT FEnet TCP + NATS | PLC/NATS 프로토콜 경계까지 포함한 하드웨어 없는 E2E |

외부 Simulator는 COM 셔터, DirectShow 카메라 장치, AgentUI 실카메라 경로를 에뮬레이션하지 않는다.

## 내부 SimulationMode

내부 모드는 앱 안에서 Fake 구현을 선택한다.

| 컴포넌트 | 실제 모드 | 내부 시뮬 구현 |
|---|---|---|
| Master PLC | `PlcXgtClient` (LS XGT FEnet, TCP 2004) | `FakePlcController` |
| Master 셔터 | `SerialShutterController` | `FakeSerialShutterController` |
| Agent 카메라 | `CameraCaptureService` | `FakeCameraCaptureService` |
| NATS | `NatsCommunicationService` | 실제 NATS 서버 사용 |

Master 내부 시뮬:

```json
{
  "SimulationMode": true,
  "Nats": { "Url": "nats://127.0.0.1:4222" }
}
```

Agent 내부 시뮬:

```powershell
dotnet run --project HeatingCameraSystem.Agent -- Agent_0 nats://127.0.0.1:4222 0 ImageStorage_0 true
dotnet run --project HeatingCameraSystem.Agent -- Agent_1 nats://127.0.0.1:4222 1 ImageStorage_1 true
```

기존 내부 E2E:

```powershell
docker compose -f docs/deployment/docker-compose.yml up -d
docs/deployment/run-e2e-simulation.ps1
```

## 외부 Simulator

외부 Simulator는 Master/Driver를 실제 하드웨어 모드로 두고, 별도 프로세스가 하드웨어처럼 응답한다.

| 경계 | Simulator 동작 |
|---|---|
| PLC | LS XGT FEnet TCP 서버, 기본 `127.0.0.1:2004` |
| 챔버 | 온도/습도 PV가 SV로 결정적 램프 이동 |
| 블랙바디 | BB1/BB2 PV/SV를 PLC 워드로 에뮬레이션 |
| 서보 | 포인트 이동 비트, busy, 현재 좌표/포인트 에뮬레이션 |
| 카메라 Agent | 기존 NATS subject로 heartbeat, live JPEG, capture result 발행 |

샘플 설정:

```powershell
HeatingCameraSystem.Simulator/simulator.example.json
```

Simulator 단독 실행:

```powershell
docs/deployment/start-external-simulator.ps1
```

처음 실행 시 `HeatingCameraSystem.Simulator/simulator.json` 이 없으면 샘플을 복사한다. 기존 설정은 덮어쓰지 않는다.

## 외부 E2E 한 번에 실행

NATS가 이미 떠 있는 경우:

```powershell
docs/deployment/run-external-simulator-e2e.ps1
```

Docker로 NATS도 같이 띄워야 하는 경우:

```powershell
docs/deployment/run-external-simulator-e2e.ps1 -StartNatsDocker
```

이 스크립트는 다음을 수행한다.

1. 솔루션 빌드
2. Simulator child process 시작
3. `E2EDriver --external-simulator nats://127.0.0.1:4222 127.0.0.1 2004` 실행
4. 2대 카메라, 4단계 recipe-equivalent 흐름 검증
5. 자신이 시작한 Simulator와 Docker NATS만 정리

성공 기준:

```text
[E2E] Captures received: 4 / 4
[E2E] Agent_0 captures: 2, Agent_1 captures: 2
[E2E] Image files: 4 present, 0 missing
[E2E] Final PLC: T=30.0, H=55.0, point=4, busy=False
[E2E] *** PASS ***
```

## Master를 외부 Simulator에 붙이기

Master의 `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` 을 백업한 뒤 아래처럼 맞춘다.

```json
{
  "SimulationMode": false,
  "Plc": {
    "IpAddress": "127.0.0.1",
    "Port": 2004,
    "CpuSeries": "XGB",
    "UseHexBitIndex": true
  },
  "Nats": {
    "Url": "nats://127.0.0.1:4222"
  }
}
```

주의: 운영 설정을 덮어쓰지 말고 백업/복원으로 전환한다.

```powershell
$dir = Join-Path $env:LOCALAPPDATA "HeatingCameraSystem"
Copy-Item "$dir\hardware.json" "$dir\hardware.before-external-simulator.json"
```

## 콘솔 명령

Simulator 실행 중 지원 명령:

```text
status
plc online
plc offline
plc fault <0-19> on|off
camera <AgentId> online|fault|offline
quit
```

예:

```text
camera Agent_1 offline
plc offline
```

## 범위 밖

- 시리얼 셔터 COM 포트 에뮬레이션 없음
- DirectShow/USB 카메라 가상 장치 없음
- AgentUI 실카메라 live path 검증 없음
- 실제 흑체 직접 제어 프로토콜 없음. 현재는 PLC 경유 BB 레지스터로 검증한다.
