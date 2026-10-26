# 인계 — 2026-09-26 (레시피 팬 속도 스텝 / CSV Import·Export)

`c9608fa`(계획서) 위에 **1커밋**. build 0/0 · tests **491 passed** (479 → +12)

계획서 `docs/01-plan/features/recipe-fan-speed-and-csv-io.plan.md`의 작업 1·2·3 전부 구현 +
운영자 피드백 반영한 CSV 2차 개정. 계획서 본문에 정정·결정 이력을 모두 반영해 두었으므로
**계획서가 이 작업의 1차 문서다.** 이 메모는 그 위의 요약과 주의사항이다.

---

## 왜 한 커밋인가

`RecipeEditorViewModel.ExportRecipe`/`ImportRecipe` 두 메서드가 작업 2(상태 메시지)와
작업 3(CSV 전환)의 결과를 **같은 줄에서** 담고 있어 hunk로 쪼갤 수 없다. `RecipeEditorView.xaml`과
`plan.md`도 세 작업이 공유한다. 선례가 있다(`901ffc7` 3건, `6d2b55d` 4건, `9f53d42` 2건).
작업 1(팬 스텝)만은 분리 가능했지만 위 두 파일을 다시 hunk 스테이징해야 해서 묶었다.

---

## 1. Import/Export가 "동작하지 않던" 진짜 원인 — XAML 바인딩 스코프

**계획서가 세운 가설 2개가 둘 다 오진이었다.** 기록을 남긴 이유가 이것이다.

`RecipeEditorView.xaml:391`이 `<Grid DataContext="{Binding SelectedRecipe}">`이고 하단 바까지
그 안에 있다. 즉 그 지점 DataContext는 `RecipeEditorViewModel`이 아니라 **`RecipeModel`**이다.
그런데 두 버튼은 `Command="{Binding ImportRecipeCommand}"`로 직접 바인딩돼 있었다 →
`RecipeModel`에서 커맨드를 찾다 실패 → **Command == null → 클릭해도 대화상자조차 안 뜨고 예외도 없다.**

- ViewModel의 `SelectedRecipe == null` 조용한 return은 **도달조차 못 하던 코드**였다.
- `Debug.WriteLine` 침묵도 마찬가지 — `ImportRecipe()`가 실행된 적이 없다.
- 단서는 바로 옆 `SAVE RECIPE`가 `DataContext.SaveRecipeCommand, RelativeSource=AncestorType=UserControl`을
  쓰고 있던 것. **같은 줄에서 바인딩 깊이가 둘로 갈려 있으면 그쪽을 먼저 의심한다.**
- 391행 이하 전수 조사 완료. 깨진 건 이 둘뿐이었다(381~383행은 391행보다 위라 무관,
  578~593행은 별도 Border로 DataContext가 VM).

교훈: WPF에서 "조용한 실패"를 볼 때 C# 쪽 침묵만 보면 안 된다. **바인딩 실패가 더 조용하다** —
컴파일도 되고 예외도 안 난다. 상태 메시지를 붙여도 그 메시지 자체가 같은 이유로 안 보일 수 있다.

`IoStatusMessage` 신설(미선택/취소/성공/역직렬화 null/예외 5경로). `AlarmSink`는 쓰지 않았다 —
`Master/AGENTS.md`가 PLC·NATS·카메라 오류용으로 규정한다. loc 키도 만들지 않았다 — 이 ViewModel은
이미 `"새 레시피"`, `" (복사)"`를 하드코딩한다. (반면 `RecipeEngine`은 `L(...)`을 쓰므로 그쪽은 loc 키가 맞다.)

---

## 2. 팬 속도 스텝 (`RecipeStepKind.FanControl`)

PLC는 이미 준비돼 있었다(`SetFanSpeedAsync` → `D350`, x100, 10.00~60.00Hz). 없던 건 레시피 쪽뿐.

### 스텝 필드 하나를 추가하면 5곳을 고쳐야 한다

계획서는 1줄로 적었지만 실제로는 5곳이고, **뒤 3곳은 빠뜨려도 경고 없이 값만 사라진다.**

