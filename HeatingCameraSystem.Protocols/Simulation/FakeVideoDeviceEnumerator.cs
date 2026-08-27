using System.Collections.Generic;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
    /// <summary>
    /// 하드웨어 없이 동작하는 가짜 영상 장치 열거자. SimulationMode와 테스트에서
    /// DirectShowVideoDeviceEnumerator 대신 사용한다.
    /// 생성자로 주입받은 고정 목록을 그대로 반환하며, 목록 순서가 곧 OpenCV 인덱스 순서로 간주된다.
    /// </summary>
    public sealed class FakeVideoDeviceEnumerator : IVideoDeviceEnumerator
    {
        private readonly IReadOnlyList<VideoDevice> _devices;

        public FakeVideoDeviceEnumerator(params VideoDevice[] devices) => _devices = devices;

        public IReadOnlyList<VideoDevice> Enumerate() => _devices;
    }
}
