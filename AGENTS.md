# AGENTS.md — HeatingCameraSystem

## 프로젝트 개요

열화상 카메라 모니터링 시스템. WPF Master PC ↔ NATS ↔ 다수의 Agent (카메라 PC) 구조.

```
Core (.NET 8)              ← 인터페이스 + 모델 + 설정만. 외부 의존성 없음.
Protocols (.NET 8)         ← Core 구현체. XGT FEnet, NATS, Serial, 카메라 프로토콜.
Master (.NET 8-windows)    ← WPF 운영자 UI. AppServices 정적 서비스 로케이터.
Agent (.NET 8)             ← 카메라 PC 콘솔 앱. OpenCvSharp4 + NATS.
AgentUI (.NET 8-windows)   ← 카메라 런타임 WPF UI.
AgentManager (.NET 8)      ← Agent 승인·감독 호스트.
Simulator (.NET 8)         ← XGT FEnet + NATS 외부 시뮬레이터.
E2EDriver (.NET 8)         ← 외부 시뮬레이터 E2E 드라이버.
ManagerE2EDriver (.NET 8)  ← AgentManager E2E 드라이버.
Tests (.NET 8-windows)     ← xUnit + Moq 통합 테스트 프로젝트.
```

## 빌드 / 테스트 명령

```powershell
dotnet build                                             # 솔루션 전체 빌드
dotnet test --no-build                                   # 테스트 (현재 684개)
dotnet run --project HeatingCameraSystem.Master          # WPF Master 실행
dotnet run --project HeatingCameraSystem.Agent           # Agent 실행 (agent.json 기준)
dotnet run --project HeatingCameraSystem.Agent -- Bay1 nats://192.168.1.10:4222  # 인수 오버라이드
```

테스트 프로젝트가 `net8.0-windows` 타겟인 이유: Master(WPF) 프로젝트를 직접 참조하기 때문.

테스트는 `HeatingCameraSystem.Tests/TestAssembly.cs`의 설정에 따라 전역 병렬 실행을 끈다. 정적
`AppServices`와 WPF 상태를 공유하므로 테스트를 병렬화하지 말고, 외부 NATS/PLC가 필요한 테스트는
시뮬레이터 또는 별도 실행 스크립트의 전제조건을 확인한다. 테스트 출력에서 Master 리소스를 읽는
경우 `HeatingCameraSystem.Tests.csproj`의 `Resources/Lang` 복사 설정을 유지한다.

프로젝트별 세부 규칙:
- `HeatingCameraSystem.Master/AGENTS.md` — WPF 시작·서비스 로케이터·ViewModel 규칙.
- `HeatingCameraSystem.Protocols/AGENTS.md` — XGT·시리얼 셔터·NATS 구현 규칙.

## 런타임 설정 파일 (저장소 외부)

