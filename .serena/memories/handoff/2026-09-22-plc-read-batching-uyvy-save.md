# 인계 — 2026-09-22 (PLC 판독 배칭 / UYVY 모드 저장)

> 후속 정정 (2026-10-26): 이 문서는 9월 22일 당시의 이력이다. 아래 Raw 요청의 JPEG 강제 전환은 이후 `45211b8`에서 제거됐다. 현재는 요청한 저장 형식을 유지하며, Raw에 필요한 방사 측정 데이터가 없으면 실패로 보고한다. 과거의 강제 전환 설명을 현행 지침으로 적용하지 않는다.

`bcc51fd` 위에 **2커밋**, origin/master 푸시 완료. build 0/0 · tests **464 passed** (456 → +8)

| 커밋 | 내용 |
|---|---|
| `9f53d42` | 개별읽기 배칭(126→8) + UYVY JPEG 저장. **PLC 진단은 "2초" 오독 기준이라 불완전했다** |
| `4229732` | 25초 정정 후 찾은 실제 1차 원인 — 쓰기 간격 대기의 IO 락 점유 + `ConfigureAwait` + 계측 |

운영자 보고 현장 문제 2건. **내일 실기 테스트 예정** — 아래 "실기 확인" 절이 이번 인계의 핵심이다.

---

## 왜 커밋이 둘인가

`9f53d42`는 두 건을 한 커밋으로 묶었다 — `AGENTS.md` 한 파일을 공유해서(PLC 절 + 생산 저장 절)
hunk 단위 스테이징을 피했고, 선례가 있다(`6d2b55d` 4건, `901ffc7` 3건). 코드 파일은 겹치지
않으므로 되돌릴 때는 파일 단위로 쪼갤 수 있다.

`4229732`는 그 뒤 증상이 2초가 아니라 **25초**라는 정정을 받고 재조사한 결과다. 첫 커밋의
배칭은 필요하지만 **그것만으로는 25초 → 1.6초에 그친다** — 왕복당 비용을 키우는 요인이
따로 있었다(1절 참조). 배칭 커밋을 되돌리면 안 되고, 두 커밋이 함께 있어야 완결된다.

---

## 1. PLC 상태가 25초마다 갱신되던 문제

> **정정 이력**: 운영자 보고를 처음에 "2초"로 읽어 배칭만으로 끝냈다가, 실제가 **25초**라는
> 정정을 받고 재조사했다. 25초는 왕복당 ~200ms를 뜻하고(정상 5~15ms) 배칭만으로는
> 25초 → 1.6초에 그친다. Oracle 검증으로 **1차 원인이 배칭이 아니라 락 점유**임을 확인했다.

### 원인 — 세 개가 맞물려 있다

**(1차) `Exec`이 `_io`를 잡은 채 `WriteGapMs`(100ms)를 잤다.**

```csharp
await _io.WaitAsync();
try {
    await Task.Run(() => action(client));
    if (_s.WriteGapMs > 0) await Task.Delay(_s.WriteGapMs);  // try 안
} finally { _io.Release(); }                                  // 100ms 뒤에야 해제
```

쓰기는 모터 이동 때만 나는 게 아니다 — `PlcStatusService.PollBlackBodyAsync`가 **1초마다**
유닛별로 `WriteBlackBodyTemperaturesAsync`(워드 쓰기 2회)를 돌린다. 유닛 2개면 **매초 400ms를
쓰기가 락을 점유**한다. 판독 126회가 그 sleep 뒤에 줄 서고, 판독이 느려질수록 더 많은 흑체
쓰기 주기를 걸쳐 더 느려지는 **양성 피드백**이 25초를 만든다. 흑체가 `Simulate.BlackBody=true`여도
`_plc`가 실물이면 미러링 쓰기는 실제로 나간다.

