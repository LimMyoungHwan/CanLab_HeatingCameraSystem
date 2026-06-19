# 세션 핸드오프 (2026-06-19)

## 현재 상태

- **빌드**: ✅ 0 errors / 0 warnings
- **테스트**: ✅ 27/27 통과
- **마지막 커밋**: `e8007e4`
- **GitHub**: https://github.com/LimMyoungHwan/CanLab_HeatingCameraSystem

## 완료된 작업 (이번 세션)

### 1. SerialShutterController 실제 바이트 프로토콜 적용 (`f148a68`)
- ASCII 명령 → 7바이트 binary 버퍼 교체 (_openBuffer / _closeBuffer)
- GetShutterStateAsync: 소프트웨어 상태 캐시(_isOpen) 반환
- NewLine / ReadTimeout 제거

### 2. camera-serial-config PDCA 풀사이클 (`93eb588` `6d729e0`)
- CameraSerialSettings 모델 + ICameraSerialSettingsRepository
- SerialConfigMessage / SerialConfigAckMessage NATS 메시지
- INatsCommunicationService serial config 발행/구독 4 메서드
- LiteDbCameraSerialSettingsRepository (data.db)
- SettingsViewModel: 저장 & 전송 + ACK 5초 타임아웃
- SettingsView: 카메라 목록 + 시리얼 설정 폼 (다크테마)
- Agent Program.cs: serial config 구독 → 재연결 → ACK
- G-02 수정: ACK 구독 누적 제거 (HashSet + ConcurrentDictionary)
- Match Rate 100%, PDCA 문서 전부 작성

### 3. agent-status-display PDCA 풀사이클 (`c44c574`)
- CameraStatus enum (Offline / Connected / Streaming)
- AgentStatusMessage.IsCameraReady → CameraStatus 교체
- AgentNode: IsOnline + LastHeartbeat (하이브리드 동적 추가)
- CameraNode: CameraStatus 3단계
- DashboardViewModel: NATS 구독 + 15초 오프라인 타이머
- DashboardView: Agent 점(녹/회) + Camera 점(cyan/green/gray)
- 더미 AgentNode 초기화 코드 제거
- Match Rate 100%, PDCA 문서 전부 작성

### 4. recipe-progress-display (`50c014b`)
- RecipeProgress 모델 (CurrentStep / TotalSteps / CurrentPhase)
- RecipeEngine: IProgress<RecipeProgress> 파라미터 + 5개 phase Report
- DashboardViewModel: RecipeProgressValue + RecipePhaseText
- DashboardView: ProgressBar + 단계명 TextBlock

### 5. recipe-backup-restore (`77a5f2f`)
- RecipeEditorViewModel: ExportRecipe (SaveFileDialog → JSON)
- RecipeEditorViewModel: ImportRecipe (OpenFileDialog → 새 ID → DB 저장)
- RecipeEditorView: IMPORT/EXPORT 버튼 추가

### 6. 배포 가이드 (`e8007e4`)
- README.md: 시스템 개요, 아키텍처, 빌드/실행, NATS 토픽
- docs/deployment/master-setup.md
- docs/deployment/agent-setup.md
- docs/deployment/docker-compose.yml

### 7. AGENTS.md 업데이트
- 시리얼 셔터 바이트 프로토콜 반영
- 카메라 USB-C 구조 (USB 카메라 + 가상 시리얼) 반영

## 기술 부채 (건드리지 말 것 — 명시적 요청 시에만)

- App.xaml.cs OnExit: `.GetAwaiter().GetResult()` — 종료 블로킹 가능성
- NatsCommunicationService 구독 Task.Run 루프: 오류 복구 없음
- Streaming CameraStatus: 현재 Connected만 사용, RecipeEngine 실행 중 Streaming 전환 미구현

## 프로젝트 구조 요약

```
HeatingCameraSystem/
├── Core/                       # 인터페이스 + 모델 + 설정 (.NET 8)
│   ├── Config/                 # HardwareSettings, AgentConfig, SerialSettings
│   ├── Interfaces/             # IPlcController, INatsCommunicationService, IRecipeRepository,
│   │                           # ICameraMappingRepository, ICaptureHistoryRepository,
│   │                           # ICameraCaptureService, ISerialShutterController,
│   │                           # ICameraSerialSettingsRepository
│   └── Models/                 # Recipe, RecipeStep, RecipeProgress, NatsMessages (CameraStatus enum,
│                               # SerialConfigMessage, SerialConfigAckMessage, AgentStatusMessage,
│                               # CaptureCommandMessage, CaptureResultMessage),
│                               # CameraMappingConfig, CaptureHistoryRecord, CameraSerialSettings
├── Protocols/                  # FluentModbus, NATS.Net, System.IO.Ports
│   ├── NatsCommunicationService.cs  # serial config 4 메서드 포함
│   ├── PlcModbusClient.cs
│   └── SerialShutterController.cs   # 7바이트 binary 프로토콜
├── Master/                     # WPF 마스터 앱 (.NET 8 Windows)
│   ├── Services/               # AppServices, RecipeEngine (IProgress), LiteDb*Repository,
│   │                           # BackgroundDataCleanupService, ConnectionMonitorService,
│   │                           # LiteDbCameraSerialSettingsRepository
│   ├── ViewModels/             # DashboardVM (NATS 구독, 오프라인 타이머), RecipeEditorVM (Export/Import),
│   │                           # CameraMappingVM, HistoryVM, MainVM, SettingsVM (serial config)
│   └── Views/                  # Dashboard, RecipeEditor, CameraMapping, History, Settings
├── Agent/                      # 콘솔 앱 - 카메라 PC용 (.NET 8)
│   └── Program.cs              # NATS 구독 + 캡처 + 하트비트 + serial config 수신/재연결/ACK
├── Tests/                      # xUnit + Moq (27개)
└── docs/
    ├── 01-plan/features/       # camera-serial-config, agent-status-display, recipe-progress, recipe-backup
    ├── 02-design/features/     # camera-serial-config, agent-status-display, recipe-progress
    ├── 03-analysis/features/   # camera-serial-config, agent-status-display
    ├── 04-report/features/     # camera-serial-config, agent-status-display
    ├── deployment/             # master-setup, agent-setup, docker-compose.yml
    └── samples/                # hardware.json, agent.json
```
