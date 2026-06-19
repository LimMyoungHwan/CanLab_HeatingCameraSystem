# camera-serial-config Planning Document

> **Summary**: 카메라별 시리얼 설정 관리 — Master UI 설정 화면 + NATS 전달 + Agent 재연결 + ACK
>
> **Project**: HeatingCameraSystem
> **Version**: 0.1
> **Author**: -
> **Date**: 2026-06-19
> **Status**: Draft

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | 시리얼 설정이 `hardware.json` 전역 1개 뿐 — 카메라마다 다른 포트/속도 지정 불가. 변경 시 파일 직접 편집 + 재시작 필요 |
| **Solution** | Master UI Settings 탭에서 카메라별 시리얼 설정 입력 → LiteDB 저장 → NATS로 해당 Agent 전달 → Agent 재연결 후 ACK 반환 |
| **Function/UX Effect** | 운영 중 포트 설정 변경 즉시 적용. Master UI에서 적용 성공/실패 피드백 즉시 확인 |
| **Core Value** | 현장 재시작 없이 카메라 시리얼 설정 실시간 변경 |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 카메라마다 다른 가상 시리얼 포트/속도를 운영 중 변경해야 함 |
| **WHO** | 열화상 카메라 시스템 운영자 (Master PC 조작) |
| **RISK** | Agent 재연결 실패 시 셔터 제어 불능 — ACK 타임아웃으로 UI에 명시 |
| **SUCCESS** | Master UI에서 설정 변경 → 5초 내 Agent 재연결 + ACK 수신 확인 |
| **SCOPE** | Core 모델/인터페이스 → Protocols NATS 구현 → Master UI → Agent 적용 |

---

## 1. Overview

### 1.1 Purpose

Agent PC에 USB-C로 연결된 카메라는 USB 영상 스트림과 가상 시리얼 포트를 동시에 생성한다.
시리얼 포트 번호(COM#)·통신 속도·패리티는 카메라/PC마다 다를 수 있으며,
현재는 전역 설정 파일(`hardware.json`) 수동 편집만 지원한다.
이 기능은 Master UI에서 카메라별 설정을 저장하고 실시간으로 Agent에 전달하여
재시작 없이 즉시 재연결되도록 한다.

### 1.2 Background

- USB-C 카메라 1개 → 가상 시리얼 포트 1개 (Agent PC)
- 셔터: 7바이트 binary 명령 (`_openBuffer` / `_closeBuffer`)
- 향후: ROM read/write 명령 추가 예정 (`ISerialShutterController` 확장)
- Master PC도 로컬 카메라 직접 제어 유지 (Q2:B)

### 1.3 Related Documents

- `AGENTS.md` — 시리얼 셔터 프로토콜 정의
- `docs/samples/hardware.json` — 현재 전역 SerialSettings 예시

---

## 2. Scope

### 2.1 In Scope

- [x] `CameraSerialSettings` 모델 (CameraIndex별 포트/속도/패리티)
- [x] `SerialConfigMessage` / `SerialConfigAckMessage` NATS 메시지 모델
- [x] `ICameraSerialSettingsRepository` + LiteDB 구현
- [x] `INatsCommunicationService` serial config 발행/구독 메서드 추가
- [x] Master: Settings 탭 (SettingsView + SettingsViewModel)
- [x] Master: 설정 저장 → NATS 전달 → ACK 수신 → UI 피드백
- [x] Agent: serial config 구독 → 재연결 → ACK 발행
- [x] 기존 `AppServices.ShutterController` (Master 로컬) 유지
- [x] 신규 테스트 (모델 직렬화, repo, NATS 메시지)

### 2.2 Out of Scope

- ROM read/write 기능 (향후 별도 기능)
- Agent 간 설정 동기화 (각 Agent 독립 관리)
- 시리얼 포트 자동 검색 (COM 포트 목록 열거)
- 설정 히스토리/롤백

---

## 3. Requirements

### 3.1 Functional Requirements

| ID | Requirement | Priority | Status |
|----|-------------|----------|--------|
| FR-01 | `CameraSerialSettings` 모델: CameraIndex, PortName, BaudRate, DataBits, Parity, StopBits | High | Pending |
| FR-02 | `ICameraSerialSettingsRepository`: GetAll / GetByCameraIndex / Upsert | High | Pending |
| FR-03 | `INatsCommunicationService`에 `PublishSerialConfigAsync` / `SubscribeSerialConfigAsync` / `PublishSerialConfigAckAsync` / `SubscribeSerialConfigAckAsync` 추가 | High | Pending |
| FR-04 | NATS 토픽: `master.config.serial.{AgentId}` (Master→Agent), `agent.config.serial.ack.{AgentId}` (Agent→Master) | High | Pending |
| FR-05 | Master Settings 탭: 카메라 목록 + 시리얼 설정 폼 + 저장/전송 버튼 | High | Pending |
| FR-06 | 전송 후 ACK 수신 시 UI에 성공/실패 메시지 표시 (5초 타임아웃) | High | Pending |
| FR-07 | Agent: serial config 수신 → 기존 포트 해제 → 새 설정으로 재연결 → ACK 발행 | High | Pending |
| FR-08 | Master 로컬 `ShutterController` (`AppServices`) 유지 — 설정 변경 시 Master도 재연결 | Medium | Pending |
| FR-09 | `LiteDbCameraSerialSettingsRepository` 구현 + `AppServices`에 등록 | High | Pending |

### 3.2 Non-Functional Requirements

| Category | Criteria | Measurement Method |
|----------|----------|-------------------|
| 응답성 | 설정 전달 후 ACK 수신까지 5초 이내 | UI 타임아웃 카운터 |
| 안정성 | ACK 타임아웃 시 오류 메시지 표시, 기존 연결 유지 | 수동 검증 |
| 확장성 | `ISerialShutterController` 인터페이스 변경 없이 ROM 기능 추가 가능 | 인터페이스 리뷰 |

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] FR-01~FR-09 전부 구현
- [ ] `dotnet build` 0 errors / 0 warnings
- [ ] `dotnet test` 기존 22개 + 신규 테스트 전부 통과
- [ ] Master UI에서 설정 변경 → Agent ACK 수신 흐름 수동 검증

