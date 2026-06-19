# agent-status-display Design Document

> **Summary**: NATS 하트비트 → 하이브리드 AgentNode 관리 → 상태 점 실시간 표시
>
> **Project**: HeatingCameraSystem
> **Date**: 2026-06-19
> **Status**: Draft
> **Selected Architecture**: Option C (Pragmatic)
> **Planning Doc**: [agent-status-display.plan.md](../../01-plan/features/agent-status-display.plan.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 실제 Agent/Camera 상태 미표시 → 운영자 판단 불가 |
| **WHO** | 열화상 카메라 시스템 운영자 |
| **RISK** | NATS 콜백에서 UI 스레드 직접 접근 → Dispatcher 필수 |
| **SUCCESS** | Agent 접속 5초 내 녹색, 15초 미수신 시 회색 전환 |
| **SCOPE** | 모델 수정 → Agent 전송 → VM 구독/타이머 → XAML 바인딩 |

---

## 1. Architecture (Option C: Pragmatic)

### 1.1 흐름

```
[Agent PC]  (5초 간격)
  CameraStatus 계산 → PublishAgentStatusAsync
       ↓ NATS: agent.status.{AgentId}
[DashboardViewModel]
  SubscribeAgentStatusAsync 콜백
       → Dispatcher.Invoke
       → _agentMap 하이브리드 관리 (신규 추가 or 기존 업데이트)
       → AgentNode.IsOnline = true, LastHeartbeat = now
       → CameraNode.CameraStatus 업데이트
  _offlineCheckTimer (5초 간격)
       → LastHeartbeat > 15초 → IsOnline = false, 모든 Camera = Offline
[DashboardView.xaml]
  Agent 헤더: IsOnline DataTrigger → 점 색상 (녹색/회색)
  Camera 항목: CameraStatus DataTrigger → 점 색상 (cyan/green/gray)
```

### 1.2 수정 파일 (5개)

```
Core/Models/NatsMessages.cs              CameraStatus enum + AgentStatusMessage 수정
Agent/Program.cs                         CameraStatus 계산 + 하트비트 수정
Master/ViewModels/DashboardViewModel.cs  AgentNode/CameraNode + 구독 + 오프라인 타이머
Master/Views/DashboardView.xaml          상태 점 DataTrigger 바인딩
HeatingCameraSystem.Tests/               기존 테스트 수정 + 신규 추가
```

---

## 2. Data Model

### 2.1 CameraStatus enum (신규)

```csharp
// Core/Models/NatsMessages.cs 상단에 추가
public enum CameraStatus
{
    Offline,    // 카메라 미감지 (InitializeCamera 실패)
    Connected,  // 카메라 감지됨, 캡처 대기
    Streaming   // 활성 캡처 중 (RecipeEngine 실행 중)
}
```

### 2.2 AgentStatusMessage 수정

```csharp
public class AgentStatusMessage
{
    public string       AgentId      { get; set; } = string.Empty;
    public int          CameraIndex  { get; set; }
    public CameraStatus CameraStatus { get; set; }  // IsCameraReady 교체
    public DateTime     Timestamp    { get; set; }
}
```

### 2.3 AgentNode 수정 (DashboardViewModel.cs)

```csharp
public partial class AgentNode : ObservableObject
{
    [ObservableProperty] private string   _name          = string.Empty;
    [ObservableProperty] private bool     _isExpanded    = true;
    [ObservableProperty] private bool     _isOnline      = false;  // 신규
    [ObservableProperty] private DateTime _lastHeartbeat = DateTime.MinValue; // 신규
    public ObservableCollection<CameraNode> Cameras { get; } = new();
}
```

### 2.4 CameraNode 수정 (DashboardViewModel.cs)

```csharp
public partial class CameraNode : ObservableObject
{
    [ObservableProperty] private string       _id                  = string.Empty;
    [ObservableProperty] private string       _status              = "IDLE";
    [ObservableProperty] private float        _currentTemperature  = 0f;
    [ObservableProperty] private CameraStatus _cameraStatus        = CameraStatus.Offline; // 신규
}
```

---

## 3. DashboardViewModel 변경

### 3.1 필드 추가

```csharp
private readonly Dictionary<string, AgentNode> _agentMap = new();
private System.Windows.Threading.DispatcherTimer? _offlineCheckTimer;
```

### 3.2 생성자 변경

```csharp
public DashboardViewModel()
{
    // 더미 AgentNode 초기화 코드 전부 제거
    // Agents 컬렉션은 비어있는 채로 시작 (하트비트 수신 시 동적 추가)

    LoadRecipes();

    _plcPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
    _plcPollTimer.Tick += async (_, _) => await PollPlcAsync();
    _plcPollTimer.Start();

    _offlineCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
    _offlineCheckTimer.Tick += (_, _) => CheckOfflineAgents();
    _offlineCheckTimer.Start();

    _ = SubscribeAgentStatusAsync();
}
```

### 3.3 NATS 구독 + 하이브리드 관리

```csharp
private async Task SubscribeAgentStatusAsync()
{
    if (AppServices.NatsService == null) return;

    await AppServices.NatsService.SubscribeAgentStatusAsync(msg =>
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // 하이브리드: 없으면 추가, 있으면 업데이트
            if (!_agentMap.TryGetValue(msg.AgentId, out var agent))
            {
                agent = new AgentNode { Name = msg.AgentId, IsExpanded = true };
                _agentMap[msg.AgentId] = agent;
                Agents.Add(agent);
            }

            agent.IsOnline      = true;
            agent.LastHeartbeat = msg.Timestamp;

            // Camera 업데이트 (없으면 추가)
            var cam = agent.Cameras.FirstOrDefault(c => c.Id == $"CAM-{msg.CameraIndex:D2}");
            if (cam == null)
            {
                cam = new CameraNode { Id = $"CAM-{msg.CameraIndex:D2}" };
                agent.Cameras.Add(cam);
            }
            cam.CameraStatus = msg.CameraStatus;
        });
    });
}
```

### 3.4 오프라인 타이머

```csharp
private void CheckOfflineAgents()
{
    var threshold = DateTime.UtcNow.AddSeconds(-15);
    foreach (var agent in Agents)
    {
        if (agent.LastHeartbeat < threshold && agent.IsOnline)
        {
            agent.IsOnline = false;
            foreach (var cam in agent.Cameras)
                cam.CameraStatus = CameraStatus.Offline;
        }
    }
}
```

---

## 4. Agent/Program.cs CameraStatus 계산

```csharp
// 기존: IsCameraReady = cameraReady
// 변경: CameraStatus 계산
CameraStatus cameraStatus = cameraReady ? CameraStatus.Connected : CameraStatus.Offline;

// 하트비트 발행
await nats.PublishAgentStatusAsync(new AgentStatusMessage
{
    AgentId      = config.AgentId,
    CameraIndex  = config.CameraIndex,
    CameraStatus = cameraStatus,
    Timestamp    = DateTime.UtcNow
});
```

> `Streaming` 상태는 향후 RecipeEngine 실행 중 신호로 전환 예정. 현 단계에서는 `Connected`와 구분 없이 `Connected` 사용.

---

## 5. UI 설계

### 5.1 색상 매핑

| 상태 | 색상 | 코드 |
|---|---|---|
| Agent 온라인 | 녹색 | `#4ae183` (Secondary) |
| Agent 오프라인 | 회색 | `#859493` (Outline) |
| Camera Connected | 시안 | `#47eaed` (Primary) |
| Camera Streaming | 녹색 | `#4ae183` (Secondary) |
| Camera Offline | 회색 | `#859493` (Outline) |

### 5.2 XAML 변경 — Agent 헤더 상태 점

```xml
<!-- 기존: TextBlock Text="{Binding Status}" 제거 -->
<!-- 추가: 상태 점 -->
<Ellipse Width="8" Height="8" HorizontalAlignment="Right" VerticalAlignment="Center">
    <Ellipse.Style>
        <Style TargetType="Ellipse">
            <Setter Property="Fill" Value="#859493"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsOnline}" Value="True">
                    <Setter Property="Fill" Value="#4ae183"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Ellipse.Style>
</Ellipse>
```

### 5.3 XAML 변경 — Camera 항목 상태 점

```xml
<!-- 기존: Fill="{StaticResource Secondary}" 고정 -->
<!-- 변경: CameraStatus 기반 3색 -->
<Ellipse Width="8" Height="8" Margin="0,0,12,0" VerticalAlignment="Center">
    <Ellipse.Style>
        <Style TargetType="Ellipse">
            <Setter Property="Fill" Value="#859493"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding CameraStatus}"
                             Value="{x:Static models:CameraStatus.Connected}">
                    <Setter Property="Fill" Value="#47eaed"/>
                </DataTrigger>
                <DataTrigger Binding="{Binding CameraStatus}"
                             Value="{x:Static models:CameraStatus.Streaming}">
                    <Setter Property="Fill" Value="#4ae183"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Ellipse.Style>
</Ellipse>
```

### 5.4 Page UI Checklist

#### Dashboard 우측 사이드바 — Agent 항목
- [ ] Ellipse: Agent 상태 점 (IsOnline True=녹색, False=회색)
- [ ] TextBlock: Agent Name (AgentId 표시)

#### Dashboard 우측 사이드바 — Camera 항목
- [ ] Ellipse: 3색 상태 점 (Connected=cyan / Streaming=green / Offline=gray)
- [ ] TextBlock: Camera ID

---

## 6. Test Plan

| # | 대상 | 설명 | 기대 결과 |
|---|---|---|---|
| 1 | `CameraStatus` | 기본값 직렬화 | `Offline` = 0 |
| 2 | `AgentStatusMessage` | `CameraStatus.Connected` 직렬화/역직렬화 | 값 일치 |
| 3 | `CheckOfflineAgents` | LastHeartbeat = 20초 전 → IsOnline false | 전환 확인 |
| 4 | 하이브리드 관리 | 동일 AgentId 2회 수신 | AgentNode 1개만 존재 |

---

## 7. Implementation Guide

### 7.1 구현 순서

```
[1] Core/Models/NatsMessages.cs
    - CameraStatus enum 추가
    - AgentStatusMessage.IsCameraReady → CameraStatus 교체

[2] Agent/Program.cs
    - 하트비트에서 CameraStatus 전달

[3] Master/ViewModels/DashboardViewModel.cs
    - AgentNode/CameraNode 프로퍼티 추가
    - 더미 데이터 제거
    - _agentMap + _offlineCheckTimer + SubscribeAgentStatusAsync

[4] Master/Views/DashboardView.xaml
    - Agent 헤더 상태 점
    - Camera 상태 점 3색

[5] Tests 업데이트 + 신규 추가
```

### 7.2 Session Guide

| Module | Scope Key | 내용 |
|---|---|---|
| 모델 + Agent | `module-1` | NatsMessages + Agent/Program.cs |
| VM + View | `module-2` | DashboardViewModel + DashboardView.xaml + Tests |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 0.1 | 2026-06-19 | Initial draft (Option C) |
