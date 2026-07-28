# Handoff — Camera Mapping 소비 + AgentUI 시리얼 자동페어링 + 배포 설정 (2026-07-28 세션3)

이전: `docs/handoff/session2-live-color-nuc-nats-handoff.md`, `.omo/session-handoff-ui-audit.md`(UI 감사).

## 이번 세션 한 일 (커밋됨)

빌드 **솔루션 12개 0/0**, 테스트 **177/177**, 실 하드웨어 검증 완료.

### 1. UI 구현 감사 (14화면 전수)
- Master 10 + AgentUI 4 화면 전부 **실제 구현 확인**(빈껍데기 0). 코드 감사 + 실행 스윕(sim 모드) 둘 다.
- 유일 갭이던 **Camera Mapping = Partial**을 아래 2로 수정 → Implemented.
- 감사 결과: `.omo/session-handoff-ui-audit.md` §7.

### 2. Camera Mapping 실동작화 (Master)
- **인벤토리**: `CameraMappingViewModel.InitializeData` — 하드코딩 4×16 제거 → `CameraDeviceRepo.GetAllAsync()`(PCId 그룹, id=`Alias`||`AgentId`).
- **소비**: `RecipeEngine` — `LoadPositionAgentMapAsync()`로 저장 맵핑 1회 로드, `RecipeStep.TargetPositionIndex`→슬롯 `P{NN}`→AgentId를 **1순위** 해석(기존 `CameraAlias`/`CameraIndex`는 폴백, 하위호환). `AppServices`가 `MappingRepo` 주입.
- SyncProgress 실제 할당비율로 교체(가짜 75 제거).

### 3. AgentUI 시리얼 COM 자동페어링
- **`CameraDescriptor`에 `DeviceName` 필드 추가**(Core, 옵션 5번째 positional — 하위호환).
- `SettingsViewModel.AutoDetectSerialCommand`: 기존 `CameraComPairingService.GetPairsAsync()`(Protocols, USB ContainerID 매칭) 호출 → 각 행 `DeviceName`(FriendlyName Contains)으로 매칭해 `SerialPortName` 자동 채움. `DeviceName` 비면 OpenCvIndex 폴백.
- `App.xaml.cs`: 페어링 서비스 배선(sim=`FakeCameraComPairingService`, real=`WmiCameraEnumerator`+`WmiUsbSerialEnumerator`).
- `MainWindow.xaml`: Settings 그리드에 **Device Name 컬럼** + **COM 자동 감지** 버튼.

### 4. 실 하드웨어 검증 (열화상 카메라 2대 연결 상태)
- 페어링 서비스 headless 실행 → `r200`→COM8(S/N 545308059), `r150`→COM7(S/N 545308020), 웹캠/프린터 필터링, **Paired**(S/N 실제 읽음).
- ContainerID 매칭 = **포트 독립**(카메라 MI_00 + 시리얼 MI_02 동일 장치인스턴스).
- **버그 발견+수정**: 최초 OpenCvIndex 조인은 실 HW에서 실패(카메라가 WMI idx 0/4, config는 DirectShow idx 1/2 — 두 인덱스 상이). → 장치명(FriendlyName) 매칭으로 교체. 임시테스트로 `r150→COM7`/`r200→COM8` 재검증 후 삭제.

### 5. 배포 설정
- **`publish.ps1`**(루트): `master_bin`(Master), `agent_bin`(AgentUI), `agent_console_bin`(Agent 콘솔) 3개 폴더로 `dotnet publish`. `-SelfContained`로 포터블. dev 빌드 미영향.
- **`docs/deployment/deployment-guide.md`**: 토폴로지 + PC별 배포 절차 + 설정파일 위치.
- `.gitignore`에 배포 폴더 3개 추가.

## 다음 세션 (열린 항목)

- **AgentManager 레인**(stale task S5/S7/S8): **다른 세션이 동시 편집 중**(`ManagerStateStore.cs` 등). 이 세션에선 안 건드림 — 계속 회피.
- **실 config에 DeviceName 채우기(선택)**: 현재 `agentui.json` DeviceName 빔. 실 배포 시 Agent_1=`CLTC_T_VGA_G2_S_r150`(→COM7), Agent_2=`CLTC_T_VGA_G2_S_r200`(→COM8) 입력하면 자동감지 바로 동작.
- **2점 NUC(gain)**: 실 블랙바디 필요.
- **PLC 실주소 치환**: ServoSpeed(D2560), 비상정지(M2000) 등 임의값 → 실 A&D 메모리맵.
- History `EmergencyStop` 죽은 버튼(상태문자열만) — 배선 또는 제거.
- 잔여 엣지: 옛 `CAM-NN` 저장 맵핑은 실 인벤토리와 불일치(운영자 재매핑); 빈 device repo → 빈 목록(정상).

## 환경/설정
- `hardware.json` SimulationMode=true(가짜 PLC/BB). `agentui.json` SimulationMode=false(실카메라 Agent_1/COM7·Agent_2/COM8, Tiff16).
- 실 카메라 연결됨: `CLTC_T_VGA_G2_S_r150`(COM7), `CLTC_T_VGA_G2_S_r200`(COM8).
- NATS 가동중. 참고 파이썬 `참고/`(커밋 제외).

## 빌드/배포
```powershell
dotnet build HeatingCameraSystem.slnx        # 12 proj 0/0
dotnet test HeatingCameraSystem.Tests/HeatingCameraSystem.Tests.csproj  # 177/177
.\publish.ps1 [-SelfContained]               # master_bin / agent_bin / agent_console_bin
```
