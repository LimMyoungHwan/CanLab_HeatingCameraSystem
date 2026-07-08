# PRD — 카메라 모델 선택 기능 (camera-model-select)

**Feature**: `camera-model-select`
**Date**: 2026-06-29
**Status**: PM Analysis

---

## Executive Summary

| 관점 | 내용 |
|------|------|
| **Problem** | 벤더 참고 코드(`참고/AISEN_CODE`, `참고/FPV_code`)가 서로 다른 두 카메라 모델(AISEN `CBS014d` 640×480 / FPV `FPV024d` 320×240)을 지원하는 것으로 확인됨. 현재 C# 시스템(`CameraDevice`, `CameraSerialSettings`)에는 모델 개념이 전혀 없어, 두 모델을 함께 운용할 수 없다. |
| **Solution** | `CameraModel` 프로필(모델 ID + 센서 해상도)을 Core에 정의하고, 카메라별로 모델을 선택·저장하는 기능을 Master UI(DevicesView)와 Agent 설정에 추가한다. |
| **Functional UX Effect** | 운영자가 Master DevicesView에서 카메라마다 모델(AISEN/FPV)을 선택 → Agent 캡처 해상도가 모델에 맞게 자동 적용됨. |
| **Core Value** | 서로 다른 센서 규격의 카메라를 같은 시스템에서 오류 없이 혼용 가능. |

---

## 1. 문제 정의

### 1.1 참고 코드 분석 결과 (2026-06-29, explore 분석)

`참고/AISEN_CODE`와 `참고/FPV_code`는 시리얼 프로토콜(`data_packet.yaml`, `serial_code.py`)은 **100% 동일**하지만, `main.py`/`two_point_viewer.py`에서 다음이 모델별로 다름:

| 항목 | AISEN (`CBS014d`) | FPV (`FPV024d`) |
|------|------|------|
| 센서 해상도 | 640×480 | 320×240 (표시 시 640×480로 업스케일) |
| 온도 구간(TempRange) | LOW/MID/HIGH (3단계) | LOW/MID (2단계) |
| Bias 레지스터 기본값(TINT/CINT/GSK_MSB/GFID) | 모델 고유값 | 모델 고유값 (전부 다름) |
| Bias 목표치, 챔버 온도표 | 모델 고유값 | 모델 고유값 |

### 1.2 현재 시스템(C#) 대비 실제 필요 범위

현재 C# 시스템은 **셔터 open/close + 프레임 캡처**만 수행하며, bias 자동조정·TINT/CINT 레지스터 설정·dead-pixel 보정 등 공장 캘리브레이션 기능은 구현되어 있지 않다(참고 코드는 벤더 제조/캘리브레이션 툴, 우리 시스템은 운영 툴).

→ 이번 기능은 **모델 식별 + 센서 해상도**만 다룬다. Bias/레지스터 값은 범위 외(§5)로 명시.

### 1.3 미해결 리스크 (물리 연결 필요)

`SerialShutterController.cs`의 현재 셔터 바이트(`04 00 01...`)는 참고 코드의 두 모델 프로토콜(`43 4C 30 01...`) 중 **어느 쪽과도 일치하지 않음**. 이 기능은 이 불일치를 해결하지 않으며, 실 카메라 연결 후 별도 검증 필요 (`mem:camera-connection-verification` 참조).

---

## 2. 사용자 / 이해관계자

| 역할 | 니즈 |
|------|------|
| 운영자 (Master PC) | 현장에 설치된 카메라가 AISEN인지 FPV인지 UI에서 지정 |
| 개발자 | 신규 모델 추가 시 코드 한 곳(모델 레지스트리)만 수정하면 되는 구조 |

---

## 3. 요구사항 (MVP)

### 필수 (Must Have)

