# Handoff — 슬레이브(카메라) PC 독립 GUI (AgentUI)

> 작성: 2026-07-23. 다음 세션이 이 문서만 읽고 이어서 진행할 수 있도록 정리.

## 목표

슬레이브(카메라) PC에서 도는 독립 GUI. 요구:
1. 카메라 전 기능(열기/설정/캡처) · 2. 실시간 열화상 라이브뷰 · 3. 데이터 조회 + 특정 폴더 저장 + 오래된 데이터 조회/삭제 · 4. 카메라 에러 로그 조회 · 5. 카메라 설정 · 6. **Master 없이 standalone 동작** · **카메라 PC당 여러 대**.
Master = 슈퍼셋(슬레이브 전 기능 + 챔버 + 흑체 + 장비 에러/로그 + 레시피 + PLC 제어).

## 검토·확정 (council codex/agy 만장일치 AGREE + Oracle High-confidence)

- **Model Y**: 카메라 PC당 AgentUI 1 프로세스가 전 로컬 카메라 소유(카메라별 `CameraRuntime` N개, 인덱스 상이 → 핸들 충돌 없음).
- **capture-by-tee**: NATS 캡처 = 라이브 루프에서 최신 프레임 tee(2차 오픈·일시정지 없음). UVC는 프로세스당 1핸들 → 라이브+캡처가 반드시 한 프로세스.
- **Manager 유지·재정의**(삭제 아님).
- **Session 0**: GUI는 **로그온 예약작업(Scheduled Task)** 으로 autostart (CreateProcessAsUser 아님). 무인 운영은 **자동 로그인** 필요.
- **카메라별 논리 AgentId 유지**(`BuildAgentId(pcId,hwid)` 해시) → 기존 Master NATS 계약 불변.
- **저장**: `.y16`(LE 14bit) + `.json` 사이드카 + LiteDB 인덱스. PNG 지속저장은 지연(`.y16`에서 온디맨드 재구성).
- **사용자 확정**: 자동로그인/운영자 로그인 OK → Model Y 유지. (무인-무로그인 즉시 캡처는 **불요**.)

## 완료 (커밋됨)

| 단계 | 내용 | 커밋 |
|---|---|---|
| S1 | AgentUI WPF 프로젝트(net8.0-windows, CommunityToolkit.Mvvm) + .slnx | f1e630e |
| S2 | `CameraRuntime`(카메라당 VideoCapture 1 + Y16 루프 1, 원자적 최신프레임, capture-by-tee). `IThermalFrameSource` 주입(real `CltcThermalFrameSource` / fake `FakeThermalFrameSource`) | f1e630e |
| S3 | `CameraRuntimeManager`(멀티카메라 소유 + 카메라별 예외격리) + 오프라인 라이브뷰 + 단일인스턴스 mutex + 패널 단위 재시작 | f1e630e |
| S4-데이터 | `ThermalCaptureWriter`(.y16+.json) + `LiteDbCaptureIndex` + `CaptureStore`(저장/조회/retention) + `ThermalFrameReader` | f1e630e |
| S5-로컬 | E2E 파이프라인 테스트(라이브+tee+저장+재구성+radiometric 무손실) | f1e630e |
| S6 | `CameraNatsConnector`(카메라별 구독→tee→로컬저장→JPG 결과+하트비트, optional 백그라운드 재시도) + `ThermalPreviewEncoder`(Y16→JPG/PNG) + App 배선 | c323c77 |
| S4-UI | MainWindow TabControl(Live/Data/Logs/Settings): 데이터 브라우저(조회/삭제/retention/프리뷰) + 로그 뷰어(Serilog NDJSON→`NdjsonLogReader`→레벨필터) + 설정 탭(agentui.json 편집/저장) + `AgentUiLog`(App레벨 라이프사이클/Faulted 로깅) | 3993175 |

**검증: 빌드 0 errors/0 warnings · 테스트 101/101(기존 97+신규 4) · 시뮬레이션 실행 4탭 시각 QA(로깅 파이프라인 라이브 증명) · 콘솔 Agent·Master 완전 무변경(회귀 0).**

## 남은 단계

