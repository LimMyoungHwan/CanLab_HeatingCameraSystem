# Design — SC-12 범위 2: 캡처 Roundtrip E2E (SimulationMode 플래그 분리)

**Feature**: `sc-12-scope2`
**Phase**: Design
**Date**: 2026-06-23
**Plan**: `docs/01-plan/features/sc-12-scope2.plan.md`

---

## Context Anchor

| 항목 | 내용 |
|------|------|
| **WHY** | SimulationMode 단일 플래그 → spawn 불가 → 캡처 roundtrip 자동화 불가. 플래그 분리로 CI 가능. |
| **WHO** | 이 codebase 유지보수 개발자·QA. 실 HW 없는 CI 환경. |
| **RISK** | Agent.exe 빌드 산출물 경로 의존 / NATS 타이밍(하트비트 wait). |
| **SUCCESS** | ManagerE2EDriver exit 0, 캡처 결과 2건 IsSuccess + ImageBytes>0, 테스트 63+/63+, 경고 0. |
| **SCOPE** | ManagerSettings(플래그 2개) + AgentSupervisor(spawn 조건) + AgentManager/Program + ManagerE2EDriver(캡처 단계). Core·Agent·Master 변경 없음. |

---

## 1. 현재 구조 분석

### 1.1 SimulationMode 제어 지점

```
ManagerSettings.SimulationMode = true
  │
  ├─ [A] AgentManager/Program.cs:44
  │       settings.SimulationMode ? FakeCameraEnumerator : WmiCameraEnumerator
  │
  ├─ [B] AgentSupervisor.cs:79
  │       if (_settings.SimulationMode || !File.Exists(_settings.AgentExePath))
  │           { skip spawn }
  │
  └─ [C] AgentSupervisor.cs:60
          args = $"... {_settings.SimulationMode}"
          → Agent에 SimulationMode=True 전달 → FakeCameraCaptureService 선택
```

### 1.2 Agent.exe 빌드 경로

| 구분 | 경로 |
|------|------|
| Agent 출력 | `HeatingCameraSystem.Agent/bin/{Debug|Release}/net8.0/HeatingCameraSystem.Agent.exe` |
| E2EDriver 출력 | `HeatingCameraSystem.ManagerE2EDriver/bin/{Debug|Release}/net8.0/win-x64/` |
| Solution root (상대) | E2EDriver exe → `../../../../../` (5레벨 상위) |

### 1.3 E2E 캡처 흐름 (목표)

```
E2EDriver (driver NATS conn)
  │
  ├─ Manager-side: Fake 열거 → inventory 발행
  ├─ Driver: Approve 2대 → AgentId 부여
  ├─ Manager: Agent.exe spawn (SimulateAgentMode=true → FakeCam)
  │
  └─ [NEW] 캡처 roundtrip
       ① wait: agent.status.{AgentId} 수신 (2대 모두 — 연결 확인)
       ② driver → master.cmd.capture.{AgentId} (각 대)
       ③ agent → FakeCameraCaptureService.CaptureFrame() → agent.result.capture.{AgentId}
       ④ driver: 결과 2건 수집 → IsSuccess + ImageBytes 검증
```

---

## 2. 아키텍처 옵션 3가지

### Option A — Minimal (SimulationMode 유지, 편의 Setter로 전환)

**변경 방식:**
- `ManagerSettings`에 `SimulateEnumeration` + `SimulateAgentMode` 추가
- `SimulationMode`를 `[JsonIgnore]` computed setter로 변환:
  ```csharp
  [System.Text.Json.Serialization.JsonIgnore]
  public bool SimulationMode
  {
      set { SimulateEnumeration = SimulateAgentMode = value; }
  }
  ```
- 제어 지점 [A][B][C] 각각 새 플래그로 교체
- E2EDriver 범위 1: `SimulationMode = true` 코드 그대로 (컴파일 OK, 두 플래그 설정됨)
- E2EDriver 범위 2: 캡처 단계 추가 (실 AgentExePath 사용)

