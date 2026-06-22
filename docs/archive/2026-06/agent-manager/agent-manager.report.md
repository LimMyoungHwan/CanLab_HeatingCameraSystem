# agent-manager Completion Report

> **Status**: Complete
>
> **Project**: HeatingCameraSystem
> **Completion Date**: 2026-06-21
> **PDCA Cycle**: #1

---

## Executive Summary

### 1.1 Project Overview

| Item | Content |
|------|---------|
| Feature | agent-manager |
| Start Date | 2026-06-20 |
| End Date | 2026-06-21 |
| Duration | 2일 (Plan → Design → Do → Check → Report) |
| Commits | 4 (`b9302f4` Plan, `eb682e3` Do, `365b20b` Design, `777f23d` Check+Fix) |

### 1.2 Results Summary

```
┌──────────────────────────────────────────────┐
│  Match Rate: 96% (PASS ≥ 90%)                │
├──────────────────────────────────────────────┤
│  ✅ FR 완료:     20 / 20                      │
│  ✅ 테스트:      59 / 59 통과                 │
│  ✅ 갭 해결:     2 / 2 Important 수정          │
│  ⚠️ 잔여 Minor:  4건 (매뉴얼 3 + UI 중복 1)   │
│  ❌ E2E sim:    미구현 (SC-12)                 │
└──────────────────────────────────────────────┘
```

### 1.3 Value Delivered

| Perspective | Content |
|-------------|---------|
| **Problem** | 카메라마다 Agent를 수동 실행, USB 포트 이동 시 OpenCV 인덱스 어긋남, 분산 Agent 로그 가시성 부재 |
| **Solution** | PC당 Agent Manager (Windows Service) — WMI 카메라 자동 발견 + HardwareId 영구 식별 + Agent supervisor (spawn/kill/respawn) + NDJSON 로그 파이프라인 + Master Devices 탭 원격 관리 |
| **Function/UX Effect** | 운영자: PC당 서비스 1회 등록 → 카메라 꽂으면 자동 등장 → Alias+Approve → 즉시 사용. USB 포트 옮겨도 동일 인식. ERROR 5초 내 Master 알림. |
| **Core Value** | 카메라 N대 × N PC 운영을 1인이 단일 GUI(Devices 탭)로 관리. 수동 Agent 실행/관리 부담 제거. |

---

### 1.4 Success Criteria 최종 상태

| # | 기준 | 상태 | 근거 |
|---|------|:----:|------|
| SC-01 | Manager install.ps1 → 재부팅 → 자동 인식 | ⚠️ Partial | install.ps1 + AddWindowsService 구현. 실 환경 E2E 미검증 |
| SC-02 | 새 USB 카메라 → 5초 내 pending | ⚠️ Partial | WMI watcher + 1s debounce + inventory 구현. 실 환경 미검증 |
| SC-03 | Approve → 10초 내 Agent 시작 | ⚠️ Partial | Approve → spawn 로직 구현. 실 환경 미검증 |
| SC-04 | USB 포트 이동 → HardwareId 매칭 | ✅ Met | WMI PnPDeviceID 기반 + GetByHardwareId 매칭 |
| SC-05 | Agent 강제 종료 → 1s 재시작 + 리셋 | ✅ Met | BackoffSeconds[0]=1, StableRunSeconds=600 + 테스트 |
| SC-06 | Agent LogError → 5초 내 빨간 점 | ⚠️ Partial | LogTailService + LogAlertMessage 구현. GAP-2 수정 완료 |
| SC-07 | Get Logs → 30초 내 다운로드 | ✅ Met | LogDumpHandler gzip + 30s timeout |
| SC-08 | 기존 Recipe (CameraIndex) 정상 | ✅ Met | ResolveAgentIdAsync fallback + 테스트 3건 |
| SC-09 | 신규 Recipe (CameraAlias) 정상 | ✅ Met | DB lookup + AgentId 변환 + 테스트 |
| SC-10 | `dotnet build` 0 err / 0 warn | ✅ Met | 9 projects, 0 errors, 0 warnings |
| SC-11 | `dotnet test` ≥13 신규 + 42 기존 | ✅ Met | 59/59 (42 + 17) |
| SC-12 | E2E sim runner 동작 | ❌ Not Met | 미구현 |

