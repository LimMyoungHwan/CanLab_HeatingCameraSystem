using System;
using System.Collections.Generic;
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
            AgentUiConfig editable = config.EditableSnapshot();
            _simulationMode = editable.SimulationMode;
            _natsUrl = editable.NatsUrl;
            _storagePath = editable.StoragePath;
            _heartbeatSeconds = editable.HeartbeatSeconds;
            _captureImageFormat = editable.CaptureImageFormat;
            _captureBurstCount = editable.CaptureBurstCount;

            foreach (CameraDescriptor camera in editable.Cameras)
            {
                Cameras.Add(new CameraRow(camera));
            }
        }

        [RelayCommand]
        private void AddCamera()
        {
            var usedIds = new HashSet<string>(Cameras.Select(row => row.AgentId), StringComparer.OrdinalIgnoreCase);
            int n = 1;
            while (usedIds.Contains($"Camera_{n}"))
            {
                n++;
            }

            int index = Cameras.Count == 0 ? 0 : Cameras.Max(row => row.OpenCvIndex) + 1;
            Cameras.Add(new CameraRow { AgentId = $"Camera_{n}", Alias = $"Camera {n}", OpenCvIndex = index });
        }

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
            // 같은 AgentId·COM 포트가 두 행이면 1대 카메라를 두 패널이 다투고 하나가
            // Faulted가 된다. 저장 전에 막는다.
            string? dupId = Cameras
                .Where(row => !string.IsNullOrWhiteSpace(row.AgentId))
                .GroupBy(row => row.AgentId.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1)?.Key;
            if (dupId is not null)
            {
                StatusText = $"저장 거부: AgentId '{dupId}' 중복. 한 행을 지우세요.";
                return;
            }

            string? dupPort = Cameras
                .Where(row => !string.IsNullOrWhiteSpace(row.SerialPortName))
                .GroupBy(row => row.SerialPortName.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1)?.Key;
            if (dupPort is not null)
            {
                StatusText = $"저장 거부: 포트 '{dupPort}' 중복. 한 행의 포트를 비우거나 지우세요.";
                return;
            }

            var draft = new AgentUiConfig
            {
                SimulationMode = SimulationMode,
                NatsUrl = NatsUrl,
                StoragePath = StoragePath,
                HeartbeatSeconds = HeartbeatSeconds,
                CaptureImageFormat = CaptureImageFormat,
                CaptureBurstCount = CaptureBurstCount,
                Cameras = Cameras.Select(row => row.ToDescriptor()).ToList()
            };
            try
            {
                bool restart = _config.SaveDraft(draft);
                StatusText = restart
                    ? "저장됨. 실행 환경·연결·저장 설정이 바뀌어 AgentUI 재시작이 필요합니다. 현재 실행 설정은 유지됩니다."
                    : "저장됨. 카메라 구성을 즉시 적용합니다.";
                if (!restart) Saved?.Invoke();
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
            }
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
                // 한 페어는 한 행에만: 같은 행값 둘에 같은 포트를 채워 중복 항목을 만들지 않는다.
                var consumed = new HashSet<CameraComPair>();
                int matched = 0;
                foreach (CameraRow row in Cameras)
                {
                    CameraComPair? pair = FindPairForRow(pairs, row);
                    if (pair?.SerialPort is not null && !consumed.Contains(pair))
                    {
                        consumed.Add(pair);
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
                    ? $"자동 감지 {matched}/{Cameras.Count} 매칭. 감지: {detected}. 저장 후 적용."
                    : $"매칭 없음 — S/N 또는 USB ContainerId를 확인하세요. 모호한 이름은 자동 연결하지 않습니다. 감지: {detected}";
            }
            catch (Exception ex)
            {
                StatusText = $"자동 감지 실패: {ex.Message}";
            }
        }

        /// <summary>강한 식별자가 있으면 불일치 시 중단한다. 모델명·WMI 순번으로 다른 카메라를 채택하지 않는다.</summary>
        internal static CameraComPair? FindPairForRow(IReadOnlyList<CameraComPair> pairs, CameraRow row)
        {
            if (IsUsableSerial(row.CameraSerialNumber))
            {
                var matches = pairs.Where(p => string.Equals(p.CameraSerialNumber, row.CameraSerialNumber, StringComparison.OrdinalIgnoreCase)).ToList();
                return matches.Count == 1 && IsUnambiguous(matches[0], pairs) ? matches[0] : null;
            }
            if (!string.IsNullOrWhiteSpace(row.UsbContainerId))
            {
                var matches = pairs.Where(p => string.Equals(p.Camera.UsbParentId, row.UsbContainerId, StringComparison.OrdinalIgnoreCase)).ToList();
                return matches.Count == 1 && IsUnambiguous(matches[0], pairs) ? matches[0] : null;
            }
            // 같은 모델이 하나만 있어도 기존 영상 인덱스와 WMI 순번의 동일성은 보장되지 않는다.
            // 물리 식별자가 없는 행은 운영자가 S/N/ContainerId를 지정해야 한다.
            return null;
        }

        private static bool IsUnambiguous(CameraComPair pair, IReadOnlyList<CameraComPair> pairs) =>
            pair.Status != PairingStatus.Ambiguous && (!IsUsableSerial(pair.CameraSerialNumber)
            || pairs.Count(candidate => string.Equals(candidate.CameraSerialNumber, pair.CameraSerialNumber, StringComparison.OrdinalIgnoreCase)) == 1);

        // 비었거나 0뿐인 S/N은 미기록 테스트 카메라 — 진짜 식별 키가 아니므로 ContainerID로 fall back한다.
        private static bool IsUsableSerial([NotNullWhen(true)] string? serial) =>
            !string.IsNullOrWhiteSpace(serial) && serial.Any(c => c is >= '1' and <= '9');
    }
}
