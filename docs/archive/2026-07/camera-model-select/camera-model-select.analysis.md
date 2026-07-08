# camera-model-select Analysis Document

> **Feature**: camera-model-select
> **Phase**: Check
> **Date**: 2026-07-08
> **Match Rate**: 100%
> **Design Doc**: [camera-model-select.design.md](../../02-design/features/camera-model-select.design.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 카메라 모델마다 센서 해상도가 다름 — 파일 기반으로 신규 모델 대응 |
| **WHO** | 개발자/설치 담당자, Agent 런타임 |
| **RISK** | `VideoCapture.Set` 실패, 파일 누락 시 크래시 — fallback으로 방지 |
| **SUCCESS** | `agent.json.CameraModel` 지정 시 해당 해상도 적용, 미지정/누락 시 기존 동작 무변경 |
| **SCOPE** | `Core/Config/AgentConfig.cs`, `Core/Models/CameraModelSpec.cs`, `Agent/Program.cs`, `Agent/Services/CameraCaptureService.cs` |

---

## 1. Structural Match (6/6 — 100%)

| 파일 | 상태 |
|---|---|
| `Core/Models/CameraModelSpec.cs` | ✅ |
| `Core/Config/AgentConfig.cs` (`CameraModel` 필드) | ✅ |
| `Agent/Services/CameraCaptureService.cs` (생성자 + InitializeCamera) | ✅ |
| `Agent/Program.cs` (로드 + 전달) | ✅ |
| `Agent/CameraModels/AISEN.json`, `FPV.json` + csproj 복사 설정 | ✅ |
| `Tests/CameraModelSpecTests.cs` (4건) + `CameraCaptureServiceTests.cs` (+1건) | ✅ |

---

## 2. FR Compliance (6/6 — 100%)

| ID | 요구사항 | 결과 |
|---|---|---|
| FR-01 | `AgentConfig.CameraModel` 추가 | ✅ |
| FR-02 | `CameraModelSpec { Width, Height }` | ✅ |
| FR-03 | Agent 시작 시 `CameraModels\{CameraModel}.json` 로드 | ✅ |
| FR-04 | 파일 없음/파싱 실패 시 경고 로그 + fallback | ✅ |
| FR-05 | `CameraCaptureService` 해상도 적용 | ✅ |
| FR-06 | `CameraModels\AISEN.json`, `FPV.json` 예시 파일 | ✅ |

---

## 3. Design 대비 구현 편차

| ID | 심각도 | 내용 | 조치 |
|---|---|---|---|
| G-01 | Minor | Design은 `Program.LoadCameraModelSpec`(private static) + `InternalsVisibleTo` 테스트 방식 제안. 구현은 `CameraModelSpec.Load(string, string?)`를 **public static 팩토리 메서드**로 Core에 배치 | ✅ 수용 — `InternalsVisibleTo` 없이 표준 public API로 테스트 가능, Core "설정 로드" 책임과도 일치, 더 단순 |

---

## 4. Runtime 검증

- `dotnet build`: 10 projects, 0 errors, 0 warnings
- `dotnet test --no-build`: **69/69** 통과 (기존 64 + 신규 5: CameraModelSpecTests 4건 + CameraCaptureServiceTests 1건)
- `ManagerE2EDriver` 재실행: exit 0, PASS (SimulationMode 경로는 `FakeCameraCaptureService` 사용 — 이번 변경과 무관, 회귀 없음 확인)
- 카메라 실물 미입고 상태 — 실제 `VideoCapture.FrameWidth/Height` 적용 여부는 미검증 (PRD §1.4, 다음 주 입고 후 확인 예정)

---

## 5. Test Results

| 테스트 | 결과 |
|---|---|
| `CameraModelSpecTests` (4개) | ✅ |
| `CameraCaptureServiceTests` (+1개) | ✅ |
| 기존 테스트 (64개) | ✅ 회귀 없음 |
| **합계** | **69/69** |

---

## 6. Match Rate

```
Structural  (×0.20): 100% → 0.200
Functional  (×0.40): 100% → 0.400  (G-01 편차는 개선 방향 수용, 감점 없음)
FR Contract (×0.40): 100% → 0.400
Overall: 100% ✅
```

**임계값 90% 초과 → Report 페이즈 진입**

---

## 7. Plan Success Criteria 최종 상태

| SC-ID | 기준 | 상태 |
|-------|------|:----:|
| SC-01 | `CameraModelSpec` JSON 왕복 직렬화 | ✅ |
| SC-02 | 미존재 모델명 → 예외 없이 null + 경고 로그 | ✅ |
| SC-03 | 해상도 지정 후 `InitializeCamera` 크래시 없음 | ✅ |
| SC-04 | width/height 미지정 시 기존 동작과 100% 동일 | ✅ |
| SC-05 | `dotnet build` 0/0, 기존 64 + 신규 전부 통과 | ✅ (69/69) |

**Success Rate**: 5/5 (100%)

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-07-08 | 최초 작성 — Match Rate 100%, 69/69 테스트, E2E 회귀 없음. 카메라 실물 검증은 다음 주 이월 |
