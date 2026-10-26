using HeatingCameraSystem.AgentManager.Config;
using HeatingCameraSystem.AgentManager.Services;
using HeatingCameraSystem.AgentManager.State;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HeatingCameraSystem.Tests;

public sealed class ManagerStartupDeepAuditTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Startup_DoesNotReloadAlreadyRunningCameraWhetherHeartbeatArrivesBeforeOrAfterRestore(bool heartbeatFirst)
    {
        using var f = new Fixture();
        f.Store.Upsert(Entry());
        if (heartbeatFirst) f.Supervisor.NoteHeartbeat(Status(CameraStatus.Connected, 4));
        f.Supervisor.RestoreDesiredState();
        f.Supervisor.NoteHeartbeat(Status(CameraStatus.Connected, 4));
        Assert.True(f.Supervisor.IsRunning("hardware-A"));
        Assert.Empty(f.Controls);
    }

    [Fact]
    public void Startup_OfflineConfirmedCameraLoadsOnceWithHeartbeatIndex()
    {
        using var f = new Fixture();
        f.Store.Upsert(Entry());
        f.Supervisor.RestoreDesiredState();
        Assert.Empty(f.Controls);
        f.Supervisor.NoteHeartbeat(Status(CameraStatus.Offline, 4));
        f.Supervisor.NoteHeartbeat(Status(CameraStatus.Offline, 4));
        CameraControlMessage load = Assert.Single(f.Controls);
        Assert.Equal(CameraControlOps.RuntimeLoad, load.Op);
        Assert.Equal("agent-A", load.AgentId);
        Assert.Equal(4, load.CameraIndex);
        Assert.False(f.Supervisor.IsRunning("hardware-A"));
    }

    [Fact]
    public void Startup_UnresolvedVideoIndexWaitsUntilPhysicalHeartbeatProvidesOne()
    {
        using var f = new Fixture();
        CameraEntry entry = Entry(); entry.OpenCvIndex = -1;
        f.Store.Upsert(entry);
        f.Supervisor.RestoreDesiredState();
        f.Supervisor.NoteHeartbeat(Status(CameraStatus.Offline, -1));
        Assert.Empty(f.Controls);
        f.Supervisor.NoteHeartbeat(Status(CameraStatus.Offline, 7));
        f.Supervisor.NoteHeartbeat(Status(CameraStatus.Offline, 7));
        Assert.Equal(7, Assert.Single(f.Controls).CameraIndex);
    }

    [Fact]
    public async Task ExplicitRestart_StillReloadsAfterConnectedStartupRestore()
    {
        using var f = new Fixture();
        f.Store.Upsert(Entry());
        f.Supervisor.RestoreDesiredState();
        f.Supervisor.NoteHeartbeat(Status(CameraStatus.Connected, 4));
        Assert.Empty(f.Controls);
        var inventory = new InventoryPublisher(f.Nats.Object, f.Settings, f.Store, f.Supervisor, NullLogger<InventoryPublisher>.Instance);
        using var handler = new ManagerCommandHandler(f.Nats.Object, f.Settings, f.Store, f.Supervisor, inventory, NullLogger<ManagerCommandHandler>.Instance);
        await handler.HandleAsync(new ManagerCommandMessage { HardwareId = "hardware-A", Op = ManagerCommandOp.Restart });
        Assert.Equal(CameraControlOps.RuntimeLoad, Assert.Single(f.Controls).Op);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void DisabledOrUnapprovedCamera_CannotBeAutomaticallyOrExplicitlyLoaded(bool approved, bool disabled)
    {
        using var f = new Fixture();
        CameraEntry entry = Entry(); entry.IsApproved = approved; entry.IsDisabled = disabled;
        f.Store.Upsert(entry);
        f.Supervisor.RestoreDesiredState();
        f.Supervisor.RequestLoadAfterHeartbeat(entry);
        f.Supervisor.NoteHeartbeat(Status(CameraStatus.Offline, 4));
        f.Supervisor.Spawn(entry);
        Assert.DoesNotContain(f.Controls, command => command.Op == CameraControlOps.RuntimeLoad);
        Assert.False(f.Supervisor.IsRunning("hardware-A"));
    }

    [Fact]
    public void LateOldHeartbeatCannotAssignNewIndexWithoutPhysicalIdentity()
    {
        using var f = new Fixture();
        f.Store.Upsert(Entry());
        f.Supervisor.RestoreDesiredState();
        AgentStatusMessage status = Status(CameraStatus.Offline, 99);
        status.UsbContainerId = string.Empty;
        f.Supervisor.NoteHeartbeat(status);
        Assert.Empty(f.Controls);
        Assert.Equal(4, f.Store.GetByHardwareId("hardware-A")?.OpenCvIndex);
        status.CameraIndex = 4;
        f.Supervisor.NoteHeartbeat(status);
        Assert.Equal(4, Assert.Single(f.Controls).CameraIndex);
    }

    private static CameraEntry Entry() => new()
    {
        HardwareId = "hardware-A", AgentId = "agent-A", UsbContainerId = "usb-A",
        CameraSerialNumber = "123", Alias = "camera-A", OpenCvIndex = 4, IsApproved = true
    };

    private static AgentStatusMessage Status(CameraStatus state, int index) => new()
    {
        AgentId = "agent-A", Alias = "camera-A", HostName = Environment.MachineName,
        UsbContainerId = "usb-A", CameraSerialNumber = "123", CameraIndex = index,
        CameraStatus = state, Timestamp = DateTime.UtcNow
    };

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "manager-startup-" + Guid.NewGuid().ToString("N"));
        public ManagerSettings Settings { get; }
        public ManagerStateStore Store { get; }
        public AgentSupervisor Supervisor { get; }
        public Mock<INatsCommunicationService> Nats { get; } = new();
        public List<CameraControlMessage> Controls { get; } = new();
        public Fixture()
        {
            Directory.CreateDirectory(_root);
            Settings = new ManagerSettings { InstallRoot = _root };
            Store = new ManagerStateStore(_root);
            Nats.Setup(service => service.PublishCameraControlAsync(It.IsAny<CameraControlMessage>()))
                .Callback<CameraControlMessage>(Controls.Add).Returns(Task.CompletedTask);
            Nats.Setup(service => service.PublishCameraInventoryAsync(It.IsAny<CameraInventoryMessage>())).Returns(Task.CompletedTask);
            Supervisor = new AgentSupervisor(Settings, Store, NullLogger<AgentSupervisor>.Instance, Nats.Object);
        }
        public void Dispose() { Supervisor.Dispose(); Directory.Delete(_root, recursive: true); }
    }
}
