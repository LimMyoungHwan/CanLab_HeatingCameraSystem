# 세션5 핸드오프 — AgentUI 시작 자동 페어링 + USB 핫플러그 자동 갱신

## 다음 세션 할 일 (승인된 범위)

USB-C 열화상 카메라의 **가상 시리얼 포트를 자동으로 찾아 매칭**하고, **USB 연결/해제 시 자동 갱신**하는 로직을 AgentUI에 넣는다. 목적: Manager 감독형(디바이스 관리 승인) 경로 없이 AgentUI 단독으로 시리얼 자동화.

## ⚠️ 핵심 기술 제약 (이번 세션 실측으로 증명 — 설계 근거)

- **시리얼 COM = 자동 매칭 가능(견고)**: USB **ContainerID**로 카메라(UVC)↔가상시리얼(CDC)을 매칭. 한 물리 USB-C 장치의 모든 기능이 같은 ContainerID 공유. `UsbTopology.DeriveContainerId`가 레지스트리 `Enum\{pnp}\ContainerID`를 읽어 제공(폴백은 PNPDeviceID 정규화).
- **영상 OpenCV 인덱스 = 자동 확정 불가**: OpenCV `VideoCapture`는 장치 ContainerID를 노출 안 함. **WMI 열거 순서 ≠ OpenCV VideoCapture 순서**(프로브로 증명: WMI는 스캐너·USB 복합 인터페이스까지 세서 0~4, 실제 열화상은 OpenCV 1·2). 따라서 새 카메라의 영상 인덱스를 신뢰성 있게 자동 배정 못 함.

**결론(설계 결정)**: `agentui.json`이 **카메라 SET + OpenCvIndex(영상)의 기준**으로 남는다. 자동 갱신 대상은 **시리얼 COM + 연결여부**뿐. 영상 인덱스 자동 발견은 범위 밖(OpenCV 한계).

## 구현 계획 (4단계)

1. **Core 모델**: `HeatingCameraSystem.Core/Models/CameraDescriptor.cs` (record) 끝에 `string? UsbContainerId = null` 추가 → 기존 호출부 무손상(default). 
2. **자동감지 저장**: `HeatingCameraSystem.AgentUI/ViewModels/SettingsViewModel.cs` `AutoDetectSerialAsync` (L134)에서 pair 매칭 시 `row.UsbContainerId = pair.Camera.UsbParentId`도 저장. `CameraRow`에 `UsbContainerId` 프로퍼티 + `ToDescriptor()`에 포함.
3. **시작 자동 페어링**: `HeatingCameraSystem.AgentUI/App.xaml.cs` `OnStartup` — 카메라 build 루프(L84~) 전에 `pairing.GetPairsAsync()` 1회 실행 → 각 config 카메라의 **현재 COM을 UsbContainerId로 재확인**해 낡은 `SerialPortName` 덮어씀(매칭 실패 시 config 폴백). 재플러그로 COM 번호 바뀌어도 항상 현재값.
4. **USB 핫플러그 감시**: `WmiCameraEnumerator.StartWatching()` + `Changed` 이벤트(이미 존재, PnP `__InstanceCreation/DeletionEvent`) 사용. App에서 별도 `WmiCameraEnumerator` 인스턴스 생성·감시 → 이벤트 **디바운스(~1s)** → UI 디스패처에서 재페어링 → 각 카메라 **시리얼 재초기화(COM 바뀌면) + 연결여부 상태 갱신**. 영상 런타임은 인덱스 고정이라 유지(끊긴 카메라는 기존 Faulted 처리, 재연결 시 런타임 재시작). 참고 패턴: `HeatingCameraSystem.AgentManager/Program.cs` `OnPnpChanged`(검증된 핫플러그 처리).

## 관련 코드 위치

