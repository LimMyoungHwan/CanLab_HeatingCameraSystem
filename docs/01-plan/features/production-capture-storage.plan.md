# production-capture-storage Planning Document

> **Summary**: 레시피 실행 결과를 고객 지정 폴더/파일 규칙(`[AISEN-TI][GEN3][열캘]`)으로 Master에 저장
>
> **Project**: HeatingCameraSystem
> **Date**: 2026-09-07
> **Status**: Draft

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | 캡처 결과가 Agent PC 로컬에 `{AgentId}_{timestamp}.y16`로만 쌓임. 고객 후처리 툴이 요구하는 4계층 폴더 트리·파일명 규칙과 무관하고, Master에 모이지도 않음 |
| **Solution** | Master가 폴더/파일명을 전부 계산해 캡처 명령에 실어 보내고, Agent는 로컬 버퍼에 `.raw`로 쓴 뒤 폴더 단위로 Master UNC에 robocopy |
| **Function/UX Effect** | 레시피 Start 시 저장 폴더·제품번호 입력 → 종료 시 동기화 진행률 표시. 센서번호는 카메라에서 자동 획득 |
| **Core Value** | 촬영 데이터가 후처리 툴이 바로 읽을 수 있는 형태로 Master 한 곳에 모인다 |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 고객 후처리(캘리브레이션) 툴이 고정된 폴더/파일 규칙을 전제로 동작 |
| **WHO** | Master PC 운영자 |
| **RISK** | ① 미완료 촬영 중 모터 이동 시 데이터가 **조용히** 오염됨 ② 픽셀(0,0) FPA 주입이 프리뷰/MinMax를 망가뜨릴 수 있음 ③ Master 디스크 런당 최대 18GB |
| **SUCCESS** | 레시피 1회전 후 Master에 `{센서번호}_{제품번호}/{조건}/{hot\|cold\|room}/BB{bb}_{nnn}.raw`가 규칙대로 생성, 픽셀0에 FPA raw 존재 |
| **SCOPE** | Core 모델 → 명명 규칙 → RawCaptureWriter → Agent 캡처/중단/동기화 → RecipeEngine fork/join → Master UI |

---

## 1. Overview

### 1.1 레퍼런스

고객 규칙의 **동작하는 구현체**가 저장소에 이미 있다.

| 파일 | 역할 |
|---|---|
| `참고/AISEN_CODE/main.py:950-1066` | `save_rawfile()` — 폴더/파일명 생성, FPA 주입, bias.json |
| `참고/AISEN_CODE/main.py:1167-1186` | 센서번호 디코딩 (우리 `ClPacket.DecodeSerialNumber`와 수식 동일) |
| `참고/FPV_code/` | 위와 동일 로직, 320×240 변종 |
| `docs/PRODUCTION-[AISEN-TI][GEN3][열캘]폴더 및 파일 생성 규칙.pdf` | 원 규칙 |

### 1.2 현재 상태

- `ThermalCaptureWriter.Write()` → `{root}/{AgentId}/{AgentId}_{yyyyMMdd_HHmmss_fff}.y16` + `.json`
- 파일은 **Agent PC 로컬**에만 존재. Master는 JPEG 미리보기(`CaptureResultMessage.ImageBytes`)만 받음
- `RecipeStep`에 챔버 온도대역·블랙바디 역할 개념 없음
- 캡처 루프(`Agent/Program.cs:244`, `AgentUI/CameraPanelViewModel.cs:135`)에 `CancellationToken` 없음 → 중단 불가
- `BackgroundDataCleanupService`는 `*.jpg`만 정리

### 1.3 목표

- Master에 규칙대로 된 트리 생성
- Master 단일 제어점 유지 (Agent/AgentUI는 명령 수행만)
- 기존 `.y16` 경로·기능은 **건드리지 않음**

---

## 2. Scope

### 2.1 In Scope

