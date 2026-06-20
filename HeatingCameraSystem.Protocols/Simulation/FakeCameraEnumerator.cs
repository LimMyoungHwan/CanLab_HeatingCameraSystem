using System;
using System.Collections.Generic;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
    /// <summary>
    /// 하드웨어 없이 동작하는 가짜 카메라 열거자.
    /// SimulationMode=true 시 WmiCameraEnumerator 대신 사용.
    /// 고정 2개 카메라를 즉시 반환하며 PnP 이벤트는 시뮬레이션하지 않는다.
    /// </summary>
    public class FakeCameraEnumerator : ICameraEnumerator
    {
        private static readonly IReadOnlyList<DiscoveredCamera> _cameras = new[]
        {
            new DiscoveredCamera { HardwareId = "USB\\VID_FAKE&PID_CAM1\\00000001", FriendlyName = "Fake Camera 1", OpenCvIndex = 0 },
            new DiscoveredCamera { HardwareId = "USB\\VID_FAKE&PID_CAM2\\00000002", FriendlyName = "Fake Camera 2", OpenCvIndex = 1 },
        };

        public event Action<PnpChange>? Changed;

        public IReadOnlyList<DiscoveredCamera> Enumerate() => _cameras;

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
