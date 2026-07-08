# Plan — 카메라 모델 파일 기반 해상도 설정 (camera-model-select)

**Feature**: `camera-model-select`
**Phase**: Plan
**Date**: 2026-07-08
**PRD**: `docs/00-pm/camera-model-select.prd.md` (v1.2)

---

## Executive Summary

| 관점 | 내용 |
|------|------|
| **Problem** | 카메라 모델(AISEN 640×480 / FPV 320×240 등)마다 센서 해상도가 다른데, 현재 Agent는 해상도 개념이 없어 대응 불가. |
| **Solution** | `agent.json`에 `CameraModel`(문자열) 필드 추가. Agent가 `CameraModels\{CameraModel}.json`을 읽어 `Width`/`Height`를 얻고, `CameraCaptureService`가 `VideoCapture` 오픈 시 적용. |
| **Functional UX Effect** | 신규 모델 도입 시 JSON 파일 하나 배치 + `agent.json` 필드 값 변경만으로 대응. 코드/UI 변경 불필요. |
| **Core Value** | 파일 기반 확장 — 기존 `hardware.json`/`agent.json` 컨벤션과 완전히 일관됨. |

---

## Context Anchor

| 항목 | 내용 |
|------|------|
| **WHY** | 참고 벤더 코드 분석 결과 모델별 해상도 상이 확인. 사용자가 "해상도는 사용자 지정" + "ini(→JSON) 파일로, 새 카메라 추가 시 파일만 만들면 되게" 요청. |
| **WHO** | 개발자/설치 담당자 (모델 JSON 작성), Agent 런타임 (자동 로드) |
| **RISK** | `CameraModels\` 파일 누락/오타 시 캡처 실패로 이어지면 안 됨 — 반드시 fallback |
| **SUCCESS** | `CameraModel` 지정 시 해당 해상도로 `VideoCapture` 오픈 확인(로그), 미지정/파일없음 시 기존 동작과 동일, 64개 기존 테스트 + 신규 테스트 전부 통과 |
| **SCOPE** | `Core/Config/AgentConfig.cs`, `Core/Models/CameraModelSpec.cs`(신규), `Agent/Program.cs`, `Agent/Services/CameraCaptureService.cs`. Master/LiteDB/NATS 변경 없음. |

---

## 1. 요구사항 (PRD FR-01~06 반영)

| ID | 요구사항 | 대응 파일 |
|----|---------|----------|
| FR-01 | `AgentConfig.CameraModel` (string?, 기본 null) 추가 | `Core/Config/AgentConfig.cs` |
| FR-02 | `CameraModelSpec` 모델 (`Width`, `Height`, 둘 다 `int`) | `Core/Models/CameraModelSpec.cs` (신규) |
| FR-03 | Agent 시작 시 `CameraModel` 있으면 `{ExeDir}\CameraModels\{CameraModel}.json` 로드 | `Agent/Program.cs` |
| FR-04 | 파일 없음/파싱 실패 시 경고 로그 + null 처리 (예외 전파 금지) | `Agent/Program.cs` |
| FR-05 | `CameraCaptureService` 생성자에 `int? width, int? height` 추가 — `InitializeCamera`에서 `VideoCapture.Set()` 호출 | `Agent/Services/CameraCaptureService.cs` |
| FR-06 | `CameraModels\AISEN.json`, `CameraModels\FPV.json` 예시 파일 커밋 | `HeatingCameraSystem.Agent/CameraModels/*.json` |

---

## 2. 성공 기준 (Success Criteria)

| SC-ID | 기준 | 검증 방법 |
|-------|------|---------|
| SC-01 | `CameraModelSpec` JSON 왕복 직렬화 정상 | xUnit |
| SC-02 | 존재하지 않는 모델명 지정 시 예외 없이 null 반환 + 경고 로그 | xUnit |
| SC-03 | `CameraCaptureService(storagePath, 640, 480)` 생성 후 `InitializeCamera` 호출 시 크래시 없음 (`VideoCapture.Set` 실패해도 무시) | xUnit |
| SC-04 | width/height 미지정(`null`) 시 기존 동작(Set 호출 안 함)과 100% 동일 | xUnit — 회귀 |
| SC-05 | `dotnet build` 0 errors/0 warnings, `dotnet test` 기존 64 + 신규 전부 통과 | CI |

---

## 3. 구현 범위 (Scope)

### 수정 파일

| 파일 | 변경 내용 |
|------|---------|
| `HeatingCameraSystem.Core/Config/AgentConfig.cs` | `CameraModel` 필드 추가 |
| `HeatingCameraSystem.Agent/Program.cs` | 모델 스펙 로드 함수 추가, `CameraCaptureService` 생성 시 전달 |
| `HeatingCameraSystem.Agent/Services/CameraCaptureService.cs` | 생성자 파라미터 추가, `InitializeCamera`에서 해상도 적용 |

### 신규 파일

| 파일 | 역할 |
|------|------|
| `HeatingCameraSystem.Core/Models/CameraModelSpec.cs` | `{ Width, Height }` — JSON 역직렬화 대상 |
| `HeatingCameraSystem.Agent/CameraModels/AISEN.json` | 예시: `{"Width":640,"Height":480}` |
| `HeatingCameraSystem.Agent/CameraModels/FPV.json` | 예시: `{"Width":320,"Height":240}` |
| `HeatingCameraSystem.Tests/CameraModelSpecTests.cs` | 로드/fallback 단위 테스트 |

### 수정 불가 (범위 외)

- `HeatingCameraSystem.Master/` — UI 변경 없음
- `HeatingCameraSystem.Protocols/` — NATS 변경 없음
- `ICameraCaptureService` 인터페이스 — 시그니처 무변경 (Mock 테스트 호환 유지, 최소 변경 원칙)
- `FakeCameraCaptureService` — 이미 고정 640×480 합성 이미지 생성, 이번 범위에서 변경 안 함

---

## 4. 리스크 및 대응

| 리스크 | 가능성 | 대응 |
|--------|--------|------|
| `CameraModels\` 폴더가 배포 시 exe와 함께 복사 안 됨 | 중 | `.csproj`에 `CopyToOutputDirectory` 설정 필요 (Design에서 확정) |
| `VideoCapture.Set()`이 예외를 던질 가능성 (OpenCvSharp 특정 버전) | 하 | try/catch로 감싸 로그만 남기고 계속 진행 |
| `CameraModel` 오타로 엉뚱한 파일 못 찾음 | 중 | FR-04 경고 로그에 시도한 전체 경로 포함 — 트러블슈팅 용이 |

---

## 5. 구현 순서

```
Step 1: Core/Models/CameraModelSpec.cs 신규
Step 2: Core/Config/AgentConfig.cs — CameraModel 필드 추가
Step 3: Agent/Services/CameraCaptureService.cs — 생성자 + InitializeCamera 수정
Step 4: Agent/Program.cs — 모델 스펙 로드 로직 + 생성자 호출부 수정
Step 5: CameraModels/AISEN.json, FPV.json 예시 파일 + csproj 복사 설정
Step 6: 신규 테스트 (CameraModelSpecTests) + 기존 테스트 회귀 확인
Step 7: 빌드 + 테스트 검증
```

---

## 6. 의존성

- 신규 NuGet 패키지 없음 (`System.Text.Json` 기존 사용)
- 전제 조건 없음 (NATS/DB 무관, 순수 파일 I/O)
