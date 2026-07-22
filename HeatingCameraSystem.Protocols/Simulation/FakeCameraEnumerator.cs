using System;
using System.Collections.Generic;
using System.Linq;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
    /// <summary>
    /// 하드웨어 없이 동작하는 가짜 카메라 열거자.
    /// SimulationMode=true 시 WmiCameraEnumerator 대신 사용.
    /// 고정 2개 카메라를 즉시 반환하며 PnP 이벤트는 시뮬레이션하지 않는다.
    /// UsbParentId 규약은 FakeUsbSerialEnumerator와 공유되어 카메라↔COM 페어링 조인이 가능하다 (CAMA↔COM7, CAMB↔COM8).
    /// </summary>
    public class FakeCameraEnumerator : ICameraEnumerator
    {
        private static readonly IReadOnlyList<DiscoveredCamera> _cameras = new[]
        {
            new DiscoveredCamera { HardwareId = "USB\\VID_0483&PID_5740\\CAMA_IF00", FriendlyName = "CLTC_T_VGA Camera A", OpenCvIndex = 0, UsbParentId = "USB\\VID_0483&PID_5740\\CAMA" },
            new DiscoveredCamera { HardwareId = "USB\\VID_0483&PID_5740\\CAMB_IF00", FriendlyName = "CLTC_T_VGA Camera B", OpenCvIndex = 1, UsbParentId = "USB\\VID_0483&PID_5740\\CAMB" },
        };

        public event Action<PnpChange>? Changed;

        public IReadOnlyList<DiscoveredCamera> Enumerate() => _cameras;

        public IReadOnlyList<DiscoveredCamera> EnumerateThermal(string friendlyNamePrefix = "CLTC_T_VGA") =>
            _cameras.Where(c => c.FriendlyName.StartsWith(friendlyNamePrefix, StringComparison.OrdinalIgnoreCase)).ToList();

        public void StartWatching() { /* no-op for fake */ }

        public void StopWatching() { /* no-op for fake */ }

        public void Dispose() { }

        /// <summary>테스트 전용: 외부에서 PnP 이벤트를 주입한다.</summary>
        public void SimulateArrival(DiscoveredCamera camera) =>
            Changed?.Invoke(new PnpChange { ChangeType = PnpChangeType.Arrival, Camera = camera });

        /// <summary>테스트 전용: 외부에서 PnP 제거 이벤트를 주입한다.</summary>
        public void SimulateRemoval(DiscoveredCamera camera) =>
            Changed?.Invoke(new PnpChange { ChangeType = PnpChangeType.Removal, Camera = camera });
    }
}
