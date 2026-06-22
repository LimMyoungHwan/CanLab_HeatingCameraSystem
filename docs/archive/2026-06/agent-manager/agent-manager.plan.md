# agent-manager Planning Document

> **Summary**: PC 당 Supervisor 프로세스(Manager) 도입. 카메라 자동 발견 + Agent 프로세스 lifecycle 관리 + 로그 수집/포워딩. Server 는 Alias 기반으로 카메라 인벤토리를 원격 관리.
>
> **Project**: HeatingCameraSystem
> **Version**: 0.1
> **Author**: -
> **Date**: 2026-06-20
> **Status**: Draft

---

## Executive Summary

| Perspective | Content |
|---|---|
| **Problem** | (1) 카메라마다 Agent 인스턴스를 운영자가 수동 실행/관리해야 함. (2) USB 포트 옮기면 OpenCvIndex 가 어긋나 Recipe 깨짐. (3) Agent 로그가 PC 로컬에 갇혀 있어 운영자가 RDP 로 들어가야 확인 가능. |
| **Solution** | Agent Manager (PC당 1 프로세스, Windows Service) 도입. WMI 로 USB 카메라 자동 열거(HardwareId 영구 키), PnP 이벤트로 신규 감지, Server 승인 후 Agent 프로세스 spawn. 설정 변경 = kill+respawn. Agent NDJSON 로그를 Manager 가 tail → ERROR 자동 push + on-demand dump. |
| **Function/UX Effect** | 운영자: PC당 Manager 서비스만 한 번 등록하면 끝. 카메라 꽂으면 Server Devices 탭에 자동 등장 → Alias 입력 + Approve → 즉시 사용. USB 포트 옮겨도 같은 카메라로 인식. Server 에서 모든 PC 의 카메라/로그 한눈에. |
| **Core Value** | 운영 자동화 + 카메라 식별 안정성 + 분산 PC 가시성 — 카메라 N대 N PC 운영을 1인이 단일 GUI 로 관리 가능. |

---

## Context Anchor

| Key | Value |
|---|---|
| **WHY** | 다중 PC × 다중 카메라 운영 시 수동 인스턴스 관리 부담 + USB 포트 이동으로 인한 식별 불안정 + 분산 로그 가시성 부재 |
| **WHO** | 열화상 카메라 시스템 운영자 (Server PC 단일 사용자), Agent PC 가 1~N대 분산 |
| **RISK** | (1) Manager↔Agent 프로세스 격리 부작용 (PnP 동안 race) (2) WMI 권한/성능 (3) 기존 Recipe 의 CameraIndex 마이그레이션 (4) NATS 토픽 5개 신설로 인한 메시지 양 증가 |
| **SUCCESS** | (1) Manager 서비스 1개 등록 후 PC 재부팅해도 등록된 카메라 자동 복구 (2) USB 포트 변경 후 자동 인식 (3) Agent ERROR 발생 5초 내 Server Devices 탭에 빨간 점 (4) Recipe 가 Alias 로 실행되어 카메라 1대 대체해도 무중단 운영 |
| **SCOPE** | Manager 신규 프로젝트 + Agent 단순화 + Master Devices 탭 + 신규 NATS 토픽 5개 + DB 마이그레이션 (CameraSerialSettings → CameraDevice) + 시뮬레이션 모드 + NDJSON 로그 파이프라인 |

---

## 1. Overview

### 1.1 Purpose

현재 1 Agent = 1 카메라 구조에서 카메라마다 인스턴스를 운영자가 수동 실행해야 한다. 카메라가 OpenCV 열거 인덱스로 식별되어 USB 포트 이동 시 Recipe 가 깨진다. Agent 로그도 분산되어 가시성이 떨어진다.

**Supervisor 패턴**으로 PC 당 Agent Manager 한 개를 두고, Manager 가:
- 카메라 자동 발견 (WMI + PnP event)
- HardwareId 기반 영구 식별
- Agent 프로세스 spawn/kill/respawn (args only IPC)
- NDJSON 로그 tail + ERROR 알림 / on-demand dump

Server 는 Manager 와 NATS 5개 신규 토픽으로 통신해 인벤토리 수집·승인·설정·로그 dump 를 처리. 캡처 명령/이미지 데이터는 기존대로 Server↔Agent 직통 (Manager 우회 — 데이터 경로 병목 방지).

### 1.2 Background

- 현 구조: Agent 1프로세스 = 1카메라, agent.json + CLI 인수, 운영자 수동 실행 (manual)
- 이전 PDCA: agent-status-display (CameraStatus 3단계), camera-serial-config (NATS 시리얼 설정 전송)
- 이전 결정: SimulationMode 2단계 (Master/Agent 별도) — Manager 도 동일 패턴 적용
- 기존 NATS 토픽 6개 모두 유지. Manager↔Server 통신은 신규 토픽으로 격리