| 항목 | 평가 |
|------|------|
| 변경 규모 | 소 (~30 lines) |
| 이전 호환 | ✅ E2EDriver 범위 1 코드 무변경 |
| 명확성 | △ `SimulationMode`가 여전히 존재 (혼란 가능) |
| 리스크 | 소 |

---

### Option B — Clean Removal (SimulationMode 완전 제거) ⭐ 추천

**변경 방식:**
- `ManagerSettings`에서 `SimulationMode` 삭제
- `SimulateEnumeration` + `SimulateAgentMode` 추가
- 제어 지점 [A][B][C] 새 플래그로 교체
- E2EDriver 범위 1: `SimulationMode = true` → `SimulateEnumeration = true, SimulateAgentMode = true` (한 줄)
- E2EDriver 범위 2: 캡처 단계 추가

| 항목 | 평가 |
|------|------|
| 변경 규모 | 소-중 (~45 lines) |
| 이전 호환 | ○ JSON 기존 `SimulationMode` 필드는 무시됨 (`[JsonIgnore]` 불필요) |
| 명확성 | ✅ 단일 책임, 플래그 두 개만 존재 |
| 리스크 | 소 (수정 지점 명확, 4개 파일만) |

> **이 옵션이 Plan에서 확정된 선택(2-B)입니다.**

---

### Option C — SimulationFlags 타입 분리

**변경 방식:**
- `ManagerSettings` 내부에 `SimulationFlags` 레코드 추출:
  ```csharp
  public record SimulationFlags(bool Enumeration, bool AgentMode);
  public SimulationFlags Simulation { get; set; } = new(false, false);
  ```
- JSON: `"Simulation": { "Enumeration": true, "AgentMode": true }`
- 전 사용 지점 `settings.Simulation.Enumeration` 형태로 변경

| 항목 | 평가 |
|------|------|
| 변경 규모 | 중 (~60 lines) |
| 이전 호환 | ✗ 기존 `manager-settings.json` 스키마 완전 변경 |
| 명확성 | ✅ 구조화 |
| 리스크 | 중 (실 배포 설정 파일 마이그레이션 필요) |

---

## 3. 옵션 비교

| 기준 | Option A | Option B ⭐ | Option C |
|------|----------|------------|----------|
| 변경 파일 수 | 4 | 4 | 5 |
| 예상 변경 라인 | ~30 | ~45 | ~60 |
| 기존 E2EDriver 범위 1 수정 | 불필요 | 1줄 | 전체 |
| 기존 manager-settings.json 호환 | ✅ | ✅ | ✗ |
| 코드 명확성 | △ | ✅ | ✅ |
| 리스크 | 소 | 소 | 중 |
| **추천** | | ⭐ | |

---

## 4. 선택된 아키텍처: Option B

Plan Checkpoint 2에서 사용자가 **2-B (SimulationMode 완전 제거)** 확정.

### 4.1 ManagerSettings 변경 상세

```csharp
// Design Ref: §4 — SimulationMode 단일 플래그를 두 독립 플래그로 분리
// 이전: SimulationMode 하나가 열거·spawn·Agent 동작 3개를 동시 제어했음
// 이후: 각 용도별 독립 플래그 → 조합 자유도 확보

public class ManagerSettings
{
    // ... 기존 필드 ...

    /// <summary>
    /// true이면 WmiCameraEnumerator 대신 FakeCameraEnumerator를 사용.
    /// 실 USB 카메라 없이 가상 카메라 2대를 발견하는 것처럼 동작.
    /// </summary>
    public bool SimulateEnumeration { get; set; } = false;

    /// <summary>
    /// true이면 AgentSupervisor가 Agent.exe를 spawn할 때
    /// CLI 인수에 "True"(SimulationMode)를 전달하여
    /// Agent가 FakeCameraCaptureService를 사용하게 함.
    /// </summary>
    public bool SimulateAgentMode { get; set; } = false;
}
```

### 4.2 AgentManager/Program.cs 변경 상세

```csharp
// [A] 제어 지점 — 열거기 선택
// SimulationMode → SimulateEnumeration 으로 교체
builder.Services.AddSingleton<ICameraEnumerator>(sp =>
    settings.SimulateEnumeration              // 변경: SimulationMode → SimulateEnumeration
        ? (ICameraEnumerator)new FakeCameraEnumerator()
        : new WmiCameraEnumerator());
```

