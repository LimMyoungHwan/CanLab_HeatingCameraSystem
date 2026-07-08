# camera-model-select Completion Report

> **Status**: Complete (해상도 실측 검증만 카메라 입고 후 이월)
>
> **Project**: HeatingCameraSystem
> **Completion Date**: 2026-07-08
> **PDCA Cycle**: #3

---

## Executive Summary

### 1.1 Project Overview

| Item | Content |
|------|---------|
| Feature | camera-model-select (카메라 모델별 해상도 파일 기반 설정) |
| Start Date | 2026-06-29 (PRD 초안) |
| End Date | 2026-07-08 (스코프 확정 → Plan/Design/Do/Check 전부 완료) |
| Duration | 3세션 (PRD 초안 → 스코프 재협의 2회 → 구현) |

### 1.2 Results Summary

```
┌──────────────────────────────────────────────┐
│  Match Rate: 100%                             │
├──────────────────────────────────────────────┤
│  ✅ FR 완료:    6 / 6                         │
│  ✅ SC 충족:    5 / 5                         │
│  ✅ 테스트:    69 / 69 통과                   │
│  ✅ E2E:       PASS (회귀 없음)               │
│  ⏳ 실물 검증:  카메라 입고 후 (다음 주)       │
└──────────────────────────────────────────────┘
```

### 1.3 Value Delivered

| Perspective | Content |
|-------------|---------|
| **Problem** | 카메라 모델(AISEN 640×480 / FPV 320×240 등)마다 센서 해상도가 다른데 시스템에 대응 방법이 없었음 |
| **Solution** | `agent.json.CameraModel` + `CameraModels\{모델}.json` 파일 기반 해상도 설정. 신규 모델 = JSON 파일 하나 |
| **Function/UX Effect** | 코드 재빌드·Master UI 변경 없이 JSON 파일 배치만으로 신규 카메라 모델 대응 |
| **Core Value** | 기존 `hardware.json`/`agent.json` 파일 기반 설정 관례와 완전히 일관된 확장 |

---

### 1.4 Success Criteria 최종 상태

| # | 기준 | 상태 |
|---|------|:----:|
| SC-01 | `CameraModelSpec` JSON 왕복 직렬화 | ✅ |
| SC-02 | 미존재 모델 → 예외 없이 null + 경고 | ✅ |
| SC-03 | 해상도 지정 후 크래시 없음 | ✅ |
| SC-04 | 미지정 시 기존 동작 100% 동일 | ✅ |
| SC-05 | 빌드 0/0, 69/69 테스트 통과 | ✅ |

**Success Rate**: 5/5 (100%)

---

### 1.5 Decision Record (스코프 변경 이력)

