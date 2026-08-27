# 세션 상태 (2026-08-07)

## 완료 A: 감독형(device 관리) 모드 UI 정리
- 삭제: `DevicesViewModel.cs`, `Views/DevicesView.xaml`(+.cs), `AgentSettingsViewModel.DevicesVm`, MainWindow DataTemplate, AgentSettingsView 죽은 주석+DarkTabItem, loc 키 13개.
- 빌드 0/0, 잔여참조 0.

## 완료 B: 촬영 이력(capture history) 통합 — 3 Phase 전부 완료
설계=Oracle 검증한 **A(Push 통합)**: 모든 캡처가 CaptureResultMessage 발행 → Master 상시 recorder가 capture_history 기록. 레시피만 RecipeEngine이 직접 기록(동기 PLC 온습도). dedup=Id(CaptureId). 키=`Alias ?? AgentId`.

- **Phase 1** (카메라 필터): HistoryViewModel 하드코딩 Agent-PC/CAM-NN 그룹 제거 → 데이터기반(`RefreshCameraFilterOptions`, distinct CameraId) + exact-match. 더미 125/1248 제거.
- **Phase 2** (수동캡처→Master 저장):
  - `NatsMessages.cs`: `CaptureSource{Unknown,Recipe,Manual,AgentUi}` enum. CaptureResultMessage에 Alias/CameraIndex/Source/CaptureId, CaptureCommandMessage에 Source 추가.
  - `CaptureHistoryRecord`: AgentId/CameraAlias/int? CameraIndex/Source 추가.
  - `CameraNatsConnector.HandleCaptureAsync`: 새 필드 채워 발행(Source 추론).
  - `ManualControlViewModel.SendCapture`: CameraControlOps.Capture → PublishCaptureCommandAsync{Source=Manual} 재라우팅.
  - 신규 `Master/Services/CaptureResultHistoryRecorder.cs`: agent.result.capture.> 상시 구독, 비레시피만 INSERT(SemaphoreSlim 직렬화). `AppServices`에서 생성+구독+dispose.
  - 신규 `Master/Services/CaptureResultImageCache.cs`: JPEG→.jpg 고정(기존 .y16 확장자 버그 수정). RecipeEngine도 사용.
  - RecipeEngine: Source=Recipe, 새 메타데이터+키, TryStoreImageLocally 제거.
- **Phase 3** (AgentUI 직접캡처):
  - `CameraPanelViewModel`: optional `publishResult` delegate. CaptureSaveAsync가 로컬저장 후 CaptureResultMessage{Source=AgentUi} push(발행실패 삼킴). `App.xaml.cs`가 lazy `_nats` publisher 주입.
  - History: 촬영 구분 필터(전체/레시피/수동/AgentUI) — HistoryViewModel SourceOptions/SelectedSourceFilter + HistoryView.xaml ComboBox.

검증: 빌드 0/0, **테스트 270 통과**(신규: CameraNatsConnector 필드 assert, CaptureResultHistoryRecorderTests 6개 incl. 실 LiteDB round-trip).

## 라이브 검증 상태
- **실 NATS 왕복 = 검증됨(2026-08-07 스모크)**: 포터블 nats-server v2.14.4로 임시 xUnit 스모크 실행 → 실 `NatsCommunicationService` publish→subscribe(`agent.result.capture.>`) → 실 `CaptureResultHistoryRecorder` → 실 LiteDB. Manual/AgentUi 기록(키=Alias??AgentId 확인), Recipe/실패 스킵 확인. 통과 후 임시 테스트+nats 바이너리 삭제(워킹트리 무흔적).
- **미검증**: 실제 WPF UI 클릭 E2E(수동캡처 버튼 → History 화면 표시)는 여전히 미실행 — 실 카메라/멀티프로세스 필요. 스모크가 전송·기록 경로는 커버하므로 잔여 리스크는 UI 바인딩뿐.

## 워킹트리 (미커밋)
A정리 + B(Phase1-3) + 기존 미커밋(대시보드 null-guard, 매뉴얼 docx). **커밋 안 함**(대기).

## AgentManager 폐기 (사용자 결정 대기)
Master↔AgentManager 결합 끊김. 참조=ManagerE2EDriver+Tests만. AgentUI가 카메라 단독관리. 실질손실=원격 로그덤프(단, pull식·AgentUI로그 아님·Master 미구독=죽은 배선). AgentUI 로그는 원래 로컬 전용(`AgentUiLog` ndjson). 대기: 원격로그 신규 구축할지?

