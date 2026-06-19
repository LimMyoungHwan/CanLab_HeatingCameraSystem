# camera-serial-config Design Document

> **Summary**: 카메라별 시리얼 설정 관리 — Core 모델 → LiteDB → NATS 전달 → Agent 재연결 → ACK
>
> **Project**: HeatingCameraSystem
> **Version**: 0.1
> **Author**: -
> **Date**: 2026-06-19
> **Status**: Draft
> **Planning Doc**: [camera-serial-config.plan.md](../../01-plan/features/camera-serial-config.plan.md)

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

### 1.1 Design Goals

- 기존 `LiteDbXxxRepository` 패턴을 그대로 따라 신규 코드 러닝커브 최소화
- `CommunityToolkit.Mvvm` 기반 SettingsViewModel — 기존 VM과 동일 구조
- 향후 ROM read/write 명령은 `ISerialShutterController` 메서드 추가만으로 확장 가능
- `INatsCommunicationService` 변경은 인터페이스 추가뿐 — 기존 캡처/상태 흐름 영향 없음

### 1.2 Design Principles

- **기존 패턴 일치**: 새 클래스가 기존 클래스와 구조적으로 동일하게 읽혀야 함
- **최소 변경**: 기존 파일 수정은 필요 최소한으로 (인터페이스 추가, AppServices 등록, Agent 구독 추가)
- **명시적 실패**: ACK 5초 타임아웃 시 예외 삼키지 않고 UI에 오류 표시

---

## 2. Architecture (Option C: Pragmatic)

### 2.1 Component Diagram

```
[Master PC]
  SettingsView (WPF)
       ↓ 저장 버튼 클릭
  SettingsViewModel
       ↓ UpsertAsync(CameraSerialSettings)
  ICameraSerialSettingsRepository  →  LiteDbCameraSerialSettingsRepository
                                              ↓ (data.db / "camera_serial_settings")
       ↓ PublishSerialConfigAsync(SerialConfigMessage)
  INatsCommunicationService
       ↓ NATS: master.config.serial.{AgentId}
[Agent PC]
  Program.cs — SubscribeSerialConfigAsync
       ↓ SerialShutterController.Disconnect() → ConnectAsync(new settings)
       ↓ PublishSerialConfigAckAsync(SerialConfigAckMessage)
  INatsCommunicationService
       ↓ NATS: agent.config.serial.ack.{AgentId}
[Master PC]
  SettingsViewModel — SubscribeSerialConfigAckAsync
       ↓ ACK 수신 (5초 타임아웃)
  UI 피드백 (성공 / 실패 / 타임아웃)
```

### 2.2 NATS 토픽

```
master.config.serial.{AgentId}         Master → 특정 Agent (설정 전달)
agent.config.serial.ack.{AgentId}     Agent → Master (적용 결과)
```

### 2.3 신규 파일 (7개)

```
Core/Models/CameraSerialSettings.cs
Core/Interfaces/ICameraSerialSettingsRepository.cs
Master/Services/LiteDbCameraSerialSettingsRepository.cs
Master/ViewModels/SettingsViewModel.cs
Master/Views/SettingsView.xaml
Master/Views/SettingsView.xaml.cs
```
+ `Core/Models/NatsMessages.cs` 에 2개 클래스 추가 (별도 파일 아님)

### 2.4 수정 파일 (5개)

```
Core/Models/NatsMessages.cs                   SerialConfigMessage, SerialConfigAckMessage 추가
Core/Interfaces/INatsCommunicationService.cs  메서드 4개 추가
Protocols/NatsCommunicationService.cs         위 구현
Master/Services/AppServices.cs               CameraSerialSettingsRepo 등록 + Master 재연결
Agent/Program.cs                             serial config 구독 + 재연결 + ACK
```

---

## 3. Data Model

### 3.1 CameraSerialSettings

```csharp
// Core/Models/CameraSerialSettings.cs
namespace HeatingCameraSystem.Core.Models
{
    public class CameraSerialSettings
    {
        public int    CameraIndex { get; set; }         // 식별자 (Agent_{CameraIndex})
        public string PortName    { get; set; } = "COM3";
        public int    BaudRate    { get; set; } = 9600;
        public int    DataBits    { get; set; } = 8;
        public string Parity      { get; set; } = "None";   // None/Odd/Even/Mark/Space
        public string StopBits    { get; set; } = "One";    // None/One/OnePointFive/Two
    }
}
```

