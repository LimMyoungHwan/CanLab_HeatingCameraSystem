# recipe-fan-speed-and-csv-io Planning Document

> **Summary**: 레시피 스텝에 팬(블로워) 속도 제어 추가 + Import/Export를 CSV로 전환
>
> **Project**: HeatingCameraSystem
> **Date**: 2026-09-23
> **Status**: 구현 완료 (작업 1·2·3) + CSV 2차 개정 — build 0 error · tests **491 passed** (479 → +12)
> **선행 커밋**: `45211b8` (카메라 Y16/Raw 저장)
>
> **정정**: 작업 2의 원인 가설 2개가 **둘 다 오진**이었다. 실제 원인은 XAML 바인딩 스코프다 —
> 아래 "작업 2" 절의 정정 기록을 먼저 읽을 것.

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | ① 레시피가 팬 속도를 못 건드린다 — 운영자가 수동 제어 화면에서 따로 눌러야 한다. ② Import/Export 버튼이 동작하지 않는다(운영자 보고). ③ JSON은 운영자가 직접 편집하기 어렵다 |
| **Solution** | ① `RecipeStepKind`에 팬 속도 스텝 추가 ② Import/Export 실패 침묵 제거 ③ 포맷을 CSV로 전환 |
| **Core Value** | 레시피 하나로 팬까지 제어 + 엑셀에서 레시피를 직접 편집 |

---

## 작업 1 — 레시피 스텝에 팬 속도 제어

### 현황

- PLC 쪽은 **이미 준비돼 있다**: `IPlcController.SetFanSpeedAsync(float hz)` → `PlcXgtClient.cs:166` → `D350`에 x100 스케일로 기록. 범위 10.00~60.00Hz.
- 상태 판독도 있다: `PlcStatusSnapshot.FanSpeedHz`.
- **없는 것은 레시피 쪽뿐이다** — `RecipeModels.cs`에 팬 관련 필드가 하나도 없다.

### 결정해야 할 것

`RecipeStepKind`(`RecipeModels.cs:68-89`)에 어떻게 넣을지 두 갈래다:

| 안 | 내용 | 장점 | 단점 |
|---|---|---|---|
| **A. 새 스텝 종류** `FanControl` | 팬 전용 스텝 | 온도와 독립적으로 순서 제어 가능. 기존 스텝 의미 안 건드림 | 스텝 수가 늘어남 |
| **B. `ChamberControl`에 필드 추가** | 온도 스텝이 팬도 같이 설정 | 스텝 수 그대로 | 팬만 바꾸고 싶을 때 온도 스텝을 만들어야 함. 기존 레시피의 기본값 처리 필요 |

**A를 권함** — `HumidityControl`을 `ChamberControl`에서 분리한 기존 판단과 같은 이유다(`RecipeModels.cs:73-79` 주석 참조). 온도 스텝에 묶으면 "팬만 바꾸는 스텝"이 불가능해진다.

### 손댈 곳

1. `Core/Models/RecipeModels.cs` — `RecipeStepKind.FanControl` + `RecipeStep.TargetFanSpeedHz`
2. `Master/Services/RecipeEngine.cs` — `ExecuteSegmentedRecipeAsync`의 switch(`:336-412`)에 `case RecipeStepKind.FanControl` 추가 → `_plcController.SetFanSpeedAsync()`
3. `Master/ViewModels/RecipeEditorViewModel.cs` + `Views/RecipeEditorView.xaml` — 스텝 종류 선택 + Hz 입력
4. `Protocols/Simulation/FakePlcController.cs` — 이미 `SetFanSpeedAsync` 있음, 확인만
5. 테스트 — `RecipeEngineTests`에 팬 스텝이 `SetFanSpeedAsync`를 부르는지

### 주의

- 범위 검증: 10.00~60.00Hz 밖의 값을 레시피에 넣을 수 있으면 PLC에 이상한 워드가 간다. 에디터에서 막을 것.
- 기존 레시피는 `TargetFanSpeedHz`가 0으로 역직렬화된다. `FanControl` 스텝이 아니면 읽지 않으므로 무해하나, A안을 택하면 신경 쓸 일 없음.

### 구현 중 발견 — 필드 복사 지점이 "손댈 곳" 1번보다 많다

