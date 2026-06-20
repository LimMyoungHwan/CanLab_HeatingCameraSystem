# 02 — 설정 매뉴얼

> `hardware.json` (Master) + `agent.json` (Agent) 두 파일이 모든 런타임 동작을 결정한다. 시뮬레이션 모드, 카메라↔Agent 매핑, 시리얼 셔터 프로토콜 변경, LiteDB 관리까지 한 곳에 정리.

설치 절차는 [01-installation.md](./01-installation.md), 운영 사용법은 [03-usage.md](./03-usage.md).

## 1. 파일 위치 요약

| 파일 | 위치 | 사용 주체 | 자동 생성 |
|---|---|---|---|
| `hardware.json` | `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` | Master | ✅ 최초 기동 시 |
| `data.db` | `%LOCALAPPDATA%\HeatingCameraSystem\data.db` | Master (LiteDB) | ✅ 최초 기동 시 (빈 상태) |
| `agent.json` | Agent exe 폴더 | Agent | ✅ 최초 기동 시 |
| 캡처 이미지 (Agent local) | `<StoragePath>` (절대경로 또는 Agent exe 폴더 기준 상대경로) | Agent | ✅ 폴더 없으면 생성 |
| 캡처 이미지 (Master cache) | `%LOCALAPPDATA%\HeatingCameraSystem\ImageCache\` | Master (NATS 로 받은 ImageBytes 저장) | ✅ Initialize 시 생성 |

샘플 파일은 [`docs/samples/`](../samples/) 에 — `hardware.json`, `hardware.simulation.json`, `agent.json`, `agent.simulation.json`, `agent.webcam.json`.

## 2. hardware.json 전체 레퍼런스 (Master)

소스: `HeatingCameraSystem.Core/Config/HardwareSettings.cs`

```jsonc
{
  "SimulationMode": false,              // §3 참조
  "Plc":          { /* §2.1 */ },
  "Nats":         { "Url": "nats://127.0.0.1:4222" },
  "Serial":       { /* §2.3 */ },
  "RecipeEngine": { /* §2.4 */ }
}
```

### 2.1 PLC 섹션

| 필드 | 타입 | 기본값 | 의미 |
|---|---|---|---|
| `IpAddress` | string | `192.168.1.100` | A&D PLC IP |
| `Port` | int | `502` | Modbus TCP 표준 포트 |
| `UnitId` | int | `0` | Modbus Unit / Slave ID |
| `RegTempPv` | int | `100` | 챔버 온도 현재값 레지스터 (PV) |
| `RegTempSv` | int | `101` | 챔버 온도 목표값 레지스터 (SV) |
| `RegHumPv` | int | `102` | 챔버 습도 PV |
| `RegHumSv` | int | `103` | 챔버 습도 SV |
| `RegServoPosSv` | int | `104` | 서보 목표 위치 인덱스 |
| `RegBb1TempSv` | int | `105` | 블랙바디 #1 목표온도 |
| `RegBb2TempSv` | int | `106` | 블랙바디 #2 목표온도 |
| `RegBb1TempPv` | int | `107` | 블랙바디 #1 현재온도 |
| `RegBb2TempPv` | int | `108` | 블랙바디 #2 현재온도 |
| `CoilRunStop` | int | `10` | 챔버 운전/정지 코일 |
| `CoilServoArrival` | int | `11` | 서보 도착 신호 코일 |
| `CoilEStop` | int | `12` | 비상정지 코일 |

**스케일 규칙** (`PlcModbusClient` 참조):
- 온도·습도 쓰기: `value * 10` → `short` (소수점 1자리)
- 온도·습도 읽기: `short / 10f`
- 즉 PLC 측은 0.1°C / 0.1%RH 단위 정수로 보관

**플레이스홀더 주의**: 기본 레지스터 주소(100~108, 10~12)는 임시값. 실제 A&D PLC 명세에 맞춰 운영자가 수정. 샘플 `docs/samples/hardware.json` 은 4096~4104 / 256~258 등 실제 사이트 예시.

### 2.2 NATS 섹션

| 필드 | 타입 | 기본값 |
|---|---|---|
| `Url` | string | `nats://127.0.0.1:4222` |

같은 네트워크의 NATS 서버 IP 로 변경. 인증 사용 시 `nats://user:pass@host:4222`.

### 2.3 Serial 섹션 (셔터 기본값)

| 필드 | 타입 | 기본값 | 비고 |
|---|---|---|---|
| `PortName` | string | `COM3` | 가상 시리얼 포트명 |
| `BaudRate` | int | `9600` | |
| `DataBits` | int | `8` | |
| `Parity` | string | `None` | `None / Odd / Even / Mark / Space` |
| `StopBits` | string | `One` | `One / OnePointFive / Two` |

> Master 의 **Settings 탭** 에서 카메라별 시리얼 설정을 NATS 로 원격 전송하는 기능이 별도로 있다. `hardware.json` 의 Serial 은 Master 자체가 직접 잡는 셔터(드물게 사용)의 기본값. 일반적으로는 매뉴얼 03 §3 의 원격 시리얼 설정을 사용.