- [x] 명명 규칙 순수 함수 + 단위 테스트
- [x] Core 모델 필드/열거형 추가
- [x] `.raw` 저장기 (FPA raw 주입, 덮어쓰기)
- [x] `ClPacket` FPA raw 노출
- [x] Agent 캡처 중단(`CaptureAbort`) + `CancellationToken`
- [x] Agent → Master robocopy 동기화 + 진행 보고
- [x] `RecipeEngine` fork/join, join 타임아웃, abort 발행/ack
- [x] Master UI: Start 다이얼로그, 레시피 에디터 콤보, abort 경고, 동기화 프로그레스

### 2.2 Out of Scope

- `_DATA/*.npy` (DPm1/2, H·M·L × FPAt/RFg/RFo/Xc), `LSB.txt`, `{센서번호}.bin` — **후처리 툴 산출물**
- Master `.raw` 자동 정리 — 사용자 수동 처리
- 기존 `.y16` 저장 경로 변경
- Agent 캡처 취소 시 부분 파일 삭제 (재촬영 시 덮어써짐)

---

## 3. 명명 규칙 (Master가 계산)

### 3.1 계층

```
{SaveRootPath}                          ← 사용자 입력 (Master 경로/UNC)
└─ {센서번호}_{제품번호}                 ← 센서번호=카메라 자동, 제품번호=사용자 입력(배치 공통)
   └─ {ChamberRangeCode}{ChamberTempCode}   ← 예: RPP40
      └─ {hot|cold|room}
         ├─ BB{bb}_{nnn}.raw
      └─ bias.json                          ← cold일 때만, 이 계층
```

### 3.2 코드 테이블

| ChamberRange | Code |
|---|---|
| 저온 (Low) | `LN` |
| 상온 (Mid) | `RP` |
| 고온 (High) | `H1P` |

| 챔버 목표온도 | Code |
|---|---|
| −30℃ | `N30` |
| −10℃ | `N10` |
| +10℃ | `P10` |
| +25℃ | `P25` |
| +40℃ | `P40` |
| +55℃ | `P55` |
| +70℃ | `P70` |

**유효 조합 9개 (그 외 금지)**

| Range | 허용 온도 | 폴더 |
|---|---|---|
| 저온 `LN` | −30, −10, +10 | `LNN30`, `LNN10`, `LNP10` |
| 상온 `RP` | +10, +25, +40 | `RPP10`, `RPP25`, `RPP40` |
| 고온 `H1P` | +40, +55, +70 | `H1PP40`, `H1PP55`, `H1PP70` |

### 3.3 블랙바디

| Range | Role | 폴더 | `bb` | 장수 |
|---|---|---|---|---|
| 저온 | cold | `cold` | `10` | 100 |
| 저온 | hot | `hot` | `70` | 100 |
| 상온·고온 | cold | `cold` | `20` | 100 |
| 상온·고온 | hot | `hot` | `80` | 100 |
| 전체 | room | `room` | `room` | **10** |

- `room` = **블랙바디 없음** (PDF: "Black body X")
- 파일: `BB{bb}_{nnn}.raw`, `nnn` = 000부터 3자리
- 예: `BB80_000.raw`, `BBroom_009.raw`

### 3.4 .raw 포맷

- 16bit LE 생바이트. 픽셀값은 14bit 마스킹(`& 0x3FFF`) — 기존 `CltcThermalFrameSource.Read()`와 동일
- **픽셀(0,0) = FPA raw ADC 값** (℃ 아님). `raw = (msb<<8)|lsb`, `>32767`이면 `-65536`
- 동일 파일명 존재 시 **덮어쓴다** (기존 `.y16`의 `_1` 접미사 규칙과 반대)

---

## 4. 회전 계획 (레시피 작성 규칙)

블랙바디 유닛 2개(hot/cold), 그룹 G개, 그룹당 카메라 N개.

- 회전마다 1그룹 hot, 1그룹 cold, 나머지 G−2 그룹 room
- 각 그룹이 hot·cold를 1회씩 받아야 함 → **최소 G회전** (G≥3), G=2면 3회전
- 총 캡처 스텝 = **9조건 × G회전**

레시피는 운영자가 작성한다. 시스템은 조합을 강제하지 않고 유효 조합 검증만 한다.