**Success Rate**: 7/12 Met, 4 Partial, 1 Not Met

---

### 1.5 Decision Record

| 출처 | 결정 | 준수 | 결과 |
|------|------|:----:|------|
| [Plan Q1] | Manager↔Agent IPC: args only (kill+respawn) | ✅ | Process.Start(args) — Named pipe 없음 |
| [Plan Q2] | 신규 NATS 토픽 5개, 기존 6개 유지 | ✅ | 11 토픽 전체 구현 |
| [Plan Q3] | Recipe: CameraAlias 우선, CameraIndex fallback | ✅ | ResolveAgentIdAsync 구현 |
| [Plan Q4] | 로그: NDJSON + Serilog + ERROR auto / on-demand dump | ✅ | CompactJsonFormatter + LogTailService + LogDumpHandler |
| [Design] | Option C: Distributed Supervisor | ✅ | PC당 Manager Windows Service |
| [Design] | AgentId = {PCId}_{SHA256[0:8]} | ✅ | BuildAgentId 정확히 일치 |
| [Design] | 지수 백오프 [1,2,5,15,60]s, 5회 한계 | ✅ | 배열 + MaxRestartAttempts=5 |
| [Design] | DB 마이그레이션 idempotent | ✅ | _migrations 플래그 + 테스트 2건 |

**Decision Compliance: 8/8 (100%)**

---

## 2. Related Documents

| Phase | Document | Status |
|-------|----------|--------|
| Plan | [agent-manager.plan.md](./agent-manager.plan.md) | ✅ |
| Design | [agent-manager.design.md](./agent-manager.design.md) | ✅ |
| Check | [agent-manager.analysis.md](./agent-manager.analysis.md) | ✅ |
| Report | 현재 문서 | ✅ |

---

## 3. Completed Items

### 3.1 Functional Requirements (20/20)

| ID | 요구사항 | 상태 |
|----|----------|------|
| FR-01 | Manager Windows Service 자동 실행 | ✅ |
| FR-02 | WMI 카메라 열거 (Win32_PnPEntity) | ✅ |
| FR-03 | PnP 이벤트 1초 디바운스 | ✅ |
| FR-04 | 신규 HardwareId → IsApproved=false | ✅ |
| FR-05 | AgentId = {PCId}_{HardwareIdHash8} | ✅ |
| FR-06 | Agent spawn 정확한 CLI args | ✅ |
| FR-07 | 지수 백오프 [1,2,5,15,60]s, 5회 한계 | ✅ |
| FR-08 | 안정 10분 카운터 리셋 | ✅ |
| FR-09 | ManagerCommand 6종 (Approve/Reject/Rename/SetSerial/Restart/Disable) | ✅ |
| FR-10 | Agent Serilog NDJSON 로그 | ✅ |
| FR-11 | LogTail ERROR/FATAL 자동 push | ✅ |
| FR-12 | Log dump gzip 요청/응답 | ✅ |
| FR-13 | 5 NATS 메시지 모델 | ✅ |
| FR-14 | Master Devices 탭 (인벤토리 + 명령 + 로그) | ✅ |
| FR-15 | CameraDevice LiteDB + 마이그레이션 | ✅ |
| FR-16 | CameraAlias → AgentId fallback | ✅ |
| FR-17 | SimulationMode FakeCameraEnumerator | ✅ |
| FR-18 | install.ps1 (sc create + 방화벽 + 복구) | ✅ |
| FR-19 | manager-state.json 스키마 + 영속 | ✅ |
| FR-20 | Graceful kill on shutdown | ✅ |

### 3.2 Non-Functional Requirements

