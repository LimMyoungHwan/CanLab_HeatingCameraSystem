# recipe-backup-restore Planning Document

> **Summary**: RecipeEditorView에서 레시피 JSON Export/Import 기능 추가
>
> **Project**: HeatingCameraSystem
> **Date**: 2026-06-19
> **Status**: Draft

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | 레시피 백업/복원 수단 없음 — DB 파일 직접 복사만 가능 |
| **Solution** | RecipeEditorView에 Export/Import 버튼 → JSON 파일로 저장/불러오기 |
| **Function/UX Effect** | 선택한 레시피를 JSON으로 내보내거나, JSON에서 불러와 DB에 저장 |
| **Core Value** | 레시피 이동·공유·백업이 파일 1개로 가능 |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 레시피 백업/복원/공유 수단 부재 |
| **WHO** | Master PC 운영자 |
| **RISK** | Import 시 ID 충돌 — 새 ID 발급으로 해결 |
| **SUCCESS** | Export → Import 왕복 시 레시피 내용 동일 |
| **SCOPE** | RecipeEditorViewModel에 Export/Import 커맨드 + XAML 버튼 |

---

## Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-01 | Export: 선택된 레시피를 JSON 파일로 저장 (SaveFileDialog) | High |
| FR-02 | Import: JSON 파일에서 레시피 로드 → 새 ID 발급 → DB 저장 → 목록 추가 | High |
| FR-03 | RecipeEditorView에 Export/Import 버튼 추가 | High |
| FR-04 | Import 시 기존 ID와 충돌 방지 (새 ID 발급) | High |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-06-19 | Initial draft |