---

## 5. 모델 변경

### 5.1 Core

```csharp
// RecipeModels.cs
enum ChamberRange   { Low, Mid, High }        // 신규
enum BlackBodyRole  { Hot, Cold, Room }       // 신규
enum RecipeStepKind { ..., CaptureJoin }      // 값 추가

class Recipe {
    string SaveRootPath                        // 신규 (Master 경로/UNC)
    string ProductNumber                       // 신규
}
class RecipeStep {
    bool WaitForCaptureResult = true           // 신규. false = fork
    int  CaptureJoinTimeoutSeconds             // 신규. CaptureJoin 전용. 0이면 전역값
}
class RecipeCameraTarget {
    ChamberRange?  TargetChamber               // 신규
    BlackBodyRole? TargetBlackBody             // 신규
}

// CameraControlMessages.cs
CameraControlOps.CaptureAbort                  // 신규 상수

// NatsMessages.cs
class AgentStatusMessage {
    int? PendingSyncFiles                      // 신규. null = 보고 안 하는 구버전
}
class CaptureCommandMessage {
    string StorageRootUnc                      // 신규  \\MASTER-PC\HeatingData
    string ProductNumber                       // 신규  ABC
    string ConditionFolder                     // 신규  RPP40
    string BlackBodyFolder                     // 신규  hot | cold | room
    string FilePrefix                          // 신규  BB80
    bool   WriteBiasJson                       // 신규  cold일 때 true
}
```

**설계 원칙**: 규칙 계산(코드 테이블·`bb`·장수)은 **Master 한 곳**. AgentUI는 자기 센서번호만 앞에 붙여 조립한다 (V1 — Master는 S/N을 모른다).

```
RelativeDirectory = {SerialNumber}_{ProductNumber}\{ConditionFolder}\{BlackBodyFolder}
Agent 로컬        = {LocalBuffer}\{RelativeDirectory}
전송 대상         = {StorageRootUnc}\{RelativeDirectory}
```

**캡처 경로**: `RecipeEngine.CaptureOnceAsync` → `PublishCaptureCommandAsync` → `master.cmd.capture.{AgentId}` → `CameraNatsConnector`(Protocols) → `CameraPanelViewModel`.

### 5.2 신규 타입

| 타입 | 위치 | 역할 |
|---|---|---|
| `CaptureNamingRule` | Core | 순수 함수. (Range, 온도, Role) → 폴더명·파일 프리픽스·`bb`·장수. 조합 검증 |
| `RawCaptureWriter` | Protocols | `.raw` 쓰기 + FPA raw 주입 + 덮어쓰기 |
| `CaptureSyncService` | Agent | 폴더 완료 → robocopy → 확인 → 로컬 삭제, pending 수 보고 |

---

## 6. 흐름

### 6.1 fork / join

```
Step(WaitForCaptureResult=false)  → 배치를 _pending에 넣고 즉시 다음 스텝
Step(CaptureJoin, timeout=60s)    → _pending 전부 대기
    ├ 전부 도착         → 진행
    └ 타임아웃
        ├ CaptureBatch.Snapshot() → 받은 것만 이력 기록   ※ 기존 기능 재사용
        ├ 미완료 타겟에 CaptureAbort 발행
        ├ ack 대기(3s)
        │   ├ ack     → "중단 완료" 알람 → 진행
        │   └ 무응답  → 레시피 **일시정지** + 운영자 선택
        └
```

**자동 join 없음.** 운영자가 `CaptureJoin` 스텝을 빠뜨리면 운영자 책임.
단 미완료 배치가 있는데 `MotorMove`/`BlackBodyControl`/`ChamberControl` 스텝이 실행되면 **알람 1건만 기록**(차단하지 않음).

### 6.2 abort ack 무응답 → 일시정지

```
[경고] {AgentId} 촬영 중단 응답 없음 — 카메라가 아직 촬영 중일 수 있습니다.
   [그대로 진행]   [멈춤]   [다시 시도]
```
- 그대로 진행 → 다음 스텝 + 오염 가능 알람
- 멈춤 → 레시피 중단
- 다시 시도 → `CaptureAbort` 재발행 + ack 재대기

