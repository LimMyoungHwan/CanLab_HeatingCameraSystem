using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Simulation;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CameraComPairingStartRecoveryTests
    {
        // A CL detector that boots STOPPED: reports all-zero S/N until SetCameraRunningAsync(true).
        private sealed class StopBootedSerialClient : ICameraSerialClient
        {
            private readonly string _recovered;
            private bool _running;

            public StopBootedSerialClient(string portName, string recovered)
            {
                PortName = portName;
                _recovered = recovered;
            }

            public string PortName { get; }
            public bool IsOpen { get; private set; }

            public Task InitializeAsync(CancellationToken ct = default)
            {
                IsOpen = true;
                return Task.CompletedTask;
            }

            public Task<string> ReadSerialNumberAsync(CancellationToken ct = default)
                => Task.FromResult(_running ? _recovered : "000000000");

            public Task<double> ReadFpaTemperatureAsync(CancellationToken ct = default) => Task.FromResult(30.0);
            public Task SetShutterAsync(bool open, CancellationToken ct = default) => Task.CompletedTask;

            public Task SetCameraRunningAsync(bool running, CancellationToken ct = default)
            {
                _running = running;
                return Task.CompletedTask;
            }

            public Task SaveConfigAsync(CancellationToken ct = default) => Task.CompletedTask;
            public void Dispose() => IsOpen = false;
        }

        [Fact]
        public async Task GetPairsAsync_StartsStoppedDetector_RecoversZeroSerial()
        {
            var svc = new CameraComPairingService(
                new FakeCameraEnumerator(),
                new FakeUsbSerialEnumerator(),
                port => new StopBootedSerialClient(port, port == "COM7" ? "545308020" : "545308059"),
                new HardwareSettings());

            var pairs = await svc.GetPairsAsync();

            Assert.Equal(2, pairs.Count);
            Assert.All(pairs, p => Assert.Equal(PairingStatus.Paired, p.Status));
            Assert.Contains(pairs, p => p.CameraSerialNumber == "545308020");
            Assert.Contains(pairs, p => p.CameraSerialNumber == "545308059");
        }
    }
}
