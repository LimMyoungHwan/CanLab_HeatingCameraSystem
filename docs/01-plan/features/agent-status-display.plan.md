# agent-status-display Planning Document

> **Summary**: Agent 연결 상태 + 카메라 3단계 상태를 실시간으로 Dashboard 우측 패널에 표시
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
| **Problem** | Agent/Camera 상태가 더미 데이터 고정값 — 실제 연결 여부를 운영자가 확인 불가 |
| **Solution** | NATS 하트비트 구독 → AgentNode 동적 추가/업데이트 → 상태 점 실시간 표시 |
| **Function/UX Effect** | 우측 사이드바에서 Agent 온라인/오프라인(2단계) + 카메라 연결/스트리밍/오프라인(3단계) 즉시 확인 |
| **Core Value** | 운영자가 전체 카메라 네트워크 상태를 한눈에 파악 |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 실제 Agent 연결 상태 미표시 → 운영자 판단 불가 |
| **WHO** | 열화상 카메라 시스템 운영자 (Master PC) |
| **RISK** | UI 스레드 업데이트 안전성 (NATS 콜백 → Dispatcher) |
| **SUCCESS** | Agent 접속 시 5초 내 녹색 점 표시, 15초 미수신 시 회색 전환 |
| **SCOPE** | NatsMessages 모델 → Agent 상태 전송 → DashboardViewModel 구독 → XAML 바인딩 |

---

## 1. Overview

### 1.1 Purpose

현재 Dashboard 우측 패널은 더미 데이터를 표시하며 실제 Agent/Camera 상태를 반영하지 않는다.
NATS 하트비트(`agent.status.{AgentId}`)를 구독하여 실시간 상태를 표시한다.

### 1.2 Background

- Agent 하트비트: 5초 간격, `agent.status.{AgentId}` 토픽
- `AgentStatusMessage`: `AgentId`, `CameraIndex`, `IsCameraReady`, `Timestamp`
- 현재 `IsCameraReady`(bool) → `CameraStatus` enum(3단계)으로 교체
- Agent 1개 = 카메라 1~N개 (USB-C 연결 시 카메라+가상 시리얼 생성)

### 1.3 Related Documents

- `AGENTS.md` — NATS 토픽 규칙, Agent 하트비트 5초 간격
- `docs/01-plan/features/camera-serial-config.plan.md` — 이전 PDCA 참조

---

## 2. Scope

### 2.1 In Scope

- [x] `CameraStatus` enum 추가 (Connected / Streaming / Offline)
- [x] `AgentStatusMessage.IsCameraReady` → `CameraStatus`로 교체
- [x] `Agent/Program.cs`: `CameraStatus` 계산 후 하트비트 전송
- [x] `AgentNode`: `IsOnline` + `LastHeartbeat` 추가
- [x] `CameraNode`: `CameraStatus` 추가
- [x] `DashboardViewModel`: NATS 구독 + 하이브리드 AgentNode 관리 + 오프라인 타이머
- [x] `DashboardView.xaml`: Agent 헤더 상태 점 (녹색/회색) + Camera 상태 점 (3색)
- [x] 기존 더미 데이터 제거 → 실 데이터 기반으로 전환
- [x] 신규 테스트 (CameraStatus 직렬화, 오프라인 판정 로직)

### 2.2 Out of Scope

- Camera 스트리밍 영상 표시 (별도 기능)
- Agent 재시작/원격 제어
- 하트비트 주기 변경 UI

---

## 3. Requirements

### 3.1 Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-01 | `CameraStatus` enum: `Connected` / `Streaming` / `Offline` | High |
| FR-02 | `AgentStatusMessage`: `IsCameraReady` → `CameraStatus` 교체 | High |
| FR-03 | Agent: `CameraStatus` 계산 — Offline(카메라 미감지) / Streaming(캡처 중) / Connected(준비됨) | High |
| FR-04 | `AgentNode`: `IsOnline`(bool) + `LastHeartbeat`(DateTime) 추가 | High |
| FR-05 | `CameraNode`: `CameraStatus` 프로퍼티 추가 | High |
| FR-06 | `DashboardViewModel`: `SubscribeAgentStatusAsync` 구독, AgentId 기준 하이브리드 관리 | High |
| FR-07 | 오프라인 타이머: 5초 간격으로 `LastHeartbeat > 15초` 확인 → `IsOnline = false` | High |
| FR-08 | `DashboardView`: Agent 헤더에 상태 점 (IsOnline → 녹색/회색) | High |
| FR-09 | `DashboardView`: Camera 항목에 3색 점 (Connected=cyan/Streaming=green/Offline=gray) | High |
| FR-10 | 기존 더미 `AgentNode` 초기화 코드 제거 | High |

