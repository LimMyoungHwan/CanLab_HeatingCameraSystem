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

        public CameraDescriptor ToDescriptor() =>
            new(AgentId, OpenCvIndex, Alias,
                string.IsNullOrWhiteSpace(SerialPortName) ? null : SerialPortName,
                string.IsNullOrWhiteSpace(DeviceName) ? null : DeviceName);
    }

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

        private void EnsureConfigSubscriptions(string agentId)
        {
            if (!_subscribedAgents.Add(agentId) || AppServices.NatsService == null) return;

            AppServices.NatsService.SubscribeAgentConfigSnapshotAsync(agentId, msg =>
                Application.Current?.Dispatcher.Invoke(() => ApplySnapshotToUi(msg)));

            AppServices.NatsService.SubscribeAgentConfigAckAsync(agentId, ack =>
                Application.Current?.Dispatcher.Invoke(() =>
                    StatusMessage = ack.IsSuccess ? $"✔ {ack.Message}" : $"✘ {ack.Message}"));
        }

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