계획서는 `RecipeModels.cs` 한 줄로 적었지만, 스텝 필드 하나를 추가하면 실제로 **5곳**을 고쳐야 한다.
세 곳은 필드를 손으로 하나씩 베껴 쓰는 매핑 함수다 — 하나만 빠뜨리면 **경고도 없이 값이 사라진다**
(저장은 되는데 다시 열면 0, 또는 복제하면 0).

| # | 위치 | 빠뜨리면 |
|---|---|---|
| 1 | `RecipeStep` (Core) | 컴파일 실패 — 즉시 발견 |
| 2 | `RecipeStepModel` (`[ObservableProperty]`) | 에디터에 입력란을 못 붙인다 |
| 3 | `RecipeEditorViewModel.ToDomain` | **저장 시 값 소실** |
| 4 | `RecipeEditorViewModel.FromDomain` | **재로드 시 값 소실** |
| 5 | `RecipeEditorViewModel.CloneRecipe` | **복제 시 값 소실** |

(+ `AddStep`에 기본값. 범위가 10~60이라 기본 0은 곧바로 무효값이 되므로 30Hz를 넣었다.)

`RecipeCopyTests.FanSpeed_SurvivesDomainRoundTripAndClone`이 3·4·5를 한 번에 잡는다. 앞으로 스텝
필드를 추가할 때는 이 테스트를 복제하는 것이 가장 싸다.

### enum은 맨 끝에 append만 — 중간 삽입 금지

`RecipeStepKind`는 서수로 저장된다. 중간에 끼워 넣으면 현장에 이미 저장된 레시피의 `CaptureJoin`(7)이
조용히 다른 스텝 종류로 바뀐다. `FanControl`은 `CaptureJoin` 뒤에 붙였다.

### 실행·알람

- `RecipeEngine`의 `switch`에 `case RecipeStepKind.FanControl`(`default:` 바로 앞). 챔버를 기동하지 않고
  `temperatureSafety`/`humiditySafety`도 건드리지 않는다 — 팬은 온습도와 독립이다.
- 10~60Hz 밖이면 `AlarmCodes.FanSpeedOutOfRange`(**RCP-006** 신규) 경고 후 **PLC 쓰기를 생략**한다.
  에디터에서도 막지만, CSV/JSON으로 들여온 레시피가 범위 밖 값을 싣고 올 수 있다. PLC 쓰기는
  하드웨어 신뢰 경계라 여기서도 검증한다.
- `switch`에 `default:` → `StepKindUnsupported` 경고가 있으므로, enum만 추가하고 `case`를 잊으면
  **빌드는 통과하고 런타임에 조용히 경고로 격하된다.** 이 조합이 위험하다.

---

## 작업 2 — Import/Export "안 되는" 원인 확인

### 현재 코드 (`RecipeEditorViewModel.cs:421-466`)

```csharp
private void ExportRecipe()
{
    if (SelectedRecipe == null) return;          // ← 조용한 no-op
    ...
}

private void ImportRecipe()
{
    ...
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[RecipeEditor] Import failed: {ex.Message}");
        // ← Release 빌드에서는 완전 침묵. 화면에 아무 표시 없음
    }
}
```

### 가장 유력한 원인 (확인 순서)

1. **Export**: 레시피를 선택하지 않고 눌렀다 → `SelectedRecipe == null`로 조용히 반환. 파일 대화상자조차 안 뜬다.
2. **Import**: 역직렬화 예외가 `Debug.WriteLine`으로만 나가 **게시된 exe에서는 흔적이 전혀 없다**. 버튼을 눌러도 아무 일이 없는 것처럼 보인다.
3. 대화상자는 떴는데 저장/불러오기 후 화면이 갱신되지 않았을 가능성 — `Recipes.Add(vm)` + `SelectRecipe(vm)`는 있으므로 낮음.

> 카메라 Y16 건과 **같은 실패 패턴**이다 — 조용한 실패. 재현 전에 먼저 상태 메시지를 붙일 것. 원인을 못 찾으면 추측으로 고치게 된다.

### 정정 — 위 가설 2개는 둘 다 오진이었다

실제 원인은 **XAML 바인딩 스코프**다. `RecipeEditorView.xaml:391`이

```xml
<Grid DataContext="{Binding SelectedRecipe}">
```