| 항목 | 목표 | 달성 | 상태 |
|------|------|------|------|
| 가용성 | Manager 크래시 → Service Recovery 자동 재시작 | install.ps1 `sc failure` 3회 설정 | ✅ |
| 호환성 | 기존 Recipe 100% 동작 | CameraAlias fallback + 테스트 3건 | ✅ |
| UI 스레드 | Devices 탭 Dispatcher.Invoke | DevicesViewModel NATS 콜백 전부 Dispatcher 경유 | ✅ |
| 마이그레이션 | idempotent + 백업 | _migrations 플래그 + data.db.bak + 테스트 | ✅ |
| Nullable | 0 warnings | 0 warnings | ✅ |

### 3.3 신규 프로젝트

| 프로젝트 | 파일 수 | 역할 |
|----------|---------|------|
| `HeatingCameraSystem.AgentManager` | 9 | PC당 Windows Service — 카메라 발견 + Agent supervisor + 로그 |

### 3.4 신규 파일 (28개)

| 영역 | 파일 | 역할 |
|------|------|------|
| **AgentManager** | `Config/ManagerSettings.cs` | PCId, NatsUrl, SimulationMode 등 7 필드 |
| | `State/ManagerStateStore.cs` | manager-state.json 영속 + CameraEntry 모델 |
| | `Services/AgentSupervisor.cs` | spawn/kill/respawn + 백오프 + 드롭 |
| | `Services/InventoryPublisher.cs` | 카메라 인벤토리 NATS publish |
| | `Services/LogTailService.cs` | NDJSON tail + ERROR auto-alert |
| | `Services/LogDumpHandler.cs` | on-demand gzip 로그 dump |
| | `Services/ManagerCommandHandler.cs` | 6종 명령 처리 + AgentId 생성 |
| | `Program.cs` | IHost + ManagerWorker BackgroundService |
| | `.csproj` | net8.0, win-x64, Hosting + WindowsServices |
| **Core** | `Models/CameraDevice.cs` | HardwareId PK, 영구 카메라 레코드 |
| | `Models/DiscoveredCamera.cs` | WMI 열거 스냅샷 + PnP 이벤트 |
| | `Models/ManagerMessages.cs` | NATS 5종 메시지 + 2 enum |
| | `Interfaces/ICameraEnumerator.cs` | 카메라 열거 + PnP 이벤트 |
| | `Interfaces/ICameraDeviceRepository.cs` | CRUD by HardwareId/Alias/PCId |
| **Protocols** | `WmiCameraEnumerator.cs` | WMI Win32_PnPEntity + PnP watcher |
| | `Simulation/FakeCameraEnumerator.cs` | 가짜 카메라 2개 + 테스트 헬퍼 |
| **Master** | `Services/LiteDbCameraDeviceRepository.cs` | ICameraDeviceRepository LiteDB |
| | `Services/MigrationService.cs` | CameraSerialSettings → CameraDevice 흡수 |
| | `ViewModels/DevicesViewModel.cs` | 인벤토리 + 승인/거부/이름/로그 |
| | `Views/DevicesView.xaml` + `.cs` | Devices 탭 UI |
| **Deployment** | `docs/deployment/install.ps1` | sc create + 방화벽 + 설정 생성 |
| **Tests** | `AgentManagerTests.cs` | 17개 테스트 (6개 클래스) |
| **Docs** | `01-plan/features/agent-manager.plan.md` | Plan 문서 |
| | `02-design/features/agent-manager.design.md` | Design 문서 (Option A/B/C 비교) |
| | `03-analysis/agent-manager.analysis.md` | Gap Analysis 문서 |

### 3.5 수정 파일 (14개)

