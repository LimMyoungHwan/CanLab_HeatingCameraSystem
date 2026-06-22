using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class DevicesViewModel : ObservableObject
    {
        public ObservableCollection<DeviceItem> Devices { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ApproveCommand))]
        [NotifyCanExecuteChangedFor(nameof(RejectCommand))]
        [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
        [NotifyCanExecuteChangedFor(nameof(GetLogsCommand))]
        [NotifyCanExecuteChangedFor(nameof(SetSerialCommand))]
        private DeviceItem? _selectedDevice;

        [ObservableProperty]
        private string _statusMessage = "대기중";

        [ObservableProperty]
        private string _logContent = string.Empty;

        [ObservableProperty] private string _serialPort = "COM3";
        [ObservableProperty] private int    _serialBaud = 9600;
        [ObservableProperty] private int    _serialDataBits = 8;
        [ObservableProperty] private string _serialParity = "None";
        [ObservableProperty] private string _serialStopBits = "One";

        public DevicesViewModel()
        {
            SubscribeInventory();
        }

        private void SubscribeInventory()
        {
            if (AppServices.NatsService == null) return;

            AppServices.NatsService.SubscribeCameraInventoryAsync(msg =>
            {
                Application.Current?.Dispatcher.Invoke(() => MergeInventory(msg));
            });

            AppServices.NatsService.SubscribeLogAlertAsync(alert =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var dev = Devices.FirstOrDefault(d => d.AgentId == alert.AgentId);
                    if (dev != null)
                    {
                        dev.HasAlert = true;
                        dev.LastAlert = $"[{alert.Level}] {alert.Message}";
                    }
                });
            });
        }

        private void MergeInventory(CameraInventoryMessage msg)
        {
            foreach (var cam in msg.Cameras)
            {
                var existing = Devices.FirstOrDefault(d => d.HardwareId == cam.HardwareId);
                if (existing != null)
                {
                    existing.Alias      = cam.Alias;
                    existing.AgentId    = cam.AgentId;
                    existing.IsApproved = cam.IsApproved;
                    existing.IsRunning  = cam.IsRunning;
                    existing.LastSeen   = cam.LastSeen;
                }
                else
                {
                    Devices.Add(new DeviceItem
                    {
                        PCId        = msg.PCId,
                        HardwareId  = cam.HardwareId,
                        Alias       = cam.Alias,
                        AgentId     = cam.AgentId,
                        OpenCvIndex = cam.OpenCvIndex,
                        IsApproved  = cam.IsApproved,
                        IsRunning   = cam.IsRunning,
                        LastSeen    = cam.LastSeen,
                    });
                }
            }

            StatusMessage = $"인벤토리 갱신: {msg.Cameras.Count} cameras from {msg.PCId}";
        }

        [RelayCommand(CanExecute = nameof(HasPendingSelection))]
        private async Task ApproveAsync()
        {
            if (SelectedDevice == null || AppServices.NatsService == null) return;

            await AppServices.NatsService.PublishManagerCommandAsync(new ManagerCommandMessage
            {
                PCId       = SelectedDevice.PCId,
                Op         = ManagerCommandOp.Approve,
                HardwareId = SelectedDevice.HardwareId,
                Payload    = SelectedDevice.Alias,
                Timestamp  = DateTime.UtcNow,
            });

            StatusMessage = $"승인 요청: {SelectedDevice.HardwareId}";
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task RejectAsync()
        {
            if (SelectedDevice == null || AppServices.NatsService == null) return;

            await AppServices.NatsService.PublishManagerCommandAsync(new ManagerCommandMessage
            {
                PCId       = SelectedDevice.PCId,
                Op         = ManagerCommandOp.Reject,
                HardwareId = SelectedDevice.HardwareId,
                Timestamp  = DateTime.UtcNow,
            });

            StatusMessage = $"거부: {SelectedDevice.HardwareId}";
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task RenameAsync()
        {
            if (SelectedDevice == null || AppServices.NatsService == null) return;

            await AppServices.NatsService.PublishManagerCommandAsync(new ManagerCommandMessage
            {
                PCId       = SelectedDevice.PCId,
                Op         = ManagerCommandOp.Rename,
                HardwareId = SelectedDevice.HardwareId,
                Payload    = SelectedDevice.Alias,
                Timestamp  = DateTime.UtcNow,
            });

            await AppServices.CameraDeviceRepo.UpsertAsync(new CameraDevice
            {
                HardwareId = SelectedDevice.HardwareId,
                AgentId    = SelectedDevice.AgentId,
                Alias      = SelectedDevice.Alias,
                PCId       = SelectedDevice.PCId,
                LastSeen   = DateTime.UtcNow,
                IsApproved = SelectedDevice.IsApproved,
            });

            StatusMessage = $"이름 변경: {SelectedDevice.Alias}";
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private async Task GetLogsAsync()
        {
            if (SelectedDevice == null || AppServices.NatsService == null) return;

            StatusMessage = $"로그 요청 중: {SelectedDevice.AgentId}...";

            var tcs = new TaskCompletionSource<LogDumpMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            await AppServices.NatsService.SubscribeLogDumpAsync(SelectedDevice.PCId, dump =>
            {
                if (dump.AgentId == SelectedDevice.AgentId)
                    tcs.TrySetResult(dump);
            });

            await AppServices.NatsService.PublishLogDumpRequestAsync(new LogDumpRequestMessage
            {
                PCId     = SelectedDevice.PCId,
                AgentId  = SelectedDevice.AgentId,
                MaxBytes = 5 * 1024 * 1024,
            });

            try
            {
                var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
                using var ms = new MemoryStream(result.GzipBytes);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var reader = new StreamReader(gz, Encoding.UTF8);
                LogContent = await reader.ReadToEndAsync();
                StatusMessage = $"로그 수신: {result.OriginalBytes:N0}B (gzip {result.GzipBytes.Length:N0}B)";
            }
            catch (TimeoutException)
            {
                StatusMessage = "✘ 로그 응답 없음 (30초 초과)";
            }
        }

        [RelayCommand(CanExecute = nameof(HasApprovedSelection))]
        private async Task SetSerialAsync()
        {
            if (SelectedDevice == null || AppServices.NatsService == null) return;

            var serial = new CameraSerialSettings
            {
                CameraIndex = SelectedDevice.OpenCvIndex,
                PortName    = SerialPort,
                BaudRate    = SerialBaud,
                DataBits    = SerialDataBits,
                Parity      = SerialParity,
                StopBits    = SerialStopBits,
            };

            await AppServices.NatsService.PublishManagerCommandAsync(new ManagerCommandMessage
            {
                PCId       = SelectedDevice.PCId,
                Op         = ManagerCommandOp.SetSerial,
                HardwareId = SelectedDevice.HardwareId,
                Payload    = JsonSerializer.Serialize(serial),
                Timestamp  = DateTime.UtcNow,
            });

            StatusMessage = $"시리얼 전송: {SelectedDevice.AgentId} ({SerialPort} {SerialBaud})";
        }

        private bool HasSelection() => SelectedDevice != null;
        private bool HasPendingSelection() => SelectedDevice is { IsApproved: false };
        private bool HasApprovedSelection() => SelectedDevice is { IsApproved: true };
    }

    public partial class DeviceItem : ObservableObject
    {
        [ObservableProperty] private string _pCId = string.Empty;
        [ObservableProperty] private string _hardwareId = string.Empty;
        [ObservableProperty] private string _alias = string.Empty;
        [ObservableProperty] private string _agentId = string.Empty;
        [ObservableProperty] private int _openCvIndex;
        [ObservableProperty] private bool _isApproved;
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private DateTime _lastSeen;
        [ObservableProperty] private bool _hasAlert;
        [ObservableProperty] private string _lastAlert = string.Empty;
    }
}