### 4.2 Quality Criteria

- [ ] `Nullable=enable` 경고 0개
- [ ] 신규 public 인터페이스 전부 Core 프로젝트에 위치
- [ ] NATS 토픽 문자열 상수화 (매직 스트링 금지)

---

## 5. Risks and Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Agent가 오프라인일 때 설정 전송 | Medium | Medium | UI에 "Agent 오프라인" 경고, 설정은 DB에 저장 (재접속 시 재전송 가능) |
| COM 포트 번호 오입력 | High | Medium | Agent ACK에 에러 메시지 포함, UI에 명시 |
| Master 로컬 ShutterController 재연결 실패 | Medium | Low | 예외 catch → `Debug.WriteLine`, UI 별도 에러 표시 없음 (로컬은 선택적) |

---

## 6. Impact Analysis

### 6.1 Changed Resources

| Resource | Type | Change Description |
|----------|------|--------------------|
| `INatsCommunicationService` | Interface | serial config 발행/구독 메서드 4개 추가 |
| `NatsCommunicationService` | Class | 위 인터페이스 구현 |
| `AppServices` | Static class | `CameraSerialSettingsRepo` 추가, `ShutterController` 재연결 로직 |
| `Agent/Program.cs` | Entry point | serial config 구독 + `SerialShutterController` 생명주기 관리 |
| `HardwareSettings.Serial` | Config | 전역 설정 유지 (Master 로컬 기본값으로 활용) |

### 6.2 Current Consumers

| Resource | Operation | Code Path | Impact |
|----------|-----------|-----------|--------|
| `INatsCommunicationService` | Implement | `NatsCommunicationService.cs` | 메서드 추가, 기존 구현 유지 |
| `INatsCommunicationService` | Use | `RecipeEngine.cs` (capture cmd) | 변경 없음 |
| `INatsCommunicationService` | Use | `Agent/Program.cs` (subscribe) | 구독 항목 추가 |
| `AppServices.ShutterController` | Use | `ConnectionMonitorService.cs` | 변경 없음 |
| `AppServices` | Init | `App.xaml.cs` | `CameraSerialSettingsRepo` 초기화 추가 |

### 6.3 Verification

- [ ] 기존 캡처 명령 NATS 흐름 영향 없음
- [ ] `ConnectionMonitorService` 기존 ShutterController 참조 유지
- [ ] `RecipeEngine` 변경 없음

---

## 7. Architecture Considerations

### 7.1 Project Level: Enterprise (기존 프로젝트 구조 유지)

레이어 분리: Core(인터페이스) → Protocols(구현) → Master/Agent(앱)

### 7.2 Key Architectural Decisions

| Decision | Selected | Rationale |
|----------|----------|-----------|
| 설정 저장소 | LiteDB (기존 `data.db`) | 신규 DB 도입 없이 기존 패턴 재사용 |
| NATS 전달 방식 | fire-and-send + ACK 구독 (5초 타임아웃) | 즉시 피드백, 구현 단순 |
| UI 위치 | 별도 SettingsView 탭 (Q1:B) | CameraMappingView 복잡도 분리 |
| Master 로컬 제어 | 유지 (Q2:B) | Master PC에도 카메라 직접 연결 가능성 |
| ACK 토픽 | `agent.config.serial.ack.{AgentId}` | 기존 `agent.*` 패턴 일관성 |

### 7.3 NATS 토픽 추가

```
master.config.serial.{AgentId}       ← Master → Agent (설정 전달)
agent.config.serial.ack.{AgentId}   ← Agent → Master (적용 결과)
```

### 7.4 신규 파일 목록

```
Core/Models/
  CameraSerialSettings.cs            ← 카메라별 시리얼 설정 모델
  NatsMessages.cs                    ← SerialConfigMessage, SerialConfigAckMessage 추가

Core/Interfaces/
  ICameraSerialSettingsRepository.cs ← GetAll / GetByCameraIndex / Upsert

Master/Services/
  LiteDbCameraSerialSettingsRepository.cs

Master/ViewModels/
  SettingsViewModel.cs

Master/Views/
  SettingsView.xaml
  SettingsView.xaml.cs
```

---

## 8. Convention Prerequisites

### 8.1 기존 규칙 (변경 없음)

- `Nullable=enable` + `ImplicitUsings=enable` 전 프로젝트
- LiteDB Repository 패턴: `LiteDbXxxRepository : IXxxRepository`
- ViewModel: `CommunityToolkit.Mvvm` (`ObservableObject`, `[RelayCommand]`)
- NATS 메시지: `Core/Models/NatsMessages.cs`에 집중 관리

### 8.2 신규 규칙

- NATS 토픽 문자열: `NatsTopics` 정적 클래스로 상수화 (`Core/Models/NatsTopics.cs`)

---

## 9. Next Steps

1. [ ] `/pdca design camera-serial-config` — 설계 문서 작성
2. [ ] `/pdca do camera-serial-config` — 구현 시작
3. [ ] `/pdca analyze camera-serial-config` — 갭 분석

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-06-19 | Initial draft | - |
