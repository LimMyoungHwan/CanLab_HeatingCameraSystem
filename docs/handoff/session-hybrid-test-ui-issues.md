# Handoff — 하이브리드 테스트(실카메라+시뮬PLC) + 다음세션 UI 이슈 4건

## 이번 세션 완료 (커밋됨)
- `bee5b49` 표준 Live/CameraMapping 뷰 삭제 정리
- `d5f514d` 데드 MappingRepo 체인 제거 + stale 문서
- `e04969f` 시뮬레이터 플랜 F1–F4 종결 (F4 Oracle APPROVE + 외부 E2E PASS)
- `40407ca` ramp F3: Serial Settings + Devices → Agent 설정 탭 통합 (nav 9→7)
- **시뮬레이터 `--plc-only` 모드** (이 세션 코드): 가짜 NATS 카메라 skip, FEnet PLC만. 실카메라와 토픽 충돌 방지.

## 하이브리드 테스트 결과 = PASS
PLC/블랙바디=시뮬레이터, 영상=실 CLTC 열화상 카메라. Master Dashboard에서 실영상 렌더링 확인.

### 재현 방법
```
# 1) NATS 가동 (127.0.0.1:4222)
# 2) 시뮬레이터 PLC 전용
HeatingCameraSystem.Simulator\bin\Debug\net8.0\HeatingCameraSystem.Simulator.exe --plc-only
#    -> "SIMULATOR READY plc=127.0.0.1:2004 cameras=0"
# 3) AgentUI (실카메라, %LOCALAPPDATA%\HeatingCameraSystem\AgentUI\agentui.json = SimMode:false,
#    Agent_1 idx1/COM7, Agent_2 idx2/COM8)
HeatingCameraSystem.AgentUI\bin\Debug\net8.0-windows\HeatingCameraSystem.AgentUI.exe
# 4) Master 하이브리드: hardware.json SimMode=false, Plc.IpAddress=127.0.0.1, Port=2004
#    (BlackBody.Enabled 없음 -> PlcBlackBodyAdapter -> 블랙바디도 시뮬 FEnet 경유)
#    테스트 후 hardware.json 원복 필수 (원본 SimMode=true, Plc.Ip=192.168.1.2)
```

## 다음 세션 수정 이슈 (사용자 지적)

### 1. Recipe Editor 카메라 맵핑 화면에 카메라가 안 나옴
- 원인: `RecipeEditorViewModel.cs:282-284` 가 `AppServices.CameraDeviceRepo.GetAllAsync()`(LiteDB `camera_device`)에서 인벤토리를 읽음.
- `CameraDeviceRepo`는 **디바이스 관리(AgentManager) 승인 흐름**으로만 채워짐 (`DevicesViewModel.RenameAsync`:148 upsert). 실 Agent_1/Agent_2는 NATS 하트비트로만 존재하고 CameraDeviceRepo에 자동 등록 안 됨 → 맵핑 목록 빈 상태.
- 수정 방향: 맵핑 인벤토리 소스를 온라인 Agent(하트비트/agentui config)로 바꾸거나, 하트비트 수신 시 CameraDeviceRepo 자동 등록.

### 2. 디바이스 관리 / 시리얼 설정 vs 에이전트 원격설정 = 중복 (사용자 정확히 지적)
- **에이전트 원격설정**(`AgentSettingsViewModel`): 특정 Agent의 `agentui.json`을 NATS로 직접 조회/편집/전송 (SimMode/NATS/Storage/Heartbeat/Format/Burst + 카메라 목록 OpenCvIndex/Alias/DeviceName/**Serial COM**). 점대점.
- **디바이스 관리**(`DevicesViewModel`): AgentManager 레인 — 카메라 인벤토리 브로드캐스트 구독 + 승인/거부/이름변경/로그/시리얼설정. AgentManager 서비스(자동 발견) 전용. `CameraDeviceRepo` 채움.
- **시리얼 설정**(`SettingsViewModel`): 카메라별 시리얼(`CameraSerialSettings`)을 별도 NATS 프로토콜(`SerialConfigMessage`)로 push. 레거시.
- **결론: 3개가 시리얼/카메라 설정을 서로 다른 경로로 중복.** 에이전트 원격설정이 이미 카메라+시리얼 다 포함 → 시리얼 설정은 사실상 중복(레거시), 디바이스 관리는 AgentManager 자동발견용. **정리 필요**: 에이전트 원격설정을 주 경로로, 시리얼 설정 폐기 검토, 디바이스 관리는 AgentManager 사용 시에만.

### 3. 하얀/흐린 글씨 가독성 (Image 1 에이전트설정, Image 2 수동조작)
- 원인: 라벨 `#859493` (저대비), 비활성(disabled) 컨트롤·DataGrid 헤더가 어두운 배경에서 너무 흐림.
- 파일: `AgentSettingsView.xaml`, `ManualControlView.xaml`, `DevicesView.xaml`, `SettingsView.xaml`. 라벨/헤더/비활성 텍스트 대비 상향.

### 4. 수동조작 라이브 프리뷰가 열화상 아닌 노트북(내장) 카메라 연결 (핵심 버그)
- 페어링 드롭다운은 CLTC_T_VGA_G2_S_r200/r150 (DetectedButUnverified, COM8/COM7) 정상 나열.
- 그러나 라이브 프리뷰는 `CltcLiveThermalCamera.StartAsync(cameraIndex)` → `new VideoCapture(cameraIndex, DSHOW)`. 넘기는 `cameraIndex`가 CLTC 물리장치 OpenCV 인덱스로 매핑 안 됨 → 기본 idx0(노트북캠) 열림.
- `CameraComPairingService.GetPairsAsync`는 카메라+COM은 USB parent id로 페어링하지만 **OpenCV VideoCapture 인덱스는 산출 안 함**. 페어링 선택 → 올바른 OpenCvIndex 해석 로직 필요.
- 참고: AgentUI는 agentui.json에 OpenCvIndex 1/2를 명시해서 정상 동작. Master 수동조작 프리뷰는 다른 경로(ILiveThermalCamera+페어링)라 인덱스 해석이 빠져 있음.
- 파일: `ManualControlViewModel.cs`(SubscribeCameraServices/프리뷰), `CameraComPairingService.cs`, `CltcLiveThermalCamera.cs`.