이고 하단 바(IMPORT/EXPORT 버튼 줄)까지 이 Grid 안에 있다. 즉 그 지점의 DataContext는
`RecipeEditorViewModel`이 아니라 **`RecipeModel`**이다. 그런데 두 버튼은

```xml
Command="{Binding ImportRecipeCommand}"   <!-- RecipeModel에서 찾다 실패 → Command == null -->
```

로 직접 바인딩돼 있었다. `Command`가 null인 `Button`은 클릭해도 아무 일도 하지 않는다 —
**대화상자조차 뜨지 않고, 예외도 나지 않는다.** 운영자 보고 "버튼이 동작하지 않는다"는 말 그대로였다.

바로 옆 `SAVE RECIPE`가 `DataContext.SaveRecipeCommand, RelativeSource={RelativeSource AncestorType=UserControl}`을
쓰고 있던 것이 단서였다. 같은 줄에서 바인딩 깊이가 둘로 갈려 있으면 그쪽이 먼저 의심 대상이다.

- `ViewModel`의 `SelectedRecipe == null` 조용한 return은 **도달조차 못 하던 코드**였다.
- `Debug.WriteLine` 침묵도 마찬가지 — `ImportRecipe()`가 실행된 적이 없다.
- 391행 이하에서 `RelativeSource` 없이 직접 바인딩된 VM 커맨드는 전수 조사했고 **이 둘뿐**이었다
  (381~383행은 391행보다 위라 무관, 578~593행은 별도 Border로 DataContext가 VM).

교훈: "조용한 실패"를 봤을 때 C# 쪽 침묵만 보면 안 된다. WPF에서는 **바인딩 실패가 더 조용하다** —
컴파일도 되고 예외도 안 난다. 상태 메시지를 붙여도 그 메시지 자체가 같은 이유로 안 보일 수 있다.

### 손댄 곳 (구현 완료)

- `RecipeEditorView.xaml` — IMPORT/EXPORT를 `DataContext.*Command` + `RelativeSource AncestorType=UserControl`로 교정. **이게 실제 수정이다.** 재발 방지 주석을 같은 자리에 남겼다.
- `RecipeEditorViewModel` — `IoStatusMessage` 신설. 미선택/취소/성공/역직렬화 null/예외 5경로 전부 문자열 노출. 취소는 오류가 아니므로 메시지를 비운다.
- `AlarmSink`는 **쓰지 않았다** — `Master/AGENTS.md`가 AlarmSink를 PLC·NATS·카메라 오류용으로 규정한다. 파일 대화상자 동작은 해당 없음.
- loc 키도 만들지 않았다 — 이 ViewModel은 이미 `"새 레시피"`, `" (복사)"`를 하드코딩한다. 기존 스타일 유지. (반면 `RecipeEngine`은 `L(...)`을 쓰므로 그쪽은 loc 키가 맞다.)

---

## 작업 3 — Import/Export를 CSV로

### 요구

운영자가 **엑셀에서 직접 편집**할 수 있어야 한다. 현재는 JSON이라 실질적으로 불가능하다.

### 설계 난점 — 레시피는 평평하지 않다

`Recipe`는 헤더(이름, `SaveRootPath`, `ProductNumber`, `SaveFormat`, 기록 조건…) + `List<RecipeStep>` 구조다. 스텝마다 쓰는 필드도 종류별로 다르다(`ChamberControl`은 온도, `MotorMove`는 좌표, `CameraCommand`는 장수·간격…).

CSV로 낼 때 세 갈래:

| 안 | 형태 | 장점 | 단점 |
|---|---|---|---|
| **A. 2-섹션 1파일** | 헤더 블록 + 빈 줄 + 스텝 표 | 파일 1개. 엑셀에서 열림 | 표준 CSV 파서로 한 번에 못 읽음 |
| **B. 2파일** | `{name}.recipe.csv` + `{name}.steps.csv` | 각각 순수 CSV | 파일 2개를 짝지어 관리해야 함 |
| **C. 스텝만 CSV** | 헤더는 JSON 유지, 스텝만 CSV | 편집 대상(스텝)만 쉬워짐 | 여전히 2파일 |

**A를 권함** — 운영자가 파일 하나를 주고받는 게 현장에서 가장 단순하다. 파싱은 우리가 하면 된다.

### 반드시 정할 것

