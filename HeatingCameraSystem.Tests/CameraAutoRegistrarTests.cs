using System.Collections.Generic;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CameraAutoRegistrarTests
    {
        private const string Host = "PC";

        private static DiscoveredCamera Camera(string friendly, string containerId, int index = 0) =>
            new() { FriendlyName = friendly, UsbParentId = containerId, OpenCvIndex = index, HardwareId = "hw-" + friendly };

        private static CameraComPair Pair(DiscoveredCamera camera, string? serial, string? port) =>
            new(
                camera,
                port is null ? null : new DiscoveredSerialPort(port, $"USB Serial ({port})", "instance-" + port, camera.UsbParentId),
                serial,
                PairingStatus.Paired,
                false);

        [Fact]
        public void RegistersDetectedCameraWithAutoNumberedAgentId()
        {
            var cams = new List<CameraDescriptor>();
            var pairs = new List<CameraComPair> { Pair(Camera("CLTC_T_VGA A", "{CID-A}", 1), "545308020", "COM9") };

            int added = CameraAutoRegistrar.Register(cams, pairs, Host);

            Assert.Equal(1, added);
            Assert.Single(cams);
            Assert.Equal("PC_Agent_1", cams[0].AgentId);
            Assert.Equal(1, cams[0].OpenCvIndex);
            Assert.Equal("CLTC_T_VGA A", cams[0].Alias);
            Assert.Equal("COM9", cams[0].SerialPortName);
            Assert.Equal("545308020", cams[0].CameraSerialNumber);
            Assert.Equal("{CID-A}", cams[0].UsbContainerId);
        }

        [Fact]
        public void ContinuesNumberingPastHighestExistingForHost()
        {
            var cams = new List<CameraDescriptor>
            {
                new("PC_Agent_2", 0, "Existing", null, null, "111", "{CID-EXIST}"),
            };
            var pairs = new List<CameraComPair> { Pair(Camera("New", "{CID-NEW}"), "222", "COM3") };

            int added = CameraAutoRegistrar.Register(cams, pairs, Host);

            Assert.Equal(1, added);
            Assert.Equal("PC_Agent_3", cams[1].AgentId);
        }

        [Fact]
        public void SkipsCameraAlreadyRegisteredBySerial()
        {
            var cams = new List<CameraDescriptor>
            {
                new("PC_Agent_1", 0, "Existing", "COM9", null, "545308020", "{CID-OLD}"),
            };
            var pairs = new List<CameraComPair> { Pair(Camera("Same", "{CID-NEW}"), "545308020", "COM5") };

            int added = CameraAutoRegistrar.Register(cams, pairs, Host);

            Assert.Equal(0, added);
            Assert.Single(cams);
        }

        [Fact]
        public void SkipsCameraAlreadyRegisteredByContainerId()
        {
            var cams = new List<CameraDescriptor>
            {
                new("PC_Agent_1", 0, "Existing", null, null, null, "{CID-1}"),
            };
            var pairs = new List<CameraComPair> { Pair(Camera("Same", "{CID-1}"), null, "COM5") };

            int added = CameraAutoRegistrar.Register(cams, pairs, Host);

            Assert.Equal(0, added);
            Assert.Single(cams);
        }

        [Fact]
        public void SkipsAnonymousDeviceWithNoSerialAndNoContainer()
        {
            var cams = new List<CameraDescriptor>();
            var pairs = new List<CameraComPair> { Pair(Camera("Anon", ""), null, "COM5") };

            int added = CameraAutoRegistrar.Register(cams, pairs, Host);

            Assert.Equal(0, added);
            Assert.Empty(cams);
        }

        [Fact]
        public void TreatsZeroSerialAsUnusableAndKeysOnContainerId()
        {
            var cams = new List<CameraDescriptor>();
            var pairs = new List<CameraComPair> { Pair(Camera("ZeroSn", "{CID-Z}"), "0000", "COM5") };

            int added = CameraAutoRegistrar.Register(cams, pairs, Host);

            Assert.Equal(1, added);
            Assert.Null(cams[0].CameraSerialNumber);
            Assert.Equal("{CID-Z}", cams[0].UsbContainerId);
        }

        [Fact]
        public void RegistersMultipleWithSequentialIds()
        {
            var cams = new List<CameraDescriptor>();
            var pairs = new List<CameraComPair>
            {
                Pair(Camera("A", "{CID-A}", 1), "111", "COM3"),
                Pair(Camera("B", "{CID-B}", 2), "222", "COM4"),
            };

            int added = CameraAutoRegistrar.Register(cams, pairs, Host);

            Assert.Equal(2, added);
            Assert.Equal("PC_Agent_1", cams[0].AgentId);
            Assert.Equal("PC_Agent_2", cams[1].AgentId);
        }
    }
}