## 영상 인덱스 포트독립 — 실측 + 수정 (2026-08-07)
**발견**: 영상 포트독립은 이미 구현·커밋됨(`5047872`): `DirectShowVideoDeviceEnumerator`(DShow순서==OpenCV DSHOW인덱스, DevicePath→ContainerId) + `VideoIndexReconciler`(UsbContainerId로 OpenCvIndex 재바인딩) + App.xaml.cs 시작/핫플러그 양쪽 reconcile. `ResolveConfidentPair`=S/N우선→ContainerId폴백.

**실측(실 r200/r150 카메라, 리플러그 포함)**:
- ✅ A1: DShow순서==OpenCV인덱스. A2: 영상ContainerId==WMI/시리얼 UsbParentId(문자열 완전일치) → 재바인딩 작동.
- ✅ ContainerID **포트 종속 확정**(리플러그 시 r150 `91852E04`→`91853045` 변동) → 동일세션 폴백으로만 정당.
- ✅ S/N **포트 무관**(r150=545308020, r200=545308059, 포트 바꿔도 유지) = 진짜 포트독립 키.
- 🔑 **근본원인**: CL 디텍터가 **STOP 상태로 부팅** → START 전까지 S/N=0(+블랙프레임) 반환. 페어링이 카메라 START 전에 S/N 읽어서 0 → 재바인딩이 S/N 못 씀 → 크로스포트 실패. `OPERATE_CTRL/CAMERA/START`(0x30/0x00/write/0x01) 보내면 복구(물리 리플러그 불필요, 평범한 재판독만으론 복구 안 됨).

**수정**: `CameraComPairingService.ReadSerialNumberAsync` — S/N이 all-zero면 `SetCameraRunningAsync(true)` 후 10×250ms 재판독. 페어링이 S/N 읽기 전 디텍터 기동 → S/N 학습 가능 → 크로스포트 포트독립 성립(+블랙프레임 부팅 완화). 회귀테스트 `CameraComPairingStartRecoveryTests`(STOP-부팅 fake) 추가. 빌드 0/0, 테스트 271 통과. 임시 프로브(tmp/portprobe) 삭제.

**미검증**: 실제 AgentUI 실행 후 물리 포트이동 최종 수용테스트(WPF 필요). 단위+실측+코드흐름으로 강검증됨.

## Master WPF 다중언어(i18n) 전환 (2026-08-07)
영문모드에서 한글 노출 버그 → 하드코딩 한글을 기존 loc 시스템(`{loc:Loc}` XAML / `LocalizationManager.Instance[]` C#)으로 전환.
- **4개 뷰**(StatusMonitor 46, History, PlcControlSettings, Dashboard) + **8개 VM** 전부 UI 문자열 loc화. 주석/Debug로그/단위(℃등)/enum명 제외.
- **HistoryViewModel 필터**: 표시+switch키 이중용도 → 인덱스 기반 매칭으로 디커플링(VM은 nav마다 재생성돼 생성시 언어 캡처).
- **레시피 기본명("새 레시피"/"(복사)")은 로컬라이즈 안 함** — 영속 데이터+테스트 고정.
- **PLC 비트명**(PlcDeviceCatalog=Core, loc 의존 불가): 표시계층(StatusMonitorVM/DashboardVM/PlcStatusService)에서 `LocalizationManager.GetOrDefault("PlcErr_"+i, catalogName)`로 로컬라이즈 — 한글 항목만 키(PlcErr_/PlcIn_/PlcOut_) 추가, 영어 항목은 카탈로그 폴백. StatusMonitorVM은 캐시VM이라 언어변경 시 비트명 리빌드 구독 추가.
- **라이브러리 예외("연결된 구성원으로부터 응답…")**: VagabondK 내부 한글이라 접두사("읽기 실패:")만 로컬라이즈, 예외본문은 잔존(알려진 한계).
- **언어 취약 테스트 7개**(하드코딩 한글 assert) → `LocalizationManager.Instance[key]`/예외메시지 기준으로 언어 무관 갱신.
- 검증: ko/en 키셋 완전일치(각 353, UTF8), 빌드 0/0, **테스트 271 통과**.
- 위임한 visual-engineering 에이전트(불안정 gemini)가 grep이슈로 스톨 → 직접 처리.
