# 세션 핸드오프 (2026-06-15)

## 현재 상태

- **빌드**: ✅ 0 errors / 0 warnings
- **테스트**: ✅ 22/22 통과
- **마지막 커밋**: `9d54ee1`

## 완료된 작업 (이번 세션)

### 1. UI Phase 5 완성 (커밋 `bed7421`)
- Dashboard 다크테마 개편 (Mode 1~5 멀티그리드 + 드래그앤드롭)
- RecipeEditorView / CameraMappingView / HistoryView 신규 추가

### 2. 백엔드 통합 (커밋 `ad70ce2`)
- LiteDB 영구 저장: IRecipeRepository / ICameraMappingRepository / ICaptureHistoryRepository
- AppServices 정적 서비스 로케이터
- RecipeEngine: ConcurrentDictionary + TCS 패턴으로 캡처 결과 await + DB 자동 저장
- Agent Program.cs: NATS 명령 구독 → 캡처 → 결과 전송 + 5초 하트비트
- DashboardViewModel: RecipeEngine 연결 + 2초 PLC 폴링
- BackgroundDataCleanupService: 30일 경과 데이터 자동 정리

### 3. 하드웨어 설정 외부화 (커밋 `6751b1b`, `3160851`)
- Core/Config/HardwareSettings.cs: PLC 주소맵 + NATS + 시리얼
- Core/Config/AgentConfig.cs: Agent ID + 카메라 인덱스 + NATS URL
- AppServices: hardware.json 자동 생성/로드
- PlcModbusClient: const 제거 → settings 주입
- Agent Program.cs: agent.json 로드
- docs/samples/hardware.json + agent.json (A&D PLC 상정 예시)
- 신규 테스트 8개 (커스텀 PLC 주소 검증)

### 4. 우선순위 1-4 처리 (커밋 `9d54ee1`)
- CS8600 경고 수정
- ISerialShutterController + SerialShutterController (시리얼 카메라 셔터)
- Dashboard 레시피 선택 ComboBox + 새로고침 버튼
- ConnectionMonitorService (30초 간격 PLC/Serial 자동 재연결)
- IPlcController.IsConnected 노출

## 다음 세션에서 진행할 작업 (우선순위)

### 🟢 5. Agent 연결 상태 실시간 표시
- DashboardViewModel: `SubscribeAgentStatusAsync` 구독
- AgentNode에 IsOnline + LastHeartbeat 추가
- 좌측 패널에 상태 점 (녹색=온라인, 회색=오프라인) 표시

### 🟢 6. Recipe 진행률 표시
- RecipeEngine.ExecuteRecipeAsync에 `IProgress<RecipeProgress>` 파라미터 추가
- RecipeProgress 모델: CurrentStep, TotalSteps, CurrentPhase("챔버 안정화"/"서보 이동"/...)
- DashboardViewModel: progress 핸들러로 ProgressBar 업데이트
- DashboardView.xaml: START 버튼 옆에 ProgressBar 추가

### 🟢 7. 배포 가이드
- README.md: 시스템 개요 + 아키텍처 다이어그램
- docs/deployment/master-setup.md: Master PC 설치 절차
- docs/deployment/agent-setup.md: Agent PC 설치 절차
- docs/deployment/docker-compose.yml: NATS 서버 1-line 실행
- docs/deployment/nats-server.conf (옵션)

### 🟢 8. Recipe 백업/복원 UI
- RecipeEditorView에 Export/Import 버튼
- Export: 선택한 레시피를 JSON 파일로 저장
- Import: JSON 파일에서 레시피 로드 → DB 저장
- 전체 백업: LiteDB 파일 복사 (data.db)

## 기술 부채 / 알려진 이슈

- App.xaml.cs `OnExit`의 `.GetAwaiter().GetResult()`가 종료 차단 가능 (요구 시 fire-and-forget으로 변경)
- SerialShutterController의 명령어 프로토콜 (OPEN/CLOSE/STATE)은 임의 - 실제 카메라 명세 받으면 교체
- PLC 주소 (hardware.json 기본값)는 placeholder - 실제 A&D PLC 명세 받으면 docs/samples/hardware.json 참조해서 운영자가 직접 수정

## 프로젝트 구조 요약

```
HeatingCameraSystem/
├── Core/                       # 인터페이스 + 모델 + 설정 (.NET 8)
│   ├── Config/                 # HardwareSettings, AgentConfig
│   ├── Interfaces/             # IPlcController, INatsCommunicationService, IRecipeRepository, ICameraMappingRepository, ICaptureHistoryRepository, ICameraCaptureService, ISerialShutterController
│   └── Models/                 # Recipe, RecipeStep, NatsMessages, CaptureHistoryRecord, CameraMappingConfig
├── Protocols/                  # 통신 구현체 (.NET 8)
│   ├── NatsCommunicationService.cs
│   ├── PlcModbusClient.cs
│   └── SerialShutterController.cs
├── Master/                     # WPF 마스터 앱 (.NET 8 Windows)
│   ├── Services/               # AppServices, RecipeEngine, LiteDb*Repository, BackgroundDataCleanupService, ConnectionMonitorService
│   ├── ViewModels/             # DashboardVM, RecipeEditorVM, CameraMappingVM, HistoryVM, MainVM
│   └── Views/                  # 각 View XAML + code-behind
├── Agent/                      # 콘솔 앱 - 카메라 PC용 (.NET 8)
│   └── Program.cs              # NATS 구독 + 캡처 + 하트비트
├── Tests/                      # xUnit + Moq
└── docs/samples/               # hardware.json / agent.json 샘플
```

## 빌드/실행 명령어

```powershell
# 빌드
rtk dotnet build

# 테스트
dotnet test --no-build

# Master 실행 (WPF)
dotnet run --project HeatingCameraSystem.Master

# Agent 실행 (콘솔)
dotnet run --project HeatingCameraSystem.Agent
# 또는 args 오버라이드:
dotnet run --project HeatingCameraSystem.Agent -- Agent_Bay1 nats://192.168.1.10:4222
```

## 설정 파일 위치

- Master: `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json`
- Master: `%LOCALAPPDATA%\HeatingCameraSystem\data.db` (LiteDB)
- Agent: `<exe폴더>\agent.json`
- 캡처 이미지: `<exe폴더>\ImageStorage\` (agent.json에서 변경 가능)