**(2차) `Query`/`Exec`에 `ConfigureAwait(false)`가 없었다.**
`PollAsync`가 `DispatcherTimer.Tick`에서 실행되어 UI 컨텍스트를 캡처하므로, 126회 × 2 =
**252회 continuation이 디스패처 큐**에 실렸다. 모터 이동 중엔 라이브 카메라 타일 렌더링이
UI 스레드를 점유해 Normal 우선순위 continuation이 Render 뒤로 밀린다.

**(3차) 변수 1개짜리 개별읽기 126회.**

### 제외된 후보 (확인 완료)

- `ConnectionMonitorService`(30초) — `!IsConnected`일 때만 `ConnectAsync`. `_io` 무관.
- 이중 폴링 — `ManualControlViewModel.PollAsync`는 공용 스냅샷만 복사.
- 스레드풀 고갈 — 126 `Task.Run`이 순차 await라 버스트가 아니다(Oracle 판단).
- Nagle/delayed-ACK — 있어도 126×40ms=5초로 25초를 못 설명.

### 수정

1. **`_writeGate` 신설.** 쓰기 간격은 `_writeGate`가 보장하고 `_io`는 실제 전송 동안만 잡는다.
   간격 대기가 IO 락 밖으로 나가 판독을 막지 않는다. **두 세마포어를 합치면 버그가 돌아온다** —
   락 밖에서 대기하되 쓰기끼리의 전역 간격은 유지해야 하므로 세마포어 두 개가 필요하다.
2. **`Query`/`Exec`/`ReadStatusAsync`의 await에 `ConfigureAwait(false)`.**
   `PollAsync`의 최상위 `await _plc.ReadStatusAsync()`는 ConfigureAwait를 안 붙였고 UI 스레드에서
   진입했으므로, ObservableProperty 갱신·`AlarmSink.Raise`·`Updated?.Invoke`는 **여전히 UI
   스레드에서** 재개된다(Oracle 확인). 문서화된 스레드 안전 불변식 유지.
3. **개별읽기 배칭 126 → 8왕복.**
   - 워드 **30 distinct**: 스칼라 17 + Admin 8 + dotted 비트의 워드부 5(`D2520`,`D2620`,`D60`,`D61`,`D280`)
   - 순수 비트 **86**: `P460`,`P461` + Error 20 + Input 32 + Output 32
   - 최대 이득은 dotted dedup — `D60.1`~`D60.9` 9개 비트가 `D60` 워드 1회로 합쳐진다.
   - 신설: `PlcSettings.ReadBatchSize=16`, `BitBlockTokens`, `DottedWordTokens`, `PureBitTokens`,
     `ReadBatchAsync`, `Lookup`. 제거: `ReadBitBlockAsync`(고아).
4. **계측**: `PollAsync`가 `ReadStatusAsync` 소요를 재서 상태 메시지에 `(NNNms)`로 붙인다.
   게시된 exe에서 디버거 없이 보이는 게 요점이다 — `Debug.WriteLine`은 현장에서 무용지물.

### 건드리면 깨지는 것 (주의)

1. **간격 대기를 `_io` 안으로 되돌리지 말 것.** 25초가 그대로 돌아온다.
2. **`_writeGate`와 `_io`를 합치지 말 것.** 중복 락처럼 보이지만 역할이 다르다.
3. **`ConfigureAwait(false)`를 "일관성"을 이유로 제거하지 말 것.**
4. **워드 배치와 비트 배치를 합치지 말 것** — 개별읽기 헤더는 데이터 타입을 하나만 싣는다(NAK).
5. **비트-오브-워드는 워드 배치로 보낸다.** 단일 판독 경로와 같은 규칙(워드 읽기+마스크).
6. `ReadBatchSize`를 16 위로 올리지 말 것 — 프로토콜 상한. NAK가 나면 **낮춘다**.

---

## 2. 제품이 UYVY(YUV) 모드일 때 저장이 안 되던 문제

### 원인

