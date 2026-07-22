using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols
{
    public sealed class CameraComPairingService : ICameraComPairingService
    {
        private readonly ICameraEnumerator _cameraEnumerator;
        private readonly IUsbSerialEnumerator _serialEnumerator;
        private readonly Func<string, ICameraSerialClient> _serialClientFactory;
        private readonly HardwareSettings _settings;

        public CameraComPairingService(
            ICameraEnumerator cameraEnumerator,
            IUsbSerialEnumerator serialEnumerator,
            Func<string, ICameraSerialClient> serialClientFactory,
            HardwareSettings settings)
        {
            _cameraEnumerator = cameraEnumerator;
            _serialEnumerator = serialEnumerator;
            _serialClientFactory = serialClientFactory;
            _settings = settings;
        }

        public async Task<IReadOnlyList<CameraComPair>> GetPairsAsync(CancellationToken ct = default)
        {
            var thermalCameras = _cameraEnumerator.Enumerate()
                .Where(c => c.FriendlyName.StartsWith("CLTC_T_VGA", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var ports = _serialEnumerator.Enumerate();
            var pairs = new List<CameraComPair>();

            foreach (var camera in thermalCameras)
            {
                var manualOverride = _settings.CameraPairings.FirstOrDefault(p => p.CameraUsbParentId == camera.UsbParentId);
                if (manualOverride is not null)
                {
                    var port = ports.FirstOrDefault(p => p.PortName == manualOverride.PortName);
                    var (serialNumber, status) = await ReadSerialNumberAsync(manualOverride.PortName, ct);
                    pairs.Add(new CameraComPair(camera, port, serialNumber, status, true));
                    continue;
                }

                var matches = ports
                    .Where(p => !string.IsNullOrEmpty(p.UsbParentId) && p.UsbParentId == camera.UsbParentId)
                    .ToList();

                if (matches.Count == 0)
                {
                    pairs.Add(new CameraComPair(camera, null, null, PairingStatus.Unpaired, false));
                    continue;
                }

                if (matches.Count > 1)
                {
                    pairs.Add(new CameraComPair(camera, null, null, PairingStatus.Ambiguous, false));
                    continue;
                }

                var matchedPort = matches[0];
                var (matchedSerialNumber, matchedStatus) = await ReadSerialNumberAsync(matchedPort.PortName, ct);
                pairs.Add(new CameraComPair(camera, matchedPort, matchedSerialNumber, matchedStatus, false));
            }

            return pairs;
        }

        public void SetManualOverride(string cameraUsbParentId, string portName)
        {
            var entry = _settings.CameraPairings.FirstOrDefault(p => p.CameraUsbParentId == cameraUsbParentId);
            if (entry is null)
            {
                _settings.CameraPairings.Add(new CameraPairingEntry
                {
                    CameraUsbParentId = cameraUsbParentId,
                    PortName = portName,
                });
                return;
            }

            entry.PortName = portName;
        }

        private async Task<(string? SerialNumber, PairingStatus Status)> ReadSerialNumberAsync(string portName, CancellationToken ct)
        {
            using var client = _serialClientFactory(portName);
            try
            {
                await client.InitializeAsync(ct);
                string serialNumber = await client.ReadSerialNumberAsync(ct);
                return (serialNumber, PairingStatus.Paired);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return (null, PairingStatus.DetectedButUnverified);
            }
        }
    }
}