| # | 위치 | 빠뜨리면 |
|---|---|---|
| 1 | `RecipeStep` (Core) | 컴파일 실패 — 즉시 발견 |
| 2 | `RecipeStepModel` (`[ObservableProperty]`) | 에디터에 입력란을 못 붙인다 |
| 3 | `RecipeEditorViewModel.ToDomain` | **저장 시 값 소실** |
| 4 | `RecipeEditorViewModel.FromDomain` | **재로드 시 값 소실** |
| 5 | `RecipeEditorViewModel.CloneRecipe` | **복제 시 값 소실** |

`RecipeCopyTests.FanSpeed_SurvivesDomainRoundTripAndClone`이 3·4·5를 한 번에 잡는다.
다음에 스텝 필드를 추가할 때는 **이 테스트를 복제하는 것이 가장 싸다.**

### 건드리면 깨지는 것

1. **`RecipeStepKind`는 맨 끝에 append만.** 서수로 저장되므로 중간 삽입하면 현장에 이미 저장된
   레시피의 `CaptureJoin`(7)이 조용히 다른 종류로 바뀐다.
2. **`switch`에 `case`를 잊으면 빌드는 통과하고 런타임에 `default:` → `StepKindUnsupported`
   경고로 조용히 격하된다.** enum만 추가하고 끝내면 안 되는 이유.
3. 범위(10~60Hz) 밖이면 `AlarmCodes.FanSpeedOutOfRange`(**RCP-006** 신규) 경고 후 **PLC 쓰기 생략.**
   에디터에서도 막지만 CSV/JSON으로 들여온 레시피가 범위 밖 값을 싣고 올 수 있다. PLC 쓰기는
   하드웨어 신뢰 경계라 여기서도 검증한다. 이 검증을 제거하지 말 것.
4. `FanControl` 케이스는 챔버를 기동하지 않고 `temperatureSafety`/`humiditySafety`도 건드리지 않는다.
   팬은 온습도와 독립이다.
5. `AlarmCodes`는 `CauseKey`/`ActionKey` 규약이 있어 `Alarm_RCP_006_Cause`/`_Action` 키가
   ko/en 양쪽에 있어야 한다.

---

## 3. CSV Import/Export — 2차 개정이 핵심이다

1차 구현은 왕복은 됐지만 **운영자가 손으로 스텝을 추가할 수 없었다**(그 파일이 `E:\tmp\Test1.csv`).
보고: `StepId`가 GUID라 만들 수 없고, `Kind`에 뭐가 올 수 있는지 파일에 없고, 컬럼 34개 뜻을 모른다.

**실제 작업 방식이 설계 제약이다 — 카메라 64대, 4대씩 촬영 → 행 복사·붙여넣고 몇 셀만 수정.**
에디터에 드래그 재정렬·복사·중간삽입이 전부 없어서 CSV로 하려는 것이므로, CSV가 그 셋을 대신해야 한다.

### 형식

```
[GUIDE]   ← 사용법 + 컬럼 설명표 + enum 전 값 범례. 파싱 시 통째로 무시
[RECIPE]  ← Key,Value 블록
[STEPS]   ← 헤더 = 프로퍼티명, 행 = 스텝. 행 순서 = 실행 순서
```

- 섹션 구분은 **빈 줄이 아니라 명시 마커**다. 엑셀이 후행 빈 행을 임의로 넣고 지우므로 빈 줄은
  "섹션 경계"와 "빈 행"을 구분할 수 없다. 마커면 우리 파일이 아닌 것도 명확히 거절한다.
- `[RECIPE]` 마커가 없으면 `InvalidDataException`. `[STEPS]`가 없으면 스텝 0개(정상).
- UTF-8 **BOM** 필수. 쓰기는 `,` 고정, 읽기는 `,`/`;`/`\t` 자동 감지. 숫자는 전부 `InvariantCulture`.
- enum은 **이름**으로 쓰고 읽을 때 대소문자 무시. bool은 `true/false/1/0` 허용.

### 결정 사항 — 되돌리지 말 것

1. **`StepId` 컬럼 없음.** 가져올 때 자동 발급.
   단순 편의 문제가 아니다 — `RecipeEngine.cs:1007`이
   `requestId = $"{step.StepId}:{agentId}:{target.CameraIndex}:{round}"`를 `captureWaiters`/
   `controlWaiters` 키로 쓴다. **행을 복사하면 StepId가 중복되고 두 스텝의 캡처 결과가 서로 엉킨다.**
   복사·붙여넣기가 주 사용법이므로 "빈칸이면 자동 발급"으로 남겨두는 것조차 함정이다.