### 3.2 NatsMessages 추가 (NatsMessages.cs)

```csharp
public class SerialConfigMessage
{
    public string               AgentId  { get; set; } = string.Empty;
    public CameraSerialSettings Settings { get; set; } = new();
    public DateTime             Timestamp{ get; set; }
}

public class SerialConfigAckMessage
{
    public string   AgentId      { get; set; } = string.Empty;
    public bool     IsSuccess    { get; set; }
    public string   ErrorMessage { get; set; } = string.Empty;
    public DateTime Timestamp    { get; set; }
}
```

### 3.3 LiteDB 스토리지

- 컬렉션명: `"camera_serial_settings"`
- 키: `CameraIndex` (BsonId)
- 패턴: 기존 `LiteDbCameraMappingRepository`와 동일 — 단일 도큐먼트 래퍼 없이 개별 도큐먼트

```
[BsonId] CameraIndex → CameraSerialSettings 1:1 저장
Upsert(settings) 호출 시 CameraIndex 기준 덮어쓰기
```

---

## 4. Interface Specification

### 4.1 ICameraSerialSettingsRepository

```csharp
// Core/Interfaces/ICameraSerialSettingsRepository.cs
public interface ICameraSerialSettingsRepository
{
    Task<IEnumerable<CameraSerialSettings>> GetAllAsync();
    Task<CameraSerialSettings?>             GetByCameraIndexAsync(int cameraIndex);
    Task                                    UpsertAsync(CameraSerialSettings settings);
}
```

### 4.2 INatsCommunicationService 추가 메서드

```csharp
// Core/Interfaces/INatsCommunicationService.cs 에 추가
// 토픽: master.config.serial.{AgentId}
Task PublishSerialConfigAsync(SerialConfigMessage message);
Task SubscribeSerialConfigAsync(string agentId, Action<SerialConfigMessage> onMessageReceived);

// 토픽: agent.config.serial.ack.{AgentId}
Task PublishSerialConfigAckAsync(SerialConfigAckMessage message);
Task SubscribeSerialConfigAckAsync(string agentId, Action<SerialConfigAckMessage> onMessageReceived);
```

---

## 5. UI/UX Design

### 5.1 SettingsView 레이아웃