| 시점 | 결정 | 이유 |
|------|------|------|
| v1.0 (2026-06-29) | `CameraModel` 프로필(enum) + Master UI/LiteDB 방식 초안 | explore 분석 기반 최초 제안 |
| v1.1 (2026-07-08) | 고정 enum 폐기, 해상도 사용자 직접 입력 방향 | 사용자: "해상도는 사용자 지정이 되어야해, 모델마다 다를꺼라서" |
| v1.2 (2026-07-08) | Master UI/LiteDB 폐기 → 파일 기반(JSON) 확정 | 사용자: "카메라설정을 ini파일로... 카메라가 새로 추가될때마다 내가 ini만들면 되니까" |
| Design | 파일 포맷 JSON(INI 아님), `agent.json.CameraModel`, `CameraModels\` 폴더 | 기존 JSON 컨벤션 일관성 — 사용자 확인 |
| Check G-01 | `CameraModelSpec.Load()` public static (Design의 private+InternalsVisibleTo 대신) | 더 단순, 신규 테스트 인프라 불필요 — 수용 |

---

## 2. Related Documents

| Phase | Document |
|-------|----------|
| PM | [camera-model-select.prd.md](../../00-pm/camera-model-select.prd.md) (v1.2) |
| Plan | [camera-model-select.plan.md](../../01-plan/features/camera-model-select.plan.md) |
| Design | [camera-model-select.design.md](../../02-design/features/camera-model-select.design.md) |
| Check | [camera-model-select.analysis.md](../../03-analysis/features/camera-model-select.analysis.md) |
| Report | 현재 문서 |

---

## 3. Completed Items

### 3.1 Functional Requirements (6/6)

| ID | 요구사항 | 상태 |
|----|----------|------|
| FR-01 | `AgentConfig.CameraModel` | ✅ |
| FR-02 | `CameraModelSpec` 모델 | ✅ |
| FR-03 | Agent 시작 시 모델 스펙 로드 | ✅ |
| FR-04 | 예외 없는 fallback | ✅ |
| FR-05 | `CameraCaptureService` 해상도 적용 | ✅ |
| FR-06 | 예시 모델 JSON 2개 | ✅ |

### 3.2 신규 파일 (6개)

| 파일 | 역할 |
|------|------|
| `Core/Models/CameraModelSpec.cs` | 모델 스펙 + 로더 |
| `Agent/CameraModels/AISEN.json` | 640×480 예시 |
| `Agent/CameraModels/FPV.json` | 320×240 예시 |
| `Tests/CameraModelSpecTests.cs` | 로더 단위 테스트 4건 |
| `docs/00-pm/camera-model-select.prd.md` | PRD (v1.2) |
| `docs/01-plan`, `docs/02-design`, `docs/03-analysis` | PDCA 문서 |

---

## 4. Incomplete Items

| 항목 | 이유 | 우선순위 |
|------|------|----------|
| 실물 카메라로 `VideoCapture.FrameWidth/Height` 실제 적용 검증 | 카메라 다음 주 입고 예정 | High |
| 셔터 바이트 프로토콜(`04 00 01...` vs `43 4C 30 01...`)/BaudRate 검증 | 별도 항목, 카메라 실물 필요 (`mem:camera-connection-verification`) | High |

---

## 5. Quality Metrics

| 지표 | 목표 | 최종 |
|------|------|------|
| Match Rate | ≥ 90% | **100%** |
| 빌드 오류/경고 | 0 | **0 / 0** |
| 테스트 통과 | 64 유지 | **69/69** |
| E2E 회귀 | 없음 | **없음** (exit 0) |

---

## 6. Lessons Learned

### Keep (잘된 점)

- **스코프 반복 협의**: PRD v1.0(enum) → v1.1(사용자 입력) → v1.2(파일 기반) — 매 반복마다 사용자 발언을 그대로 인용해 결정 근거를 문서에 남겨 추적 용이
- **기존 컨벤션 재사용**: `agent.json` 패턴을 그대로 확장 — 신규 인프라(DB/UI/NATS) 없이 최소 변경으로 완결
- **인터페이스 무변경**: `ICameraCaptureService` 시그니처를 건드리지 않아 기존 Mock 테스트·FakeCameraCaptureService 영향 0

### Problem (개선 필요)

- 최초 PRD(v1.0)에서 Master UI + LiteDB 방식을 먼저 제안했다가 두 번 스코프가 뒤집힘 — 사용자에게 "어디까지 하이레벨로만 제안하고 세부는 확인 후 진행"할지 더 일찍 물었으면 왕복 줄었을 것

### Try (다음에 시도)

- 카메라 실물 입고 후 SC-03(실제 해상도 적용) 실측 체크리스트 준비

---

## 7. Next Steps

| 항목 | 우선순위 |
|------|----------|
| 카메라 실물 입고 후 해상도 실측 + 셔터 프로토콜/BaudRate 검증 | 🔴 다음 주 |
| #18 열화상/RGB 자동판별, #14 Recipe 소요시간 추정, NATS 인증 | 🟡 Medium |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-07-08 | 최초 작성 — Match Rate 100%, 5/5 SC, 69/69 테스트 |
