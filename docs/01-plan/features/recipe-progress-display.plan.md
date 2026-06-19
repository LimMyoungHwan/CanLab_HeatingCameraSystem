# recipe-progress-display Planning Document

> **Summary**: Recipe 실행 진행률을 Dashboard에 실시간 표시 (ProgressBar + 단계 정보)
>
> **Project**: HeatingCameraSystem
> **Date**: 2026-06-19
> **Status**: Draft

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | Recipe 실행 시 "실행 중" 텍스트만 표시 — 현재 단계, 전체 진행률 확인 불가 |
| **Solution** | `IProgress<RecipeProgress>` 패턴으로 RecipeEngine → DashboardViewModel → ProgressBar 실시간 전달 |
| **Function/UX Effect** | ProgressBar + 현재 단계명 + N/M 스텝 카운터 실시간 업데이트 |
| **Core Value** | 운영자가 레시피 진행 상황을 즉시 파악, 완료 예상 가능 |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 레시피 실행 중 진행 상태 불투명 |
| **WHO** | Master PC 운영자 |
| **RISK** | RecipeEngine 내부 루프에서 UI 스레드 업데이트 — Dispatcher 처리 필요 |
| **SUCCESS** | 각 스텝 전환 시 ProgressBar + 단계명 즉시 갱신 |
| **SCOPE** | RecipeProgress 모델 → RecipeEngine 파라미터 → DashboardViewModel → XAML |

---

## 1. Overview

### 1.1 현재 상태

- `RecipeEngine.ExecuteRecipeAsync(recipe, ct)` → 진행 콜백 없음
- `DashboardViewModel.RecipeStatus` = 문자열 ("실행 중", "완료", "중지됨")
- START 버튼 옆에 상태 텍스트만 존재, ProgressBar 없음

### 1.2 목표

- `RecipeProgress` 모델: `CurrentStep`, `TotalSteps`, `CurrentPhase`
- `ExecuteRecipeAsync`에 `IProgress<RecipeProgress>` 파라미터 추가
- DashboardView: START 버튼 아래에 ProgressBar + 단계명

---

## 2. Scope

### 2.1 In Scope

- [x] `RecipeProgress` 모델 (Core/Models)
- [x] `RecipeEngine.ExecuteRecipeAsync` 시그니처에 `IProgress<RecipeProgress>?` 추가
- [x] 각 phase 진입 시 progress.Report() 호출 (챔버 안정화 / 서보 이동 / BB 안정화 / 캡처 / 완료)
- [x] `DashboardViewModel`: Progress 핸들러 + ProgressBar 바인딩 프로퍼티
- [x] `DashboardView.xaml`: ProgressBar + 단계명 TextBlock
- [x] 기존 테스트 시그니처 수정

### 2.2 Out of Scope

- 예상 남은 시간 계산
- 개별 스텝 상세 로그 UI
- 진행률 영구 저장

---

## 3. Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-01 | `RecipeProgress` 모델: CurrentStep(int), TotalSteps(int), CurrentPhase(string) | High |
| FR-02 | `RecipeEngine.ExecuteRecipeAsync` 시그니처 변경: `IProgress<RecipeProgress>?` 파라미터 추가 | High |
| FR-03 | 챔버 안정화 phase 진입 시 Report("챔버 안정화", 0, N) | High |
| FR-04 | 각 RecipeStep 시작 시 Report("서보 이동", i, N) → "BB 안정화" → "캡처" 순 | High |
| FR-05 | 전체 완료 시 Report("완료", N, N) | High |
| FR-06 | DashboardViewModel: `RecipeProgressValue`(double 0~100) + `RecipePhaseText`(string) | High |
| FR-07 | DashboardView: ProgressBar + 단계명 TextBlock (START 버튼 아래) | High |
| FR-08 | 기존 `RecipeEngineTests` 시그니처 수정 (null progress 전달) | High |

---

## 4. Success Criteria

- [ ] ProgressBar 0→100% 레시피 완료 시 도달
- [ ] 각 phase 텍스트 실시간 갱신 ("챔버 안정화" → "서보 이동" → ...)
- [ ] `dotnet build` 0 errors
- [ ] `dotnet test` 기존 + 신규 통과

---

## 5. Impact Analysis

| Resource | Change |
|----------|--------|
| `RecipeEngine.ExecuteRecipeAsync` | 시그니처 변경 (파라미터 추가) |
| `DashboardViewModel.StartRecipeAsync` | progress 핸들러 전달 |
| `RecipeEngineTests` | `ExecuteRecipeAsync` 호출부 수정 |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-06-19 | Initial draft |