저장 실패가 아니라 **프레임 기아**였다. `CltcThermalFrameSource.Read()`가
`mat.Type() != MatType.CV_16UC1`이면 무조건 null을 반환했다. 제품이 UYVY 출력 모드면
DirectShow가 `CV_8UC2`를 주므로 `FourCC('Y','1','6',' ')` 요청이 무시되고 **전 프레임 폐기** →
`CameraRuntime` 기아 감시 → Faulted → `"저장 실패: 프레임 없음"`.
`CltcLiveThermalCamera.cs:126`도 같은 방식으로 `continue`했다.

출력 포맷은 카메라 펌웨어 레지스터다 — `참고/util/DataPacket.h:88-89`
(`OUT_Y16=0x01` / `OUT_UYVY=0x00`), 쓰기는 `USER_CONFIGURATION(0x20)/SUB_CMD_CAMERA(0x00)`의
비트 3:2 (`참고/util/Viewer.cpp:1479-1501`). **우리 C# 코드에는 이 레지스터 경로가 없다.**
`ClSerialCameraClient`는 Detector(시리얼번호·FPA온도·bias)와 OperateCtrl(셔터)만 다룬다.

### 사용자 결정 (되묻지 말 것)

두 갈래를 제시하고 **B안**을 선택했다.

- A안(미채택): 시리얼로 제품을 Y16 모드로 전환 → 기존 경로 무변경, 열 데이터 유지
- **B안 채택: UYVY 프레임을 JPEG로 저장.** 제품의 출하 상태(NORMAL/UYVY) 영상을 그대로 남긴다.
  캘리브레이션은 불가 — UYVY에 방사 측정 데이터가 없다.

### 수정

| 파일 | 변경 |
|---|---|
| `Cameras/CL/ClThermalMatDecoder.cs` **(신규)** | `CV_16UC1`/`CV_8UC2`(UYVY)/`CV_8UC3` 디코딩. 프레임소스·라이브카메라 공용 |
| `Core/Models/ThermalFrame.cs` | `byte[]? Bgr24` + `IsRadiometric`. record `init` 프로퍼티라 **기존 46개 생성부 무변경** |
| `ThermalPreviewEncoder.cs` | `Bgr24` 있으면 정규화 없이 인코딩 (카메라가 이미 AGC 적용, 재정규화하면 대비 망가짐) |
| `ProductionCaptureSink.cs` | 비-radiometric이면 `SaveFormat` 무시하고 **JPEG 강제** — 파일명 확장자도 같이 바뀜 |
| `RawCaptureWriter.cs` | 비-radiometric `.raw` 거부(`ArgumentException`) |
| `ThermalNucCorrector.cs` | `Apply`/`CaptureFromFlat` 비-radiometric 통과·무시 |
| `ThermalCaptureWriter.cs` | `PixelFormat` 라벨 `Y16_14bit_LE` → 비-radiometric은 `Y8_LUMA_16bit_LE` |

`CltcThermalFrameSource`/`CltcLiveThermalCamera`는 디코더 위임으로 바뀌었고,
`TryCopyMaskedPixels`(고아)는 제거했다.

### 건드리면 깨지는 것 (주의)

1. **`Pixels`의 의미가 `Bgr24` 유무에 따라 다르다.** UYVY면 휘도(Y)가 들어 있다 —
   단일 채널 소비자(.y16 메타데이터, 표시 컨버터, FPA 평균)를 살리기 위한 것이고
   **온도로 환산하면 안 된다.** `ThermalFrame` docstring에 적어 뒀다.
2. 당시 `.raw` 요청을 JPEG로 전환한 이유는 8비트 휘도를 14비트 열 데이터로 오인하지 않게 하기 위해서였다.
   이 전환은 이후 제거됐다. 현재는 방사 측정 데이터가 없는 Raw 요청을 실패로 보고한다.
3. 확장자를 **새로 추가하지 않았으므로** `SyncFolderAsync` robocopy 패턴과 `PendingFiles`
   두 곳 함정은 이번엔 해당 없다(둘 다 이미 `.jpg` 포함). 다음에 확장자를 늘리면 다시 해당된다.

---

## 검증

