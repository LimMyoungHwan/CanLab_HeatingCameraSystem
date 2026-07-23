# Handoff — 흑체 직접제어 추상화 + 카메라 기능 구현 (2026-07-23)

> 다음 세션이 이 문서만 읽고 이어서 진행 가능하도록 정리. 이전 핸드오프: `docs/handoff/agent-ui-slave-gui-handoff.md`.

## 이번 세션 한 일

### 1. 단일 PC 통합 테스트 (검증만, 코드변경 없음)
실 카메라 2대(CLTC r200/r150) + NATS + Master를 한 PC에서 E2E 검증.
- 카메라 OpenCV 인덱스: **idx1 / idx2 = 열화상 Y16** (idx0 = 웹캠). 프로브로 확정.
- AgentUI 실카메라 라이브뷰 ✓, Master↔NATS↔AgentUI 하트비트 ✓, 캡처 왕복(`master.cmd.capture.*`→실 Y16→`agent.result.capture.*`) ✓.
- Master 전체 오퍼레이터 플로우: `hardware.json SimulationMode=true`(가짜 PLC + 실 NATS)로 레시피 START→가짜 PLC 즉시 통과→실 카메라 캡처→이력 저장. **NATS는 SimulationMode와 무관하게 항상 실서비스**가 핵심.
- 레시피 캡처는 `Agent_{CameraIndex}` 타겟(`RecipeEngine.ResolveAgentIdAsync`, alias 없으면 폴백). 그래서 AgentUI AgentId를 `Agent_1`/`Agent_2`로 맞춤.

### 2. 흑체 PLC 분리 → 직접제어 추상화 (사용자 지시: "추상화만 먼저")
흑체 온도가 PLC 경유(`Bb1Sv/Bb1Pv`)였던 걸 별도 컨트롤러로 분리. **서보 위치 이동은 PLC 유지.**
- 신규: `Core/Interfaces/IBlackBodyController.cs` (Set/GetCurrent/GetTarget, Count, Connect)
- 신규: `Protocols/Simulation/FakeBlackBodyController.cs` (타겟 스냅 — 실장비 스펙 확보 전 시뮬/실장비 공용 대체)
- 신규: `Protocols/PlcBlackBodyAdapter.cs` (하위호환 폴백 → 기존 테스트 무손상)
- `RecipeEngine`: ctor에 옵셔널 `IBlackBodyController?`(null이면 PlcBlackBodyAdapter 폴백) + BB 온도 set/wait을 그것으로 라우팅
- `AppServices`: `BlackBodyController` 프로퍼티 + 생성(현재 Fake, sim/실장비 공통) + Connect/Dispose
- `StatusMonitorViewModel`/`PlcControlSettingsViewModel`: 흑체 표시·설정을 `AppServices.BlackBodyController`로

### 3. 카메라 CL 기능 AgentUI 노출 (사용자 지시: "지금 프로토콜로 가능한 것부터 전부")
기존 `ClSerialCameraClient`(CL 프로토콜, 115200 8N1)를 AgentUI에 배선.
- `Core/Models/CameraDescriptor.cs`: 옵셔널 `SerialPortName` 추가
- `ICameraSerialClient`/`ClSerialCameraClient`/`FakeCameraSerialClient`: `SaveConfigAsync` 추가 (`ClOperateCtrlSubId.SaveConfig`)
- `CameraPanelViewModel`: 셔터 열기/닫기, RUN/STOP, 정보 읽기(S/N·FPA온도), 설정 저장 커맨드 + `HasSerialControl`(미연결 카메라 비활성)
- `App.xaml.cs`: `SerialPortName` 설정된 카메라마다 시리얼 클라이언트 생성/InitializeAsync/주입
- `MainWindow.xaml`: Live 패널에 카메라 제어 UI + Settings에 Serial COM 컬럼

