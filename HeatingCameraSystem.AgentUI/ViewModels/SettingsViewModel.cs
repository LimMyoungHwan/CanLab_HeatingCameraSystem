using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.AgentUI.ViewModels
{
    public partial class CameraRow : ObservableObject
    {
        [ObservableProperty]
        private string _agentId;

        [ObservableProperty]
        private int _openCvIndex;

        [ObservableProperty]
        private string _alias;

        [ObservableProperty]
        private string _serialPortName = string.Empty;

        [ObservableProperty]
        private string _deviceName = string.Empty;

        [ObservableProperty]
        private string _cameraSerialNumber = string.Empty;

        [ObservableProperty]
        private string _usbContainerId = string.Empty;

        public CameraRow(CameraDescriptor descriptor)
        {
            _agentId = descriptor.AgentId;
            _openCvIndex = descriptor.OpenCvIndex;
            _alias = descriptor.Alias;
            _serialPortName = descriptor.SerialPortName ?? string.Empty;
            _deviceName = descriptor.DeviceName ?? string.Empty;
            _cameraSerialNumber = descriptor.CameraSerialNumber ?? string.Empty;
            _usbContainerId = descriptor.UsbContainerId ?? string.Empty;
        }

        public CameraRow()
        {
            _agentId = "Camera";
            _openCvIndex = 0;
            _alias = "Camera";
        }

        public CameraDescriptor ToDescriptor() =>
            new(AgentId, OpenCvIndex, Alias,
                string.IsNullOrWhiteSpace(SerialPortName) ? null : SerialPortName,
                string.IsNullOrWhiteSpace(DeviceName) ? null : DeviceName,
                string.IsNullOrWhiteSpace(CameraSerialNumber) ? null : CameraSerialNumber,
                string.IsNullOrWhiteSpace(UsbContainerId) ? null : UsbContainerId);
    }

    public partial class SettingsViewModel : ObservableObject
    {
        private readonly AgentUiConfig _config;
        private readonly ICameraComPairingService? _pairing;

        [ObservableProperty]
        private bool _simulationMode;

        [ObservableProperty]
        private string _natsUrl;

        [ObservableProperty]
        private string _storagePath;

        [ObservableProperty]
        private int _heartbeatSeconds;

        [ObservableProperty]
        private CaptureImageFormat _captureImageFormat;

        [ObservableProperty]
        private int _captureBurstCount;

        public CaptureImageFormat[] ImageFormats { get; } = Enum.GetValues<CaptureImageFormat>();

        [ObservableProperty]
        private string _statusText = string.Empty;

        public ObservableCollection<CameraRow> Cameras { get; } = new();

        public SettingsViewModel(AgentUiConfig config, ICameraComPairingService? pairing = null)
        {
            _config = config;
            _pairing = pairing;
            _simulationMode = config.SimulationMode;
            _natsUrl = config.NatsUrl;
            _storagePath = config.StoragePath;
            _heartbeatSeconds = config.HeartbeatSeconds;
            _captureImageFormat = config.CaptureImageFormat;
            _captureBurstCount = config.CaptureBurstCount;

            foreach (CameraDescriptor camera in config.Cameras)
            {
                Cameras.Add(new CameraRow(camera));
            }
        }

        [RelayCommand]
        private void AddCamera() => Cameras.Add(new CameraRow());

        [RelayCommand]
        private void RemoveCamera(CameraRow? row)
        {
            if (row is not null)
            {
                Cameras.Remove(row);
            }
        }

        [RelayCommand]
        private void Save()
        {
            _config.SimulationMode = SimulationMode;
            _config.NatsUrl = NatsUrl;
            _config.StoragePath = StoragePath;
            _config.HeartbeatSeconds = HeartbeatSeconds;
            _config.CaptureImageFormat = CaptureImageFormat;
            _config.CaptureBurstCount = CaptureBurstCount;
            _config.Cameras = Cameras.Select(row => row.ToDescriptor()).ToList();

            try
            {
                _config.Save();
                StatusText = "Saved. Restart AgentUI to apply.";
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task AutoDetectSerialAsync()
        {
            if (_pairing is null)
            {
                StatusText = "Pairing unavailable.";
                return;
            }

            StatusText = "COM 자동 감지 중…";
            try
            {
                var pairs = await _pairing.GetPairsAsync();
                int matched = 0;
                foreach (CameraRow row in Cameras)
                {
                    CameraComPair? pair = FindPairForRow(pairs, row);
                    if (pair?.SerialPort is not null)
                    {
                        row.SerialPortName = pair.SerialPort.PortName;
                        if (IsUsableSerial(pair.CameraSerialNumber))
                            row.CameraSerialNumber = pair.CameraSerialNumber;
                        if (!string.IsNullOrWhiteSpace(pair.Camera.UsbParentId))
                            row.UsbContainerId = pair.Camera.UsbParentId;
                        matched++;
                    }
                }

                string detected = pairs.Count == 0
                    ? "없음"
                    : string.Join(", ", pairs.Select(p => $"{p.Camera.FriendlyName}[{p.CameraSerialNumber ?? "?"}]→{p.SerialPort?.PortName ?? p.Status.ToString()}"));

                StatusText = matched > 0
                    ? $"자동 감지 {matched}/{Cameras.Count} 매칭. 감지: {detected}. Save 후 재시작."
                    : $"매칭 없음 — 각 행 Device Name에 장치명 입력 후 재시도. 감지: {detected}";
            }
            catch (Exception ex)
            {
                StatusText = $"자동 감지 실패: {ex.Message}";
            }
        }

        private static CameraComPair? FindPairForRow(IReadOnlyList<CameraComPair> pairs, CameraRow row)
        {
            if (IsUsableSerial(row.CameraSerialNumber))
            {
                var bySerial = pairs
                    .Where(p => string.Equals(p.CameraSerialNumber, row.CameraSerialNumber, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (bySerial.Count == 1)
                    return bySerial[0];
            }

            if (!string.IsNullOrWhiteSpace(row.UsbContainerId))
            {
                CameraComPair? byContainer = pairs.FirstOrDefault(
                    p => string.Equals(p.Camera.UsbParentId, row.UsbContainerId, StringComparison.OrdinalIgnoreCase));
                if (byContainer is not null)
                    return byContainer;
            }

            if (!string.IsNullOrWhiteSpace(row.DeviceName))
                return pairs.FirstOrDefault(
                    p => p.Camera.FriendlyName.Contains(row.DeviceName, StringComparison.OrdinalIgnoreCase));

            return pairs.FirstOrDefault(p => p.Camera.OpenCvIndex == row.OpenCvIndex);
        }

        // A blank or all-zeros S/N is an unprogrammed test camera — not a real identity key; fall back to ContainerID.
        private static bool IsUsableSerial([NotNullWhen(true)] string? serial) =>
            !string.IsNullOrWhiteSpace(serial) && serial.Any(c => c is >= '1' and <= '9');
    }
}