- `Tests/ExternalFEnetContractTests.cs` — `PlcXgtClient_ReadStatusAsync_BatchesIndividualReads_AndMasksBitOfWord`
  실제 `FEnetSimulationService` TCP 루프백. 16블록 요청 수락 확인, `D60 = 1<<1` →
  `Heater` true / `Cooler1st` false로 dotted 마스킹 정확도 확인, 요청 수 `InRange(1,16)`로 126 회귀 차단.
  공용 `WordMemory` 헬퍼에 `SetWord`/`AllBitsOn`/`ReadIndividualRequests` 추가
  (기존 2개 테스트는 비트를 읽지 않아 무영향).
- `Tests/UyvyModeCaptureTests.cs` **(신규 6건)** — UYVY Mat 디코딩, Y16 14비트 마스킹 유지,
  미지원 타입 null, Raw 런에서 `.jpg` 폴백, RawCaptureWriter 거부, NUC 통과.

---

## 실기 확인 (내일 — 코드로 검증 불가)

1. **PLC 25초 → 1초.** 상태 화면 갱신 메시지 끝의 `(NNNms)`가 **이번 테스트의 판정 기준**이다.
   - **< 500ms** → 해결. 세 요인 모두 맞았다.
   - **500ms~1000ms** → 왕복당 비용이 여전히 크다. 배칭은 됐으니 남은 건 PLC 스캔 시간
     (모터 이동 중 XBF-PD02A 활성)일 가능성. 흑체 미러링 주기를 늘리는 게 다음 수순.
   - **> 1000ms** → 재진입 가드가 틱을 버리므로 갱신 간격이 배수로 튄다. 내가 못 찾은 요인이
     남아 있다는 뜻이니 그 숫자를 가지고 다시 보자.
   - 모듈이 16블록에 NAK를 내면 `hardware.json`의 `Plc.ReadBatchSize`를 8 → 4로 낮춘다
     (시뮬레이션에선 16 통과). 연결 끊김 알람이 뜨면 이 경우다.
2. **UYVY 제품의 실제 `mat.Type()`.** `CV_8UC2`(레퍼런스 `Capture.cpp:98-111` 기준)와
   `CV_8UC3`(드라이버 자동변환) 둘 다 처리했다. **그 외면 여전히 null**이고 증상이 그대로다 —
   그때는 `ClThermalMatDecoder.Decode`의 마지막 `return null`에 진단 로그를 넣고 타입을 찍어라.
   (선례: `50d8dc5 feat(agentui): log why camera FPA temperature is missing`)
3. **저장된 JPEG 육안 확인** — 제품 컬러맵이 켜져 있으면 컬러로, 꺼져 있으면 흑백으로 나와야 한다.

---

## 미해결 / 이월

- **AgentUI 로컬 패널이 UYVY에서 휘도(흑백)로 보인다.** `ThermalFrameBitmapSourceConverter`가
  `Pixels` 기반이라 그렇다. Master 대시보드는 NATS JPEG 경로(`EncodeJpeg`)라 정상 컬러다.
  요구사항이 "저장"이라 여기까지만 했다. 로컬 패널도 컬러로 원하면 컨버터에 `Bgr24` 분기를 추가한다.
- **`참고/util/`(56MB, Qt 뷰어 레퍼런스 + DLL 6개)이 untracked다.** `AGENTS.md`가
  `참고/util/DataPacket.h`를 인용하므로 신규 클론에서는 링크가 끊긴다. `참고/AISEN_CODE`는 tracked라
  선례는 있으나 크기 때문에 사용자 판단으로 남겼다. 함께 untracked: `참고/BB70_000.raw`,
  `참고/Y16_표시_파이프라인.pdf`, `docs/장비 Check List_260921.xlsx`.
- `.serena/project.yml` 수정본은 내 작업 이전부터 있던 툴 아티팩트라 커밋에서 제외했다.
- 이전 세션 이월분 유지: `Dialog_ProductionRun_NeedProduct` 미사용 키,
  "장수는 하드웨어 계약" 문구 불일치, `App.xaml.cs OnExit`의 `.GetAwaiter().GetResult()`.