### 6.3 동기화

```
Agent: 로컬 버퍼에 .raw 기록
     → 폴더(hot/cold/room) 완료 시 robocopy → {StorageRootUnc}\{RelativeDirectory}
     → 성공 확인 후 로컬 폴더 삭제
     → 하트비트에 PendingSyncFiles 보고 (5초)

Master: 레시피 마지막 스텝 종료 → 모달 프로그레스
        Σ PendingSyncFiles == 0 → 완료
        [취소] → 로컬에 남김 + 알람
```

폴더 **단위**로만 복사한다 — 복사 중인 파일을 후처리가 읽는 것을 막는다.

---

## 7. Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-01 | `CaptureNamingRule`: (ChamberRange, 온도, BlackBodyRole) → 폴더명 / `bb` / 파일프리픽스 / 장수 | High |
| FR-02 | `CaptureNamingRule`: 유효 조합 9개 외 입력은 예외 (폴더명 `RPNone` 방지) | High |
| FR-03 | `ClPacket.DecodeFpaTemperatureRaw(msb, lsb) → short` 추가, 기존 메서드는 이를 재사용 | High |
| FR-04 | `RawCaptureWriter`: 16bit LE 기록, 픽셀(0,0)=FPA raw, 동일명 덮어쓰기 | High |
| FR-05 | FPA raw는 **캐시값** 사용 (촬영 루프에서 시리얼 왕복 금지) | High |
| FR-06 | MinMax·JPEG 프리뷰는 FPA 주입 **전** 값으로 계산 | High |
| FR-07 | `Recipe.SaveRootPath` / `ProductNumber` — Start 다이얼로그 입력 | High |
| FR-08 | `RecipeCameraTarget.TargetChamber` / `TargetBlackBody` — 레시피 에디터 콤보 | High |
| FR-09 | Master가 `RelativeDirectory`·`FilePrefix`·`WriteBiasJson`·`ShotCount` 계산해 명령에 실음 | High |
| FR-10 | `RecipeStep.WaitForCaptureResult=false` → 결과 대기 없이 다음 스텝 | High |
| FR-11 | `RecipeStepKind.CaptureJoin` → 미완료 배치 전부 대기, 타임아웃 시 부분 결과 수용 | High |
| FR-12 | join 타임아웃 → `CameraControlOps.CaptureAbort` 발행 + ack 대기 | High |
| FR-13 | Agent/AgentUI 캡처 루프에 `CancellationToken`, abort 시 중단 | High |
| FR-14 | ack 무응답 → 레시피 일시정지 + [진행/멈춤/재시도] | High |
| FR-15 | 미완료 배치 중 모터·블랙바디·챔버 스텝 실행 시 알람 기록 (차단 안 함) | Medium |
| FR-16 | Agent: 폴더 완료 → robocopy → 확인 → 로컬 삭제 | High |
| FR-17 | `AgentStatusMessage.PendingSyncFiles` 보고 | High |
| FR-18 | 레시피 종료 시 동기화 프로그레스 모달 + [취소] | High |
| FR-19 | `bias.json` — cold 조건일 때 3rd layer에 기록 | Medium |
| FR-20 | 기존 `.y16` 경로·프리뷰·히스토그램 동작 불변 | High |

---

## 8. Success Criteria

- [ ] 시뮬레이션 레시피 1회전 → Master에 `{S/N}_{제품}/RPP40/{hot,cold,room}/BB{80,20,room}_{000..}.raw` 생성
- [ ] room 폴더 10장, hot/cold 폴더 100장
- [ ] `.raw` 첫 2바이트 = FPA raw (16bit LE), 나머지 픽셀은 14bit 범위
- [ ] cold 조건 3rd layer에 `bias.json`
- [ ] fork → CaptureJoin 타임아웃 → abort → ack → 다음 스텝 동작
- [ ] 종료 시 프로그레스가 0까지 내려가고 Master 파일 수 = 기대 수
- [ ] 기존 `.y16` 저장·프리뷰·히스토그램 회귀 없음
- [ ] `dotnet build` 0 errors / `dotnet test` 전부 통과