### 1.3 Related Documents

- `AGENTS.md` — NATS 토픽 규칙
- `docs/manual/00-overview.md` — 시스템 아키텍처
- `docs/01-plan/features/agent-status-display.plan.md` — CameraStatus enum 도입
- `docs/01-plan/features/camera-serial-config.plan.md` — 시리얼 설정 원격 전송 패턴

---

## 2. Scope

### 2.1 In Scope

- [ ] **신규 프로젝트** `HeatingCameraSystem.AgentManager` (`.NET 8`, Console + Windows Service 호스팅)
- [ ] **WMI 카메라 열거자** `ICameraEnumerator` + `WmiCameraEnumerator` (System.Management) + `FakeCameraEnumerator` (시뮬)
- [ ] **PnP 이벤트 감지** `ManagementEventWatcher` (USB arrival/removal, 1초 디바운스)
- [ ] **Agent 프로세스 supervisor** spawn/kill/respawn (지수 백오프 1→60s, 5회 성공 시 리셋)
- [ ] **Manager 상태 저장** `manager-state.json` (등록된 카메라 + Alias + LastSeen + Approve 여부)
- [ ] **신규 NATS 메시지 5종** `CameraInventoryMessage`, `ManagerCommandMessage`, `LogAlertMessage`, `LogDumpRequestMessage`, `LogDumpMessage`
- [ ] **신규 NATS 토픽 5개** `agent-mgr.inventory.{PCId}`, `server.cmd.mgr.{PCId}`, `agent-mgr.log.alert.{PCId}`, `server.req.log.{PCId}`, `agent-mgr.log.dump.{PCId}`
- [ ] **INatsCommunicationService 확장** (Manager 토픽 publish/subscribe 8 메서드)
- [ ] **Agent 단순화** — args-only 모드 강화, NDJSON 로그(Serilog File sink, 일일 롤링 + 7일 보관), agent.json 옵션화 유지
- [ ] **Master 신규 Devices 탭** `DevicesView.xaml` + `DevicesViewModel` (인벤토리 표시, pending 승인, Alias 편집, 로그 뷰어)
- [ ] **DB 마이그레이션** `CameraSerialSettings` 삭제 → `CameraDevice` 컬렉션 신설 (`HardwareId` PK, `AgentId`, `Alias`, `PCId`, `OpenCvIndex`, `SerialSettings`, `IsApproved`, `FirstSeen`, `LastSeen`) + 자동 마이그레이션 스크립트 (Master 기동 시 1회)
- [ ] **RecipeStep.CameraAlias 필드 추가** (`CameraIndex` 유지, 둘 다 있으면 Alias 우선). Server `RecipeEngine` 이 Alias → DB lookup → AgentId 변환 후 NATS 발행
- [ ] **install.ps1 스크립트** (Manager Windows Service 등록 + NATS URL 대화형 입력 + 방화벽 규칙)
- [ ] **시뮬레이션 모드** — Manager `SimulationMode=true` 시 `FakeCameraEnumerator` (2~3개 가짜 카메라) + Agent 프로세스 대신 in-process Fake Agent 풀
- [ ] **테스트** — Manager 단위 (열거 / supervisor / log tail / alert filter) + 통합 (Fake enumerator → spawn → inventory publish 라운드트립)
- [ ] **매뉴얼 갱신** — `00-overview` 아키텍처 그림 수정, `01-installation` Manager 설치 추가, `02-configuration` manager-state.json + 신규 NATS 토픽, `03-usage` Devices 탭 사용법

### 2.2 Out of Scope

