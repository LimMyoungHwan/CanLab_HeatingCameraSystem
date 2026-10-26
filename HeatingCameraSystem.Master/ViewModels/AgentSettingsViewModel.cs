using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Core.Interfaces;
using System.Threading;
using HeatingCameraSystem.Master.Localization;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    /// <summary>
    /// Agent 설정 화면 카메라 그리드의 한 행. <see cref="CameraDescriptor"/>를 편집 가능한
    /// observable 형태로 감싼다.
    /// </summary>
    public partial class AgentCameraRow : ObservableObject
    {
        [ObservableProperty] private string _agentId = string.Empty;
        [ObservableProperty] private int _openCvIndex;
        [ObservableProperty] private string _alias = string.Empty;
        [ObservableProperty] private string _deviceName = string.Empty;
        [ObservableProperty] private string _serialPortName = string.Empty;

        public AgentCameraRow() { }

        public AgentCameraRow(CameraDescriptor d)
        {
            _agentId = d.AgentId;
            _openCvIndex = d.OpenCvIndex;
            _alias = d.Alias;
            _deviceName = d.DeviceName ?? string.Empty;
            _serialPortName = d.SerialPortName ?? string.Empty;
        }

        /// <summary>편집 행을 다시 <see cref="CameraDescriptor"/>로 변환한다. 공백 문자열은 null로 정규화한다.</summary>
        public CameraDescriptor ToDescriptor() =>
            new(AgentId, OpenCvIndex, Alias,
                string.IsNullOrWhiteSpace(SerialPortName) ? null : SerialPortName,
                string.IsNullOrWhiteSpace(DeviceName) ? null : DeviceName);
    }

    /// <summary>
    /// Agent 설정 화면. 온라인 Agent 목록을 하트비트로 수집하고, 선택한 Agent의 설정 스냅샷
    /// (agent.json 내용)을 NATS로 조회·수정·적용한다. NATS 콜백은 백그라운드 스레드로 오므로
    /// UI 상태 갱신은 항상 Dispatcher.Invoke로 마샬링한다.
    /// </summary>
    public partial class AgentSettingsViewModel : ObservableObject, IDisposable, IAsyncDisposable
    {
        private readonly HeatingCameraSystem.Core.Interfaces.INatsCommunicationService? _nats;
        private readonly NatsSubscriptionLifetime _subscriptions = new();
        private readonly Dictionary<string, Task> _subscriptionTasks = new(StringComparer.Ordinal);
        private readonly TimeSpan _responseTimeout;
        private string? _loadedAgentId;
        private string? _loadRequestId;
        private string? _applyRequestId;
        private TaskCompletionSource<AgentConfigSnapshotMessage>? _loadResponse;
        private TaskCompletionSource<AgentConfigAckMessage>? _applyResponse;
        private CancellationTokenSource? _operationCancellation;
        private bool _disposed;

        public ObservableCollection<string> OnlineAgents { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
        private string? _selectedAgent;

        [ObservableProperty] private bool _simulationMode;
        [ObservableProperty] private string _natsUrl = string.Empty;
        [ObservableProperty] private string _storagePath = string.Empty;
        [ObservableProperty] private int _heartbeatSeconds = 5;
        [ObservableProperty] private CaptureImageFormat _captureImageFormat;
        [ObservableProperty] private int _captureBurstCount = 1;
        public CaptureImageFormat[] ImageFormats { get; } = Enum.GetValues<CaptureImageFormat>();
        public ObservableCollection<AgentCameraRow> Cameras { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
        [NotifyCanExecuteChangedFor(nameof(AddCameraCommand))]
        [NotifyCanExecuteChangedFor(nameof(RemoveCameraCommand))]
        private bool _isLoaded;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
        private bool _isBusy;
        [ObservableProperty] private string _statusMessage = LocalizationManager.Instance["Agent_SelectAndLoad"];

        public AgentSettingsViewModel() : this(AppServices.NatsService) { }

        public AgentSettingsViewModel(HeatingCameraSystem.Core.Interfaces.INatsCommunicationService? nats,
            TimeSpan? responseTimeout = null)
        {
            _nats = nats;
            _responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(20);
            _ = SubscribeStatusAsync();
        }

        partial void OnSelectedAgentChanged(string? value)
        {
            _operationCancellation?.Cancel();
            _loadedAgentId = null;
            IsLoaded = false;
            Cameras.Clear();
            StatusMessage = LocalizationManager.Instance["Agent_SelectAndLoad"];
        }

        private async Task SubscribeStatusAsync()
        {
            if (_nats is null) return;
            try
            {
                await _subscriptions.AddAsync(ct => _nats.SubscribeAgentStatusScopedAsync(msg => RunOnUi(() =>
                {
                    if (_disposed || string.IsNullOrWhiteSpace(msg.AgentId)) return;
                    if (msg.HostAgentIds is { } ids && !ids.Contains(msg.AgentId)) return;
                    if (msg.CameraStatus == CameraStatus.Offline) return;
                    if (!OnlineAgents.Contains(msg.AgentId)) OnlineAgents.Add(msg.AgentId);
                    SelectedAgent ??= msg.AgentId;
                }), ct));
            }
            catch (Exception ex) { if (!_disposed) StatusMessage = ex.Message; }
        }

        private Task EnsureConfigSubscriptionsAsync(string agentId)
        {
            if (_nats is null) return Task.CompletedTask;
            if (_subscriptionTasks.TryGetValue(agentId, out Task? pending)) return pending;
            Task task = SubscribeConfigAsync(agentId);
            _subscriptionTasks.Add(agentId, task);
            return task;
        }

        private async Task SubscribeConfigAsync(string agentId)
        {
            if (_nats is null) return;
            await _subscriptions.AddAsync(ct => _nats.SubscribeAgentConfigSnapshotScopedAsync(agentId, msg =>
            {
                if (!_disposed && msg.AgentId == SelectedAgent && msg.AgentId == agentId
                    && !string.IsNullOrEmpty(_loadRequestId) && msg.RequestId == _loadRequestId)
                    _loadResponse?.TrySetResult(msg);
            }, ct));
            await _subscriptions.AddAsync(ct => _nats.SubscribeAgentConfigAckScopedAsync(agentId, ack =>
            {
                if (!_disposed && ack.AgentId == SelectedAgent && ack.AgentId == agentId
                    && !string.IsNullOrEmpty(_applyRequestId) && ack.RequestId == _applyRequestId)
                    _applyResponse?.TrySetResult(ack);
            }, ct));
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task LoadAsync()
        {
            if (_disposed || _nats is null || SelectedAgent is not { } agentId || IsBusy) return;
            IsBusy = true;
            IsLoaded = false;
            _loadedAgentId = null;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_subscriptions.Token);
            _operationCancellation = cancellation;
            cancellation.CancelAfter(_responseTimeout);
            _loadRequestId = Guid.NewGuid().ToString("N");
            _loadResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await EnsureConfigSubscriptionsAsync(agentId).WaitAsync(cancellation.Token);
                StatusMessage = string.Format(LocalizationManager.Instance["Agent_Loading"], agentId);
                await _nats.PublishAgentConfigRequestAsync(new AgentConfigRequestMessage
                {
                    AgentId = agentId, RequestId = _loadRequestId, Timestamp = DateTime.UtcNow
                }).WaitAsync(cancellation.Token);
                AgentConfigSnapshotMessage msg = await _loadResponse.Task.WaitAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (_disposed || SelectedAgent != agentId) return;
                ApplySnapshotToUi(msg);
            }
            catch (OperationCanceledException)
            {
                if (!_disposed && SelectedAgent == agentId)
                    StatusMessage = "설정 조회가 취소되었거나 응답 시간이 초과되었습니다. Agent를 업데이트하고 다시 조회하세요.";
            }
            catch (Exception ex) { if (!_disposed && SelectedAgent == agentId) StatusMessage = $"설정 조회 실패: {ex.Message}"; }
            finally
            {
                _loadRequestId = null; _loadResponse = null; _operationCancellation = null;
                IsBusy = false;
            }
        }

        private void ApplySnapshotToUi(AgentConfigSnapshotMessage msg)
        {
            if (_disposed || msg.AgentId != SelectedAgent || msg.RequestId != _loadRequestId) return;
            AgentConfigSnapshot c = msg.Config;
            SimulationMode = c.SimulationMode; NatsUrl = c.NatsUrl; StoragePath = c.StoragePath;
            HeartbeatSeconds = c.HeartbeatSeconds; CaptureImageFormat = c.CaptureImageFormat;
            CaptureBurstCount = c.CaptureBurstCount;
            Cameras.Clear();
            foreach (CameraDescriptor cam in c.Cameras) Cameras.Add(new AgentCameraRow(cam));
            _loadedAgentId = msg.AgentId;
            IsLoaded = true;
            StatusMessage = string.Format(LocalizationManager.Instance["Agent_Loaded"], msg.AgentId, Cameras.Count)
                + (c.RestartRequired ? " · 저장된 변경은 Agent 재시작 후 적용됩니다." : string.Empty);
        }

        [RelayCommand(CanExecute = nameof(IsLoaded))]
        private void AddCamera() => Cameras.Add(new AgentCameraRow { AgentId = SelectedAgent ?? "Agent" });
        [RelayCommand(CanExecute = nameof(IsLoaded))]
        private void RemoveCamera(AgentCameraRow? row) { if (row is not null) Cameras.Remove(row); }

        [RelayCommand(CanExecute = nameof(CanApply))]
        private async Task ApplyAsync()
        {
            if (!CanApply() || _nats is null || SelectedAgent is not { } agentId) return;
            IsBusy = true;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_subscriptions.Token);
            _operationCancellation = cancellation;
            cancellation.CancelAfter(_responseTimeout);
            _applyRequestId = Guid.NewGuid().ToString("N");
            _applyResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var snapshot = new AgentConfigSnapshot
                {
                    SimulationMode = SimulationMode, NatsUrl = NatsUrl, StoragePath = StoragePath,
                    HeartbeatSeconds = HeartbeatSeconds, CaptureImageFormat = CaptureImageFormat,
                    CaptureBurstCount = CaptureBurstCount, Cameras = Cameras.Select(row => row.ToDescriptor()).ToList()
                };
                StatusMessage = string.Format(LocalizationManager.Instance["Agent_Sending"], agentId);
                await _nats.PublishAgentConfigApplyAsync(new AgentConfigApplyMessage
                {
                    AgentId = agentId, RequestId = _applyRequestId, Config = snapshot, Timestamp = DateTime.UtcNow
                }).WaitAsync(cancellation.Token);
                AgentConfigAckMessage ack = await _applyResponse.Task.WaitAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!_disposed && SelectedAgent == agentId)
                    StatusMessage = ack.IsSuccess ? $"✔ {ack.Message}" : $"✘ {ack.Message}";
            }
            catch (OperationCanceledException)
            {
                if (!_disposed && SelectedAgent == agentId) StatusMessage = "설정 적용 응답을 확인하지 못했습니다. 다시 조회해 적용 상태를 확인하세요.";
            }
            catch (Exception ex) { if (!_disposed && SelectedAgent == agentId) StatusMessage = $"설정 적용 실패: {ex.Message}"; }
            finally { _applyRequestId = null; _applyResponse = null; _operationCancellation = null; IsBusy = false; }
        }

        private bool HasSelection() => !_disposed && !IsBusy && !string.IsNullOrEmpty(SelectedAgent);
        private bool CanApply() => !_disposed && !IsBusy && IsLoaded && SelectedAgent is not null && SelectedAgent == _loadedAgentId;
        private static void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action);
        }
        public void Dispose() { if (_disposed) return; _disposed = true; _operationCancellation?.Cancel(); _subscriptions.Dispose(); }
        public async ValueTask DisposeAsync() { Dispose(); await _subscriptions.DisposeAsync(); }
    }
}