| ID | 요구사항 |
|----|---------|
| FR-01 | Core에 `CameraModel` 프로필 정의: `ModelId`("AISEN_CBS014d", "FPV_FPV024d"), `DisplayName`, `SensorWidth`, `SensorHeight` |
| FR-02 | 모델 레지스트리(정적 목록 또는 enum): 최소 AISEN/FPV 2종 내장, 신규 모델 추가 용이한 구조 |
| FR-03 | `CameraDevice`(또는 `CameraSerialSettings`)에 `CameraModelId` 필드 추가 — 카메라별 모델 저장 (기본값: 미지정 시 기존 동작 유지) |
| FR-04 | Master DevicesView: 카메라별 모델 선택 드롭다운 추가 → 기존 Repository 통해 LiteDB 저장 |
| FR-05 | Agent: 카메라 모델에 따라 OpenCV `VideoCapture` 해상도(Width/Height)를 모델의 `SensorWidth/SensorHeight`로 설정 |
| FR-06 | 기존 `CameraSerialSettings` 저장/전달 흐름(NATS `master.config.serial.{AgentId}` 등)과 충돌 없이 독립적으로 동작 |

### 선택 (Nice to Have)

| ID | 요구사항 |
|----|---------|
| NFR-01 | 미지정(기본) 카메라는 기존 해상도 동작 그대로 유지 (하위 호환) |
| NFR-02 | 회귀 테스트: 모델 필드 추가 후 기존 22개+ 테스트 전부 통과 |

---

## 4. 성공 기준

| 기준 | 측정 방법 |
|------|---------|
| Master UI에서 카메라별 모델 선택·저장 가능 | 수동 검증 (DevicesView) |
| Agent가 지정된 모델의 해상도로 `VideoCapture` 오픈 | 단위 테스트 + 로그 확인 |
| 모델 미지정 카메라는 기존 동작과 동일 | 회귀 테스트 |
| `dotnet build` 0 errors / 0 warnings, 기존 테스트 전부 통과 | CI 명령 |

---

## 5. 범위 외 (Out of Scope)

- Bias 자동조정, TINT/CINT/GSK_MSB/GFID 레지스터 설정 (현재 C# 시스템에 없는 기능 — 별도 PDCA 필요 시 신규 기능으로 분리)
- Dead-pixel 보정, NUC 모드 설정
- 셔터 바이트 프로토콜(`04 00 01...` vs `43 4C 30 01...`) 재검증 — 실 카메라 연결 후 별도 확인 (blocked, `mem:camera-connection-verification`)
- `biassetting` 외부 모듈 리버스엔지니어링 (참고 코드에 소스 없음)
- 3번째 이상 신규 카메라 모델 데이터 수집 (현재 확인된 2종만 내장)

---

## 6. 기술 제약

- `Nullable=enable`, 경고 억제 금지
- 기존 `CameraDevice`/`CameraSerialSettings` LiteDB 스키마 확장 시 기존 데이터 마이그레이션 없이 읽히도록(신규 필드는 nullable 또는 기본값 처리)
- 최소 변경 원칙 — 기존 셔터/캡처 로직 리팩터링 금지, 모델 필드만 추가

---

## 7. 리스크

| 리스크 | 대응 |
|--------|------|
| 실제 카메라가 3종 이상일 가능성 (참고 코드는 2종만 확보) | 레지스트리 구조를 확장 가능하게 설계 (enum보다 딕셔너리/설정 기반 권장) |
| 해상도 외 실제로 더 필요한 모델별 설정이 있을 수 있음 (bias 등, 범위 외로 뺐지만 추후 요구될 수 있음) | 프로필 구조를 확장 가능하게 설계, 향후 필드 추가 용이하도록 |
| Agent가 실제 카메라 없이 시뮬레이션 모드로 동작 시 해상도 설정 영향 | `SimulateAgentMode`(SC-12) 경로는 FakeCameraCaptureService 사용 — 영향 없음 확인 필요 |

---

## 8. 다음 단계

1. `docs/01-plan/features/camera-model-select.plan.md` — Plan 작성 (Repository/NATS 영향 분석)
2. `docs/02-design/features/camera-model-select.design.md` — Design 작성 (모델 레지스트리 구조, UI 배치 결정)
3. 사용자 확인 후 Do 진행