---

## 9. Impact Analysis

| Resource | Change |
|----------|--------|
| `Core/Models/RecipeModels.cs` | enum 2개 + 값 1개, 필드 6개 |
| `Core/Models/NatsMessages.cs` | `CaptureCommandMessage` 4필드, `AgentStatusMessage` 1필드 |
| `Core/Models/CameraControlMessages.cs` | `CaptureAbort` 상수 |
| `Core/` (신규) | `CaptureNamingRule` |
| `Protocols/Cameras/CL/ClPacket.cs` | `DecodeFpaTemperatureRaw` 추가 |
| `Protocols/Cameras/` (신규) | `RawCaptureWriter` |
| `Agent/Program.cs` | CTS, abort 구독, `.raw` 저장, 동기화 |
| `Agent/` (신규) | `CaptureSyncService` |
| `AgentUI/ViewModels/CameraPanelViewModel.cs` | 캡처 루프 CTS |
| `Master/Services/RecipeEngine.cs` | fork/join, abort, 일시정지 이벤트, 명령 필드 채우기 |
| `Master/ViewModels/DashboardViewModel.cs` | Start 다이얼로그, 동기화 프로그레스 |
| `Master/ViewModels/RecipeEditorViewModel.cs` | 타겟 콤보 2개, CaptureJoin 스텝 |
| `Tests/` | 명명 규칙, FPA 주입, fork/join, abort 테스트 |
| `docs/samples/*.json` | 로컬 버퍼 경로 필드 |

---

## 10. 작업 분해 (구현 순서)

| # | 작업 | 의존 |
|---|---|---|
| B1 | Core 모델: enum·필드 추가 (`RecipeModels`, `NatsMessages`, `CameraControlMessages`) | — |
| B2 | `CaptureNamingRule` + 단위 테스트 (순수 함수, 조합 검증 포함) | B1 |
| B3 | `ClPacket.DecodeFpaTemperatureRaw` + 골든 테스트 | — |
| B4 | `RawCaptureWriter` + 테스트 (FPA 주입 위치·덮어쓰기·MinMax 불변) | B3 |
| B5 | Agent: 캡처 루프 CTS + `CaptureAbort` 구독 + ack 발행 | B1 |
| B6 | Agent: `.raw` 저장 경로 연결 (명령의 `RelativeDirectory`/`FilePrefix` 사용) | B4, B5 |
| B7 | Agent: `CaptureSyncService` (robocopy, 로컬 삭제, `PendingSyncFiles` 보고) | B6 |
| B8 | AgentUI: 캡처 루프 CTS + abort | B1 |
| B9 | `RecipeEngine`: 명령 필드 채우기 (`CaptureNamingRule` 호출) | B2 |
| B10 | `RecipeEngine`: fork(`WaitForCaptureResult`) + `CaptureJoin` + 타임아웃 + abort/ack | B9, B5 |
| B11 | `RecipeEngine`: ack 무응답 일시정지 이벤트 + 미완료 배치 알람 | B10 |
| B12 | Master UI: Start 다이얼로그 (저장 폴더 + 제품번호) | B1 |
| B13 | Master UI: 레시피 에디터 — 타겟 챔버/블랙바디 콤보, CaptureJoin 스텝 | B1 |
| B14 | Master UI: abort 경고 다이얼로그 [진행/멈춤/재시도] | B11 |
| B15 | Master UI: 종료 시 동기화 프로그레스 모달 | B7 |
| B16 | 통합 테스트 (시뮬레이터 E2E) + 회귀 확인 | 전체 |

---

## 11. 검증 필요 / 가정