- **S5-full**: E2E를 NATS + Manager 모니터링까지 확장. ⚠️ "라이브" 실카메라 검증 부분은 실장비 필요(로컬 fake+NATS docker까지는 무장비 가능).
- **S7 ⚠️ 기존 SC-12 코드 수정**:
  - `AgentSupervisor`: "카메라당 Agent.exe spawn" → "AgentUI 1개 실행 보장 + 카메라별 건강 보고". **public 메서드 시그니처 보존**(AgentManagerTests 유지).
  - `ManagerCommandHandler`: Reject/Disable/Restart를 **프로세스 kill → 카메라별 런타임 언로드**로 변경(카메라 1대 거부가 전체 다운 방지).
  - `ManagerStateStore`/`InventoryPublisher`/승인·인벤토리·로그덤프 토픽 = 거의 그대로.
  - **미정 설계 포인트**: Manager(서비스, Session 0) → AgentUI(GUI, 유저세션) 카메라별 언로드 **IPC 메커니즘**(named pipe / localhost / 파일). 진행 전 결정 필요.
  - `Manager.AgentUiExePath` 추가(기존 `AgentExePath` 대체/병행).
- **S8**: `ManagerE2EDriver`를 fake runtime로 갱신(CI에서 WPF 안 띄움) + AgentUI `--headless` 스위치 + 로그온 예약작업 배포 + 자동 로그인 문서화 + 콘솔 Agent 은퇴 또는 진단 폴백 유지.

## 하드닝 (council 지적 — 잔여 반영 필요)

- 단일 인스턴스 mutex(✅ S3 완료) · 카메라별 예외격리+재시작(✅ S3) · frame freshness/max-age(✅ CaptureSnapshotAsync) · 서비스 재실행 rate-limit(S7/S8) · Y16 self-describing(✅ .json 사이드카) · `--headless`(S8).

## 신규 파일 맵

- Core: `Interfaces/{ICameraRuntime,IThermalFrameSource,ICaptureIndex}.cs`, `Models/{CameraRuntimeStatus,CameraDescriptor,CaptureMetadata,CaptureFiles,CaptureRecord,LogEntry,LogEntryLevel}.cs`
- Protocols/Cameras: `CameraRuntime.cs`, `CameraRuntimeManager.cs`, `ThermalCaptureWriter.cs`, `ThermalFrameReader.cs`, `LiteDbCaptureIndex.cs`, `CaptureStore.cs`, `CameraNatsConnector.cs`, `ThermalPreviewEncoder.cs`, `NdjsonLogReader.cs`, `CL/CltcThermalFrameSource.cs`
- Protocols/Simulation: `FakeThermalFrameSource.cs`
- AgentUI: `App.xaml(.cs)`, `MainWindow.xaml(.cs)`, `Services/{AgentUiConfig,ThermalFrameBitmapSourceConverter,AgentUiLog}.cs`, `ViewModels/{MainViewModel,CameraPanelViewModel,DataBrowserViewModel,LogViewerViewModel,SettingsViewModel}.cs`
- Tests: `CameraRuntimeTests`, `CameraRuntimeManagerTests`, `ThermalCaptureWriterTests`, `CaptureStoreTests`, `CapturePipelineE2ETests`, `CameraNatsConnectorTests`, `NdjsonLogReaderTests`

## 빌드/테스트

```powershell
dotnet build HeatingCameraSystem.slnx
dotnet test HeatingCameraSystem.slnx --no-build   # 97 passing
dotnet run --project HeatingCameraSystem.AgentUI  # 기본 SimulationMode → 실카메라 없이 라이브뷰 (실데스크톱 필요)
```

AgentUI 설정: `%LOCALAPPDATA%\HeatingCameraSystem\AgentUI\agentui.json` (SimulationMode/Cameras/NatsUrl/StoragePath/HeartbeatSeconds). 캡처 저장: `<StoragePath 또는 ConfigDir\Captures>`.

## 참고

- council 리뷰 원본(미커밋 스크래치): `.council/` (proposal.md + out_codex.md + out_agy.md + out_claude.md).
- 다음 세션 시작점 추천(실장비 무관): **S8**(ManagerE2EDriver fake화 + `--headless` + 로그온 예약작업 배포 + 자동로그인 문서) — 순수 인프라, 설계 포크 없음. 또는 **S7**(Manager 재정의 — 단 Manager→AgentUI 카메라별 언로드 **IPC 방식 결정 필요** + 기존 SC-12 코드 수정).
