# PRD — SC-12 범위 2: 캡처 Roundtrip E2E (SimulationMode 플래그 분리)

**Feature**: `sc-12-scope2`
**Date**: 2026-06-23
**Status**: PM Analysis

---

## Executive Summary

| 관점 | 내용 |
|------|------|
| **Problem** | `ManagerSettings.SimulationMode` 단일 플래그가 열거(Enumeration)·spawn 스킵·Agent 모드 3개를 동시 제어하여, 실 하드웨어 없이 캡처 roundtrip을 자동으로 검증할 방법이 없다. |
| **Solution** | `SimulationMode`를 `SimulateEnumeration` + `SimulateAgentMode` 두 플래그로 분리. spawn 스킵은 `SimulateEnumeration`이 아닌 별도 제어 경로로 이동. 이를 통해 Fake 열거 + 실제 Agent.exe spawn(FakeCam 모드) 조합이 가능해진다. |
| **Functional UX Effect** | ManagerE2EDriver가 승인 루프에 이어 캡처 커맨드 → Agent 수신 → 결과 반환까지 검증. CI 없이 `dotnet run` 한 번으로 전체 흐름 확인 가능. |
| **Core Value** | 실 카메라·Windows Service 없이 캡처 roundtrip 회귀를 자동화. 향후 NATS 토픽/모델 변경 시 즉시 감지 가능. |

---

## 1. 문제 정의

### 1.1 현황 (AS-IS)

```
ManagerSettings.SimulationMode = true
  → AgentManager/Program.cs:44  FakeCameraEnumerator 선택
  → AgentSupervisor.cs:79       process.Start() 건너뜀
  → AgentSupervisor.cs:60       Agent args에 "True" 전달
```

`SimulationMode=true`이면 Agent.exe가 spawn되지 않아 NATS에 캡처 커맨드를 수신할 주체가 없다.  
결과: Master → `master.cmd.capture.{AgentId}` → *수신자 없음* → timeout.

### 1.2 SC-12 범위 1 결과 (2026-06-22)

- **완료**: 승인 루프 E2E (Fake 발견 → inventory → Approve → AgentId → 영속)
- **미완**: 캡처 roundtrip (SimulationMode 단일 플래그 구조상 불가)

### 1.3 목표 (TO-BE)

```
SimulateEnumeration = true   → FakeCameraEnumerator
SimulateAgentMode   = true   → Agent spawn 시 "True" arg 전달 (FakeCam)
SimulateSpawn       = false  → 실제 process.Start() 실행
```

→ Fake 카메라 2대 발견 → 승인 → 실제 Agent.exe(FakeCam 모드) spawn  
→ Driver: `master.cmd.capture.{AgentId}` 발행  
→ Agent: `FakeCameraCaptureService.CaptureFrame()` 실행 → `agent.result.capture.{AgentId}` 반환  
→ Driver: 결과 수신·검증 → PASS

---

## 2. 사용자 / 이해관계자

| 역할 | 니즈 |
|------|------|
| 개발자 (내부) | CI/CD 없이 `dotnet run` 한 번으로 캡처 roundtrip 회귀 검증 |
| QA (내부) | 실 카메라 없이 NATS 캡처 토픽 계약 변경 즉시 감지 |
| 아키텍트 | SimulationMode 다목적 플래그 → 단일책임 플래그로 정리 |

---

## 3. 요구사항 (MVP)

### 필수 (Must Have)

| ID | 요구사항 |
|----|---------|
| FR-01 | `ManagerSettings`에 `SimulateEnumeration`, `SimulateAgentMode` 추가. `SimulationMode`는 두 플래그 기본값 초기화용으로만 유지 또는 제거 |
| FR-02 | `AgentSupervisor.Spawn()` — spawn 스킵 조건을 `SimulateSpawn` 또는 `!File.Exists(AgentExePath)` 로 변경 (SimulationMode와 분리) |
| FR-03 | Agent args 조합에서 `SimulationMode` → `SimulateAgentMode` 사용 |
| FR-04 | `ManagerE2EDriver` 확장: 승인 완료 후 캡처 커맨드 발행 → `agent.result.capture.*` 수신 → 검증 |
| FR-05 | E2E PASS 조건: 2대 Agent 각각 캡처 결과 수신, `IsSuccess=true`, `ImageBytes != null` |

### 선택 (Nice to Have)

| ID | 요구사항 |
|----|---------|
| NFR-01 | `manager-settings.json` 이전 호환: `SimulationMode=true`면 두 플래그 모두 true로 읽힘 |
| NFR-02 | 회귀 테스트: 분리 후 기존 SC-12 범위 1 E2E가 그대로 통과 |

---

## 4. 성공 기준

| 기준 | 측정 방법 |
|------|---------|
| `dotnet run --project HeatingCameraSystem.ManagerE2EDriver`가 exit 0 | ManagerE2EDriver 직접 실행 |
| 캡처 결과 2건 수신 (`IsSuccess=true`, `ImageBytes.Length > 0`) | E2E 드라이버 콘솔 출력 |
| 기존 61개 테스트 모두 통과 | `dotnet test --no-build` |
| `dotnet build` 경고 0 | 빌드 출력 |

---

## 5. 범위 외 (Out of Scope)

- 실 OpenCV 카메라 연동 테스트
- Windows Service 설치·실행 테스트 (SC-01~03, SC-06)
- Master WPF UI 변경
- 성능/부하 테스트

---

## 6. 기술 제약

- `ManagerSettings`는 JSON 직렬화 대상 → 필드 추가 시 기존 `manager-settings.json` 무효화 없도록
- `dotnet 10 SDK`, 타겟 `net8.0` (win-x64)
- Nullable=enable, 경고 억제 금지
- 최소 변경 원칙 (리팩터링 X)

---

## 7. 리스크

| 리스크 | 대응 |
|--------|------|
| Agent.exe 빌드 산출물이 E2E 실행 시점에 없음 | E2EDriver가 `dotnet build` 경로 또는 `dotnet run` 방식으로 Agent 기동 |
| `process.Start()` 후 Agent NATS 연결 전 커맨드 발행 → timeout | Agent 하트비트 수신 후 커맨드 발행 (wait for `agent.status.*`) |
| 기존 `SimulationMode=true` 설정 파일이 있는 환경 호환 | NFR-01: 기존 필드 읽어 두 플래그 초기화 |