### 4.3 AgentSupervisor.cs 변경 상세

**spawn 스킵 조건 (제어 지점 [B]):**
```csharp
// [B] spawn 스킵 조건
// 이전: SimulationMode=true 또는 exe 없을 때 스킵 → FakeCam E2E 불가
// 이후: exe 없을 때만 스킵 → SimulateEnumeration=true더라도 exe 있으면 spawn 실행
if (!File.Exists(_settings.AgentExePath))    // 변경: SimulationMode 조건 제거
{
    _logger.LogInformation("AgentExePath not found — skipping spawn for {AgentId}", entry.AgentId);
    _agents[entry.HardwareId] = managed;
    return;
}
```

**Agent args 조합 (제어 지점 [C]):**
```csharp
// [C] Agent 실행 인수 조합
// SimulationMode → SimulateAgentMode: Agent가 FakeCam 모드로 시작할지 결정
var args = $"{entry.AgentId} {_settings.NatsUrl} {entry.OpenCvIndex} " +
           $"\"{storagePath}\" \"{logPath}\" {_settings.SimulateAgentMode}";
//                                               ^^^^^^^^^^^^^^^^^^^^^^^^^
//                                               변경: SimulationMode → SimulateAgentMode
```

### 4.4 ManagerE2EDriver 확장 상세

```
[기존 범위 1 단계]                    [신규 범위 2 단계]
──────────────────                    ──────────────────
1. NATS 연결 (mgr + drv)              5. 하트비트 대기
2. 가짜 카메라 발견                       - agent.status.* 구독
3. inventory 발행                        - 2대 모두 수신될 때까지 wait (timeout)
4. Approve + AgentId 검증            6. 캡처 커맨드 발행
                                          - master.cmd.capture.{AgentId} x2
                                     7. 캡처 결과 수집
                                          - agent.result.capture.{AgentId} x2
                                     8. 결과 검증
                                          - IsSuccess=true, ImageBytes.Length > 0
```

**Agent exe 경로 자동 탐지:**
```csharp
// E2EDriver exe 위치: HeatingCameraSystem.ManagerE2EDriver/bin/{cfg}/net8.0/win-x64/
// Solution root: 5레벨 상위
// Agent exe: {solutionRoot}/HeatingCameraSystem.Agent/bin/{cfg}/net8.0/
static string FindAgentExe()
{
    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
    var solutionRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
    foreach (var cfg in new[] { "Debug", "Release" })
    {
        var path = Path.Combine(solutionRoot,
            "HeatingCameraSystem.Agent", "bin", cfg, "net8.0",
            "HeatingCameraSystem.Agent.exe");
        if (File.Exists(path)) return path;
    }
    return string.Empty;
}
```

### 4.5 E2EDriver 설정 변경 (범위 1 기존 코드)

```csharp
// 기존 (범위 1): SimulationMode = true
// 변경 (범위 1): 두 플래그 명시 → 동작 동일 (AgentExePath 없음 → spawn 스킵)
var settings = new ManagerSettings
{
    PCId               = pcId,
    NatsUrl            = natsUrl,
    SimulateEnumeration = true,   // 변경: FakeCameraEnumerator 사용
    SimulateAgentMode   = true,   // 변경: Agent args에 "True" 전달 (spawn 않으므로 실질 무의미)
    InstallRoot        = installRoot,
    AgentExePath       = Path.Combine(installRoot, "Agent", "HeatingCameraSystem.Agent.exe"),
    // AgentExePath → temp dir, !File.Exists → spawn 스킵 (범위 1 동작 유지)
};
```

---

## 5. NATS 토픽 (변경 없음)

기존 토픽 그대로 사용. 신규 토픽 없음.

```
master.cmd.capture.{AgentId}      ← E2EDriver(드라이버 conn)가 발행
agent.status.{AgentId}           ← Agent가 발행 (하트비트 대기용)
agent.result.capture.{AgentId}   ← Agent가 발행 (캡처 결과)
```