| 파일 | 변경 내용 |
|------|-----------|
| `Core/Interfaces/INatsCommunicationService.cs` | Manager 토픽 10 메서드 추가 |
| `Core/Models/RecipeModels.cs` | `RecipeStep.CameraAlias` nullable 필드 |
| `Core/Config/AgentConfig.cs` | `LogPath` 필드 |
| `Protocols/NatsCommunicationService.cs` | Manager 10 메서드 구현 |
| `Protocols/HeatingCameraSystem.Protocols.csproj` | System.Management 8.0.0 |
| `Agent/HeatingCameraSystem.Agent.csproj` | Serilog 5개 패키지 |
| `Agent/Program.cs` | Serilog NDJSON sink + LogPath CLI arg |
| `Master/Services/AppServices.cs` | CameraDeviceRepo + MigrationService |
| `Master/Services/RecipeEngine.cs` | ResolveAgentIdAsync + _deviceRepo |
| `Master/ViewModels/MainViewModel.cs` | NavigateToDevices 명령 |
| `Master/MainWindow.xaml` | Devices DataTemplate + nav 버튼 + BoolToVis |
| `HeatingCameraSystem.slnx` | AgentManager 프로젝트 추가 |
| `Tests/HeatingCameraSystem.Tests.csproj` | AgentManager 참조 |
| `docs/manual/00-overview.md` | 아키텍처 + NATS 토픽 5개 + Manager 런타임 파일 |

---

## 4. Incomplete Items

### 4.1 다음 사이클 이월

| 항목 | 심각도 | 이유 |
|------|--------|------|
| SC-01~03, SC-06 실 환경 E2E 검증 | Important | 실 하드웨어(USB 카메라 + Windows Service) 필요 |
| SC-12 E2E sim runner | Important | FakeEnumerator → inventory → auto-Approve → Recipe 라운드트립 자동화 |
| GAP-3~5 매뉴얼 01/02/03 Manager 섹션 | Minor | 문서 보완 |
| GAP-6 SettingsView 시리얼 중복 정리 | Minor | DevicesView로 이전 후 SettingsView 섹션 제거 |

---

## 5. Quality Metrics

| 지표 | 목표 | 최종 |
|------|------|------|
| Match Rate | ≥ 90% | **96%** ✅ |
| 빌드 오류 | 0 | **0** ✅ |
| 빌드 경고 | 0 | **0** ✅ |
| 테스트 통과 | 기존 42 + 신규 ≥13 | **59/59** (42 + 17) ✅ |
| FR 완료 | 20/20 | **20/20** ✅ |
| Decision Compliance | 100% | **100%** (8/8) ✅ |

### 5.1 해결된 이슈

| 이슈 | 해결 | 커밋 |
|------|------|------|
| GAP-1: SetSerial no-op stub | Payload JSON → CameraSerialSettings 역직렬화 → PublishSerialConfigAsync 포워딩 | `777f23d` |
| GAP-2: DevicesView BoolToVis 누락 | Resources에 BooleanToVisibilityConverter 추가 | `777f23d` |
| CA1416: WMI Windows-only 경고 14건 | `[SupportedOSPlatform("windows")]` 어트리뷰트 | `eb682e3` |
| xUnit1031: 테스트 blocking task 경고 | `GetAwaiter().GetResult()` → async/await 전환 | `eb682e3` |
| CS8619: LiteDB nullable 반환 타입 | `Task.FromResult<CameraDevice?>()` 명시 캐스트 | `eb682e3` |
| CommunityToolkit `_pcId` → `PcId` 생성 | `_pCId` 필드명으로 변경 → `PCId` 생성 | `eb682e3` |

---

## 6. Lessons Learned

### Keep (잘된 점)

- **Plan 13개 결정 사전 락**: Q1~Q4 + 9개 추가 의사결정을 Plan 단계에서 전부 확정 → Design/Do에서 방향 재결정 0회
- **3-Option Design 비교**: Option A(Embedded) / B(Centralized) / C(Distributed) 비교로 아키텍처 근거 명확화
- **기존 패턴 일치**: INatsCommunicationService 확장, LiteDbXxxRepository, CommunityToolkit.Mvvm — 러닝커브 없이 기존 코드와 동일 구조
- **Check 즉시 수정**: GAP-1(SetSerial stub)과 GAP-2(BoolToVis) 발견 즉시 수정 → Report에 깔끔한 96% 반영
- **테스트 17건**: Plan 요구(≥13) 초과 달성. RecipeEngine Alias fallback 3건이 특히 유용

