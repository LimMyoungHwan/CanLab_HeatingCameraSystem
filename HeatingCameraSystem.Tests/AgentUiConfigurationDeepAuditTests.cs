using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.AgentUI.ViewModels;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class AgentUiConfigurationDeepAuditTests
    {
        [Theory]
        [InlineData("mode")]
        [InlineData("nats")]
        [InlineData("storage")]
        [InlineData("heartbeat")]
        [InlineData("format")]
        [InlineData("burst")]
        public void RuntimeSettingChangeIsSavedAsRestartDraft_WithoutChangingActiveServices(string setting)
        {
            using var file = new ConfigFileScope();
            var active = new AgentUiConfig { SimulationMode = false };
            active.Cameras.Add(new CameraDescriptor("Agent_A", 2, "active", "COM2", CameraSerialNumber: "123", UsbContainerId: "usbA"));
            object list = active.Cameras;
            var vm = new SettingsViewModel(active);
            switch (setting)
            {
                case "mode": vm.SimulationMode = true; break;
                case "nats": vm.NatsUrl = "nats://another-host:4222"; break;
                case "storage": vm.StoragePath = Path.Combine(file.TemporaryDirectory, "captures"); break;
                case "heartbeat": vm.HeartbeatSeconds = 2; break;
                case "format": vm.CaptureImageFormat = CaptureImageFormat.Tiff16; break;
                case "burst": vm.CaptureBurstCount = 3; break;
            }
            vm.Cameras[0].Alias = "saved draft";
            bool reconfigured = false;
            vm.Saved += () => reconfigured = true;
            vm.SaveCommand.Execute(null);

            Assert.True(active.RestartRequired);
            Assert.False(reconfigured);
            Assert.Same(list, active.Cameras);
            Assert.Equal("active", active.Cameras.Single().Alias);
            Assert.False(active.SimulationMode);
            Assert.Equal("nats://127.0.0.1:4222", active.NatsUrl);
            Assert.Equal(string.Empty, active.StoragePath);
            Assert.Equal(5, active.HeartbeatSeconds);
            Assert.Equal(CaptureImageFormat.Y16Raw, active.CaptureImageFormat);
            Assert.Equal(1, active.CaptureBurstCount);
            Assert.Contains("재시작", vm.StatusText);
            Assert.Contains("현재 실행 설정은 유지", vm.StatusText);
            AgentConfigSnapshot snapshot = HeatingCameraSystem.AgentUI.App.CreateConfigSnapshot(active);
            Assert.True(snapshot.RestartRequired);
            Assert.Equal("saved draft", snapshot.Cameras.Single().Alias);

            // A later hotplug save must not overwrite the operator's pending restart changes.
            active.Save();
            using JsonDocument disk = JsonDocument.Parse(File.ReadAllText(file.Path));
            Assert.Equal("saved draft", disk.RootElement.GetProperty("Cameras")[0].GetProperty("Alias").GetString());
        }

        [Fact]
        public void RemoteDraftKeepsActiveCameraListReferenceAndBindings()
        {
            using var file = new ConfigFileScope();
            var active = new AgentUiConfig { SimulationMode = true };
            active.Cameras.Add(new CameraDescriptor("Agent_A", 2, "active"));
            object list = active.Cameras;
            AgentUiConfig draft = active.EditableSnapshot();
            draft.Cameras.Clear();
            draft.Cameras.Add(new CameraDescriptor("Agent_B", 7, "after restart"));

            Assert.True(active.SaveDraft(draft, requireRestart: true));
            Assert.Same(list, active.Cameras);
            Assert.Equal("Agent_A", active.Cameras.Single().AgentId);
            Assert.Equal("Agent_B", HeatingCameraSystem.AgentUI.App.CreateConfigSnapshot(active).Cameras.Single().AgentId);
        }

        [Fact]
        public void FailedDiskSaveDoesNotMutateActiveConfiguration()
        {
            using var file = new ConfigFileScope();
            var active = new AgentUiConfig { SimulationMode = true };
            active.Cameras.Add(new CameraDescriptor("Agent_A", 2, "active"));
            active.Save();
            var vm = new SettingsViewModel(active) { NatsUrl = "nats://changed:4222" };
            vm.Cameras[0].Alias = "uncommitted";
            bool changed = false;
            vm.Saved += () => changed = true;
            using (new FileStream(file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                vm.SaveCommand.Execute(null);

            Assert.False(changed);
            Assert.False(active.RestartRequired);
            Assert.Equal("nats://127.0.0.1:4222", active.NatsUrl);
            Assert.Equal("active", active.Cameras.Single().Alias);
            Assert.StartsWith("Save failed:", vm.StatusText);
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("999", "usbA")]
        [InlineData("", "missing-container")]
        public async Task AutoDetectDoesNotFallbackToIdenticalModelOrIndex_WhenPhysicalIdentityIsUnknownOrMismatched(string serial, string container)
        {
            CameraComPair[] pairs = { Pair("123", "usbA", "COM2", 0), Pair("456", "usbB", "COM3", 1) };
            var pairing = new Mock<ICameraComPairingService>();
            pairing.Setup(service => service.GetPairsAsync(It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(pairs);
            var active = new AgentUiConfig();
            active.Cameras.Add(new CameraDescriptor("Agent_B", 1, "camera", "COM9", "CLTC", serial, container));
            var vm = new SettingsViewModel(active, pairing.Object);

            await vm.AutoDetectSerialCommand.ExecuteAsync(null);

            Assert.Equal("COM9", vm.Cameras[0].SerialPortName);
            Assert.Equal(serial, vm.Cameras[0].CameraSerialNumber);
            Assert.Equal(container, vm.Cameras[0].UsbContainerId);
        }

        [Fact]
        public void PhysicalIdentityMatchIsIndependentOfEnumerationOrder()
        {
            CameraComPair[] pairs = { Pair("123", "usbA", "COM2", 0), Pair("456", "usbB", "COM3", 1) };
            var row = new CameraRow(new CameraDescriptor("Agent_B", 0, "camera", DeviceName: "CLTC", CameraSerialNumber: "456", UsbContainerId: "old-usb"));
            CameraComPair? selected = SettingsViewModel.FindPairForRow(pairs, row);
            Assert.NotNull(selected);
            Assert.Equal("COM3", selected.SerialPort?.PortName);
        }

        [Fact]
        public void DuplicateSensorSerialCannotBeResolvedByContainer()
        {
            CameraComPair[] pairs = { Pair("123", "usbA", "COM2", 0), Pair("123", "usbB", "COM3", 1) };
            var row = new CameraRow(new CameraDescriptor("Agent_A", 0, "camera", UsbContainerId: "usbA"));
            Assert.Null(SettingsViewModel.FindPairForRow(pairs, row));
        }

        [Fact]
        public void EnumerationFailureInvalidatesAllVideoIndices_AndRecoveryCannotBindRemainingCameraToRemovedSerial()
        {
            var config = new AgentUiConfig();
            var cameraA = new CameraDescriptor("Agent_A", 0, "A", "COM2", CameraSerialNumber: "123", UsbContainerId: "usbA");
            var cameraB = new CameraDescriptor("Agent_B", 1, "B", "COM3", CameraSerialNumber: "456", UsbContainerId: "usbB");
            config.Cameras.AddRange(new[] { cameraA, cameraB });
            var enumerator = new Mock<IVideoDeviceEnumerator>();
            enumerator.Setup(service => service.Enumerate()).Throws(new IOException("DirectShow inventory unavailable"));

            Assert.Equal(2, HeatingCameraSystem.AgentUI.App.ReconcileVideoIndicesFromEnumeration(config, enumerator.Object));
            Assert.Equal(cameraA with { OpenCvIndex = -1 }, config.Cameras[0]);
            Assert.Equal(cameraB with { OpenCvIndex = -1 }, config.Cameras[1]);
            Assert.False(CameraBindingPolicy.SameBinding(cameraA, config.Cameras[0]));

            // Camera A disappeared; B has moved from index 1 to 0 when enumeration recovers.
            enumerator.Setup(service => service.Enumerate()).Returns(new[] { new VideoDevice(0, "path-B", "usbB", "CLTC") });
            Assert.Equal(1, HeatingCameraSystem.AgentUI.App.ReconcileVideoIndicesFromEnumeration(config, enumerator.Object));
            Assert.Equal(-1, config.Cameras[0].OpenCvIndex);
            Assert.Equal("COM2", config.Cameras[0].SerialPortName);
            Assert.Equal(0, config.Cameras[1].OpenCvIndex);
            Assert.Equal("COM3", config.Cameras[1].SerialPortName);
        }

        [Fact]
        public void AliasOnlyEditPreservesBinding_ButSerialCameraAndIndexChangesDoNot()
        {
            var original = new CameraDescriptor("Agent_A", 2, "old alias", "COM2", CameraSerialNumber: "123", UsbContainerId: "usbA");
            Assert.True(CameraBindingPolicy.SameBinding(original, original with { Alias = "new alias" }));
            Assert.False(CameraBindingPolicy.SameBinding(original, original with { OpenCvIndex = 3 }));
            Assert.False(CameraBindingPolicy.SameBinding(original, original with { SerialPortName = "COM3" }));
            Assert.False(CameraBindingPolicy.SameBinding(original, original with { CameraSerialNumber = "456" }));
            Assert.False(CameraBindingPolicy.SameBinding(original, original with { UsbContainerId = "usbB" }));
        }

        [Fact]
        public void SerialConfigRejectsWrongCameraAndUnsupportedProtocol()
        {
            var camera = new CameraDescriptor("Agent_A", 2, "camera");
            var settings = new CameraSerialSettings { CameraIndex = 2, PortName = "COM2", BaudRate = 115200 };
            CameraBindingPolicy.ValidateSerialConfig(camera, settings);
            settings.CameraIndex = 0;
            Assert.Throws<InvalidOperationException>(() => CameraBindingPolicy.ValidateSerialConfig(camera, settings));
            settings.CameraIndex = 2;
            settings.BaudRate = 9600;
            Assert.Throws<ArgumentException>(() => CameraBindingPolicy.ValidateSerialConfig(camera, settings));
        }

        private static CameraComPair Pair(string serial, string container, string port, int index) => new(
            new DiscoveredCamera { FriendlyName = "CLTC_T_VGA", HardwareId = "camera-" + serial, UsbParentId = container, OpenCvIndex = index },
            new DiscoveredSerialPort(port, "serial", "serial-" + serial, container), serial, PairingStatus.Paired, false);

        private sealed class ConfigFileScope : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(AgentUiConfig.ConfigDir, "agentui.json");
            public string TemporaryDirectory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hcs_deep_config_" + Guid.NewGuid().ToString("N"));
            private readonly byte[]? _backup;
            public ConfigFileScope()
            {
                Directory.CreateDirectory(AgentUiConfig.ConfigDir);
                Directory.CreateDirectory(TemporaryDirectory);
                _backup = File.Exists(Path) ? File.ReadAllBytes(Path) : null;
            }
            public void Dispose()
            {
                if (_backup is null) File.Delete(Path); else File.WriteAllBytes(Path, _backup);
                Directory.Delete(TemporaryDirectory, recursive: true);
            }
        }
    }
}