### 2.4 RecipeEngine 섹션

| 필드 | 타입 | 기본값 | 의미 |
|---|---|---|---|
| `TemperatureTolerance` | float | `0.5` | 챔버 / 블랙바디 안정화 판정 허용 오차(°C) |
| `CaptureResultTimeoutSeconds` | int | `30` | Agent 캡처 결과 대기 타임아웃 |

> 현재 `TemperatureTolerance` 는 코드에 하드코딩(`RecipeEngine._tempTolerance = 0.5f`)되어 있어 이 값을 바꿔도 즉시 반영되지 않는다. 운영 시점에 필요하면 별도 이슈로 처리.

## 3. SimulationMode — 3가지 시나리오

### 3.1 운영(실 하드웨어)

```json
{ "SimulationMode": false, ... }
```

- Master: 실제 `PlcModbusClient` + `SerialShutterController` 사용
- Agent: 실제 `CameraCaptureService` (OpenCvSharp `VideoCapture`) 사용
- `ConnectionMonitorService` 30초 주기 재연결 ON

### 3.2 전체 시뮬 (PLC + Shutter + Camera 모두 없음)

```json
// hardware.json
{ "SimulationMode": true, ... }
```
```json
// agent.json
{ "SimulationMode": true, ... }
```

- Master: `FakePlcController` (값 즉시 안정화), `FakeSerialShutterController` (open/close 메모리)
- Agent: `FakeCameraCaptureService` (합성 JPEG — HSV 그라디언트 + 카메라 인덱스/타임스탬프 텍스트)
- `ConnectionMonitorService` 시작 안함

전체 자동화 러너: `docs/deployment/run-e2e-simulation.ps1`

### 3.3 하이브리드 — 실 웹캠 + Fake PLC/Shutter

```json
// hardware.json (Master 시뮬)
{ "SimulationMode": true, ... }
```
```json
// agent.json (Agent 는 실 카메라)
{ "SimulationMode": false, "CameraIndex": 0, ... }
```

데스크탑 웹캠 한 대 꽂아두고 PLC 없이 시각적으로 캡처 흐름 확인할 때 유용. CLI 로:
```powershell
.\HeatingCameraSystem.Agent.exe Agent_0 nats://127.0.0.1:4222 0 ImageStorage_0 false
```

## 4. agent.json 전체 레퍼런스 (Agent)

소스: `HeatingCameraSystem.Core/Config/AgentConfig.cs`

| 필드 | 타입 | 기본값 | 의미 |
|---|---|---|---|
| `AgentId` | string | `<MachineName>` (자동) | Master 토픽 라우팅 키. 권장 형식 `Agent_<숫자>` |
| `CameraIndex` | int | `0` | OpenCvSharp `VideoCapture(index)`. **NATS 토픽 키와 1:1 매칭됨** (§5) |
| `NatsUrl` | string | `nats://127.0.0.1:4222` | NATS 서버 주소 |
| `StoragePath` | string | `ImageStorage` | 캡처 저장 폴더. 상대경로 시 exe 폴더 기준 |
| `HeartbeatIntervalSeconds` | int | `5` | `agent.status.{AgentId}` 발행 주기 |
| `SimulationMode` | bool | `false` | true 시 `FakeCameraCaptureService` 사용 |

### 4.1 CLI 인수 우선순위

```
Agent.exe <AgentId> <NatsUrl> [<CameraIndex>] [<StoragePath>] [<SimulationMode>]
```

| 위치 | 오버라이드 대상 |
|---|---|
| `args[0]` | `AgentId` |
| `args[1]` | `NatsUrl` |
| `args[2]` | `CameraIndex` |
| `args[3]` | `StoragePath` |
| `args[4]` | `SimulationMode` |

CLI 인수가 있으면 `agent.json` 값을 덮어쓴다. 다중 인스턴스 운영 시 같은 폴더에서 인스턴스마다 다른 인수만 주면 됨.

## 5. 카메라 ↔ Agent ↔ NATS 매핑 규칙 (★ 중요)

Recipe 의 각 단계에는 `CameraIndex` 가 박혀 있다. `RecipeEngine` 은 이를 다음 토픽으로 변환해 발행한다:

```
master.cmd.capture.Agent_{step.CameraIndex}
```

즉 **`AgentId` 는 반드시 `Agent_<step.CameraIndex>` 형태**여야 캡처 명령을 받을 수 있다. 매핑 예:

| Recipe Step.CameraIndex | 발행 토픽 | 받아야 할 Agent 의 AgentId |
|---|---|---|
| `0` | `master.cmd.capture.Agent_0` | `Agent_0` |
| `1` | `master.cmd.capture.Agent_1` | `Agent_1` |
| `7` | `master.cmd.capture.Agent_7` | `Agent_7` |

Agent 측 `agent.json` 의 `CameraIndex` 는 **OpenCV 가 잡을 물리 카메라 인덱스**(보통 0/1)이며 `AgentId` 의 숫자와 다를 수 있다. 예: PC#3 에 USB 카메라 한 대만 꽂혀 있어도 (`CameraIndex=0`) AgentId 는 `Agent_3` 으로 둘 수 있다 — 이 경우 Recipe Step 에 `CameraIndex=3` 으로 작성해야 매칭됨.

