# agent-manager Design Document

> **Summary**: PC당 Agent Manager (Windows Service) — WMI 카메라 자동 발견, HardwareId 영구 식별, Agent 프로세스 supervisor (spawn/kill/respawn), NDJSON 로그 파이프라인, Master Devices 탭 원격 관리.
>
> **Project**: HeatingCameraSystem
> **Version**: 0.1
> **Author**: -
> **Date**: 2026-06-21
> **Status**: Implemented
> **Planning Doc**: [agent-manager.plan.md](../../01-plan/features/agent-manager.plan.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 다중 PC × 다중 카메라 환경에서 Agent 수동 실행 부담 + USB 포트 이동 시 OpenCV 인덱스 어긋남 + 분산 Agent 로그 가시성 부재 |
| **WHO** | 열화상 카메라 시스템 운영자 (Master PC 단일 사용자), Agent PC 1~N대 분산 |
| **RISK** | WMI 권한 부족, OpenCV↔WMI 인덱스 매칭 불안정, 기존 Recipe CameraIndex 마이그레이션, Agent fork bomb |
| **SUCCESS** | Manager 서비스 1개 등록 후 PC 재부팅 → 카메라 자동 복구. USB 포트 옮겨도 동일 카메라 인식. Agent ERROR 5초 내 Master Devices 탭 빨간 점 |
| **SCOPE** | AgentManager 신규 프로젝트 + Core 모델/인터페이스 확장 + Protocols 열거자 + Master Devices 탭 + DB 마이그레이션 + Agent Serilog + install.ps1 |

---

## 1. Overview

### 1.1 Design Goals

- 운영자 개입 최소화: PC당 Manager 서비스 1회 등록 → 카메라 꽂으면 자동 발견 → 승인 후 Agent 자동 시작
- 카메라 식별 안정성: WMI HardwareId(PnPDeviceID) 기반 영구 키 — USB 포트 독립
- 분산 로그 가시성: Agent NDJSON 로그를 Manager가 tail → ERROR 즉시 Master push + on-demand dump
- 기존 호환: CameraIndex 기반 Recipe 100% 동작 유지 (CameraAlias 미설정 시 fallback)
- 시뮬레이션: FakeCameraEnumerator로 하드웨어 없이 전체 흐름 검증

### 1.2 Design Principles

- **기존 패턴 일치**: INatsCommunicationService 확장, LiteDbXxxRepository 패턴, CommunityToolkit.Mvvm ViewModel
- **프로세스 격리**: Manager↔Agent IPC = CLI args only (kill+respawn). Named pipe 등 IPC 없음
- **데이터 경로 분리**: 캡처 명령/이미지는 기존대로 Master↔Agent 직통 (Manager 우회 — 병목 방지)
- **점진적 마이그레이션**: CameraSerialSettings → CameraDevice 자동 흡수, 기존 Recipe fallback 유지

---

## 2. Architecture Options Analysis

### 2.1 Option A: Embedded Manager (Agent 내장)

각 Agent에 카메라 열거 + 자기 관리 로직을 내장. Manager 별도 프로세스 없음.

```
[Agent PC]
  Agent.exe (카메라 1대)
    ├── WMI 열거 (자기 카메라만)
    ├── 자기 자신 watchdog (self-restart)
    └── 로그 자체 push
```

| 장점 | 단점 |
|---|---|
| 배포 단순 (Agent.exe 하나) | 자기 자신의 크래시를 감지/복구할 수 없음 (watchdog 불가) |
| 프로세스 수 최소 | 카메라 N대면 N개 Agent가 각자 WMI 열거 → 중복 |
| IPC 불필요 | 새 카메라 자동 발견 불가 (이미 실행 중인 Agent만 관리) |

**기각 사유**: 자기 자신의 크래시 복구 불가. 신규 카메라 자동 발견 불가.

### 2.2 Option B: Centralized Manager (Master 내장)

Master WPF 앱에 Manager 로직을 통합. Agent PC의 카메라 열거를 NATS로 원격 수행.

```
[Master PC]
  Master.exe (WPF)
    ├── RemoteCameraEnumerator (NATS request → Agent PC)
    ├── Agent 프로세스 관리 (NATS 명령)
    └── 로그 수집
```

| 장점 | 단점 |
|---|---|
| 단일 프로세스 (Master에 통합) | Master가 Agent PC의 프로세스를 직접 제어할 수 없음 (원격) |
| 추가 배포 없음 | WMI는 로컬 전용 — 원격 WMI는 보안/방화벽 복잡 |
| | Master 재시작 시 모든 PC의 Agent 관리 중단 |
| | Master PC와 Agent PC가 같은 머신일 때만 프로세스 spawn 가능 |

**기각 사유**: WMI 원격 호출의 보안/방화벽 복잡성. Master 단일 장애점.

### 2.3 Option C: Distributed Supervisor (채택)

PC당 Agent Manager 1개 (Windows Service). Manager가 로컬 WMI로 카메라 열거, Agent 프로세스 spawn/kill/respawn. Master와는 NATS로만 통신.

```
[Agent PC #1]                           [Master PC]
  AgentManager.exe (Windows Service)      Master.exe (WPF)
    ├── WmiCameraEnumerator (로컬)           ├── DevicesViewModel
    ├── AgentSupervisor (spawn/kill)          │   ├── Approve/Reject
    ├── LogTailService (NDJSON tail)          │   ├── Alias 편집
    └── NATS ↔ Master                        │   └── 로그 뷰어
        ↓                                    └── NATS ↔ Manager
  Agent.exe #1 (카메라 0)
  Agent.exe #2 (카메라 1)

[Agent PC #2]
  AgentManager.exe (Windows Service)
    └── ...
```

| 장점 | 단점 |
|---|---|
| 로컬 WMI — 권한/방화벽 문제 없음 | 추가 프로세스 (PC당 1개 Manager 서비스) |
| Manager가 Agent 크래시 감지 + 자동 재시작 | install.ps1 배포 필요 |
| 신규 카메라 PnP 자동 발견 | Manager 자체 크래시 시 Windows Service Recovery로 복구 |
| Master와 분리 — Master 재시작해도 Agent 영향 없음 | |
| PC당 독립 운영 — 네트워크 단절 시에도 로컬 Agent 관리 | |

**채택 사유**: 유일하게 (1) 자기 자신 크래시 복구, (2) 신규 카메라 자동 발견, (3) 원격 WMI 불필요, (4) Master 독립성을 모두 충족.

### 2.4 Decision Summary

| 결정 | 내용 |
|---|---|
| 아키텍처 | Option C: Distributed Supervisor (PC당 Manager Windows Service) |
| Manager↔Agent IPC | CLI args only (kill+respawn). Named pipe 등 불필요 |
| Manager↔Master 통신 | NATS 5개 신규 토픽 |
| 카메라 식별 | WMI PnPDeviceID (HardwareId) — USB 포트 독립 영구 키 |
| AgentId 형식 | `{PCId}_{SHA256(HardwareId)[0:8]}` |
| 재시작 정책 | 지수 백오프 [1, 2, 5, 15, 60]초, 5회 한계 후 영구 드롭 + FATAL alert |
| 안정 판정 | 10분(600초) 무사고 실행 시 카운터 리셋 |
| 로그 형식 | Serilog CompactJsonFormatter (NDJSON), 일일 롤링, 7일 보관 |
| Alert 필터 | ERROR/FATAL 자동 push, WARN은 설정 가능 (기본 OFF) |
| PnP 디바운스 | 1초 |
| 설치 경로 | `C:\HeatingCameraSystem\` (Manager/Agent/logs 통합) |
| Recipe 호환 | CameraAlias 우선, 미설정 시 `Agent_{CameraIndex}` fallback |
| DB 마이그레이션 | CameraSerialSettings → CameraDevice 자동 흡수 (1회, idempotent) |

---

## 3. Component Diagram

### 3.1 전체 시스템 (Manager 추가 후)

```
┌─────────────────────────────┐         ┌──────────────────────────────┐
│      Master PC (WPF)        │         │       Agent PC #N            │
│                             │         │                              │
│  Dashboard / Recipe Editor  │         │  ┌─ AgentManager.exe ──────┐ │
│  Camera Mapping / History   │         │  │  (Windows Service)      │ │
│  Serial Settings            │         │  │  WmiCameraEnumerator    │ │
│  Devices (신규)             │         │  │  AgentSupervisor        │ │
│                             │  NATS   │  │  LogTailService         │ │
│  ┌─ PLC ───┐ ┌─ Shutter ─┐ │◄───────►│  │  LogDumpHandler         │ │
│  └─────────┘ └───────────┘  │         │  │  ManagerCommandHandler  │ │
│  ┌─ LiteDB ──────────────┐  │         │  └──┬──────────────────────┘ │
│  │  Recipe / CameraDevice │  │         │     │ spawn/kill (Process)   │
│  │  History / Mapping     │  │         │     ▼                        │
│  └────────────────────────┘  │         │  Agent.exe #0 (카메라 0)     │
└──────────────┬───────────────┘         │  Agent.exe #1 (카메라 1)     │
               │                         │  ...                         │
               ▼                         └──────────────────────────────┘
       ┌───────────────┐
       │  NATS Server  │
       │  4222 / 8222  │
       └───────────────┘
```

### 3.2 NATS 토픽 맵 (기존 6개 + 신규 5개)

| Subject | 방향 | Payload | 빈도 |
|---|---|---|---|
| **기존** | | | |
| `master.cmd.capture.{AgentId}` | Master → Agent | `CaptureCommandMessage` | Recipe 실행 시 |
| `master.cmd.capture.all` | Master → 전체 Agent | `CaptureCommandMessage` | 브로드캐스트 |
| `master.config.serial.{AgentId}` | Master → Agent | `SerialConfigMessage` | 설정 변경 시 |
| `agent.result.capture.{AgentId}` | Agent → Master | `CaptureResultMessage` | 캡처 완료 시 |
| `agent.status.{AgentId}` | Agent → Master | `AgentStatusMessage` | 5초 주기 |
| `agent.config.serial.ack.{AgentId}` | Agent → Master | `SerialConfigAckMessage` | 설정 ACK |
| **신규** | | | |
| `agent-mgr.inventory.{PCId}` | Manager → Master | `CameraInventoryMessage` | 부팅 + PnP 변경 시 |
| `server.cmd.mgr.{PCId}` | Master → Manager | `ManagerCommandMessage` | 운영자 조작 시 |
| `agent-mgr.log.alert.{PCId}` | Manager → Master | `LogAlertMessage` | ERROR 발생 즉시 |
| `server.req.log.{PCId}` | Master → Manager | `LogDumpRequestMessage` | 운영자 클릭 시 |
| `agent-mgr.log.dump.{PCId}` | Manager → Master | `LogDumpMessage` | 위 요청 응답 |

### 3.3 파일 시스템 배치 (Agent PC)

```
C:\HeatingCameraSystem\
├── Manager\
│   ├── HeatingCameraSystem.AgentManager.exe
│   ├── manager-settings.json
│   └── manager-state.json
├── Agent\
│   └── HeatingCameraSystem.Agent.exe
└── logs\
    ├── {AgentId_1}\
    │   └── agent-20260620.log   (NDJSON, Serilog)
    └── {AgentId_2}\
        └── agent-20260620.log
```

---

## 4. Data Model

### 4.1 CameraDevice (Core/Models — LiteDB 컬렉션)

```csharp
public class CameraDevice
{
    public string   HardwareId      { get; set; }  // PK — WMI PnPDeviceID
    public string   AgentId         { get; set; }  // {PCId}_{HardwareIdHash8}
    public string   Alias           { get; set; }  // 운영자 부여 이름
    public string   PCId            { get; set; }  // 머신 식별자
    public int      OpenCvIndex     { get; set; }  // VideoCapture 인덱스
    public CameraSerialSettings SerialSettings { get; set; }
    public bool     IsApproved      { get; set; }
    public DateTime FirstSeen       { get; set; }
    public DateTime LastSeen        { get; set; }
}
```

### 4.2 DiscoveredCamera (Core/Models — 순간 스냅샷)

```csharp
public class DiscoveredCamera
{
    public string HardwareId    { get; set; }  // WMI PnPDeviceID
    public string FriendlyName  { get; set; }  // 표시용
    public int    OpenCvIndex   { get; set; }  // DirectShow 열거 순서
}

public enum PnpChangeType { Arrival, Removal }

public class PnpChange
{
    public PnpChangeType  ChangeType { get; set; }
    public DiscoveredCamera Camera   { get; set; }
}
```

### 4.3 CameraEntry (AgentManager/State — manager-state.json)

```csharp
public class CameraEntry
{
    public string   HardwareId    { get; set; }
    public string   AgentId       { get; set; }
    public string   Alias         { get; set; }
    public int      OpenCvIndex   { get; set; }
    public string   StoragePath   { get; set; }
    public bool     IsApproved    { get; set; }
    public DateTime FirstSeen     { get; set; }
    public DateTime LastSeen      { get; set; }
    public int      RestartFails  { get; set; }
    public bool     IsDisabled    { get; set; }
}
```

### 4.4 NATS 메시지 모델 (Core/Models/ManagerMessages.cs)

| 모델 | 토픽 | 핵심 필드 |
|---|---|---|
| `CameraInventoryMessage` | `agent-mgr.inventory.{PCId}` | PCId, Cameras: `List<CameraInventoryItem>` |
| `ManagerCommandMessage` | `server.cmd.mgr.{PCId}` | Op: `ManagerCommandOp` (6종), HardwareId, Payload |
| `LogAlertMessage` | `agent-mgr.log.alert.{PCId}` | AgentId, Level: `LogAlertLevel`, Message |
| `LogDumpRequestMessage` | `server.req.log.{PCId}` | AgentId, MaxBytes (기본 5MB) |
| `LogDumpMessage` | `agent-mgr.log.dump.{PCId}` | GzipBytes, OriginalBytes, IsTruncated |

### 4.5 ManagerCommandOp 열거형

```csharp
public enum ManagerCommandOp
{
    Approve,     // 신규 카메라 승인 → Agent spawn
    Reject,      // 승인 거부 → Agent 미시작
    Rename,      // Alias 변경
    SetSerial,   // 시리얼 설정 변경 (Payload = JSON CameraSerialSettings)
    Restart,     // Agent 강제 재시작 (kill → spawn)
    Disable      // Agent 영구 비활성화
}
```

### 4.6 RecipeStep 확장

```csharp
public class RecipeStep
{
    public string StepId { get; set; }
    public int    CameraIndex { get; set; }
    public string? CameraAlias { get; set; }  // 신규: 비어있으면 CameraIndex fallback
    public int    TargetPositionIndex { get; set; }
    public float  TargetBlackBodyTemperature { get; set; }
}
```

### 4.7 DB 마이그레이션 전략

```
[Master 기동 시 1회]
  1. data.db 백업 → data.db.bak.{timestamp}
  2. CameraSerialSettings 컬렉션 전체 읽기
  3. 각 레코드를 CameraDevice 컬렉션에 upsert:
     - HardwareId = "legacy_{CameraIndex}"
     - AgentId = "Agent_{CameraIndex}"
     - Alias = "(legacy CAM-{CameraIndex})"
     - IsApproved = true
  4. CameraSerialSettings 컬렉션 drop
  5. _migrations 컬렉션에 완료 플래그 저장 (재실행 방지)
```

---

## 5. Interface Specification

### 5.1 ICameraEnumerator

```csharp
public interface ICameraEnumerator : IDisposable
{
    IReadOnlyList<DiscoveredCamera> Enumerate();
    event Action<PnpChange> Changed;
    void StartWatching();
    void StopWatching();
}
```

구현체:
- `WmiCameraEnumerator` — Win32_PnPEntity WQL 쿼리 + ManagementEventWatcher (1초 디바운스)
- `FakeCameraEnumerator` — 고정 2개 카메라 + SimulateArrival/Removal 테스트 헬퍼

### 5.2 ICameraDeviceRepository

```csharp
public interface ICameraDeviceRepository
{
    Task<IEnumerable<CameraDevice>> GetAllAsync();
    Task<CameraDevice?>             GetByHardwareIdAsync(string hardwareId);
    Task<CameraDevice?>             GetByAliasAsync(string alias);
    Task<IEnumerable<CameraDevice>> GetByPCIdAsync(string pcId);
    Task                            UpsertAsync(CameraDevice device);
    Task                            DeleteByHardwareIdAsync(string hardwareId);
}
```

구현체: `LiteDbCameraDeviceRepository` — HardwareId unique index

### 5.3 INatsCommunicationService 확장 (10 메서드 추가)

```csharp
// Inventory
Task PublishCameraInventoryAsync(CameraInventoryMessage message);
Task SubscribeCameraInventoryAsync(Action<CameraInventoryMessage> onMessageReceived);

// Manager Command
Task PublishManagerCommandAsync(ManagerCommandMessage message);
Task SubscribeManagerCommandAsync(string pcId, Action<ManagerCommandMessage> onMessageReceived);

// Log Alert
Task PublishLogAlertAsync(LogAlertMessage message);
Task SubscribeLogAlertAsync(Action<LogAlertMessage> onMessageReceived);

// Log Dump Request
Task PublishLogDumpRequestAsync(LogDumpRequestMessage message);
Task SubscribeLogDumpRequestAsync(string pcId, Action<LogDumpRequestMessage> onMessageReceived);

// Log Dump Response
Task PublishLogDumpAsync(LogDumpMessage message);
Task SubscribeLogDumpAsync(string pcId, Action<LogDumpMessage> onMessageReceived);
```

---

## 6. Module Specification

### 6.1 AgentSupervisor

| 항목 | 내용 |
|---|---|
| 역할 | 카메라별 Agent.exe 프로세스 spawn/kill/respawn 관리 |
| 재시작 정책 | 지수 백오프 [1, 2, 5, 15, 60]초 |
| 최대 재시도 | 5회 연속 실패 시 영구 드롭 + FATAL alert |
| 안정 판정 | 10분(600초) 무사고 실행 시 카운터 0 리셋 |
| Agent 종료 | CloseMainWindow → 5초 timeout → Process.Kill |
| SimulationMode | 실제 spawn 스킵 (AgentExePath 불필요) |

Agent CLI args:
```
{AgentId} {NatsUrl} {OpenCvIndex} "{StoragePath}" "{LogPath}" {SimulationMode}
```

### 6.2 InventoryPublisher

| 항목 | 내용 |
|---|---|
| 역할 | manager-state.json + supervisor 실행 상태를 CameraInventoryMessage로 NATS publish |
| 발행 시점 | 부팅 1회, PnP 변경 시, 명령 처리 후 |

### 6.3 LogTailService

| 항목 | 내용 |
|---|---|
| 역할 | 각 Agent의 NDJSON 로그 파일을 FileSystemWatcher + sequential read로 tail |
| Alert 조건 | `@l` ∈ {Error, Fatal} → 즉시 push. Warning은 `WarnAlertEnabled` 설정 |
| NDJSON 파싱 | `@l` (level), `@mt` 또는 `@m` (message) 필드 |

### 6.4 LogDumpHandler

| 항목 | 내용 |
|---|---|
| 역할 | Master 요청(LogDumpRequestMessage) 시 지정 Agent 최근 N MB 로그 → gzip → NATS 응답 |
| 최대 크기 | 요청측 MaxBytes (기본 5MB) |
| 파일 선택 | 최신 파일부터 역순 읽기 |

### 6.5 ManagerCommandHandler

| 항목 | 내용 |
|---|---|
| 역할 | Master에서 발행한 ManagerCommandMessage 6종 처리 |
| AgentId 생성 | `{PCId}_{SHA256(HardwareId)[0:8].ToLower()}` — Approve 시점 |
| 처리 후 | InventoryPublisher.PublishAsync() 호출 (상태 갱신 알림) |

### 6.6 ManagerStateStore

| 항목 | 내용 |
|---|---|
| 역할 | manager-state.json 영속 저장 + 인메모리 캐시 |
| 스레드 안전 | lock 기반 |
| 저장 시점 | Upsert/Remove 시 즉시 디스크 flush |

### 6.7 ManagerWorker (Program.cs BackgroundService)

부팅 시퀀스:
1. NATS 연결
2. ManagerCommand + LogDumpRequest 구독
3. WMI 카메라 열거 → state와 diff merge (신규 = IsApproved=false)
4. 승인된 카메라 Agent spawn
5. 모든 Agent 로그 디렉터리 tail 시작
6. PnP watcher 시작
7. AgentDropped 이벤트 → FATAL alert
8. 초기 inventory publish

---

## 7. UI/UX Design

### 7.1 DevicesView 레이아웃

```
┌─────────────────────────────────────────────────────────────────┐
│  디바이스 관리 (Devices)                                         │
├───────────────────────────────────────────┬─────────────────────┤
│  [DataGrid]                              │  액션                │
│  Status │ PCId │ Alias │ AgentId │ HwId  │                     │
│  ───────┼──────┼───────┼─────────┼────── │  Alias: [________]  │
│  ✅     │ Bay1 │ Top   │ Bay1_ab │ USB.. │                     │
│  ⏳     │ Bay1 │       │         │ USB.. │  [승인] [거부]       │
│  ❌     │ Bay2 │ Left  │ Bay2_cd │ USB.. │  [이름저장] [로그]   │
│                                          │                     │
│                                          │  ┌───────────────┐  │
│                                          │  │ 로그 뷰어     │  │
│                                          │  │ (NDJSON)      │  │
│                                          │  └───────────────┘  │
├──────────────────────────────────────────┴─────────────────────┤
│  상태: 인벤토리 갱신: 3 cameras from Bay1                       │
└─────────────────────────────────────────────────────────────────┘
```

### 7.2 상태 표시

| 상태 | DataGrid Status | 의미 |
|---|---|---|
| `IsRunning=true` | `True` (초록) | Agent 프로세스 실행 중 |
| `IsApproved=false` | `False` (노란) | 신규 발견, 승인 대기 |
| `IsRunning=false, IsApproved=true` | `False` (빨간) | Agent 종료/크래시 |

### 7.3 Alert 표시

- `HasAlert=true` 시 빨간 배경 Border에 `LastAlert` 메시지 표시
- ERROR/FATAL 수신 즉시 `Application.Dispatcher.Invoke`로 UI 갱신

### 7.4 로그 뷰어

- "로그 가져오기" 버튼 → `LogDumpRequestMessage` 발행 → 30초 타임아웃
- 수신한 gzip 바이트 해제 → TextBox에 표시 (Consolas, 읽기 전용)

---

## 8. Error Handling

| 상황 | 처리 |
|---|---|
| Agent 비정상 종료 | AgentSupervisor: 지수 백오프 재시작, 5회 초과 시 영구 드롭 + FATAL alert |
| WMI 열거 실패 (권한 부족) | install.ps1에서 LocalSystem 계정 명시. 실패 시 빈 목록 반환 |
| PnP 이벤트 race (remove+arrival 동시) | 1초 디바운스로 최종 상태만 반영 |
| Manager 크래시 | Windows Service Recovery 정책: 3회 자동 재시작 (5초 간격) |
| NATS 연결 끊김 | NATS.Net 라이브러리 내부 자동 재연결 (기존 패턴) |
| 로그 파일 접근 실패 | LogTailService: Warning 로그 후 계속 시도 (FileShare.ReadWrite) |
| 로그 dump 요청 시 파일 없음 | LogDumpHandler: Warning 로그 후 무응답 |
| DB 마이그레이션 실패 | data.db 자동 백업으로 복원 가능. 에러 로그 + Master 정상 기동 |
| Recipe CameraAlias 미매칭 | RecipeEngine: DB lookup null → CameraIndex fallback |

---

## 9. Security Considerations

| 항목 | 대응 |
|---|---|
| Manager Windows Service 권한 | LocalSystem 계정 (install.ps1 명시) |
| NATS 인증 | 현재 없음 (로컬 네트워크 전용, 기존 설계 유지) |
| manager-state.json | 로컬 파일, 서비스 계정만 접근 |
| AgentId 해시 | SHA256 첫 8자 — 충돌 확률 무시 가능 (2^32 namespace) |
| 방화벽 | install.ps1에서 4222/tcp outbound 규칙 자동 추가 |

---

## 10. Test Plan

### 10.1 테스트 범위

| Type | 대상 | 건수 | 도구 |
|---|---|---|---|
| L1: 단위 | FakeCameraEnumerator (열거, PnP 이벤트) | 3 | xUnit |
| L1: 단위 | ManagerStateStore (CRUD, 디스크 영속) | 3 | xUnit |
| L1: 단위 | ManagerCommandHandler.BuildAgentId (결정적 해시) | 2 | xUnit |
| L1: 단위 | LiteDbCameraDeviceRepository (CRUD, Alias lookup) | 4 | xUnit + LiteDB :memory: |
| L1: 단위 | MigrationService (마이그레이션 + idempotent) | 2 | xUnit + LiteDB :memory: |
| L2: 단위 | RecipeEngine CameraAlias (Alias 우선, fallback, 미매칭 fallback) | 3 | xUnit + Moq |
| | **합계** | **17** | |

### 10.2 테스트 시나리오 상세

| # | 대상 | 시나리오 | 기대 결과 |
|---|---|---|---|
| 1 | FakeCameraEnumerator | Enumerate() | 2개 카메라, HardwareId 비어있지 않음 |
| 2 | FakeCameraEnumerator | SimulateArrival | Changed 이벤트 발생, Arrival 타입 |
| 3 | FakeCameraEnumerator | SimulateRemoval | Changed 이벤트 발생, Removal 타입 |
| 4 | ManagerStateStore | Upsert + GetByHardwareId | 라운드트립 일치 |
| 5 | ManagerStateStore | 디스크 저장 + 새 인스턴스 Load | 데이터 복원 |
| 6 | ManagerStateStore | Remove | GetByHardwareId null |
| 7 | BuildAgentId | 동일 입력 | 동일 출력 (결정적) |
| 8 | BuildAgentId | 다른 HardwareId | 다른 해시 |
| 9 | CameraDeviceRepo | Upsert + GetByHardwareId | 라운드트립 일치 |
| 10 | CameraDeviceRepo | GetByAlias | 올바른 디바이스 반환 |
| 11 | CameraDeviceRepo | GetByAlias 미존재 | null |
| 12 | CameraDeviceRepo | Delete | GetByHardwareId null |
| 13 | MigrationService | Run (기존 데이터) | CameraDevice 생성, 기존 컬렉션 삭제 |
| 14 | MigrationService | Run 2회 | idempotent (중복 없음) |
| 15 | RecipeEngine | CameraAlias → DB lookup | AgentId = DB의 AgentId |
| 16 | RecipeEngine | CameraAlias 미설정 | AgentId = `Agent_{CameraIndex}` |
| 17 | RecipeEngine | CameraAlias 미매칭 | AgentId = `Agent_{CameraIndex}` fallback |

---

## 11. Clean Architecture (레이어 맵)

| 컴포넌트 | 레이어 | 위치 |
|---|---|---|
| `CameraDevice`, `DiscoveredCamera`, `ManagerMessages` | Core (Model) | `Core/Models/` |
| `ICameraEnumerator`, `ICameraDeviceRepository` | Core (Interface) | `Core/Interfaces/` |
| `INatsCommunicationService` 확장 | Core (Interface) | `Core/Interfaces/` |
| `RecipeStep.CameraAlias` | Core (Model) | `Core/Models/RecipeModels.cs` |
| `AgentConfig.LogPath` | Core (Config) | `Core/Config/` |
| `WmiCameraEnumerator` | Protocols (Infrastructure) | `Protocols/` |
| `FakeCameraEnumerator` | Protocols (Simulation) | `Protocols/Simulation/` |
| `NatsCommunicationService` 확장 | Protocols (Infrastructure) | `Protocols/` |
| `AgentSupervisor`, `InventoryPublisher`, `LogTailService`, `LogDumpHandler`, `ManagerCommandHandler` | AgentManager (App) | `AgentManager/Services/` |
| `ManagerSettings` | AgentManager (Config) | `AgentManager/Config/` |
| `ManagerStateStore`, `CameraEntry` | AgentManager (State) | `AgentManager/State/` |
| `ManagerWorker` (Program.cs) | AgentManager (Host) | `AgentManager/` |
| `LiteDbCameraDeviceRepository` | Master (Infrastructure) | `Master/Services/` |
| `MigrationService` | Master (Infrastructure) | `Master/Services/` |
| `DevicesViewModel` | Master (Presentation) | `Master/ViewModels/` |
| `DevicesView` | Master (Presentation) | `Master/Views/` |
| `RecipeEngine` (Alias 변환) | Master (App) | `Master/Services/` |
| `AppServices` (wiring) | Master (App) | `Master/Services/` |
| Agent `Program.cs` (Serilog) | Agent (App) | `Agent/` |

---

## 12. Coding Convention

| 항목 | 규칙 |
|---|---|
| Repository | `LiteDbXxxRepository : IXxxRepository` 패턴 유지 |
| ViewModel | `ObservableObject` 상속, `[ObservableProperty]`, `[RelayCommand]` |
| NATS 구독 | `_ = Task.Run(async () => { await foreach ... })` 기존 패턴 유지 |
| Nullable | 모든 `string` 프로퍼티 기본값 명시 (`= string.Empty`) |
| 예외 | catch 후 `Debug.WriteLine` 또는 `ILogger` + 필요 시 UI 상태 업데이트, 빈 catch 금지 |
| 플랫폼 | WMI 사용 클래스에 `[SupportedOSPlatform("windows")]` 명시 |
| AgentManager | `Microsoft.Extensions.Hosting` + `WindowsServices` 표준 Worker 패턴 |
| 로깅 | Agent: `Serilog` + `CompactJsonFormatter`, Manager: `Microsoft.Extensions.Logging` |

---

## 13. Implementation Guide

### 13.1 구현 순서 (의존성 기준)

```
[Phase 1] Core 모델 + 인터페이스 (의존성 없음)
    CameraDevice.cs, DiscoveredCamera.cs, ManagerMessages.cs
    ICameraEnumerator.cs, ICameraDeviceRepository.cs
    INatsCommunicationService.cs (+10 메서드)
    RecipeModels.cs (CameraAlias 필드)
    AgentConfig.cs (LogPath 필드)

[Phase 2] Protocols (Core 참조)
    NatsCommunicationService.cs (+10 메서드 구현)
    WmiCameraEnumerator.cs, FakeCameraEnumerator.cs
    Protocols.csproj (System.Management 패키지)

[Phase 3] AgentManager 신규 프로젝트 (Core + Protocols 참조)
    .csproj (net8.0, win-x64, Hosting + WindowsServices + System.Management)
    Config/ManagerSettings.cs
    State/ManagerStateStore.cs
    Services/AgentSupervisor.cs, InventoryPublisher.cs
    Services/LogTailService.cs, LogDumpHandler.cs, ManagerCommandHandler.cs
    Program.cs (ManagerWorker BackgroundService)

[Phase 4] Agent (Core + Protocols 참조)
    Agent.csproj (Serilog 5개 패키지)
    Program.cs (Serilog NDJSON sink, LogPath CLI arg)

[Phase 5] Master (Core + Protocols + LiteDB 참조)
    LiteDbCameraDeviceRepository.cs
    MigrationService.cs
    AppServices.cs (CameraDeviceRepo + Migration 호출 + RecipeEngine deviceRepo 전달)
    RecipeEngine.cs (CameraAlias → DB lookup → AgentId 변환)
    DevicesViewModel.cs + DevicesView.xaml
    MainViewModel.cs + MainWindow.xaml (Devices 탭)

[Phase 6] Tests + Docs
    AgentManagerTests.cs (17개)
    install.ps1
    docs/manual/00-overview.md 갱신
```

### 13.2 핵심 구현 패턴

#### AgentId 생성
```csharp
public static string BuildAgentId(string pcId, string hardwareId)
{
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(hardwareId));
    string hash8 = Convert.ToHexString(hash)[..8].ToLower();
    return $"{pcId}_{hash8}";
}
```

#### RecipeEngine CameraAlias 변환
```csharp
private async Task<string> ResolveAgentIdAsync(RecipeStep step)
{
    if (!string.IsNullOrEmpty(step.CameraAlias) && _deviceRepo != null)
    {
        var device = await _deviceRepo.GetByAliasAsync(step.CameraAlias);
        if (device != null && !string.IsNullOrEmpty(device.AgentId))
            return device.AgentId;
    }
    return $"Agent_{step.CameraIndex}";
}
```

#### AgentSupervisor 백오프
```csharp
private static readonly int[] BackoffSeconds = { 1, 2, 5, 15, 60 };
private const int MaxRestartAttempts = 5;
private const int StableRunSeconds = 600;
```

#### Manager Windows Service 등록 (install.ps1)
```powershell
sc.exe create HCS-Manager binPath= "\"$exePath\" \"$InstallRoot\"" start= auto obj= LocalSystem
sc.exe failure HCS-Manager reset= 0 actions= restart/5000/restart/5000/restart/5000
```

---

## 14. State Machine — Camera Lifecycle (per camera)

```
[Discovered] ──IsApproved=false──► [Pending]
                                       │
                                  (운영자 Approve)
                                       │
                                       ▼
                                   [Approved] ──spawn──► [Running]
                                       ▲                    │
                                       │           (Agent 비정상 종료)
                                       │                    ▼
                                       │               [Crashing]
                                       │                    │
                                       │         (5회 미만, 백오프 후)
                                       │                    │
                                       └────────────────────┘
                                                    │
                                          (5회 초과)
                                                    ▼
                                                [Dropped]
                                         (FATAL alert + 영구 비활성)
```

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-06-21 | Initial design — Option C (Distributed Supervisor) 채택, 전체 모듈 분할 + 구현 가이드. 구현 완료 후 정식 문서화. | - |
