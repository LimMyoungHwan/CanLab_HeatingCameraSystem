# PRD — 카메라 모델 파일 기반 해상도 설정 (camera-model-select)

**Feature**: `camera-model-select`
**Date**: 2026-06-29 (v1.0), 2026-07-08 (v1.2 — 파일 기반 설계 확정)
**Status**: PM Analysis — 스코프/구조 확정됨, Plan 진행 가능

---

## Executive Summary

| 관점 | 내용 |
|------|------|
| **Problem** | 벤더 참고 코드(`참고/AISEN_CODE`, `참고/FPV_code`)가 서로 다른 두 카메라 모델(AISEN `CBS014d` 640×480 / FPV `FPV024d` 320×240)을 지원하는 것으로 확인됨. 현재 C# 시스템에는 해상도 개념이 전혀 없어 모델별 대응 불가. |
| **Solution** | 모델별 해상도를 **JSON 파일**(`CameraModels\{ModelName}.json`)로 정의. Agent는 자기 `agent.json`의 `CameraModel` 필드로 모델명을 알아내고, 해당 JSON 파일을 읽어 캡처 해상도를 적용한다. 신규 모델 추가 = JSON 파일 하나 새로 놓는 것으로 끝 — 코드/UI 변경 없음. |
| **Functional UX Effect** | 운영자(또는 개발자)가 `CameraModels\AISEN.json` 같은 파일을 만들어 배치 → `agent.json`에 `"CameraModel": "AISEN"` 지정 → Agent 재시작 시 해당 해상도로 캡처. |
| **Core Value** | 신규 카메라 모델이 나와도 코드 재빌드·Master UI 변경 없이 JSON 파일 하나로 즉시 대응. 기존 `hardware.json`/`agent.json` 파일 기반 설정 관례와 일관됨. |

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

→ 이번 기능은 **모델별 센서 해상도**만 다룬다. Bias/레지스터 값은 범위 외(§5)로 명시.

### 1.3 스코프/구조 확정 (사용자 확인, 2026-07-08)

> "해상도는 사용자 지정이 되어야해, 모델마다 다를꺼라서" → "카메라설정을 ini파일로 해서 특정파일에 넣으면 읽어와서 처리하는 방식으로... 카메라가 새로 추가될때마다 내가 ini만들면 되니까"

**확정된 구조** (Master UI/LiteDB 방식 폐기, 파일 기반으로 전환):

| 결정 | 선택 | 이유 |
|------|------|------|
| 파일 포맷 | **JSON** (INI 아님) | 프로젝트 전체가 `hardware.json`/`agent.json`/`manager-settings.json` 등 JSON 컨벤션. INI는 .NET 표준 파서 없어 신규 의존성 필요, JSON은 기존 `System.Text.Json` 재사용 |
| 모델 지정 위치 | **`agent.json`에 `CameraModel` 필드 추가** | 기존 `agent.json`(AgentId, NatsUrl 등)과 동일 패턴. Agent PC 1대 = 카메라 1대 매핑 구조와 일치 |
| 모델 스펙 파일 위치 | **Agent exe 옆 `CameraModels\` 폴더** | `hardware.json`/`agent.json`과 같은 "설치 폴더 기준" 관례 |

**동작 흐름:**
```
Agent 시작
  → agent.json 읽기 → CameraModel = "AISEN"
  → CameraModels\AISEN.json 읽기 → { "Width": 640, "Height": 480 }
  → VideoCapture 오픈 시 해당 Width/Height 적용
  → CameraModels\AISEN.json 없으면 → 경고 로그 + 카메라 기본 해상도로 fallback