### Problem (개선 필요)

- **E2E sim runner 미구현**: SC-12 미달성. Manager + FakeEnumerator + Agent 풀 라운드트립 자동 검증 누락
- **매뉴얼 갱신 지연**: 00-overview만 갱신, 01/02/03은 Manager 섹션 미추가
- **SetSerial 초기 구현 누락**: Do 단계에서 6종 ManagerCommandOp 중 SetSerial만 stub으로 남긴 것은 검토 부족
- **CommunityToolkit 필드명 규칙**: `_pcId` → `PcId` (대문자 연속 시 소문자 변환) — 패턴 숙지 필요

### Try (다음에 시도)

- Plan 단계에서 ManagerCommandOp별 "구현 필수 동작" 체크리스트 작성 → Do 단계에서 1:1 확인
- E2E sim runner를 Do 단계에서 함께 작성 (SC에 포함되면 같이 구현)
- Design 문서에 CommunityToolkit 필드명 규칙 섹션 추가

---

## 7. Architecture Summary

### 7.1 채택된 아키텍처: Option C — Distributed Supervisor

```
[Agent PC]                              [Master PC]
  AgentManager.exe (Windows Service)      Master.exe (WPF)
    ├── WmiCameraEnumerator (로컬)          ├── Devices 탭 (신규)
    ├── AgentSupervisor (spawn/kill)         │   ├── Approve/Reject
    ├── LogTailService (NDJSON tail)         │   ├── Alias/Serial 편집
    └── NATS ↔ Master (5 토픽)               │   └── 로그 뷰어
        ↓                                   └── RecipeEngine (CameraAlias)
  Agent.exe #0 (카메라 0, Serilog NDJSON)
  Agent.exe #1 (카메라 1, Serilog NDJSON)
```

### 7.2 NATS 토픽 (기존 6 + 신규 5 = 11)

| 신규 Subject | 방향 | Payload |
|---|---|---|
| `agent-mgr.inventory.{PCId}` | Manager → Master | `CameraInventoryMessage` |
| `server.cmd.mgr.{PCId}` | Master → Manager | `ManagerCommandMessage` |
| `agent-mgr.log.alert.{PCId}` | Manager → Master | `LogAlertMessage` |
| `server.req.log.{PCId}` | Master → Manager | `LogDumpRequestMessage` |
| `agent-mgr.log.dump.{PCId}` | Manager → Master | `LogDumpMessage` |

---

## 8. Next Steps

### 8.1 즉시

- [ ] SC-01~03, SC-06: 실 환경(USB 카메라 + Windows Service) E2E 수동 검증
- [ ] SC-12: E2E sim runner 작성 (FakeEnumerator → inventory → auto-Approve → Recipe 라운드트립)

### 8.2 다음 iteration

| 항목 | 우선순위 |
|------|----------|
| 매뉴얼 01/02/03 Manager 섹션 추가 | 🟡 Medium |
| SettingsView → DevicesView 시리얼 이전 정리 | 🟡 Medium |
| AgentSupervisor Process 추상화 + 단위 테스트 | 🟢 Low |
| LogTailService/LogDumpHandler 단위 테스트 | 🟢 Low |

### 8.3 다음 PDCA 사이클 후보

| 항목 | 우선순위 |
|------|----------|
| 카메라 열화상 vs RGB 자동 판별 (#18) | 🟡 Medium |
| Recipe 단계 소요시간 추정 (#14, 실 PLC 도입 후) | 🟢 Low |
| NATS 인증 (production 보안) | 🟢 Low |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-06-21 | 최초 작성 — Match Rate 96%, 59/59 테스트 통과, FR 20/20, GAP 2건 수정 |