2. **Kind와 무관한 셀은 공란으로 쓴다.** 이게 가독성의 핵심이다. `FanControl` 행은 Hz 한 칸만 찬다.
3. **공란의 의미가 Kind에 따라 다르다 (중요)**
   - 그 행의 Kind가 **안 쓰는** 컬럼이 비면 → C# 기본값. 정상.
   - 그 행의 Kind가 **쓰는** 컬럼이 비면 → `InvalidDataException`(컬럼명 + 문제 값 포함).

   `ChamberControl` 행의 `TargetChamberTemperature`가 조용히 0이 되면 챔버 목표가 **0℃**다.
   **이 구분을 없애고 전부 0 폴백으로 바꾸지 말 것.**
4. **`CameraTargets`는 한 셀에 `CameraIndex:대역:흑체[:AgentId]`, 여러 대는 `|`.** 뒷부분 생략 가능.
   `AgentId`를 맨 뒤로 보낸 이유: **비어 있는 게 정상**이다(`ResolveTargetAgentIdAsync`가
   `Agent_{CameraIndex}`로 해결, `RecipeEditorViewModel:760`이 빈 AgentId 타겟을 일부러 만든다).
   실제 데이터는 `FOXSTARSOFTPC_Agent_3`처럼 호스트명 접두가 붙거나 비어 있다 — 둘 다 정상.
   4대 배치 → `1:Mid:Hot|2:Mid:Hot|3:Mid:Hot|4:Mid:Hot`. `:`/`|` 포함 AgentId는 쓰기 시 예외
   (조용히 깨지면 저장 규칙 폴더가 오분류된다).
5. **`LegacyCapture`만 전 컬럼을 쓴다.** `switch`에 `case`가 없어 `default:` 경고로 빠지므로
   런타임엔 죽은 값이다(실행 경로는 `ExecuteRecipeAsync` → `ExecuteSegmentedRecipeAsync` 하나뿐).
   그래도 전 컬럼을 쓴다 — 현장 구버전 레시피를 export→import하면 `ShotCount` 등이 **조용히
   지워지기** 때문. 엑셀로 열었다는 이유로 저장된 데이터가 사라지면 안 된다.
6. **컬럼 매핑은 리플렉션.** 손으로 매핑하면 위 "복사 지점 5곳"이 7곳이 된다. 이름 기준 읽기라
   순서 무관, 필드 추가 시 직렬화 코드 수정 0. 컬럼 순서는 `MetadataToken` 정렬로 선언 순서 고정
   (`GetProperties()` 순서는 런타임 보장이 없다).
7. **카탈로그 한 표가 3가지를 구동**한다: `[GUIDE]` 컬럼표 + 쓰기 공란화 + 읽기 관련성 판정.
   Kind↔컬럼 지식이 두 곳에 갈라지면 안 된다. **컬럼이 카탈로그에 없으면 테스트가 실패**하므로
   새 필드를 설명 없이 넣을 수 없다.
8. 하위호환: 모르는 컬럼은 무시(신버전 export를 구버전이 열 수 있게), 아는 컬럼이 없으면 C# 기본값
   유지. `WaitForCaptureResult`·`WaitForStabilization`·`WaitForChamberStabilization`은 기본값이
   `true`이므로 "없음"을 `false`로 읽으면 안 된다.
9. Import는 확장자 분기(`.csv` → CSV, 그 외 → 기존 JSON 경로). **현장에 기존 `.json` 레시피가 있다.**
10. `recipe.Id = Guid.NewGuid()` 재발급은 ViewModel에 남겼다. 직렬화기는 `Id`를 충실히 왕복시키고
    덮어쓰기 방지는 호출자 책임이다.

### Kind↔컬럼 판정 근거

추측하지 않고 읽어서 정했다: `RecipeEngine`의 `switch`, `ExecuteCameraStepAsync`,
`ExecuteBlackBodyStepAsync`, `JoinCapturesAsync`, `WaitForMotorMoveAsync`, `SoakAsync`,
`EnforceSafetyBandAsync`, `ToleranceC`/`ToleranceRh`. 안전 범위 필드는 `EnforceSafetyBandAsync`가
`temperatureSafety`/`humiditySafety`로 기억한 스텝에서 읽으므로 각각 `ChamberControl`·
`HumidityControl` 소속이다.

