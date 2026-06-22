# 03 — 사용 매뉴얼

> 일상 운영 — Master UI 둘러보기, Recipe 작성/실행, Agent 운영, 시뮬레이션, 이력 조회, 문제 해결.

설치는 [01-installation.md](./01-installation.md), 설정 필드는 [02-configuration.md](./02-configuration.md), 시스템 개요는 [00-overview.md](./00-overview.md).

## 1. Master UI 둘러보기

Master 실행 후 좌측 사이드바에 5개 탭:

| 탭 | View | 무엇을 보고/하는가 |
|---|---|---|
| **Dashboard** | `DashboardView.xaml` | 챔버 온/습도 실시간, 카메라 피드 그리드, Agent 패널, Recipe 시작/정지 |
| **Recipe Editor** | `RecipeEditorView.xaml` | Recipe CRUD, Step 추가/삭제, JSON Import/Export |
| **Camera Mapping** | `CameraMappingView.xaml` | Dashboard 슬롯 ↔ 카메라 ID 매핑 (Mode 2~5) |
| **History** | `HistoryView.xaml` | 캡처 이력 조회, 이미지 미리보기 |
| **Settings** | `SettingsView.xaml` | 카메라별 시리얼 포트 설정을 Agent 로 원격 전송 + ACK 대기 |

상단 좌상단에 PLC 온도/습도가 실시간 표시(2초 폴링). 우측에 Agent 트리 — 녹색 점은 5초 이내 하트비트, 회색은 15초 무응답. 카메라 점은 cyan(Streaming, 캡처 중) / green(Connected) / gray(Offline) 3단계.

## 2. Recipe 워크플로

### 2.1 새 Recipe 만들기

1. **Recipe Editor** 탭 → `NEW` 버튼
2. `Name` 입력 (예: `BB_5pt_50to70`)
3. `GlobalTargetTemperature` (°C), `GlobalTargetHumidity` (%RH) 입력 — 챔버 안정화 목표
4. **Add Step** 으로 단계 추가, 각 단계에 다음 입력:
   - `CameraIndex` — 캡처를 받을 Agent (매뉴얼 02 §5 매핑 규칙)
   - `TargetPositionIndex` — 서보 위치 인덱스
   - `TargetBlackBodyTemperature` — 해당 단계의 BB 목표온도
5. `SAVE` → LiteDB 에 저장

### 2.2 Export / Import (JSON 백업)

- **EXPORT** → `SaveFileDialog` → `.json` 으로 저장. Step 배열 포함 전체 직렬화.
- **IMPORT** → `OpenFileDialog` 로 JSON 선택 → 새 `Id` (Guid) 할당 후 DB 저장. 원본 ID 중복 충돌 회피.

다른 PC 로 Recipe 옮기거나 버전관리할 때 사용.

### 2.3 실행

1. **Dashboard** 탭 → `SelectedRecipe` 드롭다운에서 Recipe 선택 (없으면 `REFRESH`)
2. `START` → `RecipeEngine.ExecuteRecipeAsync` 가 백그라운드 실행
3. 진행 상태:
   - 진행률 바 (0~100%)
   - `RecipePhaseText` — `챔버 안정화` → `서보 이동 (i/total)` → `BB 안정화 (i/total)` → `캡처 (i/total)` → … → `완료`
   - `RecipeStatus` — `실행 중: <Name>` / `완료` / `중지됨` / `오류: <메시지>`
4. 중간 중단: `STOP` → `CancellationToken` 으로 안전 정지 (현재 단계 완료 후 chamber stop)

### 2.4 캡처 흐름 (단계 1회 = 5단계)

| Phase | 누가 | 무엇을 |
|---|---|---|
| Chamber stabilization | Master `RecipeEngine` | `StartChamberAsync` → `SetTargetTemperature/Humidity` → `GetCurrentTemperature` 폴링 (2초 간격) |
| Servo move | Master | `MoveServoToPositionAsync(positionIdx)` → `IsServoAtPositionAsync` 폴링 (500ms) |
| BB stabilization | Master | `SetBlackBodyTemperatureAsync(0, temp)` → `GetCurrentBlackBodyTemperatureAsync` 폴링 (1초) |
| Capture | Master ↔ Agent | NATS `master.cmd.capture.Agent_<idx>` 발행 → Agent 캡처 → `agent.result.capture.<id>` 응답 (30초 타임아웃) |
| History save | Master | PLC 온/습도 재읽기 + `CaptureHistoryRecord` 저장 |