| 파일 | 역할 |
|---|---|
| `Protocols/CameraComPairingService.cs` | 카메라↔COM 페어링(ContainerID 조인, 열화상 필터 CLTC_T_VGA, S/N 검증, Unpaired/Ambiguous 처리). `GetPairsAsync()`. |
| `Protocols/UsbTopology.cs` | `DeriveContainerId(pnp)` — 레지스트리 ContainerID(견고) → PNPDeviceID 정규화 폴백. |
| `Protocols/WmiUsbSerialEnumerator.cs` | COM 포트 열거 + WMI 메타 조인, UsbParentId=ContainerID. |
| `Protocols/WmiCameraEnumerator.cs` | 카메라 열거(`Enumerate`=전체, `EnumerateThermal`=CLTC 필터) + `StartWatching()`/`Changed` PnP 감시. |
| `AgentUI/App.xaml.cs` | 카메라 build 루프, serialFactory, pairing 생성(L68~), SettingsViewModel 주입(L218). |
| `AgentUI/ViewModels/CameraPanelViewModel.cs` | 패널 VM, `_serial`(ClSerialCameraClient) 보유. 시리얼 재초기화하려면 여기 메서드 추가 필요. |
| `Core/Models/CameraComPair.cs`, `DiscoveredCamera.cs` | 페어링 결과 모델. `DiscoveredCamera.UsbParentId`가 조인 키. |

## 검증 방법

- 빌드 `dotnet build` 0/0, 테스트 `dotnet test --no-build` 254 통과 유지.
- 핫플러그는 **실제 USB 꽂았다 뺐다** 해야 완전 검증. 로직은 Manager `OnPnpChanged` 패턴 미러.
- 실측 검증 도구: windows-mcp로 AgentUI 실행 + `State-Tool(use_vision=true)` 스크린샷. 임시 OpenCV 프로브(temp)로 DShow 인덱스/프레임 변산 확인 가능(이번 세션에 씀).

## 이번 세션에서 밝혀진 것 (배경)

- **idx2 블랙 미스터리 해결**: 소프트웨어 버그 아님. USB **재연결(전원 재인가)로 복구**. 카메라의 UVC 영상 인터페이스가 나쁜 상태였고, 시리얼(CDC)은 별개 인터페이스라 정상이었음(S/N·FPA 읽혔음). standalone OpenCV 프로브로 AgentUI 밖에서도 재현(idx2 mean 79 near-black vs idx1 mean 8315).
- **재시작 TaskCanceledException = 무해**: `CameraRuntime.StopAsync`가 프레임 루프 취소 → `Task.Delay(token)` 취소 예외, 바로 `catch when(token.IsCancellationRequested)`에서 잡힘. first-chance 노이즈.
- **좀비 프로세스 = 재현+수정 검증됨**: `Stop-Process`로 안 죽음(네이티브 카메라 스레드 종료 차단)→`taskkill /F /T` 필요. App.xaml.cs OnExit 워치독(6s Process.Kill)이 정상 종료 시 8초 내 소멸 확인(커밋 `897e7d6`).
- **디바이스 관리 CV Idx ≠ 원격설정 CV Idx**: 디바이스 관리는 WMI 순번(스캐너·중복 인터페이스 포함 0~4), 원격설정은 실제 OpenCV 인덱스(1,2). 감독형 접으면 무의미. (감독형 유지 시 개선: Manager `Program.cs` L107 `Enumerate()`→`EnumerateThermal()`로 junk 제거.)

## 커밋 상태 (푸시됨)

이번 세션 커밋(origin/master):
- `9d37d18` fix(agent-manager): 인벤토리 15s 주기 재방송 (디바이스 관리 빈 화면 수정)
- `700768f` fix(agent-settings): DataGrid 헤더 다크 스타일 (흰 배경 글씨 안보임 수정)
- (앞선) `b98d0c2` NUC 캡처 적용, `3d1d634` Master 유령 셔터 제거, `897e7d6` AgentUI 종료 워치독, `d9e8044` 원격설정 조회 레이스, `5dfd76f` 구성도 이미지, `f35ec0e` chamber_history prune.

미해결/보류: 이 핸드오프의 USB 핫플러그+시작 자동페어링 기능(다음 세션).

## 미착수 결정 필요 (다음 세션 시작 시 확인)

- 위 4단계 범위로 진행(시리얼+연결여부만 자동, 영상 인덱스는 config 기준)에 사용자 GO 받았는지 재확인 후 착수.