```

신규 모델 추가 시: `CameraModels\{새모델명}.json` 파일 하나 작성 + `agent.json`의 `CameraModel` 값 변경만으로 끝. 코드 재빌드·Master UI 작업 불필요.

### 1.4 미해결 리스크 (물리 연결 필요)

`SerialShutterController.cs`의 현재 셔터 바이트(`04 00 01...`)는 참고 코드의 두 모델 프로토콜(`43 4C 30 01...`) 중 **어느 쪽과도 일치하지 않음**. 이 기능은 이 불일치를 해결하지 않으며, 카메라 실물이 다음 주 입고 예정 — 입고 후 별도 검증 (`mem:camera-connection-verification` 참조).

---

## 2. 사용자 / 이해관계자

| 역할 | 니즈 |
|------|------|
| 개발자/설치 담당자 | 신규 카메라 모델 도입 시 JSON 파일 하나 작성만으로 대응 |
| Agent 운영 환경 | 재시작 시 `agent.json` + `CameraModels\*.json` 읽어 자동으로 올바른 해상도 적용 |

---

## 3. 요구사항 (MVP)

### 필수 (Must Have)

| ID | 요구사항 |
|----|---------|
| FR-01 | `AgentConfig`(`agent.json` 역직렬화 모델, Core)에 `CameraModel` 필드 추가 (string, nullable — 미지정 시 기존 동작 유지) |
| FR-02 | `CameraModelSpec` 모델 정의(Core): `Width`, `Height` — `CameraModels\{ModelName}.json`으로 직렬화되는 최소 구조 |
| FR-03 | Agent 시작 시: `CameraModel` 필드가 있으면 `CameraModels\{CameraModel}.json`을 exe 옆 폴더에서 읽어 `CameraModelSpec` 로드 |
| FR-04 | 파일이 없거나 파싱 실패 시: 경고 로그 남기고 카메라 기본 해상도로 fallback (예외로 죽지 않음) |
| FR-05 | Agent 캡처 서비스(OpenCV `VideoCapture`): 로드된 `CameraModelSpec`이 있으면 `Width`/`Height` 프로퍼티 설정 후 오픈 |
| FR-06 | `CameraModels\` 폴더에 예시 파일 2개 배치: `AISEN.json`({Width:640, Height:480}), `FPV.json`({Width:320, Height:240}) |

### 선택 (Nice to Have)

| ID | 요구사항 |
|----|---------|
| NFR-01 | `CameraModel` 미지정 카메라는 기존 동작과 100% 동일 (하위 호환) |
| NFR-02 | 회귀 테스트: 필드 추가 후 기존 64개 테스트 전부 통과 |

---

## 4. 성공 기준

| 기준 | 측정 방법 |
|------|---------|
| `agent.json`에 `CameraModel` 지정 → `CameraModels\{모델}.json` 읽어 해상도 적용 | 단위 테스트 + 로그 확인 |
| 모델 JSON 파일 없음/파싱 실패 시 예외 없이 기본 해상도로 fallback | 단위 테스트 |
| `CameraModel` 미지정 카메라는 기존 동작과 동일 | 회귀 테스트 |
| `dotnet build` 0 errors / 0 warnings, 기존 테스트 전부 통과 | CI 명령 |

---

## 5. 범위 외 (Out of Scope)

- **Master UI 변경** — 모델 선택은 파일 기반, DevicesView 등 UI 작업 없음
- **LiteDB/NATS 관련 변경** — 기존 `CameraSerialSettings`/`CameraDevice` 스키마·NATS 흐름 무변경
- Bias 자동조정, TINT/CINT/GSK_MSB/GFID 레지스터 설정 (현재 C# 시스템에 없는 기능)
- Dead-pixel 보정, NUC 모드 설정
- 셔터 바이트 프로토콜(`04 00 01...` vs `43 4C 30 01...`) 재검증 — 카메라 실물 입고(다음 주) 후 별도 확인
- `biassetting` 외부 모듈 리버스엔지니어링 (참고 코드에 소스 없음)

---

## 6. 기술 제약

- `Nullable=enable`, 경고 억제 금지
- `System.Text.Json`만 사용 (신규 NuGet 패키지 도입 없음)
- 최소 변경 원칙 — 기존 셔터/캡처 로직 리팩터링 금지, 모델 로드 단계만 추가
- `agent.json`은 최초 실행 시 자동 생성됨(`AGENTS.md`) — `CameraModel` 필드도 기본값(null/빈문자열)으로 자동 포함되도록

---

## 7. 리스크

| 리스크 | 대응 |
|--------|------|
| `CameraModels\` 폴더/파일이 배포 시 누락 | FR-04 fallback으로 크래시 방지, 로그로 원인 확인 가능 |
| 잘못된 Width/Height 값(카메라가 실제 지원 안 함) | `VideoCapture.Set()` 실패는 OpenCV 레벨에서 조용히 무시되거나 실제 카메라 기본값 유지 — 로그에 요청값 남겨 추적 가능하게 |
| 카메라 실물 입고 전까지 실제 해상도 값 검증 불가 | 다음 주 입고 후 실측 검증 (§1.4) |

---

## 8. 다음 단계

1. `docs/01-plan/features/camera-model-select.plan.md` — Plan 작성
2. `docs/02-design/features/camera-model-select.design.md` — Design 작성 (정확한 파일 경로 해석 로직, 캡처 서비스 통합 지점)
3. Do 진행

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-06-29 | 초안 — `CameraModel` 프로필(enum) + Master UI/LiteDB 방식 제안, 스코프 확인 대기 |
| 1.1 | 2026-07-08 | 사용자 확인 — 고정 enum 폐기, 해상도 사용자 직접 입력 방향으로 결정 |
| 1.2 | 2026-07-08 | 사용자 확인 — Master UI/LiteDB 방식 폐기, 파일 기반(JSON, `agent.json`+`CameraModels\*.json`) 구조로 최종 확정 |