- **인코딩**: 한글 헤더를 쓸 거면 **UTF-8 BOM**. BOM 없으면 엑셀이 한글을 깨뜨린다.
- **구분자**: 한국어 Windows 엑셀은 로케일에 따라 `;`를 기본으로 쓰기도 한다. 쉼표 고정 + 읽을 때 자동 감지가 안전하다.
- **enum 표기**: `RecipeStepKind`를 숫자로 낼지 이름으로 낼지. **이름**이어야 운영자가 읽는다. 읽을 때는 대소문자 무시.
- **JSON 하위호환**: 기존 `.json` 레시피 파일이 현장에 있다. Import는 **확장자로 분기해 둘 다 받는 게** 맞다. Export는 CSV로 전환.
- **왕복 보존**: Export → 엑셀 편집 → Import 후 레시피가 동일한지가 유일한 수용 기준이다. 테스트로 고정할 것.

### 손댄 곳 (구현 완료)

- `Master/Services/RecipeCsvSerializer.cs` **(신규)** — `Serialize`/`Deserialize`/`WriteFile`/`ReadFile`.
  `Core`에 두지 않았다: 루트 `AGENTS.md`가 Core를 "인터페이스 + 모델 + 설정만"으로 규정한다.
- `RecipeEditorViewModel.ExportRecipe/ImportRecipe` — Export는 CSV, Import는 확장자 분기(`.csv`는 CSV, 그 외는 기존 JSON 경로). `recipe.Id = Guid.NewGuid()` 재발급은 ViewModel에 그대로 남겼다 — 직렬화기는 `Id`를 충실히 왕복시키고, 덮어쓰기 방지는 호출자 책임이다.
- `Tests/RecipeCsvSerializerTests.cs` **(신규 9건)**

### 계획 대비 변경한 결정 3개

| # | 계획 | 실제 | 이유 |
|---|---|---|---|
| 1 | 헤더 블록 + **빈 줄** + 스텝 표 | `[RECIPE]` / `[STEPS]` **명시 마커** | 엑셀이 후행 빈 행을 임의로 넣고 지운다 → 빈 줄은 "섹션 경계"와 "빈 행"을 구분할 수 없다. 마커면 우리 파일이 아닌 것도 명확한 메시지로 거절한다 |
| 2 | (미정) | **리플렉션 기반 컬럼 매핑** | `RecipeStep` 필드가 35개고, 손으로 매핑하면 위 "복사 지점 5곳"이 **7곳**이 된다. 이름으로 읽으므로 순서 무관, 필드 추가 시 직렬화기 수정 0 |
| 3 | (언급 없음) | `CameraTargets`를 **한 셀**에 `AgentId:Idx:대역:흑체`, 다중은 `\|` | `CameraTargets`는 스텝마다 붙는 리스트라 2차원 CSV에 안 들어간다. 2-섹션 1파일 결정을 지키는 유일한 방법. `:`/`\|` 포함 AgentId는 쓰기 시 예외 — 조용히 깨지면 저장 규칙 폴더가 오분류된다 |

컬럼 순서는 `MetadataToken` 정렬로 선언 순서에 고정했다(`GetProperties()` 순서는 런타임 보장이 없다).
읽기는 항상 헤더 이름 기준이라 순서가 달라도 왕복은 깨지지 않는다.

### 하위호환 규칙

- 파일에 **모르는 컬럼**이 있으면 무시한다(신버전 export를 구버전이 열 수 있게).
- **아는 컬럼이 없으면** C# 기본값을 유지한다. `WaitForCaptureResult`·`WaitForStabilization`·`WaitForChamberStabilization`은 기본값이 `true`이므로, "없음"을 `false`로 읽으면 안 된다.

### 빈 셀은 일부러 예외를 던진다

숫자·bool 셀이 비어 있으면 `InvalidDataException`을 던지고 **0/false로 폴백하지 않는다.**
`TargetChamberTemperature`가 조용히 0이 되면 챔버 목표가 **0℃**가 된다. 대신 예외 메시지에
`'컬럼명' 열의 값을 읽을 수 없습니다: "입력값"` 형태로 열 이름과 문제 값을 싣는다 — 엑셀로 손편집하는
포맷이니 운영자가 자기 오타를 찾을 수 있어야 한다.

