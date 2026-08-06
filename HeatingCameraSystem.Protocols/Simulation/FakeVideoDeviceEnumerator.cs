using System.Collections.Generic;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
    public sealed class FakeVideoDeviceEnumerator : IVideoDeviceEnumerator
    {
        private readonly IReadOnlyList<VideoDevice> _devices;

        public FakeVideoDeviceEnumerator(params VideoDevice[] devices) => _devices = devices;

        public IReadOnlyList<VideoDevice> Enumerate() => _devices;
    }
}