| # | 항목 | 상태 |
|---|---|---|
| V1 | Master가 카메라 센서번호를 알 수 있나 | **불가 확정.** `CameraDevice`(Core/Models/CameraDevice.cs)에 S/N 필드 없음 → **AgentUI가 자기 S/N을 앞에 붙인다** |
| V2 | 카메라 런타임 주체 | **AgentUI 확정.** `Agent/Program.cs:21` — 콘솔 Agent는 보조/진단용. `CameraPanelViewModel`이 S/N(`SerialNumber`)·FPA(`ReadCameraTemperatureAsync`) 모두 보유 |
| V2a | FPA **raw** 캐시 | 없음. 현재 `double`(℃)만. `ClPacket.DecodeFpaTemperatureRaw` + 캐시 필드 추가 필요 |
| V2b | `.raw`에 NUC 보정을 적용하나 | **미적용 확정.** `CaptureSaveAsync`는 `_nuc.Apply(snap)` 보정본을 `.y16`에 저장하지만, 후처리 툴이 캘리브레이션을 하므로 `.raw`는 **원본 `snap`** 이어야 한다 (AISEN 파이썬도 `frame & 0x3FFF` 원본) |
| V3 | 센서번호 미확인 카메라의 폴더명 정책 | 미정 (AISEN은 `"UNKNOWN"`) |
| V4 | `bias.json` payload 형식 — 우리 bias 탐색 결과를 어떤 스키마로 남길지 | 미정 |
| V5 | Master 공유 폴더 인증/권한 (Agent가 UNC 쓰기 가능해야 함) | 배포 시 설정 |
| A1 | 후처리 툴은 Master에서 실행 | 확정 |
| A2 | Master `.raw` 정리는 사용자 수동 | 확정 |
| A3 | 제품번호는 배치 공통 1개 | 확정 |
| A4 | 그룹 수·그룹당 카메라 수 제약 없음 | 확정 |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-09-07 | Initial draft |
| 1.0 | 2026-09-07 | B0~B16 구현 완료. 아래 "구현 결과" 참조 |

---

## 구현 결과 (2026-09-07)

`dotnet build` 0 errors / `dotnet test` **440 passed**.

### 설계에서 바뀐 것

| 항목 | 계획 | 실제 | 이유 |
|---|---|---|---|
| 챔버 온도 출처 | 캡처 스텝의 목표 온도 | **기록 조건이 남긴 실측값** (`_lastRecordedTemperature`) | 캡처 스텝과 챔버 스텝이 분리되어 목표를 알 수 없음 |
| 온도 → 코드 | 정확 일치 | 허용 오차(`TemperatureTolerance`) 내 **최근접 스냅** | 실측값이라 39.98℃처럼 들어옴. 밖이면 예외 → `RCP-005` |
| BIAS 대역 | 스텝 op으로 지정 | **카메라 타겟의 `TargetChamber`가 op을 덮어씀** (`CameraControlOps.Bias`) | 대역이 두 곳에 있으면 어긋나서 조용히 오염됨 |
| 동기화 큐 | 전용 서비스 + 카운터 | `robocopy /MOVE` + 버퍼 폴더 `*.raw` 개수 세기 | 재시도·삭제가 robocopy에 이미 있음 |
| 종료 프로그레스 | 모달 다이얼로그 | **기존 레시피 진행바·중지 버튼 재사용** | 새 UI 없이 같은 요구 충족 |
| 콘솔 `Agent` | 규칙 저장 적용 | **미적용** | 시리얼·S/N이 없는 보조·진단용. 실 런타임은 AgentUI |

### 미구현 (합의된 범위 밖)

- `_DATA/*.npy`, `LSB.txt`, `{센서번호}.bin` — 후처리 툴 산출물
- bias.json **재적용**(이전 런 값을 카메라에 다시 쓰기) — 쓰기만 구현. 필요해지면 추가
- Master `.raw` 자동 정리 — 사용자 수동

### 현장 확인 필요

1. `robocopy` UNC 쓰기 권한 — Agent PC 계정이 Master 공유에 쓸 수 있어야 함
2. 실제 카메라로 `.raw` 픽셀(0,0) FPA raw 값이 후처리 툴 기대와 맞는지
3. 9개 조건 × G회전 레시피를 실제로 작성해 폴더 트리 확인