### 2차 개정 — 운영자가 실제 export로 스텝 추가를 시도한 결과

첫 구현은 왕복은 됐지만 **손으로 스텝을 추가할 수 없었다**(`E:\tmp\Test1.csv`가 그 파일). 운영자 보고:
`StepId`가 GUID라 만들 수 없고, `Kind`에 뭐가 올 수 있는지 파일에 없고, 컬럼 34개의 뜻을 알 수 없다.

실제 작업 방식이 설계 제약이다 — **카메라 64대, 4대씩 촬영 → 행을 복사·붙여넣고 몇 셀만 수정.**
에디터에 드래그 재정렬·복사·중간 삽입이 전부 없어서 CSV로 하려는 것이므로, CSV가 그 세 가지를
대신해야 한다.

| # | 변경 | 이유 |
|---|---|---|
| 1 | **`StepId` 컬럼 제거**, 가져올 때 자동 발급 | `RecipeEngine.cs:1007`이 `requestId = $"{StepId}:{agentId}:{cameraIndex}:{round}"`를 `captureWaiters`/`controlWaiters` 키로 쓴다. **행을 복사하면 StepId가 중복**되고 그 순간 두 스텝의 캡처 결과가 서로 엉킨다. 복사·붙여넣기가 주 사용법이므로 "선택 입력"으로 남겨두는 것조차 함정이다 |
| 2 | **`[GUIDE]` 섹션을 파일 맨 앞에** | 컬럼별 뜻·단위·쓰는 스텝·예시 표 + `RecipeStepKind`·`MotorMoveType`·`ChamberRange`·`BlackBodyRole`·`ProductionCaptureFormat`·`CameraControlOps` 전 값의 한글 설명. 파싱 시 통째로 무시하므로 엑셀이 다시 저장해도 무해 |
| 3 | **Kind와 무관한 셀은 공란으로 쓴다** | 예전엔 `CameraCommand` 행에 의미 없는 `0`/`False`가 34개 늘어서 무엇을 고쳐야 할지 알 수 없었다. 이제 `FanControl` 행은 Hz 한 칸만 찬다 |
| 4 | **`CameraTargets` 셀을 `CameraIndex:대역:흑체[:AgentId]`로 뒤집고 뒷부분 생략 허용** | `AgentId`는 **비어 있는 게 정상**이다(`ResolveTargetAgentIdAsync`가 `Agent_{CameraIndex}`로 해결). 모르는 AgentId를 먼저 쓰게 강요한 게 예전 순서의 잘못. 4대 배치가 `1:Mid:Hot\|2:Mid:Hot\|3:Mid:Hot\|4:Mid:Hot` |
| 5 | **카탈로그 한 표가 3가지를 동시에 구동** | 컬럼→(한글 뜻, 단위, 예시, 쓰는 Kind 집합). `[GUIDE]` 생성 + 쓰기 공란화 + 읽기 관련성 판정이 모두 이 표를 본다. Kind↔컬럼 지식이 두 곳에 갈라지지 않게 |

Kind↔컬럼은 추측하지 않고 `RecipeEngine`의 `switch`, `ExecuteCameraStepAsync`,
`ExecuteBlackBodyStepAsync`, `JoinCapturesAsync`, `WaitForMotorMoveAsync`, `SoakAsync`,
`EnforceSafetyBandAsync`, `ToleranceC`/`ToleranceRh`를 읽어 정했다. 안전 범위 필드는
`EnforceSafetyBandAsync`가 `temperatureSafety`/`humiditySafety`로 기억한 스텝에서 읽으므로
각각 `ChamberControl`·`HumidityControl` 소속이다.

컬럼이 카탈로그에 없으면 **테스트가 실패**한다. 새 필드가 설명 없이 들어오는 걸 막는 장치이고,
"복사 지점 5곳"과 같은 계열의 실수를 컴파일/테스트 단계에서 잡는다.

#### 공란의 의미가 Kind에 따라 다르다 (중요)

- 그 행의 Kind가 **쓰지 않는** 컬럼이 비어 있으면 → C# 기본값. 정상이다.
- 그 행의 Kind가 **쓰는** 컬럼이 비어 있으면 → 여전히 `InvalidDataException`.

