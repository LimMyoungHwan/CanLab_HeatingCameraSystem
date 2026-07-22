using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
    public sealed class FakeCameraComPairingService : ICameraComPairingService
    {
        public Task<IReadOnlyList<CameraComPair>> GetPairsAsync(CancellationToken ct = default)
        {
            IReadOnlyList<CameraComPair> pairs = new[]
            {
                new CameraComPair(
                    new DiscoveredCamera { HardwareId = "USB\\VID_0483&PID_5740\\CAMA_IF00", FriendlyName = "CLTC_T_VGA Camera A", OpenCvIndex = 0, UsbParentId = "USB\\VID_0483&PID_5740\\CAMA" },
                    new DiscoveredSerialPort("COM7", "USB Serial Device (COM7)", "USB\\VID_0483&PID_5740\\CAMA_IF01", "USB\\VID_0483&PID_5740\\CAMA"),
                    "000100001",
                    PairingStatus.Paired,
                    false),
                new CameraComPair(
                    new DiscoveredCamera { HardwareId = "USB\\VID_0483&PID_5740\\CAMB_IF00", FriendlyName = "CLTC_T_VGA Camera B", OpenCvIndex = 1, UsbParentId = "USB\\VID_0483&PID_5740\\CAMB" },
                    new DiscoveredSerialPort("COM8", "USB Serial Device (COM8)", "USB\\VID_0483&PID_5740\\CAMB_IF01", "USB\\VID_0483&PID_5740\\CAMB"),
                    "000100002",
                    PairingStatus.Paired,
                    false),
            };

            return Task.FromResult(pairs);
        }

        public void SetManualOverride(string cameraUsbParentId, string portName) { }
    }
}