모든 단계 후 `StopChamberAsync` → `완료`.

## 3. Agent 운영

### 3.1 단일 인스턴스 (운영 PC)

```powershell
C:\HeatingCameraSystem\Agent\HeatingCameraSystem.Agent.exe
```

콘솔 로그 예:
```
[Agent_3] Camera idx=0 ready (REAL)
[Agent_3] Connected to NATS (nats://10.0.0.5:4222)
[Agent_3] Agent running. Press Ctrl+C to stop.
[Agent_3] Step <stepId>: OK -> D:\CaptureImages\capture_20260620_153012_117.jpg
```

종료는 `Ctrl+C`.

### 3.2 다중 인스턴스 (같은 PC, 시뮬·테스트)

```powershell
.\Agent.exe Agent_0 nats://127.0.0.1:4222 0 ImageStorage_0 false  # 웹캠
.\Agent.exe Agent_1 nats://127.0.0.1:4222 1 ImageStorage_1 true   # 합성
.\Agent.exe Agent_2 nats://127.0.0.1:4222 2 ImageStorage_2 true   # 합성
```

CLI 인수를 모두 넘기는 다중 인스턴스 기동은 `agent.json` 을 사용하지 않으므로 동시 기동해도 안전 (v2.1+). 자동 러너: `docs/deployment/run-e2e-simulation.ps1`.

### 3.3 원격 시리얼 설정 전송

Master **Settings** 탭에서:
1. 카메라 목록에서 대상 선택
2. PortName / BaudRate / DataBits / Parity / StopBits 입력
3. `SAVE & SEND` → NATS `master.config.serial.<AgentId>` 발행
4. Agent 가 셔터 재연결 → `agent.config.serial.ack.<AgentId>` 로 결과 응답 (5초 타임아웃)
5. UI 에 OK / FAIL 표시

설정은 LiteDB `CameraSerialSettings` 컬렉션에도 저장됨. Agent 재시작 시 Master 가 동일 설정을 다시 push 하는 흐름은 없음 — 필요하면 운영자가 Settings 탭에서 다시 보냄.

## 4. 시뮬레이션으로 검증

### 4.1 자동 E2E 러너 (가장 빠름)

```powershell
./docs/deployment/run-e2e-simulation.ps1
```