`ChamberControl` 행의 `TargetChamberTemperature`가 조용히 0이 되면 챔버 목표가 **0℃**다.
읽기 쪽 안전장치는 그대로 두고, 무관한 30칸만 비운 것이다. 이 구분을 없애면 안 된다.

#### `LegacyCapture`는 예외적으로 전 컬럼을 쓴다

`LegacyCapture`는 `switch`에 `case`가 없어 `default:` → `StepKindUnsupported` 경고로 빠진다
(실행 경로는 `ExecuteRecipeAsync` → `ExecuteSegmentedRecipeAsync` 하나뿐임을 확인). 즉 필드가
런타임에는 죽은 값이다. 그래도 **전 컬럼을 쓴다** — 현장의 구버전 레시피를 export→import하면
`ShotCount` 등이 조용히 지워지기 때문이다. 엑셀로 열었다는 이유로 저장된 데이터가 사라지면 안 된다.
`[GUIDE]` 범례에도 "실행되지 않으므로 새 스텝에 쓰지 말라"고 명시했다.

### 남은 함정 (미해결)

`ReadFile`은 BOM을 감지하고 없으면 UTF-8로 읽으며, 잘못된 바이트면 예외를 던진다.
따라서 운영자가 엑셀에서 **"CSV (쉼표로 분리)"로 새로 만든 한글 포함 파일**(= BOM 없는 CP949)은
읽기에서 실패한다. Export가 항상 BOM을 쓰므로 "Export → 편집 → Import" 주경로는 영향이 없다.
증상이 보고되면 UTF-8 디코딩 실패 시 시스템 기본 코드페이지로 재시도하는 폴백을 넣는다
(지금 넣지 않은 이유: 조용한 오디코딩으로 한글 레시피명이 깨지는 쪽이 더 나쁘다).

---

## 순서 (제안대로 진행 완료)

1. **작업 2 먼저** — 상태 메시지를 붙여 "왜 안 되는지" 눈으로 확인. 여기서 원인이 드러나면 작업 3의 설계가 달라질 수 있다.
2. **작업 1** — 팬 스텝. 독립적이고 PLC 쪽이 이미 준비돼 있어 가장 빠르다.
3. **작업 3** — CSV. 팬 스텝이 먼저 들어가야 CSV 스키마를 한 번만 정한다(나중에 넣으면 컬럼을 또 바꿔야 함).

> 작업 1을 3보다 먼저 하는 이유가 이것이다. 순서를 바꾸면 CSV 컬럼을 두 번 정하게 된다.

이 순서는 결과적으로 맞았지만, 이유는 예상과 달랐다. 작업 2에서 드러난 것은 Import/Export의
**파일 포맷 문제가 아니라 바인딩 문제**였다 → 작업 3의 CSV 설계 자체는 바뀌지 않았다.
다만 이 순서가 아니었다면 CSV를 다 만들고도 "여전히 버튼이 안 눌린다"는 보고를 다시 받았을 것이다.
실제로 작업 1(리플렉션 매핑 근거가 된 "복사 지점 5곳")이 작업 3의 설계를 바꾼 쪽이었다.

## 남은 작업 (이 계획서 범위 밖)

- **실기 확인**: 팬 스텝이 실제 D350에 기대한 회전수를 쓰는지, Export한 CSV를 현장 PC 엑셀에서 열고
  편집해 다시 Import했을 때 왕복이 유지되는지. 단위 테스트는 우리 파서만 검증한다 — **엑셀이 실제로
  무엇을 저장하는지는 검증하지 못한다.**
- 별건: 작업 트리에 흑체 판독을 `PlcStatus` 스냅샷 대신 `BlackBodyController` 직접 호출로 바꾸는
  손수정이 `DashboardViewModel`·`StatusMonitorViewModel`에 남아 있다(이 계획서와 무관, 미커밋).
  UI 스레드에서 `.Wait()`를 부르고 있어 `4229732`가 고친 폴링 지연과 같은 계열의 위험이 있다.

---

## 참고

- 기존 Import/Export 설계 이력: `docs/01-plan/features/recipe-backup-restore.plan.md`
- 레시피 실행 흐름: `HeatingCameraSystem.Master/Services/RecipeEngine.cs`
- 팬 PLC 계약: `PlcXgtClient.SetFanSpeedAsync` (`D350`, x100, 10.00~60.00Hz)
