# recipe-fan-speed-and-csv-io Planning Document

> **Summary**: 레시피 스텝에 팬(블로워) 속도 제어 추가 + Import/Export를 CSV로 전환
>
> **Project**: HeatingCameraSystem
> **Date**: 2026-09-23
> **Status**: Draft — 착수 전
> **선행 커밋**: `45211b8` (카메라 Y16/Raw 저장)

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

### 손댈 곳

- `RecipeEditorViewModel`에 상태 메시지 속성(없으면 신설) → Export/Import 성공·실패·미선택 모두 표시
- `Debug.WriteLine` → 화면 메시지 + `AlarmSink` 병행 검토

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

### 손댈 곳

- `Core` 또는 `Master/Services`에 `RecipeCsvSerializer`(왕복 단위 테스트 가능하도록 UI와 분리)
- `RecipeEditorViewModel.ExportRecipe/ImportRecipe` — 필터를 CSV로, Import는 `.csv`/`.json` 분기
- 테스트 — 모든 `RecipeStepKind`를 담은 레시피의 왕복 동일성

---

## 순서 제안

1. **작업 2 먼저** — 상태 메시지를 붙여 "왜 안 되는지" 눈으로 확인. 여기서 원인이 드러나면 작업 3의 설계가 달라질 수 있다.
2. **작업 1** — 팬 스텝. 독립적이고 PLC 쪽이 이미 준비돼 있어 가장 빠르다.
3. **작업 3** — CSV. 팬 스텝이 먼저 들어가야 CSV 스키마를 한 번만 정한다(나중에 넣으면 컬럼을 또 바꿔야 함).

> 작업 1을 3보다 먼저 하는 이유가 이것이다. 순서를 바꾸면 CSV 컬럼을 두 번 정하게 된다.

---

## 참고

- 기존 Import/Export 설계 이력: `docs/01-plan/features/recipe-backup-restore.plan.md`
- 레시피 실행 흐름: `HeatingCameraSystem.Master/Services/RecipeEngine.cs`
- 팬 PLC 계약: `PlcXgtClient.SetFanSpeedAsync` (`D350`, x100, 10.00~60.00Hz)
