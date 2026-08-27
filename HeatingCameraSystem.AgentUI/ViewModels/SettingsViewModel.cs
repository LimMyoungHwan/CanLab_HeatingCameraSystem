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
    /// <summary>설정 그리드에서 편집하는 카메라 한 행. <see cref="CameraDescriptor"/>와 상호 변환한다.</summary>
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

    /// <summary>
    /// AgentUI 설정 편집 화면 뷰모델. 저장 시 agentui.json에 기록하고 <see cref="Saved"/>를 발행한다.
    /// App이 이를 받아 카메라 구성을 재시작 없이 즉시 적용한다(패널/런타임 재구성 + NATS 인벤토리
    /// 재발행). COM 자동 감지는 카메라-COM 페어링 서비스에 위임한다.
    /// </summary>
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

        /// <summary>저장이 성공해 config.Cameras가 갱신된 직후 발행된다. App이 구독해 런타임·패널·
        /// NATS 인벤토리를 재시작 없이 라이브로 재구성한다.</summary>
        public event Action? Saved;

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

            // ponytail: 새 List로 재할당 금지 — CameraNatsConnector가 시작 시점의 이 리스트 객체를
            // 참조로 붙들고 하트비트 인벤토리를 만든다. in-place로 갈아끼워야 카메라 삭제/추가가
            // 재시작 없이 인벤토리에 즉시 반영된다. (상한: 하트비트 타이머 스레드의 열거와 겹칠 수
            // 있음 — 실사용 빈도상 무해. 문제 시 config.Cameras 접근에 락.)
            List<CameraDescriptor> updated = Cameras.Select(row => row.ToDescriptor()).ToList();
            _config.Cameras.Clear();
            _config.Cameras.AddRange(updated);

            try
            {
                _config.Save();
                StatusText = "저장됨. 재시작 없이 즉시 적용됩니다.";
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
                return;
            }

            Saved?.Invoke();
        }

        /// <summary>
        /// 페어링 서비스로 카메라-COM 쌍을 감지해 각 행의 포트/S/N/ContainerID를 채운다.
        /// 결과는 행에만 반영되며 Save 후 재시작해야 적용된다.
        /// </summary>
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

        /// <summary>
        /// 행과 감지된 페어를 매칭한다. 우선순위: 고유 카메라 S/N → UsbContainerId →
        /// Device Name 부분 일치 → OpenCvIndex.
        /// </summary>
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

        // 비었거나 0뿐인 S/N은 미기록 테스트 카메라 — 진짜 식별 키가 아니므로 ContainerID로 fall back한다.
        private static bool IsUsableSerial([NotNullWhen(true)] string? serial) =>
            !string.IsNullOrWhiteSpace(serial) && serial.Any(c => c is >= '1' and <= '9');
    }
}
