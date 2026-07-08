# sc-12-scope2 Analysis Document

> **Feature**: sc-12-scope2
> **Phase**: Check
> **Date**: 2026-06-29
> **Match Rate**: 100%
> **Design Doc**: [sc-12-scope2.design.md](../../02-design/features/sc-12-scope2.design.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | SimulationMode 단일 플래그 → spawn 불가 → 캡처 roundtrip 자동화 불가. 플래그 분리로 CI 가능. |
| **WHO** | 이 codebase 유지보수 개발자·QA. 실 HW 없는 CI 환경. |
| **RISK** | Agent.exe 빌드 산출물 경로 의존 / NATS 타이밍(하트비트 wait). |
| **SUCCESS** | ManagerE2EDriver exit 0, 캡처 결과 2건 IsSuccess + ImageBytes>0, 테스트 63+/63+, 경고 0. |
| **SCOPE** | ManagerSettings(플래그 2개) + AgentSupervisor(spawn 조건) + AgentManager/Program + ManagerE2EDriver(캡처 단계). Core·Agent·Master 변경 없음. |

---

## 1. Structural Match (5/5 — 100%)

| 파일 | 상태 |
|---|---|
| `HeatingCameraSystem.AgentManager/Config/ManagerSettings.cs` (SimulateEnumeration/SimulateAgentMode) | ✅ |
| `HeatingCameraSystem.AgentManager/Program.cs` (열거기 선택 조건) | ✅ |
| `HeatingCameraSystem.AgentManager/Services/AgentSupervisor.cs` (spawn 조건 + args) | ✅ |
| `HeatingCameraSystem.ManagerE2EDriver/Program.cs` (캡처 단계 확장 + FindAgentExe) | ✅ |
| `HeatingCameraSystem.Tests/AgentManagerTests.cs` (신규 3건) | ✅ |

---

## 2. FR Compliance (6/6 — 100%)

| ID | 요구사항 | 결과 |
|---|---|---|
| FR-01 | `ManagerSettings`에 `SimulateEnumeration`/`SimulateAgentMode` 추가 (Option B: SimulationMode 완전 제거) | ✅ |
| FR-02 | `AgentManager/Program.cs` — `SimulateEnumeration`으로 enumerator 선택 | ✅ |
| FR-03 | `AgentSupervisor.Spawn()` spawn 스킵 조건: `!File.Exists(AgentExePath)`만 | ✅ |
| FR-04 | `AgentSupervisor.Spawn()` args[4] = `SimulateAgentMode` (기존 인덱스 버그 수정 포함) | ✅ |
| FR-05 | `ManagerE2EDriver` 하트비트 대기 → 캡처 커맨드 → 결과 수집 단계 추가 | ✅ |
| FR-06 | E2E PASS 조건: 2대 Agent 각각 `IsSuccess=true` & `ImageBytes.Length>0` | ✅ |

---

## 3. Runtime E2E Verification (2026-06-29, 실행 완료)

이전 세션(2026-06-23)에서는 NATS 미기동으로 Check 페이즈가 정적 분석(96%)에 머물렀음.
본 세션에서 사용자가 NATS를 기동, `ManagerE2EDriver`를 실제 실행하여 런타임 검증 완료.

```
[MGR-E2E] === [범위 1] 승인 루프 VERIFICATION ===
  hw=USB\VID_FAKE&PID_CAM1\00000001 approved=True agentId=E2E-MGR-PC_7031a4e0 idOk=True aliasOk=True
  hw=USB\VID_FAKE&PID_CAM2\00000002 approved=True agentId=E2E-MGR-PC_16c79834 idOk=True aliasOk=True
  manager-state.json 영속 & 승인: True

[MGR-E2E] === [범위 2] 캡처 VERIFICATION ===
  E2E-MGR-PC_7031a4e0: success=True bytes=21295 ok=True
  E2E-MGR-PC_16c79834: success=True bytes=20903 ok=True

[MGR-E2E] *** PASS ***
```

Exit Code: `0`

---

## 4. Test Results

| 테스트 | 결과 |
|---|---|
| 전체 (`dotnet test --no-build`) | ✅ 64/64 통과, 0 warnings |
| `dotnet build` | ✅ 10 projects, 0 errors, 0 warnings |

---

## 5. Match Rate

```
Structural  (×0.20): 100% → 0.200
Functional  (×0.40): 100% → 0.400  (런타임 E2E PASS로 상향, 2026-06-23 정적분석 96%에서 개선)
FR Contract (×0.40): 100% → 0.400
Overall: 100% ✅
```

**임계값 90% 초과 → Report 페이즈 진입**

---

## 6. Plan Success Criteria 최종 상태

| SC-ID | 기준 | 상태 | 근거 |
|-------|------|:----:|------|
| SC-01 | `ManagerE2EDriver` exit 0 | ✅ | 실행 로그 §3 |
| SC-02 | 캡처 결과 2건, IsSuccess=true, ImageBytes>0 | ✅ | bytes=21295 / 20903 |
| SC-03 | `dotnet test --no-build` 전체 통과 (61+신규≥2) | ✅ | 64/64 (61+3) |
| SC-04 | `dotnet build` 경고 0 | ✅ | 10 projects, 0 warnings |
| SC-05 | 기존 승인 루프 E2E(범위 1) 여전히 PASS | ✅ | 로그 §3 범위1 VERIFICATION |

**Success Rate**: 5/5 (100%)

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-06-23 | 정적 분석만 — Match Rate 96%, 런타임 E2E 미실행 (NATS 필요) |
| 1.0 | 2026-06-29 | NATS 기동 후 런타임 E2E 실행 — Match Rate 100%, 전 SC 통과 |