---

## 실기 확인 (다음 세션 — 코드로 검증 불가)

1. **CSV 왕복을 현장 엑셀로.** 단위 테스트는 **우리 파서만** 검증한다 — 엑셀이 이 파일을 열고
   저장했을 때 실제로 무엇을 뱉는지는 검증하지 못한다. Export → 엑셀에서 `CameraCommand` 행을
   복사·붙여넣고 `CameraTargets`만 수정 → Import 했을 때 스텝이 하나 늘고 값이 맞는지.
2. **`[GUIDE]`만 보고 스텝을 추가할 수 있는지.** 이게 이번 개정의 수용 기준이다. 못 하면 어느 설명이
   부족했는지 받아서 카탈로그의 `뜻`/`예시`를 고친다.
3. **팬 스텝이 실제 `D350`에 기대한 회전수를 쓰는지.** `PlcStatusSnapshot.FanSpeedHz`로 읽어 확인.
4. **엑셀이 `;`로 저장하는지** — 자동 감지가 있지만 실제로 어느 구분자가 나오는지 확인해 두면 좋다.

## 미해결 / 이월

- **BOM 없는 CP949 CSV는 읽기 실패한다.** `ReadFile`은 BOM 감지 후 없으면 UTF-8로 읽고 잘못된
  바이트면 예외를 던진다. 운영자가 엑셀에서 **"CSV (쉼표로 분리)"로 새로 만든 한글 포함 파일**이
  이 경우다. Export가 항상 BOM을 쓰므로 "Export → 편집 → Import" 주경로는 영향 없다.
  증상이 보고되면 UTF-8 디코딩 실패 시 시스템 기본 코드페이지로 재시도하는 폴백을 넣는다.
  (지금 넣지 않은 이유: 조용한 오디코딩으로 한글 레시피명이 깨지는 쪽이 더 나쁘다.)
- **`[STEPS]` 표가 34컬럼이라 엑셀에서 가로 스크롤이 길다.** 복사·붙여넣기 워크플로에서는
  `CameraTargets`가 3번째 컬럼이라 문제되지 않지만, `CaptureJoinTimeoutSeconds`(34번째) 같은 걸
  직접 고치려면 멀다. 불편 보고가 오면 컬럼 순서를 "자주 쓰는 것 우선"으로 바꾸는 건
  카탈로그 정렬만 손대면 된다(읽기는 이름 기준이라 안전).
- **사용자 손수정 4파일이 미커밋으로 남아 있다** — 이번 커밋에서 제외했다.
  `DashboardViewModel.cs` / `StatusMonitorViewModel.cs`: 흑체 PV/SV를 `PlcStatus` 스냅샷 대신
  `IBlackBodyController`에서 직접 재판독하도록 바꾸는 중. **UI 스레드에서 `.Wait()`를 호출**하고
  있어 `4229732`가 고친 폴링 지연과 같은 계열의 위험이 있고, `AGENTS.md`가 적어둔
  "화면은 재판독하지 않는다" 규약과 어긋난다. 빈 `finally {  }`와 `Debug.WriteLine` 삼킴도 있다.
  `PlcXgtClient.cs`: 챔버 start/stop 비트를 100ms 뒤 리셋(모멘터리화). `BitTempStart`(M10)·
  `BitTempStopLamp`(M11)·`BitTempStop`(M21)은 타당하지만 **`BitChamberRun`(M1910)은
  "PC RUN 표시등"**이라 켜둬야 한다 — 시작 직후 끄면 램프가 꺼진다. `StartChamberAsync`는 펄스로,
  `StopChamberAsync`는 상태로 다루고 있어 같은 비트를 모순되게 취급한다.
  `App.xaml.cs`: `simulationRawPath`를 `.\BB70_000.raw`로 하드코딩해 env var를 무력화. 로그에
  "Simulation RAW replay enabled"가 찍히지만 실제 리플레이는 걸리지 않는다(로그만 거짓).
- 이전 세션 이월분 유지: AgentUI 로컬 패널이 UYVY에서 흑백으로 보임, `참고/util/`(56MB) untracked,
  `Dialog_ProductionRun_NeedProduct` 미사용 키, `App.xaml.cs OnExit`의 `.GetAwaiter().GetResult()`.
