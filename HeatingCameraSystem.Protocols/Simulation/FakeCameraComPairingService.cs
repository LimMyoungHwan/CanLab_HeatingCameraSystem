using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
    /// <summary>
    /// 하드웨어 없이 동작하는 가짜 카메라↔COM 페어링 서비스. SimulationMode=true 시
    /// CameraComPairingService(WMI 기반 자동 검색) 대신 사용한다.
    /// 고정 2쌍(CAMA↔COM7 시리얼 000100001, CAMB↔COM8 시리얼 000100002)을 항상
    /// Paired 상태로 즉시 반환하며, USB 토폴로지 조회나 수동 재정의는 수행하지 않는다.
    /// UsbParentId 규약은 FakeCameraEnumerator·FakeUsbSerialEnumerator와 공유된다.
    /// </summary>
    public sealed class FakeCameraComPairingService : ICameraComPairingService
    {
        /// <summary>고정 페어링 2쌍을 즉시 반환한다. 호출마다 결과가 동일하다.</summary>
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

        /// <summary>아무 동작도 하지 않는다. 가짜 페어링은 고정이므로 재정의가 필요 없다.</summary>
        public void SetManualOverride(string cameraUsbParentId, string portName) { }
    }
}