**권장**: 헷갈리지 않게 두 값을 같은 숫자로 맞춰라.

## 6. 시리얼 셔터 바이트 프로토콜

현 구현은 **7바이트 raw binary** (`SerialShutterController.cs` 11~13 라인):

```csharp
_openBuffer  = { 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
_closeBuffer = { 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
```

- 두 명령 모두 길이 7, `_openBuffer[2] = 0x01`, `_closeBuffer[2] = 0x00` 만 차이
- `cameraIndex` 인수는 상위 식별자 전용 — 버퍼에 들어가지 않는다
- 셔터 하드웨어가 상태 조회 명령을 지원하지 않아 `GetShutterStateAsync` 는 마지막 명령 기준 소프트웨어 캐시(`_isOpen`) 반환

다른 셔터 모델을 쓸 때 변경 절차:
1. `HeatingCameraSystem.Protocols/SerialShutterController.cs` 의 `_openBuffer / _closeBuffer` 수정
2. 필요 시 응답 읽기 로직 추가 (`_port.Read(...)`)
3. `dotnet test --filter SerialShutter` 로 단위 테스트 통과 확인
4. 재배포

> 시뮬레이션 모드에서는 `FakeSerialShutterController` 가 호출만 기록하고 실제 바이트 전송은 없음.

## 7. LiteDB (data.db) 관리

### 7.1 위치 / 스키마

`%LOCALAPPDATA%\HeatingCameraSystem\data.db` — LiteDB 단일 파일.

저장되는 컬렉션 (Master 내 Repository 4종):

| 컬렉션 | 모델 | 용도 |
|---|---|---|
| Recipe | `Recipe` + `RecipeStep[]` | Recipe Editor 작업물 |
| CameraMapping | `CameraMappingConfig` | Dashboard 슬롯 ↔ 카메라 매핑 |
| CaptureHistory | `CaptureHistoryRecord` | 캡처 이력 (단계별 이미지 경로 + 온도/습도) |
| CameraSerialSettings | `CameraSerialSettings` | 카메라별 시리얼 포트 설정 |

### 7.2 백업

Master 종료 후 (LiteDB 파일 락 해제):
```powershell
Copy-Item "$env:LOCALAPPDATA\HeatingCameraSystem\data.db" "D:\backup\data_$(Get-Date -Format 'yyyyMMdd_HHmm').db"
```

운영 중 실시간 백업이 필요하면 LiteDB Studio 의 `BACKUP` 명령 사용. 또는 Recipe 단위 Export 는 Recipe Editor UI 에서 JSON 으로 가능 (매뉴얼 03 §2).

### 7.3 초기화

```powershell
# Master 종료 후
Remove-Item "$env:LOCALAPPDATA\HeatingCameraSystem\data.db"
```

다음 Master 기동 시 빈 DB 자동 생성. **이력·레시피·매핑 모두 사라지므로 백업 먼저.**

### 7.4 캡처 이미지 보존 정책

`BackgroundDataCleanupService` 가 30 일 이전 이미지를 자동 삭제 (`App.xaml.cs` 의 `retentionDays: 30`). 변경하려면 코드 수정 후 재배포. 삭제 대상 폴더는 `%LOCALAPPDATA%\HeatingCameraSystem\ImageStorage\` (Master 가 직접 관리하는 캡처 경로). Agent 가 저장한 이미지는 **Agent PC 의 `StoragePath` 폴더에 남음** — Master 가 직접 삭제하지 않는다.

## 8. 자주 묻는 설정 변경

| 하고 싶은 일 | 어디를 만지나 |
|---|---|
| NATS 서버 IP 변경 | `hardware.json` `Nats.Url` + 모든 Agent `agent.json` `NatsUrl` |
| PLC 레지스터 주소 변경 | `hardware.json` `Plc.Reg*` / `Coil*` |
| 카메라 한 대 추가 (PC 1대 추가) | 새 Agent PC 에 publish + `agent.json` 의 `AgentId=Agent_N`, `CameraIndex=N` |
| 카메라 한 대 추가 (기존 PC 의 USB 두 번째 카메라) | 같은 Agent.exe 두 번째 인스턴스, `CameraIndex=1`, 다른 `AgentId`, 다른 `StoragePath` |
| 캡처 보관기간 변경 | `App.xaml.cs` `retentionDays` (코드 수정 필요) |
| 안정화 허용 오차 변경 | `RecipeEngine._tempTolerance` (코드 수정 필요, 향후 설정화 예정) |
| 캡처 타임아웃 늘리기 | `RecipeEngine.cs` 의 `TimeSpan.FromSeconds(30)` — 현재 하드코딩 (`RecipeEngineSettings.CaptureResultTimeoutSeconds` 는 미사용 상태) |

다음: [03-usage.md](./03-usage.md) — Recipe 작성 / 실행 / 이력 조회 / 트러블슈팅.