### 3.2 Non-Functional Requirements

| Category | Criteria |
|----------|----------|
| UI 스레드 안전 | NATS 콜백 → `Application.Current.Dispatcher.Invoke` |
| 타임아웃 정확도 | 15초 ± 5초 이내 오프라인 전환 |
| GC | 오프라인 타이머 1개 (DispatcherTimer 재사용) |

---

## 4. Success Criteria

- [ ] Agent 실행 시 5초 내 녹색 점 표시
- [ ] Agent 종료 15초 후 회색 전환
- [ ] Camera 상태 점 3색 정상 표시
- [ ] `dotnet build` 0 errors / 0 warnings
- [ ] `dotnet test` 기존 27 + 신규 통과

---

## 5. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| UI 스레드 충돌 | High | `Dispatcher.Invoke` 래핑 |
| `AgentStatusMessage` 변경으로 기존 테스트 깨짐 | Medium | 테스트 업데이트 포함 |
| 더미 데이터 제거 후 빈 목록 | Low | NATS 미연결 시 빈 상태 명시 |

---

## 6. Impact Analysis

### 6.1 Changed Resources

| Resource | Type | 변경 내용 |
|----------|------|----------|
| `AgentStatusMessage` | Model | `IsCameraReady`(bool) → `CameraStatus`(enum) |
| `AgentNode` | ViewModel 모델 | `IsOnline`, `LastHeartbeat` 추가 |
| `CameraNode` | ViewModel 모델 | `CameraStatus` 추가 |
| `DashboardViewModel` | ViewModel | 더미 제거, NATS 구독, 타이머 추가 |
| `DashboardView.xaml` | View | 상태 점 바인딩 변경 |

### 6.2 Current Consumers

| Resource | Code Path | Impact |
|----------|-----------|--------|
| `AgentStatusMessage.IsCameraReady` | `Agent/Program.cs` | 교체 필요 |
| `AgentNode` | `DashboardViewModel` 더미 초기화 | 제거 |
| Camera 상태 점 XAML | `DashboardView.xaml` line 291 | 바인딩 변경 |

---

## 7. Architecture

### 7.1 상태 정의

```
CameraStatus:
  Offline    → 카메라 미감지 (InitializeCamera 실패)
  Connected  → 카메라 감지됨, 캡처 대기 중
  Streaming  → 활성 캡처 중 (RecipeEngine 실행 중)

AgentNode.IsOnline:
  true  → LastHeartbeat ≤ 15초 전
  false → LastHeartbeat > 15초 전 OR 미수신
```

### 7.2 색상 매핑

| 상태 | 색상 | 코드 |
|---|---|---|
| Agent 온라인 | 녹색 | `#4ae183` (Secondary) |
| Agent 오프라인 | 회색 | `#859493` (Outline) |
| Camera Connected | 시안 | `#47eaed` (Primary) |
| Camera Streaming | 녹색 | `#4ae183` (Secondary) |
| Camera Offline | 회색 | `#859493` (Outline) |

### 7.3 하이브리드 AgentNode 관리

```
하트비트 수신 (agentId, cameraIndex, cameraStatus):
  1. _agentMap.TryGetValue(agentId, out var node)
  2. node == null → 신규 생성, Agents.Add(node), _agentMap[agentId] = node
  3. node.IsOnline = true, node.LastHeartbeat = DateTime.UtcNow
  4. node.Cameras에서 CameraIndex 일치하는 CameraNode 찾아 CameraStatus 업데이트
     없으면 신규 CameraNode 추가
```

### 7.4 수정 파일 (5개)

```
Core/Models/NatsMessages.cs          CameraStatus enum + AgentStatusMessage 수정
Agent/Program.cs                     CameraStatus 계산 + 하트비트 전송 수정
Master/ViewModels/DashboardViewModel.cs  AgentNode/CameraNode + 구독 + 타이머
Master/Views/DashboardView.xaml      상태 점 바인딩
HeatingCameraSystem.Tests/           기존 테스트 업데이트 + 신규 추가
```

---

## 8. Convention Prerequisites

- `Dispatcher.Invoke` 패턴: `Application.Current.Dispatcher.Invoke(() => { ... })`
- `DispatcherTimer` 기존 `_plcPollTimer` 패턴 동일하게 적용
- `ObservableObject` + `[ObservableProperty]` 패턴 유지

---

## 9. Next Steps

1. [ ] `/pdca design agent-status-display`
2. [ ] `/pdca do agent-status-display`

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-06-19 | Initial draft |