| 파일 | 위치 | 비고 |
|---|---|---|
| `hardware.json` | `%LOCALAPPDATA%\HeatingCameraSystem\` | 최초 실행 시 자동 생성 |
| `data.db` | `%LOCALAPPDATA%\HeatingCameraSystem\` | LiteDB |
| `agent.json` | `<Agent exe 폴더>\` | 최초 실행 시 자동 생성 |
| 캡처 이미지 | `<Agent exe 폴더>\ImageStorage\` | agent.json `StoragePath`로 변경 가능 |

설정 파일이 없으면 기본값으로 자동 생성됨. 편집 후 재시작 필요.

## 아키텍처 핵심

### 서비스 초기화
`AppServices.Initialize()` (정적 서비스 로케이터) → `App.xaml.cs`에서 호출.  
DI 컨테이너 없음. 서비스 추가 시 `AppServices.cs`에 프로퍼티 + 초기화 코드 추가.

### 시뮬레이션 모드

`hardware.json`의 `SimulationMode`(마스터 스위치) + `Simulate.{Plc,BlackBody,Camera}`(장비별 선택). 판정은 `AppServices.IsSimulated` 한 곳.

- `Simulate` 절이 없는 예전 json은 세 항목이 전부 true라 기존 동작(전부 가짜)을 유지한다.
- 챔버 온습도·서보는 별도 컨트롤러가 없고 `IPlcController`의 메서드다 → **`Simulate.Plc`에 포함**. 쪼갤 수 없다.
- Agent PC 카메라는 NATS 너머라 Master가 못 바꾼다. `agent.json`의 `SimulationMode` 소관.
- 구성은 `Initialize()`에서만 결정된다 → PLC 설정화면에서 저장 후 **프로그램 재시작**. 재실행은 `App.OnExit` **맨 끝**에서만 한다(그 전에 띄우면 두 프로세스가 `data.db`를 동시에 열어 LiteDB 잠금 예외).
- 시뮬레이션 중에는 최상단에 배너가 뜨고 어느 장비가 가짜인지까지 표시한다.

### NATS 토픽 규칙
```
master.cmd.capture.{AgentId}    ← Master → 특정 Agent (캡처 명령)
master.cmd.capture.all          ← Master → 전체 Agent (브로드캐스트)
agent.result.capture.{AgentId} ← Agent → Master (캡처 결과)
agent.status.{AgentId}         ← Agent → Master (하트비트, 5초 간격)
```

### 연결 재시도
- **NATS**: `NATS.Net` 라이브러리 내부 자동 재연결. `ConnectionMonitorService` 대상 아님.
- **PLC / Serial**: `ConnectionMonitorService`가 30초 간격으로 점검 + 재연결.

### 시리얼 셔터 프로토콜
raw binary 전송. ASCII 문자열 명령 아님.

```csharp
// 셔터 열기
byte[] _openBuffer  = { 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
// 셔터 닫기
byte[] _closeBuffer = { 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
// 포트에 쓰기: _port.Write(buffer, 0, buffer.Length)
```

`ISerialShutterController.OpenShutterAsync(int cameraIndex)` / `CloseShutterAsync(int cameraIndex)`의  
`cameraIndex`는 **식별자 전용** — 바이트 버퍼에는 사용하지 않음.  
`GetShutterStateAsync`는 하드웨어 조회 불가 → 소프트웨어 상태 캐시(`bool _isOpen`) 반환.

### PLC 프로토콜 (LS XGT FEnet)

Modbus → **XGT 전용 프로토콜**(TCP 2004)로 변경됨. 구현: `PlcXgtClient` (VagabondK.Protocols.LSElectric).
- 논리 디바이스 토큰(`D100`, `M10`, `P000`, `D2520.0`)을 `PlcSettings`에 저장 → VagabondK `DeviceVariable`로 변환.
- 비트-오브-워드(`D2520.0`)는 워드 읽기+마스크(쓰기는 read-modify-write). 순수 비트(`M10`/`P000`)는 직접.
- CPU=**XGB**(XBC-DN64H) 확인 → `UseHexBitIndex=true` 기본. 비트 오독 시 반전. 위치결정: XBF-PD02A(X/Y 2축).
- 전체 상태 일괄: `IPlcController.ReadStatusAsync()` → `PlcStatusSnapshot` (Master 상태 화면 1초 폴링).
- 상태 폴링이 느려지는 함정이 **세 개 맞물려 있다**. 운영자가 "모터 이동 시 25초마다 갱신"으로 보고한 증상이며, 하나만 고치면 재발한다.
  1. **쓰기 간격 대기를 IO 락 안에서 하지 말 것.** `Exec`이 `_io`를 잡은 채 `WriteGapMs`(100ms)를 자면 그동안 상태 판독이 통째로 줄 선다. 쓰기는 모터 이동 때만 나는 게 아니다 — `PlcStatusService.PollBlackBodyAsync`가 **1초마다** 흑체 유닛별로 워드를 2개씩 미러링해 쓰기가 상시 발생한다. 판독이 느려질수록 더 많은 쓰기 주기를 걸쳐 더 느려지는 **양성 피드백**이 25초를 만든다. 쓰기끼리의 간격은 `_writeGate`가, 실제 전송 직렬화는 `_io`가 맡는다 — **두 세마포어를 합치지 말 것**.
  2. **`PlcXgtClient`의 await는 `ConfigureAwait(false)`여야 한다.** 폴링이 `DispatcherTimer`에서 시작해 WPF UI 컨텍스트를 캡처하므로, 빼면 판독 왕복마다 continuation이 디스패처 큐에 실려 라이브 영상 렌더링 뒤에 줄 선다. 결과를 UI 스레드에서 받는 것은 `PollAsync`의 최상위 await가 보장한다.
  3. **`ReadStatusAsync`는 개별읽기 배칭 필수.** 변수 1개씩 읽으면 126왕복이다. 워드 토큰과 순수 비트 토큰을 각각 모아 `PlcSettings.ReadBatchSize`(기본 16, FEnet 상한)씩 청크로 읽는다 → 8왕복. 개별읽기 헤더는 데이터 타입을 하나만 싣기 때문에 **워드 배치와 비트 배치를 합치면 NAK**다. 비트-오브-워드는 워드 배치에 실어 마스킹하므로 같은 워드를 공유하는 비트가 왕복 하나로 합쳐진다(`D60.1`~`D60.9` → `D60` 한 번).
- 판독 소요는 상태 화면의 갱신 메시지에 `(NNNms)`로 노출된다. 1초를 넘으면 `_polling` 재진입 가드가 틱을 버려 갱신 간격이 배수로 튀므로, 느려졌을 때 이 숫자부터 본다.
- 온도 램프: **사용 안 함.** `RecipeEngine`이 램프 0을 넘겨 목표 온도를 한 번에 쓴다. `Recipe.TemperatureRampMinutes`와 `TemperatureRampController`는 남아 있지만 호출되지 않고, 에디터 UI도 숨겨져 있다. 예전 레시피에 남은 값도 무시된다.

### 생산 저장 규칙 (`.raw` 트리)

고객 규칙(`docs/PRODUCTION-[AISEN-TI][GEN3][열캘]...pdf`)대로 촬영 데이터를 Master에 모은다.
설계·결정 이력은 `docs/01-plan/features/production-capture-storage.plan.md`.

```
{SaveRootPath}/{센서번호}_{제품번호}/{대역코드}{온도코드}/{hot|cold|room}/BB{bb}_{nnn}.raw
                                                        └ cold일 때 이 계층에 bias.json
```

- 폴더·파일명·장수 계산은 **`CaptureNamingRule` 한 곳**. Master가 계산해 `CaptureCommandMessage`에 실어 보내고 AgentUI는 자기 센서번호만 앞에 붙인다.
- 조건 폴더 온도는 **실측(PV)이 아니라 목표 온도**다. 순서는 ① 직전 `ChamberControl` 스텝의 목표 ② `ReadStatusAsync().TargetTemperature`(PLC 목표 워드) ③ 실측(PV). ①을 1순위로 두는 덕분에 PLC 없이 시뮬레이션으로 돌려도 폴더명이 실제와 같다.
- 폴더 코드 온도는 대역별 3개(총 9개)뿐이고, 위에서 정한 온도는 **가장 가까운 코드로 스냅**된다. 얼마나 벗어났든 예외를 던지지 않는다 — 이건 검증이 아니라 저장 규칙이다. 온도 불일치로 저장을 막는 게이트를 다시 넣지 말 것.
- `.raw`는 **NUC 미보정 원본**이고 픽셀(0,0)에 FPA 온도 **raw 값**(℃ 아님)이 들어간다. 후처리 툴 계약이다.
- 저장 포맷은 런 단위 선택이다(`Recipe.SaveFormat` → `CaptureCommandMessage.SaveFormat`). `Raw`(기본)와 `Jpeg` 둘뿐이고 **폴더·파일명·장수는 동일, 확장자만 바뀐다**. JPEG는 8비트라 열 데이터가 없어 캘리브레이션에 못 쓴다 — `.raw`로 캘리브레이션한 뒤 육안검사·보고서용으로 다시 찍는 용도다. **두 포맷은 원본 프레임도 다르다**: `.raw`는 NUC **미보정** 스냅샷 그대로고, `.jpg`는 NUC **보정** 프레임을 프레임별 min/max 선형 정규화(`ThermalPreviewEncoder.EncodeJpeg`)로 인코딩한다 — 레퍼런스 C++/Qt Viewer의 `normalize16To8`와 같은 매핑이다. plateau AGC는 라이브 컬러 미리보기(`EncodeColorJpeg`)에만 쓴다. 확장자를 추가하면 `ProductionCaptureSink`의 robocopy 패턴과 `PendingFiles` 판정을 **둘 다** 고쳐야 한다(하나만 고치면 전송 실패가 "완료"로 보고된다).
- 제품이 **UYVY(YUV422) 출력 모드**면 카메라가 이미 AGC·컬러맵을 적용한 8비트 영상만 나온다 — 방사 측정 데이터가 없다. `ClThermalMatDecoder`가 `CV_8UC2`를 받아 BGR24를 `ThermalFrame.Bgr24`에 싣고 `Pixels`에는 휘도를 채운다(단일 채널 소비자 보호용이며 **온도로 환산하면 안 된다**). `IsRadiometric == false`인 프레임에 Raw 런이 걸리면 `RawCaptureWriter`가 거부해 **촬영이 실패로 보고된다**(NUC은 통과시킨다). 예전에는 조용히 JPEG으로 바꿔 저장했는데, 운영자는 캘리브레이션에 못 쓰는 8비트 사진을 성공으로 받아 놓고 나중에야 알게 됐다 — **폴백을 다시 넣지 말 것.** 실패해도 `.y16`+`.json`은 남으므로(`CameraNatsConnector`가 `representative`를 `WriteShot` **앞에서** 잡는다) 사이드카의 `pixelFormat`으로 원인을 본다: `Y16_14bit_LE`면 정상, `Y8_LUMA_16bit_LE`면 카메라가 16비트를 안 주고 있다.
- 카메라를 여는 순서는 `ClCaptureSetup.OpenRawY16` 한 곳이다 — **`ConvertRgb=0`을 `FourCC`보다 먼저** 건다. 뒤집으면 DSHOW가 Y16 요청을 흘려버리고 드라이버 기본 8비트 BGR(`CV_8UC3`)이 남아 위의 `Y8_LUMA` 증상이 난다. 벤더 레퍼런스 `참고/util/Capture.cpp:19-22`가 쓰는 순서다. 촬영(`CltcThermalFrameSource`)과 라이브(`CltcLiveThermalCamera`)가 같은 헬퍼를 쓰는 이유는 한쪽만 고쳐져 두 경로의 픽셀 포맷이 갈리는 걸 막기 위함이다. 해상도는 일부러 강제하지 않는다(네이티브 유지).
- 출력 모드 레지스터는 `USER_CONFIGURATION(0x20)` / `SUB_CMD_CAMERA(0x00)`이고 **한 바이트에 다섯 필드가 패킹**돼 있다(`참고/util/Viewer.cpp:1463,1479-1483`): `bit7 autoStart`, `bit6-4 dispMode`, `bit3-2 outFormat`(`OUT_UYVY=0`/`OUT_Y16=1`), `bit1 captureType`, `bit0 opMode`. **반드시 read-modify-write** — `ClPacket.ReplaceOutputFormat`을 거치지 않고 통째로 쓰면 나머지 네 필드가 0으로 밀린다.
- 이 레지스터를 쓰면 **카메라가 즉시 USB 링크를 끊는다**(`참고/util/Viewer.cpp:414-425`). 따라서 `ICameraSerialClient.SetOutputFormatAsync`는 촬영 중에 부르면 안 되고, 호출 뒤에는 AgentUI 재시작이 필요하다. 이어지는 명령이 실패하는 건 정상이므로 실패로 보고하지 말 것. 재부팅 후에도 유지하려면 재시작 뒤 `SaveConfigAsync`(`0x30/0x02`)를 부른다.
- 운영자 경로는 AgentUI 카메라 패널의 시리얼 제어 절이다 — 현재 포맷을 표시하고(앱 시작 시 `StartLiveAsync`가 자동 판독) 콤보박스+적용 버튼으로 전환한다. 자동 전환은 **일부러 넣지 않았다**: 링크 끊김을 시작 시퀀스에 숨기면 원인 불명 실패가 된다.
- 저장 파일은 Agent 로컬 버퍼 → 폴더 완성 후 `robocopy /MOVE` → Master UNC. 기존 `.y16` 경로는 별개이며 건드리지 않는다.
- 규칙 저장 중에는 `.y16`을 **배치당 1장**(결과 화면 대표)만 쓰고 캡처 결과도 **배치당 1건**만 발행한다. 장마다 쓰면 디스크가 두 배, 장마다 발행하면 미리보기 JPEG이 Master 메모리에 쌓인다. `RecipeEngine`의 `PendingCapture.ExpectedResults`가 이 규약을 따라간다 — **찍을 장수가 아니다.**
- 레시피 결과 히스토그램은 운영자가 검증·저장한 `CaptureHistoryRecord.LinkedRawPath`를 최우선, 그다음 `CaptureResultMessage.ProductionRawPath` → `CaptureHistoryRecord.ProductionRawPath`의 **정확한 Master 목적지**를 매번 읽는다. 자동 기록/자동 추정(근거)/운영자 지정을 구분하고 전체 경로를 표시한다. Agent가 센서번호·제품번호·촬영 offset과 **실제 대표 프레임의 촬영 번호**로 경로를 확정한다. 배치 전체 합산이 아니라 `.y16` 대표와 같은 마지막 촬영 1장이다. 결과 발행은 robocopy보다 먼저이므로 **확인한 경로**가 없을 때만 `원본 .raw 없음/미전송`과 대표 `.y16 (NUC 보정)`을 표시하고 다음 조회 때 재확인한다. 경로 정보가 없는 이전 이력에는 `원본 위치 정보 없음(이전 기록)`을 표시한다 — provenance 누락을 파일 부재로 간주하지 않는다. 존재하지만 손상/읽기 실패한 `.raw`를 `.y16`으로 조용히 대체하지 않는다.
- 이전 이력은 실행 측정의 RecipeId(없으면 요청 키에서 추출한 StepId)로 레시피 SaveRootPath를 찾고, 실제 AgentId/CameraIndex에 대응하는 스텝의 대역·역할, 직전 챔버 목표(없으면 기록 온도)를 `CaptureNamingRule`에 적용해 생산 트리의 `BB*_nnn.raw` 후보를 조사한다. 조건/역할/BB 일치 순위, 파일 수정 UTC 시각과 촬영 UTC 시각의 차이 순으로 표시한다. **조건/역할/BB가 모두 일치하는 폴더가 정확히 1개이고 가장 가까운 파일이 동률 없이 ±2초 이내**일 때만 자동 추정 사용한다. 다른 센서/제품 폴더가 함께 일치하거나 조건·카메라 근거가 부족하면 자동 연결하지 않는다. 현재 레시피 조건은 과거 provenance가 아닌 추정 근거임을 명시한다. 검색 실패/불완전도 자동 연결 금지다. 후보/파일 선택은 운영자가 확인하며 `LinkedRawPath`와 확인한 프레임 크기를 LiteDB 이력에 저장한다. 선택 파일은 읽기 가능하고 크기가 **width×height×2**여야 한다(대표 이미지 또는 Agent 원본 응답으로 해상도 확인; 모르면 거절). 변경·연결 해제를 지원하며 자동 기록 경로는 보존한다.
- 캡처의 `RecipeStepId`는 실행을 구분하는 `{StepId}:{AgentId}:{CameraIndex}:{runId}:{round}` 요청 키다. 생성·해석은 Core의 `RecipeCaptureRequestKey` 한 곳을 사용한다. 첫 `:` 앞부분만 StepId로 읽으며 GUID 또는 레시피에 정확히 존재하는 ID만 인정한다. 구형 `{StepId}:{AgentId}:{CameraIndex}:{round}`와 bare StepId도 허용하고 AgentId 안의 `_`·`:`는 보존한다. 요청/결과 대기는 전체 키로 상관관계를 유지하고, 스텝/레시피 조회에만 추출한 StepId를 사용한다.
- `.raw`는 헤더·고정 해상도·비트 이동 없이 프레임 크기만큼의 little-endian `ushort` 나열이다. 히스토그램과 평균/모집단 표준편차/중앙값은 FPA 슬롯(첫 2바이트)을 **항상 제외**한다. 히스토그램은 기존 14비트 256구간을 유지하고 통계는 실제 픽셀 값으로 계산한다.
- 레퍼런스 구현: `참고/AISEN_CODE/main.py:950-1066`(저장), `:1531-1599`(bias).

### BIAS 보정

- 목표 기본값은 `CameraBiasDefaults`: LOW=8500, MID/HIGH=5000, 허용범위 ±100. 양수인 레시피 `BiasTargetLevel`은 기본값보다 우선하며 기존 레시피를 자동 변경하지 않는다.
- AgentUI는 LSB마다 write → 100ms 안정화 → 안정화 종료보다 새 timestamp의 서로 다른 원본 프레임 5장 평균값들의 median을 측정한다. 이전 프레임/동일 timestamp의 timeout 폴백은 버리고, 2초 동안 신선한 프레임이 없으면 실패다. timestamp는 호스트 수신 시각이므로 센서의 실제 노출 시각을 보증하지 않는다.
- coarse → fine → binary 탐색 순서는 유지한다. 최선 LSB는 미수렴이어도 적용하되, 최종 적용 후 같은 방식으로 재측정하여 범위 밖이면 `BIAS 실패: 0x.. (측정 N, 목표 T±100)`와 실패 ACK를 반환한다. Master는 이를 timeout과 구분해 메시지를 보존하고 스텝 실패 영수증/경고를 남긴다.
- `LastAppliedBias`와 `LastBiasJson`은 적용 시작·실패·취소 때 무효화하고 최종 성공 때만 채운다. 레시피에서 필수 BIAS 보정 또는 최종 `bias.json` 저장·읽기 확인이 실패하면 **최초 1회 + 재시도 3회 후 NG로 중단**하고 이후 촬영·이동을 실행하지 않는다. 파일 생략을 생산 성공으로 처리하지 않는다. AgentUI가 재시도를 담당하며 Master는 최종 증빙을 기다린다. 파일은 센서·제품·온도 조건별 최종 경로에서 현재 요청의 JSON과 일치해야 한다.
- AgentUI 수동 복원은 원래 보정 대역과 이전 `GSK_LSB` 16진수 값을 적용하고 실제 LSB 읽기값을 확인한다. 현재 온도에서 8500을 다시 탐색하지 않는다. Master는 새 실행·온도 조건의 생산 촬영 전에 `biasUseCurrent`로 적용 ID·대역·LSB와 최종 파일을 확인한다. 같은 숫자를 재적용해도 적용 ID를 새로 발급하며 재연결·적용 변경 뒤 이전 증빙을 재사용하지 않는다. 자세한 정책과 로그는 `docs/BIAS-RETRY-NG.md`, `docs/MANUAL-BIAS-RESTORE.md`를 따른다.
- AgentUI 기존 로그에 LSB별 측정 레벨과 모드/목표/최선 LSB/최종 레벨/경계 오차/성공 여부를 남긴다. 콘솔 Agent에는 별도 BIAS 구현이 없다. 변경·고객 불일치 조사 근거: `docs/01-plan/features/bias-mismatch.analysis.md`.

### 촬영 계획 레시피

- 설계/저장 계약: `docs/01-plan/features/capture-plan-matrix.plan.md`. `Recipe.Plan == null`은 기존 스텝 레시피이며 **레거시 편집기·실행을 유지**한다. Plan은 안정 Key/템플릿 RecipeId/선택 카메라를 가진 순서 있는 Entries다. 이전 고정 패턴 모델은 사용하지 않는다.
- 템플릿은 **카메라 한 대용**이다. 항목→카메라→원본 스텝 순서로 **레시피 전체**를 반복한다. 챔버/BB/Wait/BIAS도 카메라마다 원본대로 반복하며 dedup/준비 절차 추출을 하지 않는다.
- `UseCellCamera` 슬롯은 카메라 명령의 AgentId/CameraIndex만 바꾸고 대역/역할은 유지한다. `CellPositionRole` 슬롯은 MotorMove의 Hot/Cold/Room 위치만 바꾼다. 기본은 슬롯 꺼짐이며 저장된 targets/Point/XY는 독립 실행 기본값이다.
- 카메라 identity는 heartbeat의 **AgentId + 실제 CameraIndex(0 포함)**다. 1..64 목록/AgentId 접미사 추정은 금지한다. 신규 후보는 실제 온라인 카메라만, 저장된 오프라인 카메라는 표시·보존한다. 위치표 키도 실제 identity × 역할이다.
- 생성 StepId는 planRecipeId/entryKey/cameraIdentity/templateStepId에서 결정한다. 인덱스·시각·임의 Guid로 재발급하지 않는다. 화면에는 항목 이름/실제 카메라/명령별 역할을 표시하고 내부 키·해시는 숨긴다.
- 링크는 **RecipeId live link**다. 매 실행 시작에 현재 템플릿/공유 위치로 컴파일한 실행 복사본을 고정한다. 초안은 Plan/실패 이유를 저장하고 Steps를 비운다. 저장된 생성 Steps를 실행 소스로 신뢰하지 않는다.
- 재개/실패 재촬영은 새 RunId를 root/parent에 연결하고 **선택 카메라의 전체 절차를 처음부터** 실행한다. 현재 템플릿으로 재컴파일하되 Done 셀은 유지한다. fingerprint가 바뀌면 보드/확인창에 **템플릿이 이전 시도 이후 변경됨**을 표시한다. 부분 실행 Steps로 원본을 덮어쓰지 않는다.
- 완료 기준은 LiteDB `capture_plan_runs` v2의 **모든 필수 스텝 영수증**이다. Capture/BIAS/NUC/이동/Wait까지 전부 성공한 뒤만 Done이며 일부 성공 이력으로 추정하지 않는다. 중단 Running 시도를 복구한다. v1 영수증은 보존하되 v2 체인에서 제외한다.
- `Entries`가 없는 이전 Plan은 **이전 형식 계획입니다. 다시 만들어 주세요.**로 실행 거절하고 낡은 Steps를 비운다. 자동 변환/폴백하지 않는다. 사용 중 템플릿 삭제는 계획 이름을 안내하며 거절한다.
- 공유 위치의 **Point 1은 좌표 이동 예약**, 저장된 **Point→Coordinate 변경 금지**다. 모호한 구형 위치는 보존·보고하며 실제 카메라/역할을 추측해 이관하지 않는다.
- 같은 카메라/생산 폴더의 여러 Capture는 총 장수(반복 포함)를 누적한 `ShotNumberOffset`으로 이어 찍는다. 기본 해제인 `Recipe.OverwriteDuplicateShots`는 엔진이 **실제 계산된 생산 목적지(AgentId + CameraIndex + ConditionFolder + BlackBodyFolder + FilePrefix)**별 다음 빈 번호를 실행마다 관리하도록 한다: 전송 offset = `max(스텝 offset + round × ShotCount, 다음 빈 번호)`, 이후 다음 빈 번호 = `전송 offset + ShotCount`. 일반 스텝 레시피의 기본 offset 0도 안전하게 이어지고, 계획/스텝 목록으로 푼 계획의 사전 누적 offset은 이중 가산하지 않는다. SaveRootPath가 없어 생산 naming이 null이면 할당하지 않는다. **할당 상태는 새 실행마다 초기화**하며 재개/실패 재촬영도 `CompileRemaining`이 배정한 offset 그대로 시작한다(선택 제외된 셀은 컴파일 계산에서 제외; 이전 실행의 할당 상태를 가져오지 않음). 체크하면 계획 컴파일 offset이 모두 0이고 엔진도 저장된 offset과 반복 회차를 무시하고 항상 0을 보내 000부터 덮어쓴다(계획은 템플릿이 아니라 계획 자신의 값 사용). 재촬영 회차 폴더/잔여 파일 규칙은 유지하고 임의 병합/삭제 정책을 추가하지 않는다.

### Agent ↔ 카메라 매핑
`RecipeStep.CameraIndex` → NATS 대상 `Agent_{CameraIndex}`.  
Agent `agent.json`의 `AgentId`와 `CameraIndex`가 일치해야 함.

## 알려진 플레이스홀더

- `hardware.json` PLC 디바이스 주소(D/M/P): A&D PLC 실제 명세 확인 후 운영자가 직접 수정. CPU=XGB(XBC-DN64H) 확인 → `UseHexBitIndex=true` 기본.
- `ServoSpeedPercent`(D2560), Y축 JOG 비트(P725/P726): 문서 미기재 임의값 — 실제 비트 확인 후 교체. 비상정지는 M901로 확인됨.
- `SerialSettings` 기본값(`COM3`, `9600 8N1`): 실제 카메라 가상 포트 설정에 맞게 수정 필요.
- `ServoPointYBase`(D3012): 절대좌표 이동(`MoveToCoordinateAsync`)의 Y 목표 워드. 이동 트리거는 `ServoPointMoveBase`(P601) 재사용 — 실제 주소 하드웨어 확인 후 교체.

## 코드 규칙

- `Nullable=enable` + `ImplicitUsings=enable` — 전 프로젝트 공통.
- nullable 경고 억제(`!`, `#pragma warning disable`) 금지. 원인 수정.
- `as any` / `@ts-ignore` 상당 패턴 없음 (C# 프로젝트).
- 버그 수정 시 리팩터링 금지. 최소 변경만.

## 기술 부채 (건드리지 말 것 — 명시적 요청 시에만)

- `App.xaml.cs OnExit`: `.GetAwaiter().GetResult()` — 종료 블로킹 가능성 있음.
- `NatsCommunicationService` 구독 `Task.Run` 루프: 오류 복구 없음.
