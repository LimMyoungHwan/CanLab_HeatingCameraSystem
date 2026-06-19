# recipe-progress-display Design Document

> **Summary**: IProgress<RecipeProgress> → ProgressBar + 단계명 실시간 표시
>
> **Project**: HeatingCameraSystem
> **Date**: 2026-06-19
> **Selected Architecture**: Option C (Pragmatic)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 레시피 실행 중 진행 상태 불투명 |
| **WHO** | Master PC 운영자 |
| **RISK** | UI 스레드 업데이트 안전성 |
| **SUCCESS** | 각 스텝 전환 시 ProgressBar + 단계명 즉시 갱신 |
| **SCOPE** | RecipeProgress 모델 → RecipeEngine → DashboardViewModel → XAML |

---

## 1. Data Model

### RecipeProgress (Core/Models)

```csharp
public class RecipeProgress
{
    public int    CurrentStep  { get; set; }
    public int    TotalSteps   { get; set; }
    public string CurrentPhase { get; set; } = string.Empty;
}
```

---

## 2. RecipeEngine 변경

### 시그니처

```csharp
public async Task ExecuteRecipeAsync(
    Recipe recipe,
    CancellationToken ct = default,
    IProgress<RecipeProgress>? progress = null)
```

### Report 호출 위치

| Phase | CurrentPhase | CurrentStep |
|---|---|---|
| 챔버 온도 안정화 시작 | `"챔버 안정화"` | 0 |
| Step[i] 서보 이동 시작 | `"서보 이동 ({i+1}/{N})"` | i |
| Step[i] BB 안정화 시작 | `"BB 안정화 ({i+1}/{N})"` | i |
| Step[i] 캡처 전송 | `"캡처 ({i+1}/{N})"` | i |
| 전체 완료 | `"완료"` | N |

---

## 3. DashboardViewModel 변경

### 프로퍼티 추가

```csharp
[ObservableProperty] private double _recipeProgressValue = 0;   // 0~100
[ObservableProperty] private string _recipePhaseText = string.Empty;
```

### StartRecipeAsync 수정

```csharp
var progress = new Progress<RecipeProgress>(p =>
{
    RecipeProgressValue = p.TotalSteps > 0
        ? (double)p.CurrentStep / p.TotalSteps * 100
        : 0;
    RecipePhaseText = p.CurrentPhase;
});

await AppServices.RecipeEngine.ExecuteRecipeAsync(SelectedRecipe, _recipeCts.Token, progress);
```

---

## 4. DashboardView.xaml 변경

START 버튼과 STOP/CONFIG 사이에 삽입:

```xml
<ProgressBar Value="{Binding RecipeProgressValue}" Height="6"
             Foreground="#4ae183" Background="#1a1f2e" BorderThickness="0"
             Margin="0,8"/>
<TextBlock Text="{Binding RecipePhaseText}" Foreground="#bac9c9"
           FontSize="11" Margin="0,0,0,8"/>
```

---

## 5. 구현 순서

```
[1] Core/Models/RecipeProgress.cs — 모델 생성
[2] Master/Services/RecipeEngine.cs — 시그니처 + Report 호출
[3] Master/ViewModels/DashboardViewModel.cs — 프로퍼티 + progress 핸들러
[4] Master/Views/DashboardView.xaml — ProgressBar + TextBlock
[5] Tests — RecipeEngineTests 시그니처 수정
```

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-06-19 | Initial draft (Option C) |
