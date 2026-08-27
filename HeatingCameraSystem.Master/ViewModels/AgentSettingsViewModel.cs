using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Models;
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
    public partial class AgentSettingsViewModel : ObservableObject
    {
        private readonly HashSet<string> _subscribedAgents = new();

        public ObservableCollection<string> OnlineAgents { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
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

        [ObservableProperty] private string _statusMessage = LocalizationManager.Instance["Agent_SelectAndLoad"];

        public AgentSettingsViewModel()
        {
            SubscribeStatus();
        }

        /// <summary>
        /// 하트비트(<c>agent.status.{AgentId}</c>)를 구독해 온라인 Agent 목록을 채운다.
        /// 첫 Agent는 자동 선택하고, 설정 스냅샷/ACK 구독도 미리 걸어 둔다.
        /// </summary>
        private void SubscribeStatus()
        {
            if (AppServices.NatsService == null) return;

            AppServices.NatsService.SubscribeAgentStatusAsync(msg =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(msg.AgentId) && !OnlineAgents.Contains(msg.AgentId))
                    {
                        OnlineAgents.Add(msg.AgentId);
                        SelectedAgent ??= msg.AgentId;
                        EnsureConfigSubscriptions(msg.AgentId);
                    }
                });
            });
        }

        /// <summary>선택한 Agent에 설정 스냅샷 전송을 요청한다. 응답은 스냅샷 구독 콜백으로 돌아온다.</summary>
        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task LoadAsync()
        {
            if (SelectedAgent == null || AppServices.NatsService == null) return;

            string agentId = SelectedAgent;
            EnsureConfigSubscriptions(agentId);
            StatusMessage = string.Format(LocalizationManager.Instance["Agent_Loading"], agentId);

            await AppServices.NatsService.PublishAgentConfigRequestAsync(new AgentConfigRequestMessage
            {
                AgentId = agentId,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Agent별 설정 스냅샷·적용 ACK 구독을 최초 한 번만 등록한다
        /// (NATS 서비스에 해제 API가 없으므로 중복 구독을 막는 것이 중요하다).
        /// </summary>
        private void EnsureConfigSubscriptions(string agentId)
        {
            if (!_subscribedAgents.Add(agentId) || AppServices.NatsService == null) return;

            AppServices.NatsService.SubscribeAgentConfigSnapshotAsync(agentId, msg =>
                Application.Current?.Dispatcher.Invoke(() => ApplySnapshotToUi(msg)));

            AppServices.NatsService.SubscribeAgentConfigAckAsync(agentId, ack =>
                Application.Current?.Dispatcher.Invoke(() =>
                    StatusMessage = ack.IsSuccess ? $"✔ {ack.Message}" : $"✘ {ack.Message}"));
        }

        /// <summary>수신한 설정 스냅샷을 화면에 반영하고 편집 가능 상태(<see cref="IsLoaded"/>)로 전환한다.</summary>
        private void ApplySnapshotToUi(AgentConfigSnapshotMessage msg)
        {
            AgentConfigSnapshot c = msg.Config;
            SimulationMode = c.SimulationMode;
            NatsUrl = c.NatsUrl;
            StoragePath = c.StoragePath;
            HeartbeatSeconds = c.HeartbeatSeconds;
            CaptureImageFormat = c.CaptureImageFormat;
            CaptureBurstCount = c.CaptureBurstCount;

            Cameras.Clear();
            foreach (CameraDescriptor cam in c.Cameras)
                Cameras.Add(new AgentCameraRow(cam));

            IsLoaded = true;
            StatusMessage = string.Format(LocalizationManager.Instance["Agent_Loaded"], msg.AgentId, Cameras.Count);
        }

        [RelayCommand(CanExecute = nameof(IsLoaded))]
        private void AddCamera() => Cameras.Add(new AgentCameraRow { AgentId = SelectedAgent ?? "Agent" });

        [RelayCommand(CanExecute = nameof(IsLoaded))]
        private void RemoveCamera(AgentCameraRow? row)
        {
            if (row != null) Cameras.Remove(row);
        }

        /// <summary>
        /// 편집한 설정 전체를 스냅샷으로 묶어 Agent에 적용 요청한다.
        /// 성공/실패는 ACK 구독 콜백이 <see cref="StatusMessage"/>로 보고한다.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanApply))]
        private async Task ApplyAsync()
        {
            if (SelectedAgent == null || AppServices.NatsService == null) return;

            string agentId = SelectedAgent;
            var snapshot = new AgentConfigSnapshot
            {
                SimulationMode = SimulationMode,
                NatsUrl = NatsUrl,
                StoragePath = StoragePath,
                HeartbeatSeconds = HeartbeatSeconds,
                CaptureImageFormat = CaptureImageFormat,
                CaptureBurstCount = CaptureBurstCount,
                Cameras = Cameras.Select(r => r.ToDescriptor()).ToList()
            };

            StatusMessage = string.Format(LocalizationManager.Instance["Agent_Sending"], agentId);
            await AppServices.NatsService.PublishAgentConfigApplyAsync(new AgentConfigApplyMessage
            {
                AgentId = agentId,
                Config = snapshot,
                Timestamp = DateTime.UtcNow
            });
        }

        private bool HasSelection() => !string.IsNullOrEmpty(SelectedAgent);
        private bool CanApply() => IsLoaded && !string.IsNullOrEmpty(SelectedAgent);
    }
}