```
┌─────────────────────────────────────────────────────────────┐
│  Settings                                                    │
├─────────────────────────────────────────────────────────────┤
│  카메라 선택:  [ComboBox: Camera 0 ▼]                        │
├──────────────────────────────┬──────────────────────────────┤
│  Port Name   [COM3      ]    │  Baud Rate  [9600       ]   │
│  Data Bits   [8         ]    │  Parity     [None       ▼]  │
│  Stop Bits   [One       ▼]   │                             │
├──────────────────────────────┴──────────────────────────────┤
│  [저장 & 전송]          상태: ● 대기중                        │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 상태 표시

| 상태 | 텍스트 | 색상 |
|---|---|---|
| 대기 | `대기중` | 회색 |
| 전송 중 | `전송 중...` | 파란색 |
| 성공 | `✔ 적용 완료 (Agent_N)` | 녹색 |
| 실패 | `✘ 오류: {ErrorMessage}` | 빨간색 |
| 타임아웃 | `✘ 응답 없음 (5초 초과)` | 빨간색 |

### 5.3 Page UI Checklist

#### SettingsView

- [ ] ComboBox: 카메라 선택 (`CameraIndex` 기준, 저장된 항목 목록)
- [ ] TextBox: PortName (COM3, COM4 ...)
- [ ] TextBox: BaudRate (숫자)
- [ ] TextBox: DataBits (숫자)
- [ ] ComboBox: Parity (None / Odd / Even / Mark / Space)
- [ ] ComboBox: StopBits (One / OnePointFive / Two)
- [ ] Button: 저장 & 전송 (`SaveAndSendCommand`)
- [ ] TextBlock: 상태 표시 (`StatusMessage` 바인딩)

---

## 6. Error Handling

| 상황 | 처리 |
|---|---|
| Agent 오프라인 (NATS 전달 실패) | 설정은 DB 저장 완료, UI에 "전달 실패" 표시 |
| Agent COM 포트 오류 (재연결 실패) | ACK `IsSuccess=false`, `ErrorMessage`에 예외 메시지, UI 빨간색 표시 |
| ACK 5초 타임아웃 | `CancellationTokenSource(5s)` 만료 → UI "응답 없음" 표시 |
| Master 로컬 ShutterController 재연결 실패 | `Debug.WriteLine`만 (UI 별도 표시 없음) |

---

## 7. Security Considerations

- NATS는 로컬 네트워크 전용 (인증 불필요, 기존 설계 유지)
- COM 포트명 입력값 별도 검증 없음 (SerialPort 생성자가 예외 처리)

---

## 8. Test Plan

### 8.1 테스트 범위

| Type | 대상 | 도구 |
|---|---|---|
| L1: 단위 | `CameraSerialSettings` JSON 직렬화, `LiteDbCameraSerialSettingsRepository` CRUD | xUnit |
| L2: 단위 | `SerialConfigMessage` / `SerialConfigAckMessage` 모델 | xUnit |
| L3: 통합 | Agent 오프라인 시 UI 상태 전이 | 수동 |

### 8.2 L1 테스트 시나리오

| # | 대상 | 설명 | 기대 결과 |
|---|---|---|---|
| 1 | `CameraSerialSettings` | 기본값 생성 | `PortName="COM3"`, `BaudRate=9600` |
| 2 | `LiteDbCameraSerialSettingsRepository.UpsertAsync` | 신규 저장 후 GetByCameraIndex | 동일 값 반환 |
| 3 | `LiteDbCameraSerialSettingsRepository.UpsertAsync` | 같은 CameraIndex 덮어쓰기 | 최신 값 반환, 중복 없음 |
| 4 | `LiteDbCameraSerialSettingsRepository.GetAllAsync` | 3개 저장 후 조회 | count = 3 |
| 5 | `SerialConfigAckMessage` | IsSuccess=false, ErrorMessage 설정 | 직렬화/역직렬화 일치 |

---

## 9. Clean Architecture (기존 프로젝트 레이어)

| 컴포넌트 | 레이어 | 위치 |
|---|---|---|
| `CameraSerialSettings` | Core (Model) | `Core/Models/` |
| `SerialConfigMessage`, `SerialConfigAckMessage` | Core (Model) | `Core/Models/NatsMessages.cs` |
| `ICameraSerialSettingsRepository` | Core (Interface) | `Core/Interfaces/` |
| `INatsCommunicationService` 추가 메서드 | Core (Interface) | `Core/Interfaces/` |
| `LiteDbCameraSerialSettingsRepository` | Master (Infrastructure) | `Master/Services/` |
| `NatsCommunicationService` 구현 | Protocols (Infrastructure) | `Protocols/` |
| `SettingsViewModel` | Master (Presentation) | `Master/ViewModels/` |
| `SettingsView` | Master (Presentation) | `Master/Views/` |
| Agent `Program.cs` 수정 | Agent (App) | `Agent/` |

---

## 10. Coding Convention

| 항목 | 규칙 |
|---|---|
| Repository | `LiteDbXxxRepository : IXxxRepository` 패턴 유지 |
| ViewModel | `ObservableObject` 상속, 커맨드는 `[RelayCommand]` |
| NATS 구독 | `_ = Task.Run(async () => { await foreach ... })` 기존 패턴 유지 |
| Nullable | 모든 `string` 프로퍼티 기본값 명시 (`= string.Empty` or `= "default"`) |
| 예외 | catch 후 `Debug.WriteLine` + 필요 시 UI 상태 업데이트, 빈 catch 금지 |

---

## 11. Implementation Guide

### 11.1 구현 순서 (의존성 기준)

```
[1] Core 모델/인터페이스 (의존성 없음)
    CameraSerialSettings.cs
    NatsMessages.cs (+SerialConfigMessage, SerialConfigAckMessage)
    ICameraSerialSettingsRepository.cs
    INatsCommunicationService.cs (+4 메서드)

[2] Protocols 구현 (Core 참조)
    NatsCommunicationService.cs (+4 메서드 구현)

[3] Master 인프라 (Core + LiteDB)
    LiteDbCameraSerialSettingsRepository.cs
    AppServices.cs (repo 등록, ShutterController 재연결 메서드)

[4] Master UI (Master 인프라 참조)
    SettingsViewModel.cs
    SettingsView.xaml + .cs
    MainWindow/NavigationView에 Settings 탭 연결

[5] Agent (Core + Protocols 참조)
    Program.cs (serial config 구독 + 재연결 + ACK)

[6] Tests
    CameraSerialSettingsTests.cs
