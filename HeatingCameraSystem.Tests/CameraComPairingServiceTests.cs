using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Simulation;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CameraComPairingServiceTests
    {
        [Fact]
        public async Task GetPairsAsync_WithFakeEnumerators_ReturnsPairedCameras()
        {
            var service = CreateService(new FakeCameraEnumerator(), new FakeUsbSerialEnumerator());

            var pairs = (await service.GetPairsAsync()).ToList();

            Assert.Equal(2, pairs.Count);
            Assert.All(pairs, pair => Assert.Equal(PairingStatus.Paired, pair.Status));
            Assert.Contains(pairs, pair => pair.CameraSerialNumber == "000100001" && pair.SerialPort?.PortName == "COM7");
            Assert.Contains(pairs, pair => pair.CameraSerialNumber == "000100002" && pair.SerialPort?.PortName == "COM8");
        }

        [Fact]
        public async Task GetPairsAsync_WhenNoPortSharesUsbParent_ReturnsUnpaired()
        {
            var camera = new DiscoveredCamera
            {
                HardwareId = "USB\\VID_0483&PID_5740\\CAMX_IF00",
                FriendlyName = "CLTC_T_VGA Camera X",
                OpenCvIndex = 0,
                UsbParentId = "USB\\VID_0483&PID_5740\\CAMX",
            };
            var port = new DiscoveredSerialPort("COM7", "USB Serial Device (COM7)", "USB\\OTHER", "USB\\OTHER");
            var service = CreateService(new StaticCameraEnumerator(camera), new StaticUsbSerialEnumerator(port));

            var pair = Assert.Single(await service.GetPairsAsync());

            Assert.Equal(PairingStatus.Unpaired, pair.Status);
            Assert.Null(pair.SerialPort);
            Assert.Null(pair.CameraSerialNumber);
        }

        [Fact]
        public async Task GetPairsAsync_WhenTwoPortsShareUsbParent_ReturnsAmbiguous()
        {
            var camera = new DiscoveredCamera
            {
                HardwareId = "USB\\VID_0483&PID_5740\\CAMX_IF00",
                FriendlyName = "CLTC_T_VGA Camera X",
                OpenCvIndex = 0,
                UsbParentId = "USB\\VID_0483&PID_5740\\CAMX",
            };
            var ports = new[]
            {
                new DiscoveredSerialPort("COM7", "USB Serial Device (COM7)", "USB\\CAMX_A", camera.UsbParentId),
                new DiscoveredSerialPort("COM8", "USB Serial Device (COM8)", "USB\\CAMX_B", camera.UsbParentId),
            };
            var service = CreateService(new StaticCameraEnumerator(camera), new StaticUsbSerialEnumerator(ports));

            var pair = Assert.Single(await service.GetPairsAsync());

            Assert.Equal(PairingStatus.Ambiguous, pair.Status);
            Assert.Null(pair.SerialPort);
            Assert.Null(pair.CameraSerialNumber);
        }

        [Fact]
        public async Task SetManualOverride_UsesConfiguredPortFirst()
        {
            var settings = new HardwareSettings();
            var service = CreateService(new FakeCameraEnumerator(), new FakeUsbSerialEnumerator(), settings);

            service.SetManualOverride("USB\\VID_0483&PID_5740\\CAMA", "COM8");
            var pairs = await service.GetPairsAsync();
            var pair = pairs.Single(p => p.Camera.UsbParentId == "USB\\VID_0483&PID_5740\\CAMA");

            Assert.True(pair.IsManualOverride);
            Assert.Equal("COM8", pair.SerialPort?.PortName);
            Assert.Equal("000100002", pair.CameraSerialNumber);
            Assert.Equal(PairingStatus.Paired, pair.Status);
        }

        private static CameraComPairingService CreateService(
            ICameraEnumerator cameraEnumerator,
            IUsbSerialEnumerator serialEnumerator,
            HardwareSettings? settings = null) =>
            new(
                cameraEnumerator,
                serialEnumerator,
                portName => new FakeCameraSerialClient(portName),
                settings ?? new HardwareSettings());

        private sealed class StaticCameraEnumerator : ICameraEnumerator
        {
            private readonly IReadOnlyList<DiscoveredCamera> _cameras;

            public StaticCameraEnumerator(params DiscoveredCamera[] cameras)
            {
                _cameras = cameras;
            }

            public event Action<PnpChange>? Changed
            {
                add { }
                remove { }
            }

            public IReadOnlyList<DiscoveredCamera> Enumerate() => _cameras;

            public void StartWatching() { }

            public void StopWatching() { }

            public void Dispose() { }
        }

        private sealed class StaticUsbSerialEnumerator : IUsbSerialEnumerator
        {
            private readonly IReadOnlyList<DiscoveredSerialPort> _ports;

            public StaticUsbSerialEnumerator(params DiscoveredSerialPort[] ports)
            {
                _ports = ports;
            }

            public StaticUsbSerialEnumerator(IEnumerable<DiscoveredSerialPort> ports)
            {
                _ports = ports.ToList();
            }

            public IReadOnlyList<DiscoveredSerialPort> Enumerate() => _ports;
        }
    }
}