- 카메라 데이터가 **열화상 vs 일반 RGB** 인지 자동 판별 (향후 #18, 별도 PDCA)
- Manager↔Agent live IPC (named pipe 등) — args only 로 충분
- Manager 자체의 원격 업데이트 (수동 install.ps1 재실행 권장)
- 비-Windows OS 지원 (WMI 의존)
- Recipe 단계 소요시간 추정 (#14, 실 PLC 도입 후)
- 카메라 영상 스트리밍 (현재 캡처만)

---

## 3. Requirements

### 3.1 Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| FR-01 | Manager 가 PC 시작 시 자동 실행 (Windows Service) | High |
| FR-02 | Manager 가 WMI 로 카메라(Image/Camera PnPClass) 전체 열거 후 inventory publish | High |
| FR-03 | Manager 가 USB PnP 이벤트(arrival/removal) 1초 디바운스 후 inventory 재발행 | High |
| FR-04 | 신규 HardwareId 감지 시 `IsApproved=false` 로 표시. Server 운영자 명시 승인까지 Agent spawn 금지 | High |
| FR-05 | 승인 시 Manager 가 `{PCId}_{HardwareIdHash8}` 형식 AgentId 자동 부여 + manager-state.json 영구 저장 | High |
| FR-06 | Manager 가 등록된 카메라마다 Agent.exe spawn (args: AgentId, NatsUrl, OpenCvIndex, StoragePath, LogPath, SimulationMode) | High |
| FR-07 | Agent 비정상 종료 시 Manager 가 지수 백오프(1→2→5→15→60s) 로 최대 5회 재시도. 5회 연속 실패 시 영구 드롭 + Server LogAlert push | High |
| FR-08 | Agent 5회 내 안정 실행 후 카운터 리셋 (다음 크래시 시 1s 부터 재시작) | High |
| FR-09 | Server 가 설정 변경(`ManagerCommandMessage`) 발행 시 Manager 가 해당 Agent kill → manager-state.json 갱신 → 새 args 로 respawn | High |
| FR-10 | Agent 가 Serilog File sink 로 NDJSON 형식 로그 작성 (일일 롤링, 7일 보관, `C:\HeatingCameraSystem\logs\{AgentId}\agent-yyyyMMdd.log`) | High |
| FR-11 | Manager 가 각 Agent 로그 파일 tail. `level=ERROR/FATAL` 라인 즉시 `agent-mgr.log.alert.{PCId}` push (WARN 은 설정 가능, 기본 OFF) | High |
| FR-12 | Server 가 `server.req.log.{PCId}` 발행 시 Manager 가 지정 Agent 의 최근 N MB 로그 gzip → `agent-mgr.log.dump.{PCId}` 응답 | High |
| FR-13 | 신규 NATS 토픽 5개 메시지 모델: `CameraInventoryMessage`, `ManagerCommandMessage`, `LogAlertMessage`, `LogDumpRequestMessage`, `LogDumpMessage` (Core/Models) | High |
| FR-14 | Master 신규 Devices 탭 — 전체 PC 인벤토리 표시 (PCId 별 카메라 목록 + 상태 점), pending 승인 + Alias 편집 + 시리얼 설정 편집 + 로그 뷰어 | High |
| FR-15 | LiteDB `CameraDevice` 컬렉션 신설 (HardwareId PK, AgentId, Alias, PCId, OpenCvIndex, SerialSettings, IsApproved, FirstSeen, LastSeen). 기존 `CameraSerialSettings` 컬렉션 자동 마이그레이션 (Alias 미부여 상태로 흡수) 후 삭제 | High |
| FR-16 | `RecipeStep.CameraAlias` 필드 신설. 둘 다 있으면 Alias 우선. Server `RecipeEngine` 이 Alias → DB lookup → AgentId 변환 후 기존 `master.cmd.capture.{AgentId}` 발행 | High |
| FR-17 | Manager `SimulationMode=true` 시 `FakeCameraEnumerator` (가짜 카메라 2~3개) + Agent 프로세스 대신 in-process Fake Agent 로 동작 | Medium |
| FR-18 | install.ps1 스크립트 — sc create + start + NATS URL 대화형 입력 + 방화벽 4222/tcp outbound 규칙 | Medium |
| FR-19 | manager-state.json schema: `{ PCId, Cameras: [{ HardwareId, AgentId, Alias, OpenCvIndex, StoragePath, IsApproved, FirstSeen, LastSeen, RestartFails }] }` | High |
| FR-20 | Manager 종료(SIGINT/서비스 stop) 시 모든 Agent 프로세스 graceful kill (Process.CloseMainWindow → 5s timeout → Process.Kill) | High |

### 3.2 Non-Functional Requirements

| Category | Criteria |
|---|---|
| 가용성 | Manager 크래시 시 Windows Service Recovery 로 자동 재시작 (install.ps1 가 설정) |
| 성능 | WMI 열거 < 2 초, PnP 이벤트 → inventory publish < 3 초 (디바운스 포함), Agent spawn < 1 초 |
| 보안 | Manager Windows Service 는 LocalSystem 계정. NATS auth 는 향후 별도. install.ps1 에 권한 상승 (관리자) 확인 |
| 호환성 | 기존 Recipe (CameraIndex 기반) 100% 동작 — `CameraAlias` 미사용 시 기존 동작 그대로 |
| UI 스레드 | Devices 탭의 NATS 콜백 → `Dispatcher.Invoke` 패턴 일관 적용 |
| 로그 양 | Manager push (ERROR only) 평시 0/분, 비정상 시 < 60/분. Server 가 받는 inventory 메시지 평시 카메라 변동 시에만 |
| 마이그레이션 | DB 마이그레이션 1회성, idempotent (재실행해도 안전). 백업 자동 생성 (`data.db.bak.<timestamp>`) |

---

## 4. Success Criteria

- [ ] Manager Windows Service install.ps1 1회 실행 후 PC 재부팅 → 등록된 카메라 자동 인식 + Agent 자동 시작
- [ ] 새 USB 카메라 꽂으면 Server Devices 탭에 5초 내 pending 으로 표시
- [ ] 운영자 Alias 입력 + Approve 클릭 후 10초 내 Agent 프로세스 시작 + 캡처 가능
- [ ] USB 포트 옮겨 다시 꽂으면 동일 HardwareId 로 매칭 → 기존 Alias 유지, 신규 pending 등록 안 됨
- [ ] Agent 강제 종료 (Task Manager kill) → 1s 내 Manager 재시작 시도, 안정 실행 시 카운터 리셋
- [ ] Agent 가 `_logger.LogError(...)` 호출 → 5초 내 Server Devices 탭에 빨간 점 + 알림
- [ ] Server Devices 탭에서 "Get Logs" 클릭 → 30초 내 최근 N MB 로그 다운로드 (gzip)
- [ ] 기존 Recipe (CameraIndex 사용) 시뮬 실행 시 100% 정상 (FR-16 fallback)
- [ ] 신규 Recipe (CameraAlias 사용) 시뮬 실행 시 정상
- [ ] `dotnet build` 0 errors / 0 warnings
- [ ] `dotnet test` 기존 42 + 신규 (Manager 단위 ≥ 10 + 통합 ≥ 3) 통과
- [ ] E2E sim runner 동작 (FakeEnumerator → 가짜 카메라 2개 → inventory → 자동 Approve → Recipe 실행)

---

## 5. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| WMI 권한 부족 (Manager 가 LocalSystem 아니면 일부 PnP 속성 차단) | High | install.ps1 가 sc create 시 obj=LocalSystem 명시 + 권한 검증 |
| OpenCV `VideoCapture(index)` 와 WMI 열거 순서 매칭 불안정 | High | DirectShow 열거 순서를 1차 추정으로 사용 + Manager 가 spawn 한 Agent 가 실제 카메라 열기 실패 시 다음 인덱스 시도. 매칭 실패한 카메라는 pending 으로 표시해 운영자 개입 |
| USB 포트 옮길 때 PnP 이벤트 race (remove + arrival 동시) | Medium | 1초 디바운스 + inventory 비교(diff 기반) |
| 기존 CameraSerialSettings 마이그레이션 실패 시 시리얼 설정 유실 | High | data.db 자동 백업 + 마이그레이션 트랜잭션 + 실패 시 rollback + 로그 |
| Manager↔Agent 프로세스 fork bomb (계속 크래시) | Medium | 5회 한계 + Server alert. 운영자가 Devices 탭에서 수동 disable 가능 |
| 신규 NATS 토픽 5개 추가로 대역폭 증가 | Low | 정상 시 토픽당 분당 1건 미만. inventory 는 변화 있을 때만 publish |
| Windows Service Recovery 정책 누락 | Medium | install.ps1 에 `sc failure HCS-Manager reset= 0 actions= restart/5000/restart/5000/restart/5000` 명시 |
| Recipe 호환성 — 기존 Recipe 가 CameraIndex 만 가짐, 새 환경에 Alias 없음 | Medium | RecipeEngine 이 CameraIndex → `Agent_{idx}` AgentId 변환 fallback 유지 (현재 동작 그대로). Devices 탭에 "Map CameraIndex → Alias" 도구 제공 |
| Manager 가 일부 Agent 만 죽으면 다른 카메라 영향 — 격리 검증 | High | 프로세스 격리는 OS 보장. Manager 가 PID 별 상태 추적, 한 Agent fault 가 다른 Agent 에 미치는 영향 0 |
| WMI 이벤트 watcher 메모리 누수 | Low | Manager 종료 시 명시적 Dispose + 단위 테스트로 검증 |

---

## 6. Impact Analysis

### 6.1 Changed Resources

| Resource | Type | 변경 내용 |
|---|---|---|
| (신규) `HeatingCameraSystem.AgentManager/` | Project | Console + Windows Service host. Program.cs, Services/AgentSupervisor.cs, Services/LogTailService.cs, Services/InventoryPublisher.cs |
| (신규) `ICameraEnumerator` | Interface | `IReadOnlyList<DiscoveredCamera> Enumerate()` + `event Action<PnpChange> Changed` |
| (신규) `WmiCameraEnumerator` | Class (Protocols) | System.Management 기반 |
| (신규) `FakeCameraEnumerator` | Class (Protocols/Simulation) | 가짜 카메라 2~3개 |
| (신규) `CameraInventoryMessage` 외 4종 | Models (Core) | NATS 페이로드 |
| `INatsCommunicationService` | Interface | Manager 토픽 publish/subscribe 8 메서드 추가 |
| `NatsCommunicationService` | Class | 위 8 메서드 구현 + RunSubscriptionLoop 재사용 |
| `AgentConfig` | Model | `LogPath` 필드 추가 (NDJSON 출력 경로) |
| `HardwareSettings` | Model | (변경 없음 — Manager 는 별도 설정 사용) |
| (신규) `ManagerSettings` | Model | `PCId`, `NatsUrl`, `SimulationMode`, `LogRetentionDays=7`, `WarnAlertEnabled=false`, `InstallRoot=C:\HeatingCameraSystem\` |
| (신규) `manager-state.json` | File | `C:\HeatingCameraSystem\manager-state.json` |
| `Agent/Program.cs` | Console | NDJSON 로그 (Serilog) 적용, CLI 인수 1개 추가 (LogPath) |
| `Agent/HeatingCameraSystem.Agent.csproj` | csproj | Serilog + Serilog.Formatting.Compact 패키지 추가 |
| (신규) `CameraDevice` | Model (Core) | `HardwareId`, `AgentId`, `Alias`, `PCId`, `OpenCvIndex`, `SerialSettings`, `IsApproved`, `FirstSeen`, `LastSeen` |
| (신규) `ICameraDeviceRepository` | Interface | CRUD + by HardwareId, by PCId |
| (신규) `LiteDbCameraDeviceRepository` | Class (Master/Services) | LiteDB 구현 |
| `LiteDbCameraSerialSettingsRepository` | Class | 삭제 (마이그레이션 후) |
| `ICameraSerialSettingsRepository` | Interface | 삭제 |
| (신규) `MigrationService` | Class (Master/Services) | Master 기동 시 1회 실행: CameraSerialSettings → CameraDevice 흡수 + data.db 백업 |
| `RecipeStep` | Model | `CameraAlias` 필드 추가 (선택), `CameraIndex` 유지 |
| `RecipeEngine` | Class | Alias 우선, fallback 으로 CameraIndex → `Agent_{idx}` 변환 |
| `AppServices` | Class | `CameraDeviceRepo` 추가, `CameraSerialSettingsRepo` 제거, `MigrationService` 호출 |
| (신규) `DevicesView.xaml` + `DevicesViewModel.cs` | View + VM | 신규 탭 — 인벤토리 / pending 승인 / Alias 편집 / 시리얼 / 로그 뷰어 |
| `SettingsView.xaml` + `SettingsViewModel.cs` | View + VM | 시리얼 설정 섹션을 Devices 탭으로 이동 (단순 정리만 남김) |
| `MainView.xaml` | View | Devices 탭 추가 |
| (신규) `install.ps1` | Script | `docs/deployment/install.ps1` (Manager service 등록) |
| `HeatingCameraSystem.slnx` | Solution | AgentManager 프로젝트 추가 |
| `docs/manual/00-overview.md` | Doc | 아키텍처 그림 + NATS 토픽 맵 갱신 |
| `docs/manual/01-installation.md` | Doc | Manager 설치 절차 추가 |
| `docs/manual/02-configuration.md` | Doc | manager-state.json + ManagerSettings + 신규 NATS 토픽 |
| `docs/manual/03-usage.md` | Doc | Devices 탭 사용법 + 로그 뷰어 + Alias 부여 |
| `HeatingCameraSystem.Tests/` | Tests | Manager 단위 ≥ 10 + 통합 ≥ 3 (FakeEnumerator 라운드트립, Migration, RecipeEngine Alias 변환) |

### 6.2 Current Consumers (영향 평가)

| Resource | Code Path | Impact |
|---|---|---|
| `agent.json` | `Agent/Program.cs` `LoadOrCreateConfig` | LogPath 필드 추가 (선택). 하위 호환. |
| `CameraSerialSettings` 컬렉션 | `SettingsViewModel`, `AppServices.ApplySerialSettingsLocallyAsync` | MigrationService 가 1회 흡수 후 삭제. SettingsViewModel 의 해당 로직을 DevicesViewModel 로 이전. |
| `RecipeStep.CameraIndex` | `RecipeEngine`, `RecipeEditorViewModel`, JSON Export/Import | 유지. Alias 가 우선이지만 없으면 기존 동작. |
| `master.cmd.capture.{AgentId}` | `RecipeEngine`, `Agent/Program.cs` | 토픽 자체는 그대로. AgentId 가 새 형식(`{PCId}_{hash}`)일 수도 있다는 점만 다름. |
| `master.config.serial.{AgentId}` | `SettingsViewModel`, `Agent/Program.cs` | 토픽 그대로. 발행자가 SettingsViewModel → DevicesViewModel 로 변경. |
| 기존 단위 테스트 42건 | `HeatingCameraSystem.Tests/` | RecipeEngine 테스트 일부 (`Agent_0`, `Agent_1` 하드코딩) 가 새 AgentId 형식에서 동작하는지 검증 필요. fallback 동작 보존되면 그대로 통과. |

---

## 7. Architecture

### 7.1 컴포넌트 배치 (PC 1대 = Bay1 가정)

```
C:\HeatingCameraSystem\                               ← 설치 루트
├── Manager\
│   ├── HeatingCameraSystem.AgentManager.exe          ← Windows Service
│   ├── manager-settings.json                         ← PCId, NatsUrl, SimulationMode, ...
│   ├── manager-state.json                            ← 등록된 카메라 목록
│   └── manager.log                                   ← Manager 자체 로그
├── Agent\
│   └── HeatingCameraSystem.Agent.exe                 ← Manager 가 spawn (PER 카메라)
└── logs\
    ├── Bay1_a3f72c8b\
    │   ├── agent-20260620.log                        ← Agent NDJSON (Serilog)
    │   └── agent-20260619.log
    └── Bay1_b9d12a4c\
        └── agent-20260620.log
```

### 7.2 부팅 시퀀스

```
[Windows 부팅]
   ↓
[Service Control Manager → HCS-Manager 시작]
   ↓
[Manager.OnStart]
   1. ManagerSettings 로드 (manager-settings.json)
   2. NATS 연결
   3. manager-state.json 로드 (등록된 카메라 목록)
   4. WmiCameraEnumerator.Enumerate()  → 현재 PC 의 카메라 전체
   5. 등록된 카메라 중 현재 PC 에 존재(HardwareId 매칭)하는 것들에 대해 Agent.exe spawn
   6. PnP ManagementEventWatcher 시작
   7. agent-mgr.inventory.{PCId} 발행 (전체 인벤토리)
```

### 7.3 신규 카메라 핫플러그 흐름

```
[USB 카메라 꽂힘]
   ↓
[Windows PnP] → ManagementEventWatcher event (USB arrival)
   ↓
[Manager] 1초 디바운스 → WMI 재열거
   ↓
[Manager] 신규 HardwareId 발견 → manager-state.json 에 IsApproved=false 로 임시 추가
   ↓
[Manager] agent-mgr.inventory.{PCId} 발행 (pending 표시 포함)
   ↓
[Master Devices 탭] 노란 점 + 신규 카메라 등장
   ↓
[운영자] Alias 입력 + Approve 클릭
   ↓
[Master] server.cmd.mgr.{PCId} 발행 (Approve + Alias)
   ↓
[Manager] manager-state.json 갱신 (IsApproved=true, Alias 저장)
   ↓
[Manager] AgentId={PCId}_{HardwareIdHash8} 부여
   ↓
[Manager] Agent.exe spawn (CLI args 전달)
   ↓
[Master Devices 탭] 초록 점 (Connected)
```

### 7.4 Agent 프로세스 lifecycle

```
[Manager 가 Process.Start]
   ↓
Agent.exe Bay1_a3f72c8b nats://...:4222 0 "C:\...\Cam-0\" "C:\...\logs\Bay1_a3f72c8b\" false
   ↓
[Agent] Serilog File sink + NATS 연결 + 카메라 InitializeCamera
   ↓
[Agent] 정상 동작 (캡처 명령 대기, 5초 하트비트)
   ↓
[종료 이벤트]:
  A. Manager 가 kill (설정 변경, Approve 취소) → graceful (5s timeout) → 종료
  B. Agent 정상 종료 (Ctrl+C) → Process.Exited 이벤트
  C. Agent 비정상 종료 (크래시) → Process.Exited (ExitCode≠0)
     ↓
     [Manager.OnAgentExited]
     1. 재시작 카운터 증가
     2. 5회 도달? → 영구 드롭 + LogAlert (FATAL) → 운영자 개입까지 대기
        아니면 → 지수 백오프 (1s, 2s, 5s, 15s, 60s) 대기 후 spawn
     3. 안정 실행 (예: 10분 무사고) → 카운터 0 리셋
```

### 7.5 로그 파이프라인

```
[Agent] _logger.LogError(...) (Serilog)
   ↓
[Serilog File sink] NDJSON 1줄 append
   {"@t":"...","@l":"Error","@m":"...","AgentId":"..."}
   ↓
[Manager.LogTailService] file tail (System.IO.FileSystemWatcher + sequential read)
   ↓
[Manager] @l ∈ {Error, Fatal} 또는 (WarnAlertEnabled && @l=Warning)?
   YES → agent-mgr.log.alert.{PCId} 발행 (LogAlertMessage: AgentId, Level, Message, Timestamp)
   NO  → 무시 (파일에만 남음)

별도:
[Master Devices 탭] "Get Logs" 클릭
   ↓ server.req.log.{PCId} 발행 (LogDumpRequestMessage: AgentId, MaxBytes=5MB)
   ↓
[Manager.LogDumpHandler] 지정 Agent 의 최근 N MB 로그 → gzip → agent-mgr.log.dump.{PCId} 발행
   ↓
[Master Devices 탭] gzip 압축 해제 → 로그 뷰어 표시
```

### 7.6 NATS 토픽 5개 신규

| Subject | 방향 | Payload | 발행 빈도 |
|---|---|---|---|
| `agent-mgr.inventory.{PCId}` | Manager → Server | `CameraInventoryMessage { PCId, Cameras: [{ HardwareId, Alias, AgentId, OpenCvIndex, IsApproved, IsRunning, LastSeen }] }` | 부팅 시 1회, PnP 변경 시, 그 외 정상시 평시 없음 |
| `server.cmd.mgr.{PCId}` | Server → Manager | `ManagerCommandMessage { Op: Approve/Reject/Rename/SetSerial/Restart/Disable, HardwareId, Payload }` | 운영자 조작 시 |
| `agent-mgr.log.alert.{PCId}` | Manager → Server | `LogAlertMessage { AgentId, Level: Warning/Error/Fatal, Message, Timestamp }` | ERROR 발생 시 즉시 |
| `server.req.log.{PCId}` | Server → Manager | `LogDumpRequestMessage { AgentId, MaxBytes }` | 운영자 클릭 시 |
| `agent-mgr.log.dump.{PCId}` | Manager → Server | `LogDumpMessage { AgentId, GzipBytes, OriginalBytes, TruncatedAt }` | 위 요청 응답 |

### 7.7 Manager 상태 머신 (per camera)

```
Discovered → Pending → Approved → Spawning → Running ⇄ Crashing → Dropped
                                                       ↑
                                                       └ (5회 미만, 백오프 후 재시작)
```

### 7.8 데이터 흐름 — Recipe 실행 (Alias 기반)

```
[Master Recipe Editor] CameraAlias="Bay1-Top"
   ↓
[Master DevicesViewModel/AppServices] DB lookup
   Alias "Bay1-Top" → CameraDevice { HardwareId, AgentId="Bay1_a3f72c8b" }
   ↓
[Master RecipeEngine] master.cmd.capture.Bay1_a3f72c8b 발행
   ↓
[Bay1 Agent_Bay1_a3f72c8b] 캡처 → agent.result.capture.Bay1_a3f72c8b
   ↓
[Master RecipeEngine] 이미지 바이트 저장 + 이력 기록 (기존 흐름)
```

### 7.9 수정/신규 파일 (총 ~25개)

```
신규:
  HeatingCameraSystem.AgentManager/
    HeatingCameraSystem.AgentManager.csproj
    Program.cs
    Services/AgentSupervisor.cs
    Services/InventoryPublisher.cs
    Services/LogTailService.cs
    Services/LogDumpHandler.cs
    Services/ManagerCommandHandler.cs
    State/ManagerStateStore.cs
    Config/ManagerSettings.cs

  HeatingCameraSystem.Core/
    Interfaces/ICameraEnumerator.cs
    Interfaces/ICameraDeviceRepository.cs
    Models/CameraDevice.cs
    Models/CameraInventoryMessage.cs
    Models/ManagerCommandMessage.cs
    Models/LogAlertMessage.cs
    Models/LogDumpRequestMessage.cs
    Models/LogDumpMessage.cs
    Models/DiscoveredCamera.cs

  HeatingCameraSystem.Protocols/
    WmiCameraEnumerator.cs
    Simulation/FakeCameraEnumerator.cs

  HeatingCameraSystem.Master/
    Services/LiteDbCameraDeviceRepository.cs
    Services/MigrationService.cs
    ViewModels/DevicesViewModel.cs
    Views/DevicesView.xaml + .cs

  docs/deployment/install.ps1

수정:
  HeatingCameraSystem.Core/
    Config/AgentConfig.cs (LogPath 필드)
    Models/RecipeModels.cs (CameraAlias 필드)
    Interfaces/INatsCommunicationService.cs (Manager 토픽 8 메서드)

  HeatingCameraSystem.Protocols/
    NatsCommunicationService.cs (Manager 토픽 publish/subscribe)

  HeatingCameraSystem.Agent/
    Program.cs (Serilog 전환, LogPath args)
    HeatingCameraSystem.Agent.csproj (Serilog 패키지)

  HeatingCameraSystem.Master/
    Services/AppServices.cs (CameraDeviceRepo, Migration 호출, CameraSerialSettings 제거)
    Services/RecipeEngine.cs (Alias → AgentId 변환)
    ViewModels/SettingsViewModel.cs (시리얼 설정 → DevicesViewModel 이전)
    Views/SettingsView.xaml (시리얼 섹션 제거)
    Views/MainView.xaml (Devices 탭 추가)

  HeatingCameraSystem.slnx (AgentManager 프로젝트 추가)

  HeatingCameraSystem.Tests/
    AgentSupervisorTests.cs (신규)
    LogTailServiceTests.cs (신규)
    WmiCameraEnumeratorTests.cs (Fake 기반, 신규)
    CameraDeviceRepositoryTests.cs (신규)
    MigrationServiceTests.cs (신규)
    RecipeEngineAliasTests.cs (신규)
    + 기존 테스트 일부 갱신

  docs/manual/00-overview.md
  docs/manual/01-installation.md
  docs/manual/02-configuration.md
  docs/manual/03-usage.md
```

---

## 8. Convention Prerequisites

- `Nullable=enable` + `ImplicitUsings=enable` 전 프로젝트 공통 유지
- Manager 는 `Microsoft.Extensions.Hosting` + `Microsoft.Extensions.Hosting.WindowsServices` (.NET 8 표준 Worker Service 패턴)
- 로그: Agent 는 `Serilog` + `Serilog.Formatting.Compact` (NDJSON). Manager 는 `Microsoft.Extensions.Logging` + Serilog provider (동일 NDJSON 으로 통일)
- WMI 호출: `System.Management` 패키지 (NuGet `System.Management` 8.0.0). Windows 전용. csproj `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` 명시
- `ObservableObject` + `[ObservableProperty]` + `[RelayCommand]` (CommunityToolkit.Mvvm) — 기존 패턴 유지
- NATS 메시지: `INatsCommunicationService` 인터페이스에 Manager 토픽도 통합 (별도 인터페이스 분리 안 함 — YAGNI)
- Manager 와 Agent 는 서로 직접 NATS 통신 안 함 (각자 Server 와만 통신)

---

## 9. Migration Strategy (중요)

1. **Master 기동 시 1회 MigrationService 실행**
   - data.db 백업 → `data.db.bak.<timestamp>`
   - `CameraSerialSettings` 컬렉션 모든 레코드 읽기
   - 각 레코드를 `CameraDevice` 컬렉션에 upsert (HardwareId 없으므로 임시 키 `legacy_{CameraIndex}` 사용, `IsApproved=true`, `Alias="(legacy CAM-{idx})"`)
   - `CameraSerialSettings` 컬렉션 drop
   - 마이그레이션 완료 플래그 별도 컬렉션에 저장 (재실행 방지)
2. **Recipe 동작 검증**
   - 기존 Recipe (CameraIndex 만) → RecipeEngine fallback (`Agent_{CameraIndex}` AgentId 직접 변환) → 정상 동작
   - 신규 Recipe (CameraAlias) → DB lookup → 정상 동작
3. **운영자 후속 작업**
   - Devices 탭에서 legacy 카메라들을 실제 HardwareId 와 매칭 (수동 또는 "Auto-match by CameraIndex" 도구)
4. **롤백 시나리오**
   - 마이그레이션 실패 → data.db 자동 복원 + 에러 로그 + Master 정상 기동 (CameraSerialSettings 유지)

---

## 10. Next Steps

1. [ ] `/pdca design agent-manager` — 3가지 아키텍처 옵션 비교 + 선택 + 모듈 분할
2. [ ] `/pdca do agent-manager --scope manager-skeleton` — Manager 골격 + Fake enumerator 먼저
3. [ ] `/pdca do agent-manager --scope master-devices` — Master Devices 탭 + DB 마이그레이션
4. [ ] `/pdca do agent-manager --scope log-pipeline` — Serilog + tail + alert + dump
5. [ ] `/pdca do agent-manager --scope install-script` — install.ps1
6. [ ] `/pdca analyze agent-manager` — gap 분석 + E2E 시뮬 검증
7. [ ] `/pdca report agent-manager`

---

## Version History

| Version | Date | Changes |
|---|---|---|
| 0.1 | 2026-06-20 | Initial draft. 모든 Q1~Q4 + 9개 추가 의사결정 락 완료. Plan-only session. |