```

### 11.2 핵심 구현 패턴

#### LiteDbCameraSerialSettingsRepository
```csharp
// CameraIndex를 BsonId로 직접 사용
_col = db.GetCollection<CameraSerialSettings>("camera_serial_settings");
_col.EnsureIndex(x => x.CameraIndex);

// Upsert: CameraIndex 기준 덮어쓰기
public Task UpsertAsync(CameraSerialSettings s)
{
    _col.Upsert(s);   // LiteDB Upsert by BsonId
    return Task.CompletedTask;
}
```

> ⚠️ `CameraSerialSettings.CameraIndex`에 `[BsonId]` 어트리뷰트 필요
> (`using LiteDB;` → `LiteDB.BsonIdAttribute`)

#### SettingsViewModel — 저장 & 전송 흐름
```csharp
[RelayCommand]
private async Task SaveAndSendAsync()
{
    StatusMessage = "전송 중...";
    await _repo.UpsertAsync(CurrentSettings);

    string agentId = $"Agent_{CurrentSettings.CameraIndex}";
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var ackTcs = new TaskCompletionSource<SerialConfigAckMessage>();

    await _nats.SubscribeSerialConfigAckAsync(agentId, ack => ackTcs.TrySetResult(ack));
    await _nats.PublishSerialConfigAsync(new SerialConfigMessage
    {
        AgentId   = agentId,
        Settings  = CurrentSettings,
        Timestamp = DateTime.UtcNow
    });

    try
    {
        var ack = await ackTcs.Task.WaitAsync(cts.Token);
        StatusMessage = ack.IsSuccess
            ? $"✔ 적용 완료 ({agentId})"
            : $"✘ 오류: {ack.ErrorMessage}";
    }
    catch (OperationCanceledException)
    {
        StatusMessage = "✘ 응답 없음 (5초 초과)";
    }
}
```

#### Agent Program.cs — 수신 + 재연결 + ACK
```csharp
await nats.SubscribeSerialConfigAsync(config.AgentId, async msg =>
{
    bool success = true;
    string error = string.Empty;
    try
    {
        shutterController?.Disconnect();
        shutterController = new SerialShutterController(new SerialSettings
        {
            PortName = msg.Settings.PortName,
            BaudRate = msg.Settings.BaudRate,
            DataBits = msg.Settings.DataBits,
            Parity   = msg.Settings.Parity,
            StopBits = msg.Settings.StopBits
        });
        await shutterController.ConnectAsync();
    }
    catch (Exception ex)
    {
        success = false;
        error   = ex.Message;
    }

    await nats.PublishSerialConfigAckAsync(new SerialConfigAckMessage
    {
        AgentId      = config.AgentId,
        IsSuccess    = success,
        ErrorMessage = error,
        Timestamp    = DateTime.UtcNow
    });
});
```

#### AppServices — Master 로컬 ShutterController 재연결
```csharp
public static async Task ApplySerialSettingsLocallyAsync(CameraSerialSettings s)
{
    ShutterController?.Dispose();
    ShutterController = new SerialShutterController(new SerialSettings
    {
        PortName = s.PortName,
        BaudRate = s.BaudRate,
        DataBits = s.DataBits,
        Parity   = s.Parity,
        StopBits = s.StopBits
    });
    try { await ShutterController.ConnectAsync(); }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[AppServices] Local shutter reconnect failed: {ex.Message}");
    }
}
```

### 11.3 Session Guide

#### Module Map

| Module | Scope Key | 내용 | 예상 턴 |
|---|---|---|---|
| Core 모델 + 인터페이스 | `module-1` | 신규 모델 2종, 인터페이스 2개 수정 | 10 |
| Protocols + Master 인프라 | `module-2` | NATS 구현 4개, LiteDB repo, AppServices | 15 |
| Master UI | `module-3` | SettingsViewModel, SettingsView.xaml | 20 |
| Agent + Tests | `module-4` | Program.cs 수신/ACK, xUnit 5개 | 15 |

#### Recommended Session Plan

| Session | Scope | 내용 |
|---|---|---|
| Session 1 (현재) | Plan + Design | ✅ 완료 |
| Session 2 | `module-1` + `module-2` | Core/Protocols/Master 인프라 |
| Session 3 | `module-3` | Master UI |
| Session 4 | `module-4` + Check | Agent + 테스트 + 갭 분석 |

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-06-19 | Initial draft (Option C) | - |
