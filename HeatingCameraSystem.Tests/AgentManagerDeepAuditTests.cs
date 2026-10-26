using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HeatingCameraSystem.AgentManager.Config;
using HeatingCameraSystem.AgentManager.Services;
using HeatingCameraSystem.AgentManager.State;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class AgentManagerDeepAuditTests
    {
        [Fact]
        public async Task ApprovalWaitsForPhysicalHeartbeat_AndAdoptsAgentUiIdentityForDisableAndSerial()
        {
            using var fixture = new Fixture();
            fixture.Store.Upsert(new CameraEntry { HardwareId = "HW_A", UsbContainerId = "usbA", OpenCvIndex = 0 });
            using var handler = fixture.CreateHandler();
            await handler.HandleAsync(new ManagerCommandMessage { Op = ManagerCommandOp.Approve, HardwareId = "HW_A" });
            Assert.Equal(string.Empty, fixture.Store.GetByHardwareId("HW_A")?.AgentId);
            Assert.Empty(fixture.Controls);

            CameraEntry? mapped = fixture.Supervisor.NoteHeartbeat(Status("PC_Agent_1", "usbA", 3));
            Assert.NotNull(mapped);
            Assert.Equal("PC_Agent_1", mapped.AgentId);
            Assert.Equal(3, mapped.OpenCvIndex);
            Assert.True(fixture.Supervisor.IsRunning("HW_A"));
            Assert.Empty(fixture.Controls); // Receiving a running camera does not unnecessarily reload it.

            await handler.HandleAsync(new ManagerCommandMessage
            {
                Op = ManagerCommandOp.SetSerial, HardwareId = "HW_A",
                Payload = JsonSerializer.Serialize(new CameraSerialSettings { CameraIndex = 0, PortName = "COM7", BaudRate = 115200 })
            });
            SerialConfigMessage serial = Assert.Single(fixture.SerialConfigurations);
            Assert.Equal("PC_Agent_1", serial.AgentId);
            Assert.Equal(3, serial.Settings.CameraIndex);

            await handler.HandleAsync(new ManagerCommandMessage { Op = ManagerCommandOp.Disable, HardwareId = "HW_A" });
            CameraControlMessage unload = Assert.Single(fixture.Controls);
            Assert.Equal("PC_Agent_1", unload.AgentId);
            Assert.Equal(3, unload.CameraIndex);
            Assert.Equal(CameraControlOps.RuntimeUnload, unload.Op);
        }

        [Fact]
        public void LegacyHeartbeatWithoutContainerOnlyRefreshesAnAlreadyMappedAgent()
        {
            using var fixture = new Fixture();
            var existing = new CameraEntry { HardwareId = "HW_A", AgentId = "console-agent", UsbContainerId = "usbA", OpenCvIndex = 3, IsApproved = true };
            fixture.Store.Upsert(existing);
            fixture.Supervisor.Spawn(existing);
            var heartbeat = new AgentStatusMessage
            {
                AgentId = "console-agent", CameraIndex = 99, CameraStatus = CameraStatus.Connected, HostName = string.Empty
            };
            Assert.Same(existing, fixture.Supervisor.NoteHeartbeat(heartbeat));
            Assert.True(fixture.Supervisor.IsRunning("HW_A"));
            Assert.Equal(3, existing.OpenCvIndex); // No physical identity means no index/identity reassignment.
            heartbeat.AgentId = "unknown-agent";
            Assert.Null(fixture.Supervisor.NoteHeartbeat(heartbeat));
            Assert.Null(fixture.Store.GetByAgentId("unknown-agent"));
            Assert.Single(fixture.Store.GetAll());
        }

        [Fact]
        public void WrongHostOrAmbiguousContainerCannotAdoptAnAgentId()
        {
            using var fixture = new Fixture();
            fixture.Store.Upsert(new CameraEntry { HardwareId = "HW_A", UsbContainerId = "usbA" });
            AgentStatusMessage wrongHost = Status("foreign_agent", "usbA", 0);
            wrongHost.HostName = Environment.MachineName + "-other";
            Assert.Null(fixture.Supervisor.NoteHeartbeat(wrongHost));
            Assert.Equal(string.Empty, fixture.Store.GetByHardwareId("HW_A")?.AgentId);

            fixture.Store.Upsert(new CameraEntry { HardwareId = "HW_B", UsbContainerId = "usbA" });
            Assert.Null(fixture.Supervisor.NoteHeartbeat(Status("PC_Agent_1", "usbA", 0)));
            Assert.All(fixture.Store.GetAll(), entry => Assert.Empty(entry.AgentId));
        }

        [Fact]
        public void DifferentSensorDoesNotInheritPreviousApprovalOrIdentity()
        {
            using var fixture = new Fixture();
            fixture.Store.Upsert(new CameraEntry
            {
                HardwareId = "HW_A", UsbContainerId = "usbA", AgentId = "old-agent", CameraSerialNumber = "123", IsApproved = true
            });
            AgentStatusMessage replacement = Status("new-agent", "usbA", 0);
            replacement.CameraSerialNumber = "456";
            Assert.Null(fixture.Supervisor.NoteHeartbeat(replacement));
            Assert.Equal("old-agent", fixture.Store.GetByHardwareId("HW_A")?.AgentId);
            Assert.Empty(fixture.Controls);
        }

        [Fact]
        public void OfflineHeartbeatIsNotReportedAsRunning()
        {
            using var fixture = new Fixture();
            fixture.Store.Upsert(new CameraEntry { HardwareId = "HW_A", UsbContainerId = "usbA", IsApproved = true });
            AgentStatusMessage status = Status("PC_Agent_1", "usbA", -1);
            status.CameraStatus = CameraStatus.Offline;
            fixture.Supervisor.NoteHeartbeat(status);
            Assert.False(fixture.Supervisor.IsRunning("HW_A"));
            Assert.Empty(fixture.Controls);
        }

        [Fact]
        public async Task AgentUiNdjsonTailAndDumpUseReportedTrustedDirectory_AndFilterByAgentIdAcrossRollingFiles()
        {
            using var fixture = new Fixture();
            string logDirectory = Path.Combine(fixture.DirectoryPath, "agent-ui-logs");
            fixture.Settings.AgentUiLogDirectory = logDirectory;
            Directory.CreateDirectory(logDirectory);
            fixture.Store.Upsert(new CameraEntry { HardwareId = "HW_A", UsbContainerId = "usbA", IsApproved = true });
            AgentStatusMessage status = Status("PC_Agent_1", "usbA", 3);
            status.LogDirectory = logDirectory;
            fixture.Supervisor.NoteHeartbeat(status);
            Assert.Equal(logDirectory, fixture.Store.GetByHardwareId("HW_A")?.AgentLogDirectory);

            string first = Path.Combine(logDirectory, "agentui-20261026.ndjson");
            string second = Path.Combine(logDirectory, "agentui-20261027.ndjson");
            await File.WriteAllTextAsync(first, Line("PC_Agent_1", "BIAS NG first") + Line("other-camera", "foreign-secret"));
            await File.WriteAllTextAsync(second, Line("PC_Agent_1", "BIAS NG next day"));
            using var tail = new LogTailService(fixture.Nats.Object, fixture.Settings, NullLogger<LogTailService>.Instance);
            await tail.TailFileAsync("PC_Agent_1", first, filterByAgentId: true);
            await tail.TailFileAsync("PC_Agent_1", first, filterByAgentId: true);
            await tail.TailFileAsync("PC_Agent_1", second, filterByAgentId: true);
            Assert.Equal(2, fixture.Alerts.Count);
            Assert.All(fixture.Alerts, alert => Assert.Equal("PC_Agent_1", alert.AgentId));
            Assert.Contains(fixture.Alerts, alert => alert.Message == "BIAS NG next day");

            using var dump = new LogDumpHandler(fixture.Nats.Object, fixture.Settings, NullLogger<LogDumpHandler>.Instance, fixture.Store);
            await dump.HandleAsync(new LogDumpRequestMessage { AgentId = "PC_Agent_1", MaxBytes = 100_000 });
            LogDumpMessage message = Assert.Single(fixture.Dumps);
            using var input = new MemoryStream(message.GzipBytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            string content = await reader.ReadToEndAsync();
            Assert.Contains("BIAS NG first", content);
            Assert.Contains("BIAS NG next day", content);
            Assert.DoesNotContain("foreign-secret", content);
        }

        [Fact]
        public void HeartbeatCannotRedirectLogsToArbitraryDirectory()
        {
            using var fixture = new Fixture();
            fixture.Settings.AgentUiLogDirectory = Path.Combine(fixture.DirectoryPath, "allowed-logs");
            fixture.Store.Upsert(new CameraEntry { HardwareId = "HW_A", UsbContainerId = "usbA" });
            AgentStatusMessage status = Status("PC_Agent_1", "usbA", 0);
            status.LogDirectory = Path.Combine(fixture.DirectoryPath, "private-other-folder");
            fixture.Supervisor.NoteHeartbeat(status);
            Assert.Empty(fixture.Store.GetByHardwareId("HW_A")?.AgentLogDirectory ?? "");
            Assert.False(AgentLogFiles.IsTrustedAgentUiDirectory(status.LogDirectory, fixture.Settings));
            Assert.Throws<ArgumentException>(() => AgentLogFiles.ResolveDirectory(fixture.Settings, "..", null));
        }

        private static AgentStatusMessage Status(string id, string container, int index) => new()
        {
            AgentId = id, HostName = Environment.MachineName, UsbContainerId = container,
            CameraSerialNumber = "123", CameraIndex = index, CameraStatus = CameraStatus.Connected, Timestamp = DateTime.UtcNow
        };

        private static string Line(string agentId, string message) => JsonSerializer.Serialize(new Dictionary<string, string>
        { ["@l"] = "Error", ["@m"] = message, ["AgentId"] = agentId }) + "\n";

        private sealed class Fixture : IDisposable
        {
            public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "hcs_deep_manager_" + Guid.NewGuid().ToString("N"));
            public ManagerSettings Settings { get; }
            public ManagerStateStore Store { get; }
            public AgentSupervisor Supervisor { get; }
            public Mock<INatsCommunicationService> Nats { get; } = new();
            public List<CameraControlMessage> Controls { get; } = new();
            public List<SerialConfigMessage> SerialConfigurations { get; } = new();
            public List<LogAlertMessage> Alerts { get; } = new();
            public List<LogDumpMessage> Dumps { get; } = new();
            public Fixture()
            {
                Directory.CreateDirectory(DirectoryPath);
                Settings = new ManagerSettings { InstallRoot = DirectoryPath, PCId = "manager-pc" };
                Store = new ManagerStateStore(DirectoryPath);
                Nats.Setup(nats => nats.PublishCameraControlAsync(It.IsAny<CameraControlMessage>())).Callback<CameraControlMessage>(Controls.Add).Returns(Task.CompletedTask);
                Nats.Setup(nats => nats.PublishSerialConfigAsync(It.IsAny<SerialConfigMessage>())).Callback<SerialConfigMessage>(SerialConfigurations.Add).Returns(Task.CompletedTask);
                Nats.Setup(nats => nats.PublishLogAlertAsync(It.IsAny<LogAlertMessage>())).Callback<LogAlertMessage>(Alerts.Add).Returns(Task.CompletedTask);
                Nats.Setup(nats => nats.PublishLogDumpAsync(It.IsAny<LogDumpMessage>())).Callback<LogDumpMessage>(Dumps.Add).Returns(Task.CompletedTask);
                Nats.Setup(nats => nats.PublishCameraInventoryAsync(It.IsAny<CameraInventoryMessage>())).Returns(Task.CompletedTask);
                Supervisor = new AgentSupervisor(Settings, Store, NullLogger<AgentSupervisor>.Instance, Nats.Object);
            }
            public ManagerCommandHandler CreateHandler()
            {
                var inventory = new InventoryPublisher(Nats.Object, Settings, Store, Supervisor, NullLogger<InventoryPublisher>.Instance);
                return new ManagerCommandHandler(Nats.Object, Settings, Store, Supervisor, inventory, NullLogger<ManagerCommandHandler>.Instance);
            }
            public void Dispose()
            {
                Supervisor.Dispose();
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