내부 동작:
1. `Agent` + `E2EDriver` publish (`%TEMP%\HCS_E2E\`)
2. Agent_0 / Agent_1 백그라운드 (SimulationMode=true)
3. E2EDriver 실행 — 4단계 Recipe → FakePlc + 실제 NATS + 합성 JPEG
4. 결과 검증 (캡처 4건, JPEG 매직바이트 0xFFD8FF) → exit code 반환
5. Agent 종료

기대 출력 끝부분:
```
[E2E] Captures received: 4 / 4
[E2E] Agent_0 captures: 2, Agent_1 captures: 2
[E2E] Image files: 4 present, 0 missing
[E2E] *** PASS ***
```

전체 시뮬레이션 상세는 [../deployment/simulation-mode.md](../deployment/simulation-mode.md).

### 4.2 Master GUI 로 시뮬

1. `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` 에 `"SimulationMode": true` 추가 (또는 `docs/samples/hardware.simulation.json` 복사)
2. Master 재시작 → Debug Output 에서 `[AppServices] SimulationMode=true -> using Fake PLC + Fake Shutter`
3. Agent 인스턴스 2~3개 띄움 (위 3.2)
4. Recipe Editor 에서 단계 작성 (예: 4 step, CameraIndex 0/1/0/1)
5. Dashboard → Recipe 선택 → START
6. 진행률 100% → 완료 → History 탭에서 4건 확인

### 4.3 단위·통합 테스트

```powershell
dotnet test --no-build
# 통과 59 / 실패 0
```

추가 시뮬 테스트는 `HeatingCameraSystem.Tests/SimulationTests.cs` (11건).

## 5. History 조회

**History** 탭:
- 필터 — 기간 / Agent / Recipe
- 각 행 = 1 캡처 = `CaptureHistoryRecord` 1개
  - `CameraId` (예: `CAM-03`)
  - `ImagePath` (Agent PC 의 절대 경로)
  - `RecipeStepId`
  - `Timestamp` (UTC)
  - `Temperature` / `Humidity` (캡처 직후 PLC 값)
- 더블클릭 → 이미지 미리보기 (이미지 파일이 Master PC 에서도 접근 가능한 경로여야 함 — 보통 Agent PC 의 공유 폴더 또는 동일 PC 시뮬 환경)

> v2.2+ 부터 Agent 가 캡처 직후 JPEG 바이트를 `CaptureResultMessage.ImageBytes` 로 NATS 에 같이 실어 보내고, Master `RecipeEngine` 이 `%LOCALAPPDATA%\HeatingCameraSystem\ImageCache\` 에 자동 저장한다. `CaptureHistoryRecord.ImagePath` 는 Master 로컬 경로라 History 탭에서 바로 미리보기 가능.
>
> Agent 가 보낸 바이트가 비어있으면 (구버전 Agent 또는 캡처 파일 읽기 실패) 기존 `ImagePath` (Agent PC 로컬 경로) 가 그대로 기록됨 — 이 경우는 Master 에서 자동 미리보기 불가, SMB 공유 등 별도 수단 필요.

이미지가 30일 지나면 `BackgroundDataCleanupService` 가 자동 삭제 (`%LOCALAPPDATA%\HeatingCameraSystem\ImageCache\` 만 청소). Agent 측 `<StoragePath>` 폴더는 Agent 가 직접 관리 — 보관 정책 없음.

## 6. 시작 → 종료 시퀀스 (운영 기준)

```
1. NATS 서버 기동 (Docker 또는 nats-server.exe)
2. Master 실행 → 자동으로 NATS / PLC 연결 시도 (실패해도 GUI 는 뜸)
3. Agent N 대 기동 → 5초 내 Master Dashboard 에 녹색 점
4. (필요시) Settings 탭에서 시리얼 설정 전송
5. Recipe 선택 → START
6. 모니터링 — Dashboard 진행률, Agent 로그
7. 완료 후 History 탭에서 이력 확인
8. 종료 — Agent Ctrl+C, Master 창 닫기, NATS down (선택)
```

## 7. 트러블슈팅

### 7.1 Agent 가 Dashboard 에 안 보임

| 증상 | 점검 |
|---|---|
| 녹색 점 안 뜸 | Agent 콘솔에 `Connected to NATS` 로그 있나? 없으면 NATS URL / 방화벽 점검 |
| 5초 후 회색으로 변함 | Agent 프로세스 죽었거나 NATS 연결 끊김 |
| 카메라 idx unavailable 로그 | `agent.json` `CameraIndex` 가 실제 USB 카메라 인덱스와 다름. 장치관리자 확인 |

### 7.2 캡처 명령은 갔는데 결과 안 옴

| 증상 | 점검 |
|---|---|
| 30초 후 `capture timeout` | Recipe `Step.CameraIndex` 와 Agent `AgentId` 매칭 확인 (매뉴얼 02 §5). 예: Step `CameraIndex=1` → Agent `AgentId` 는 반드시 `Agent_1` |
| `capture failed` | Agent 콘솔 로그 확인. `frame.Empty()` 면 카메라가 프레임 못 뽑음 (드라이버 / USB 전원 / 다른 앱 점유) |
| Master 에서만 안 됨 | 다른 Agent 도 같은 증상인지. NATS 모니터링 (`http://<nats>:8222/connz`) 으로 subscriber 확인 |

### 7.3 PLC 연결 실패

| 증상 | 점검 |
|---|---|
| Dashboard 온/습도 `0` 고정 | Master Debug Output 에 `PLC connect failed` 로그 확인. `hardware.json` `Plc.IpAddress` / `Port` 정확한지, 핑·텔넷으로 502 도달 확인 |
| 일시적 끊김 | `ConnectionMonitorService` 가 30초마다 재연결 시도 — 잠시 기다림 |
| 값은 오는데 이상한 숫자 | `RegTempPv` 등 레지스터 주소 잘못. PLC 설계서 확인. 온도는 `/10` 스케일 |

### 7.4 시리얼 셔터 미응답

| 증상 | 점검 |
|---|---|
| `Local shutter reconnect failed` | `PortName` 이 실제 가상 COM 과 일치? 다른 프로그램이 포트 점유? |
| 명령은 가는데 실 셔터 안 움직임 | `SerialShutterController._openBuffer/_closeBuffer` 가 실제 모델 프로토콜과 일치? 매뉴얼 02 §6 |
| ACK 가 timeout (5초) | Agent 콘솔에 `Serial config FAIL` 로그 + 원인 메시지 확인 |

### 7.5 Recipe 진행 중 멈춤

| 멈춘 Phase | 원인 후보 |
|---|---|
| `챔버 안정화` | PLC 가 실제로 목표 온도에 도달 못함 (히터·습도제어기 오류). `_tempTolerance=0.5°C` 안에 못 들어오면 무한 대기 — `STOP` 으로 중단 후 PLC 확인 |
| `서보 이동` | `IsServoAtPositionAsync` 가 계속 `false`. PLC `CoilServoArrival` 주소 또는 서보 자체 문제 |
| `BB 안정화` | BB 히터 응답 안함. 또는 PV 레지스터 (`RegBb1TempPv`) 잘못 |
| `캡처` | §7.2 참조 |

### 7.6 빌드 / 테스트 실패

| 증상 | 점검 |
|---|---|
| `dotnet build` 실패 | .NET 8 SDK 설치 여부 (`dotnet --info`). 패키지 복원: `dotnet restore` |
| 테스트 일부 실패 | 환경 의존 테스트 (`CameraCaptureServiceTests`) 는 카메라 없으면 스킵될 수 있음. 사용 가능한 카메라 점유 해제 |
| WPF 빌드만 실패 | Windows 가 아닌 환경에서 빌드 중. `net8.0-windows` 타겟은 Windows + Windows Desktop Runtime 필요 |

### 7.7 LiteDB 락 / 손상

| 증상 | 조치 |
|---|---|
| `data.db` 열 수 없음 | Master 가 이미 실행 중인지 확인. 강제 종료 후 `*.db-log` 파일 있으면 자동 복구됨 |
| Recipe 갑자기 사라짐 | LiteDB Studio 로 직접 열어 확인. 백업본이 있으면 복구 |
| 초기화 필요 | 매뉴얼 02 §7.3 |

## 8. Agent Manager 운영 (카메라 승인)

> Agent Manager(`HCS-Manager` 서비스)를 설치([01-installation.md §8](./01-installation.md#8-agent-manager-설치-선택))한 경우의 일상 운영. Manager 가 USB 카메라를 자동 발견하면 Master **Devices** 탭에서 승인해야 Agent 가 기동된다. Manager 를 안 쓰고 Agent 를 직접 띄우는 운영(§3)은 이 섹션과 무관.

### 8.1 Devices 탭 둘러보기

Master 좌측 사이드바 **Devices**(디바이스 관리) 탭:

| 영역 | 내용 |
|---|---|
| 좌측 DataGrid | 발견된 카메라 목록. 컬럼: `Status`(IsRunning) / `PCId` / `Alias` / `AgentId` / `HardwareId` / `Approved` / `CV Idx` |
| 우측 패널 — Alias | 선택 카메라의 별칭 입력란 |
| 우측 패널 — 액션 버튼 | **승인** / **거부** / **이름 저장** / **로그 가져오기** |
| 우측 패널 — Alert | 해당 Agent 의 최근 ERROR/FATAL alert (있을 때만 빨간 박스) |
| 우측 패널 — Log Viewer | "로그 가져오기" 로 받은 NDJSON 로그 표시 |
| 하단 Status Bar | 마지막 작업 상태 메시지 |

카메라를 행으로 선택하면 우측 액션이 활성화된다.

### 8.2 카메라 승인 워크플로

```
1. Agent PC 에 USB 카메라 연결 → Manager 가 WMI 로 자동 발견
2. Master Devices 탭에 새 카메라가 Approved=False 로 표시
3. 카메라 행 선택 → (선택) Alias 입력 (예: "Bay1-Left")
4. "승인" 클릭
     → server.cmd.mgr.{PCId} (Approve) 발행
     → Manager 가 AgentId 부여 + Agent.exe spawn
     → 인벤토리 재발행 → 그리드에 Approved=True, AgentId 채워짐
5. Dashboard Agent 패널에 녹색 점 (5초 내) → 캡처 준비 완료
```

> "승인" 버튼은 **미승인(Approved=False) 카메라 선택 시에만** 활성화된다. 이미 승인된 카메라엔 비활성.

### 8.3 액션별 동작

| 버튼 | NATS Op | 효과 |
|---|---|---|
| **승인** | `Approve` | `IsApproved=true`, `IsDisabled=false`. AgentId 없으면 `{PCId}_{해시8}` 부여 후 Agent spawn. 영구 드롭된 카메라 재기동에도 사용 |
| **거부** | `Reject` | `IsApproved=false` + 해당 Agent 프로세스 kill |
| **이름 저장** | `Rename` | Alias 를 `manager-state.json` + Master LiteDB(`CameraDevice`) 양쪽에 저장. Recipe `CameraAlias` 매칭에 사용 |
| **로그 가져오기** | `LogDumpRequest` | Agent NDJSON 로그를 gzip 으로 요청(최대 5MB) → 30초 내 수신 시 Log Viewer 에 표시 |

> **Restart / Disable / SetSerial** Op 은 Manager 핸들러에 구현돼 있으나 현재 Devices 탭 UI 버튼으로는 노출되지 않는다. 필요 시 카메라 "거부" 후 "승인"(=재기동) 으로 대체하거나, 시리얼 설정은 **Settings** 탭(§3.3)의 기존 경로를 사용.

### 8.4 자동 재시작 / 영구 드롭

Agent 프로세스가 크래시하면 Manager 가 지수 백오프(`1→2→5→15→60`초)로 재시작한다. 5회 초과하면 영구 드롭(`IsDisabled=true`)되고 Devices 탭에 alert 가 뜬다. 원인 해결 후 해당 카메라를 다시 **승인**하면 재기동된다. 정책 상세는 [02-configuration.md §9.4](./02-configuration.md#94-재시작-정책-참고).

### 8.5 Manager 트러블슈팅

| 증상 | 점검 |
|---|---|
| Devices 탭에 카메라 안 뜸 | `HCS-Manager` 서비스 Running? (`Get-Service HCS-Manager`). NATS URL(`manager-settings.json`) 이 Master 와 같은 서버인지. `manager-state.json` 에 엔트리 생겼는지 |
| 승인했는데 Agent 안 뜸 | `AgentExePath`(매뉴얼 02 §9.1) 경로에 Agent.exe 있나? `<InstallRoot>\logs\{AgentId}\` 로그 확인. SimulationMode=true 면 실제 spawn 안함(매뉴얼 02 §9.3) |
| 카메라가 계속 사라졌다 나타남 | Agent 반복 크래시 → 백오프 재시작 중. 로그 가져오기로 원인 확인. 5회 초과 시 영구 드롭 |
| "로그 가져오기" 30초 초과 | 해당 Agent 가 죽었거나 NATS 연결 끊김. Dashboard 점 색 확인 |

### 8.6 승인 루프 E2E 검증 (SC-12)

하드웨어 없이 Manager 승인 흐름이 정상인지 확인하는 자동 러너. NATS 서버만 떠 있으면 된다.

```powershell
./docs/deployment/run-manager-e2e.ps1
```

내부 동작:
1. `ManagerE2EDriver` 가 AgentManager(SimulationMode)를 in-process 호스팅
2. `FakeCameraEnumerator` 카메라 2대 발견 → `agent-mgr.inventory.{PCId}` 발행
3. 드라이버가 `server.cmd.mgr.{PCId}` 로 Approve 2건 발행
4. Manager 가 AgentId(`{PCId}_{해시8}`) 부여 + 승인 인벤토리 재발행
5. 검증: 2대 모두 IsApproved=true, AgentId 부여, `manager-state.json` 영속

종료 코드: `0` PASS / `1` FAIL / `2` NATS 연결 실패 / `3` timeout.

> 이 러너는 **승인 흐름**만 검증한다 (SimulationMode 라 실제 Agent.exe spawn·캡처는 없음 — 매뉴얼 02 §9.3). 캡처 roundtrip 검증은 [§4.1 자동 E2E 러너](#41-자동-e2e-러너-가장-빠름) 사용.

## 9. 관련 문서

| 주제 | 위치 |
|---|---|
| 시스템 개요 / 토픽 맵 | [00-overview.md](./00-overview.md) |
| 설치 절차 | [01-installation.md](./01-installation.md) |
| 설정 파일 필드 | [02-configuration.md](./02-configuration.md) |
| 시뮬레이션 전체 가이드 | [../deployment/simulation-mode.md](../deployment/simulation-mode.md) |
| 캡처 roundtrip E2E 러너 | [../deployment/run-e2e-simulation.ps1](../deployment/run-e2e-simulation.ps1) |
| Manager 승인 E2E 러너 (SC-12) | [../deployment/run-manager-e2e.ps1](../deployment/run-manager-e2e.ps1) |
| 샘플 설정 파일 | [../samples/](../samples/) |