`NatsCommunicationService`에 `SubscribeAgentStatusAsync` / `SubscribeCaptureResultAsync`가  
이미 구현되어 있으므로 추가 Protocols 변경 없음.

---

## 6. 데이터 흐름

```
ManagerE2EDriver (범위 2 추가 단계)

Driver conn                              Manager-side (in-process)
    │                                          │
    │  ←─ agent.status.Agent_X ─────────────  │  (Agent_X spawned with FakeCam)
    │  ←─ agent.status.Agent_Y ─────────────  │  (Agent_Y spawned with FakeCam)
    │  [wait: 2대 모두 수신]
    │
    │  ─── master.cmd.capture.Agent_X ──────► Agent_X
    │  ─── master.cmd.capture.Agent_Y ──────► Agent_Y
    │
    │  ←─ agent.result.capture.Agent_X ─────  Agent_X (FakeCaptureService)
    │  ←─ agent.result.capture.Agent_Y ─────  Agent_Y (FakeCaptureService)
    │  [wait: 2건 모두 수신]
    │
    │  [검증] IsSuccess=true, ImageBytes.Length > 0
    │
    └─ exit 0 (PASS) / exit 1 (FAIL)
```

---

## 7. 예외 처리

| 상황 | 처리 |
|------|------|
| Agent exe 없음 | 사전 체크 → "Agent.exe not found at {path}" 출력 → exit 2 |
| 하트비트 timeout (20s) | "FAIL — heartbeat timeout for {AgentId}" → exit 3 |
| 캡처 결과 timeout (30s) | "FAIL — capture result timeout" → exit 3 |
| 결과 IsSuccess=false | "FAIL — capture failed for {AgentId}" → exit 1 |
| 결과 ImageBytes 없음 | "FAIL — empty ImageBytes for {AgentId}" → exit 1 |

---

## 8. 테스트 계획

| 레벨 | 테스트 | 방법 |
|------|--------|------|
| L1 — 단위 | `ManagerSettings` 직렬화: `SimulateEnumeration` + `SimulateAgentMode` JSON 왕복 | xUnit |
| L1 — 단위 | `AgentSupervisor`: exe 없으면 spawn 스킵, 있으면 spawn | xUnit + Moq (Process mock) |
| L2 — E2E | `ManagerE2EDriver` PASS (범위 1 + 범위 2 포함) | 직접 실행 (NATS 필요) |
| L3 — 회귀 | 기존 61개 테스트 전부 통과 | `dotnet test --no-build` |

---

## 9. 구현 가이드 (Module Map)

| 모듈 | 파일 | 변경 종류 |
|------|------|---------|
| M1 | `ManagerSettings.cs` | SimulationMode 제거, 두 플래그 추가 |
| M2 | `AgentManager/Program.cs` | 열거기 선택 조건 수정 |
| M3 | `AgentSupervisor.cs` | spawn 스킵 조건 + args 수정 |
| M4 | `ManagerE2EDriver/Program.cs` | settings 수정 + 캡처 단계 추가 |
| M5 | `HeatingCameraSystem.Tests/` | 신규 테스트 2건 이상 |

### 추천 세션 플랜

한 세션으로 완료 가능 (전체 ~45줄 변경):
```
Step 1: M1 — ManagerSettings (5분)
Step 2: M2 — AgentManager/Program.cs (2분)
Step 3: M3 — AgentSupervisor.cs (5분)
Step 4: M5 — 신규 테스트 (10분)
Step 5: M4 — ManagerE2EDriver (15분)
Step 6: 빌드 + 테스트 + E2E 실행 검증 (10분)
```

---

## 10. 주석 컨벤션 (사용자 요청)

모든 변경 지점에 아래 형식 주석 추가:

```csharp
// [SC-12 범위 2] Design Ref: §4.N — {변경 이유 한 줄}
// Plan SC: SC-0N — {검증 기준}
```

예시:
```csharp
// [SC-12 범위 2] Design Ref: §4.3 — SimulationMode 대신 SimulateAgentMode 사용
// Plan SC: SC-01 — ManagerE2EDriver exit 0 검증
var args = $"... {_settings.SimulateAgentMode}";
```
