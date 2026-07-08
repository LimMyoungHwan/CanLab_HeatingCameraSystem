# Plan — SC-12 범위 2: 캡처 Roundtrip E2E (SimulationMode 플래그 분리)

**Feature**: `sc-12-scope2`
**Phase**: Plan
**Date**: 2026-06-23
**PRD**: `docs/00-pm/sc-12-scope2.prd.md`

---

## Executive Summary

| 관점 | 내용 |
|------|------|
| **Problem** | `SimulationMode` 단일 플래그가 열거·spawn·Agent 동작 3가지를 동시 제어하여 실 HW 없는 캡처 roundtrip E2E가 코드상 불가능하다. |
| **Solution** | `ManagerSettings`에 `SimulateEnumeration` / `SimulateAgentMode` 플래그 추가, spawn 스킵 조건 분리. ManagerE2EDriver가 Fake 열거 + 실제 Agent.exe(FakeCam) 조합으로 캡처 roundtrip 검증. |
| **Functional UX Effect** | `dotnet run --project HeatingCameraSystem.ManagerE2EDriver` 한 번으로 승인 루프 + 캡처 roundtrip 전체 PASS/FAIL 확인. |
| **Core Value** | 실 카메라·Windows Service 없이 NATS 캡처 계약 회귀를 자동 감지. 기존 61개 테스트에 캡처 E2E 2건 추가. |

---

## Context Anchor

| 항목 | 내용 |
|------|------|
| **WHY** | SC-12 범위 1(승인 루프)은 완료. 캡처 roundtrip은 SimulationMode 구조상 미검증 상태. 플래그 분리로 CI 자동화 가능. |
| **WHO** | 이 codebase를 유지보수하는 개발자·QA. 실 HW 없는 CI 환경. |
| **RISK** | Agent.exe 빌드 산출물 경로 의존, NATS 연결 타이밍(하트비트 wait 필요). |
| **SUCCESS** | ManagerE2EDriver exit 0, 캡처 결과 2건 IsSuccess=true & ImageBytes>0, 테스트 63+/63+ 통과, 경고 0. |
| **SCOPE** | ManagerSettings 플래그 2개 추가, AgentSupervisor spawn 로직 수정, ManagerE2EDriver 캡처 단계 확장. 프로덕션 WPF·Agent·Core·Protocols 변경 최소화. |

---

## 1. 배경 및 목표

### 1.1 현재 구조 (문제)

`ManagerSettings.SimulationMode = true` 시 세 가지가 동시에 활성화:

```
[1] AgentManager/Program.cs:44  → FakeCameraEnumerator 선택
[2] AgentSupervisor.cs:79       → process.Start() 건너뜀 (spawn 스킵)
[3] AgentSupervisor.cs:60       → Agent args 5번째 = "True" (FakeCam 모드)
```

[2]로 인해 SimulationMode=true이면 Agent.exe가 존재하지 않아  
`master.cmd.capture.*` 토픽에 수신자가 없음 → 캡처 roundtrip 검증 불가.

### 1.2 목표

[1]·[3]을 독립 플래그로 분리하여 [2]만 선택적으로 끌 수 있게 한다.

| 플래그 | 역할 |
|--------|------|
| `SimulateEnumeration` | FakeCameraEnumerator vs WmiCameraEnumerator 선택 |
| `SimulateAgentMode`   | Agent spawn 시 args에 전달하는 SimulationMode 값 |
| spawn 스킵 조건       | `!SimulateEnumeration && !File.Exists(AgentExePath)` — 실 exe 없을 때만 스킵 |

→ E2E 조합: `SimulateEnumeration=true`, `SimulateAgentMode=true`, spawn 스킵 OFF  
→ FakeCam 모드의 Agent.exe가 실제로 spawn되어 캡처 커맨드 수신 가능.

---

## 2. 요구사항

### 2.1 필수 (Must Have)

| ID | 요구사항 | 근거 |
|----|---------|------|
| FR-01 | `ManagerSettings`에 `SimulateEnumeration` (bool, 기본 false), `SimulateAgentMode` (bool, 기본 false) 추가. `SimulationMode` 기존 필드는 두 플래그의 초기화 편의 속성으로 유지(get/set 시 두 플래그 동기화) 또는 `[Obsolete]` 처리. | 이전 호환 |
| FR-02 | `AgentManager/Program.cs` — `ICameraEnumerator` 선택 조건을 `settings.SimulateEnumeration`으로 변경. | 플래그 분리 |
| FR-03 | `AgentSupervisor.Spawn()` — spawn 스킵 조건: `SimulateEnumeration=false && !File.Exists(AgentExePath)`. `SimulationMode` 직접 참조 제거. | 플래그 분리 |
| FR-04 | `AgentSupervisor.Spawn()` — Agent args 5번째 인수를 `settings.SimulateAgentMode`로 변경. | 플래그 분리 |
| FR-05 | `ManagerE2EDriver` — 승인 루프 완료 후 캡처 커맨드 발행 단계 추가. Agent 하트비트(`agent.status.*`) 대기 후 커맨드 발행. | 캡처 E2E |
| FR-06 | E2E PASS 조건: 2대 Agent 각각 `IsSuccess=true` & `ImageBytes.Length > 0`. | 검증 기준 |

