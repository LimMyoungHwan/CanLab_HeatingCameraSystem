# Session Handoff (2026-07-23) — 흑체 직접제어 추상화 + 카메라 CL 기능

Full doc: `docs/handoff/blackbody-camera-features-handoff.md` (read first).
Prev topic (슬레이브 GUI): `docs/handoff/agent-ui-slave-gui-handoff.md`.

## Done this session (UNCOMMITTED in working tree)
빌드 0/0, 테스트 101/101 (회귀 0), 카메라 시리얼 실HW QA + Tiff16 검증됨. 테스트 코드는 미작성(사용자 지시).

1. **단일 PC 통합테스트**(검증만): 실카메라 idx1/idx2 + NATS + Master. `hardware.json SimulationMode=true`(가짜 PLC + **실 NATS는 항상**)로 레시피 START→실카메라 캡처 E2E. 레시피는 `Agent_{CameraIndex}` 타겟 → AgentUI AgentId를 `Agent_1`/`Agent_2`로 맞춤.
2. **흑체 PLC→직접제어 추상화**(사용자: 추상화만): 신규 `IBlackBodyController`(Core) + `FakeBlackBodyController`(Protocols.Simulation) + `PlcBlackBodyAdapter`(폴백→테스트 무손상). RecipeEngine 옵셔널 param(null→어댑터), AppServices 프로퍼티+생성(Fake), StatusMonitor/PlcControlSettings VM 재배선. **서보 위치는 PLC 유지.**
3. **카메라 CL 기능 AgentUI**(사용자: 지금 가능한 것 전부): CameraDescriptor `SerialPortName`, ICameraSerialClient/Cl/Fake `SaveConfigAsync`, CameraPanelViewModel 시리얼 제어(셔터/RUN/STOP/정보읽기/설정저장 + S/N·FPA표시, HasSerialControl), App.xaml.cs 배선, MainWindow.xaml 제어 UI + Settings COM 컬럼.
4. **이미지 포맷**: `CaptureImageFormat`(Y16Raw/Tiff16) + ThermalCaptureWriter TIFF(`Mat.FromPixelData`) + CaptureStore/.tif 정리 + AgentUiConfig/Settings 콤보.

## 다음 세션 열린 항목 (스펙 대기)
- **A. 카메라 S/N 스트리밍중 0**: idle 첫 읽기는 실제 S/N, RUN(스트리밍)중엔 `000000000`(FPA는 항상 라이브). 카메라 펌웨어 거동 — CL 명세(Q1) 필요(STOP→읽기→RUN 등). 배선 결함 아님. `DiscardInBuffer` 시도는 원인 아니라 되돌림.
- **B. 흑체 실 직접제어 I/O 방식 미정**(시리얼 Modbus/ASCII/TCP?): 확정 시 실 구현 후 AppServices 한 줄 교체.
- **C. NUC/gain/방사율**: `ClMainId.Nuc/UserConfig` enum만 있고 서브ID 미정의 → CL 명세 필요.

## 환경/설정 상태
- AgentUI 실행중(실카메라 Agent_1/COM7·Agent_2/COM8, Tiff16). NATS 가동중. Master 종료.
- `hardware.json SimulationMode=true`(가짜PLC) — **실 PLC 연결시 false 원복**.
- data.db `E2E_Test_FakePlc` 레시피 시드됨(데모용).
- 참고자료 untracked: `docs/EnetClient*`, `docs/PC통신라이브러리예제*`(XGCommLib PLC 예제), `.council/`.

## 변경 파일
신규(4): `Core/Interfaces/IBlackBodyController.cs`, `Core/Models/CaptureImageFormat.cs`, `Protocols/PlcBlackBodyAdapter.cs`, `Protocols/Simulation/FakeBlackBodyController.cs`.
수정(15): AgentUI(App.xaml.cs, MainWindow.xaml, AgentUiConfig, CameraPanelViewModel, SettingsViewModel) · Core(ICameraSerialClient, CameraDescriptor) · Master(AppServices, RecipeEngine, PlcControlSettingsViewModel, StatusMonitorViewModel) · Protocols(ClSerialCameraClient[원상], CaptureStore, ThermalCaptureWriter, FakeCameraSerialClient).

## Env — WPF QA (this session)
Desktop interactive. windows-mcp `State-Tool use_vision=true`가 DPI-aware 실좌표 + 스크린샷 제공(PowerShell CopyFromScreen은 DPI 가상화로 잘림 주의). `Click-Tool loc=[physX,physY]`로 UIA 좌표 클릭. 카메라 시리얼 QA: COM7/COM8이 CLTC 시리얼 채널.