### 4. 이미지 포맷 옵션
- 신규: `Core/Models/CaptureImageFormat.cs` (`Y16Raw` / `Tiff16`)
- `ThermalCaptureWriter`: `Tiff16`이면 `.y16`(항상, 소스오브트루스) + `.json` + `.tif`(16bit TIFF via OpenCvSharp `Mat.FromPixelData`). CaptureStore/AppServices? → 인덱스 미변경(basename 파생), `CaptureStore.DeleteFiles`에서 `.tif`도 정리
- `AgentUiConfig.CaptureImageFormat` + Settings 콤보

## 검증
- 빌드 **0 errors / 0 warnings** (11 projects)
- 기존 테스트 **101/101** (회귀 0 — PlcBlackBodyAdapter 폴백 덕에 흑체 PLC 테스트 유지)
- 카메라 시리얼 **실 하드웨어**: idx1 `S/N 545308020` / idx2 `S/N 545308059`, FPA `62.4/59.3℃` 라이브, 명령 실행 ✓ (COM7/COM8)
- Tiff16: 캡처 시 `.tif`(12212B/42506B) 생성 확인 ✓
- **테스트 코드는 작성 안 함**(사용자 지시)

## ⚠️ 다음 세션 열린 항목

### A. 카메라 S/N — 스트리밍 중 0 반환 (하드웨어 거동, 배선 결함 아님)
- **idle 첫 읽기는 정상**(545308020/545308059)이나 **RUN(스트리밍) 중엔 S/N 레지스터가 `000000000` 반환**. FPA온도는 모든 상태 라이브.
- CL 프로토콜 명세(기존 미제 Q1)가 있어야 정석 처리(예: STOP→S/N 읽기→RUN, 또는 다른 레지스터/타이밍).
- 진단 중 넣었던 `ClSerialCameraClient` `DiscardInBuffer` 하드닝은 원인이 아니라 **되돌림**(원상복구됨).

### B. 흑체 실 직접제어 I/O (방식 미정)
- 현재 추상화 + Fake만. 사용자가 흑체 온도컨트롤러 인터페이스(시리얼 Modbus RTU / ASCII / TCP + 장비모델/프로토콜) 확정 시 → 실 `IBlackBodyController` 구현 후 `AppServices` 한 줄 교체.

### C. 카메라 NUC / gain / 방사율
- `ClMainId.Nuc(0x10)`/`UserConfig(0x20)`는 enum만 있고 서브ID 미정의. CL 프로토콜 서브ID 명세 확보 후 구현.

## 현재 환경/설정 상태
- **AgentUI 실행 중**: 실카메라 Agent_1(idx1,COM7)/Agent_2(idx2,COM8), CaptureImageFormat=Tiff16. `%LOCALAPPDATA%\HeatingCameraSystem\AgentUI\agentui.json`.
- **NATS 가동 중**(사용자). Master는 종료됨.
- `hardware.json` `SimulationMode=true`(가짜 PLC — 통합테스트용). **실 PLC 연결 시 `false`로 원복 필요.**
- `data.db`에 `E2E_Test_FakePlc` 레시피(2스텝→Agent_1/Agent_2) 시드됨 — Master START 데모용, Recipe Editor에서 삭제 가능.
- 참고자료 untracked: `docs/EnetClient*`, `docs/PC통신라이브러리예제*`(XGCommLib 데모 — PLC 통신 예제), `.council/`.

## 변경 파일
- 신규(4): `Core/Interfaces/IBlackBodyController.cs`, `Core/Models/CaptureImageFormat.cs`, `Protocols/PlcBlackBodyAdapter.cs`, `Protocols/Simulation/FakeBlackBodyController.cs`
- 수정(15): App.xaml.cs, MainWindow.xaml, AgentUiConfig, CameraPanelViewModel, SettingsViewModel, ICameraSerialClient, CameraDescriptor, AppServices, RecipeEngine, PlcControlSettingsViewModel, StatusMonitorViewModel, ClSerialCameraClient(원상), CaptureStore, ThermalCaptureWriter, FakeCameraSerialClient

## 빌드/테스트
```powershell
dotnet build HeatingCameraSystem.slnx     # 0/0
dotnet test HeatingCameraSystem.slnx      # 101/101
```