### 2.2 선택 (Nice to Have)

| ID | 요구사항 |
|----|---------|
| NFR-01 | 기존 `manager-settings.json`에 `SimulationMode: true`가 있으면 역직렬화 후 두 플래그를 자동 true로 초기화. |
| NFR-02 | 회귀: 기존 SC-12 범위 1 E2E(승인 루프만)가 새 플래그 조합으로도 동일하게 통과. |

### 2.3 비기능

- `dotnet build` 경고 0
- Nullable=enable, `!` 억제 금지
- 버그 수정 외 리팩터링 금지 (최소 변경)
- NATS Docker 실행 필요 (`nats://127.0.0.1:4222`)

---

## 3. 성공 기준 (Success Criteria)

| SC-ID | 기준 | 검증 방법 |
|-------|------|---------|
| SC-01 | `ManagerE2EDriver` exit 0 (NATS 구동 환경) | `dotnet run --project HeatingCameraSystem.ManagerE2EDriver` |
| SC-02 | 캡처 결과 2건 수신, `IsSuccess=true`, `ImageBytes.Length > 0` | E2E 콘솔 출력 |
| SC-03 | `dotnet test --no-build` 전체 통과 (기존 61 + 신규 ≥2) | CI |
| SC-04 | `dotnet build` 경고 0 | 빌드 출력 |
| SC-05 | 기존 승인 루프 E2E(범위 1) 여전히 PASS | ManagerE2EDriver 범위 1 재실행 |

---

## 4. 구현 범위 (Scope)

### 수정 파일

| 파일 | 변경 내용 |
|------|---------|
| `HeatingCameraSystem.AgentManager/Config/ManagerSettings.cs` | `SimulateEnumeration`, `SimulateAgentMode` 추가 |
| `HeatingCameraSystem.AgentManager/Program.cs` | `SimulationMode` → `SimulateEnumeration` |
| `HeatingCameraSystem.AgentManager/Services/AgentSupervisor.cs` | spawn 스킵 조건, args 조합 수정 |
| `HeatingCameraSystem.ManagerE2EDriver/Program.cs` | 캡처 커맨드 발행 + 결과 수신 단계 추가 |

### 신규 파일

없음. 기존 파일 수정만.

### 수정 불가 (범위 외)

- `HeatingCameraSystem.Core/` — 인터페이스·모델 변경 없음
- `HeatingCameraSystem.Agent/` — Agent 자체 변경 없음 (args[4] 수신은 기존 그대로)
- `HeatingCameraSystem.Master/` — WPF UI 변경 없음
- `HeatingCameraSystem.Protocols/` — NATS 토픽 변경 없음

---

## 5. 리스크 및 대응

| 리스크 | 가능성 | 대응 |
|--------|--------|------|
| E2E 실행 시 Agent.exe 빌드 산출물이 없음 | 중 | E2EDriver가 `dotnet run`으로 Agent 기동하거나, 빌드 경로 자동 탐지 |
| NATS 연결 전 캡처 커맨드 발행 → timeout | 중 | Agent 하트비트 수신 대기 후 커맨드 발행 |
| 기존 `ManagerStateStore` `SimulationMode` 필드 호환 | 하 | JSON에 새 필드만 추가. 기존 필드 없어도 false 기본값 |
| 두 Agent가 같은 NATS 토픽으로 결과 발행 시 순서 비결정 | 하 | 결과 수집을 set 기반으로, 순서 무관 |

---

## 6. 구현 순서 (Implementation Order)

```
Step 1: ManagerSettings.cs — SimulateEnumeration / SimulateAgentMode 추가
Step 2: AgentManager/Program.cs — enumerator 선택 조건 수정
Step 3: AgentSupervisor.cs — spawn 스킵 조건 + args 수정
Step 4: ManagerE2EDriver/Program.cs — 캡처 단계 추가 (하트비트 wait → cmd → 결과 wait → 검증)
Step 5: Tests — 새 플래그 동작 회귀 테스트 추가
Step 6: 빌드·E2E 실행 검증
```

---

## 7. 의존성

- **기존 의존**: NATS.Net, xUnit, Moq (변경 없음)
- **신규 의존**: 없음
- **전제 조건**: NATS 서버 `nats://127.0.0.1:4222` 실행 중
