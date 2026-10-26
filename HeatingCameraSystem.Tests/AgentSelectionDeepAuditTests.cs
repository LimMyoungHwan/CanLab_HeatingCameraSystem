using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using Moq;

namespace HeatingCameraSystem.Tests;

public sealed class AgentSelectionDeepAuditTests
{
    [Fact]
    public void Directory_RejectsInventorySentinelAndOffline_AndReplacesCurrentCameraIndex()
    {
        var directory = new AgentDirectory();
        directory.Note(Status("A", 0, new[] { "A", "B" }));
        directory.Note(Status("B", 1, new[] { "A", "B" }));
        Assert.Equal(2, directory.OnlineCameras.Count);
        directory.Note(Status("A", 3, new[] { "A", "B" }));
        Assert.False(directory.IsCameraOnline(new CameraIdentity { AgentId = "A", CameraIndex = 0 }));
        Assert.True(directory.IsCameraOnline(new CameraIdentity { AgentId = "A", CameraIndex = 3 }));
        directory.Note(Status("host-status", 0, Array.Empty<string>()));
        Assert.Empty(directory.OnlineCameras);
        Assert.False(directory.IsAgentOnline("host-status"));
        Assert.Null(directory.ResolveByAlias("alias-A"));
        directory.Note(Status("A", 3, new[] { "A" }));
        AgentStatusMessage offline = Status("A", 3, new[] { "A" });
        offline.CameraStatus = CameraStatus.Offline;
        directory.Note(offline);
        Assert.False(directory.IsAgentOnline("A"));
        Assert.Empty(directory.OnlineCameras);
    }

    [Fact]
    public void Directory_LegacyAgentWithoutInventoryCanKeepMultipleCameras()
    {
        var directory = new AgentDirectory();
        directory.Note(Status("A", 0, null));
        directory.Note(Status("A", 2, null));
        Assert.Equal(new[] { 0, 2 }, directory.OnlineCameras.Select(camera => camera.CameraIndex));
    }

    [Fact]
    public void OfflineTargets_KeepSavedAndSelectedIdentityBandAndRole()
    {
        var saved = new CameraTargetModel
        {
            AgentId = "A", CameraIndex = 2, IsPersisted = true,
            TargetChamber = ChamberRange.Low, TargetBlackBody = BlackBodyRole.Cold
        };
        var selected = new CameraTargetModel { AgentId = "B", CameraIndex = 4, IsSelected = true };
        var targets = new List<CameraTargetModel> { saved, selected, new() { AgentId = "candidate", CameraIndex = 9 } };
        Assert.Equal(1, RecipeEditorViewModel.PruneStaleTargets(targets, Array.Empty<AgentCameraOption>()));
        Assert.Equal(2, targets.Count);
        Assert.Same(saved, targets[0]);
        Assert.Same(selected, targets[1]);
        Assert.False(saved.IsOnline);
        Assert.Equal(ChamberRange.Low, saved.TargetChamber);
        Assert.Equal(BlackBodyRole.Cold, saved.TargetBlackBody);
        Assert.Equal(0, RecipeEditorViewModel.PruneStaleTargets(targets, new[] { new AgentCameraOption { AgentId = "A", CameraIndex = 2 } }));
        Assert.True(saved.IsOnline);
        Assert.True(selected.IsSelected);
    }

    [Fact]
    public async Task ChangingAgent_InvalidatesLoadedConfig_AndDelayedRepliesCannotOverwriteSelection()
    {
        var bus = new NatsScopeFixtureDeepAudit();
        var reads = new List<AgentConfigRequestMessage>();
        AgentConfigApplyMessage? applied = null;
        bus.Nats.Setup(service => service.PublishAgentConfigRequestAsync(It.IsAny<AgentConfigRequestMessage>()))
            .Callback<AgentConfigRequestMessage>(reads.Add).Returns(Task.CompletedTask);
        bus.Nats.Setup(service => service.PublishAgentConfigApplyAsync(It.IsAny<AgentConfigApplyMessage>()))
            .Callback<AgentConfigApplyMessage>(message => applied = message).Returns(Task.CompletedTask);
        await using var vm = new AgentSettingsViewModel(bus.Nats.Object, TimeSpan.FromSeconds(2));
        vm.SelectedAgent = "A";
        Task loadA = vm.LoadCommand.ExecuteAsync(null);
        AgentConfigRequestMessage a = Assert.Single(reads);
        bus.Emit("agent.config.agent.snapshot.A", Snapshot(a, "A-folder", "COM1"));
        await loadA;
        Assert.True(vm.IsLoaded);
        Assert.Equal("COM1", Assert.Single(vm.Cameras).SerialPortName);

        vm.SelectedAgent = "B";
        Assert.False(vm.IsLoaded);
        Assert.Empty(vm.Cameras);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Null(applied);
        Task loadB = vm.LoadCommand.ExecuteAsync(null);
        AgentConfigRequestMessage b = reads[1];
        bus.Emit("agent.config.agent.snapshot.A", Snapshot(a, "wrong-A", "COM1"));
        AgentConfigSnapshotMessage stale = Snapshot(b, "wrong-B", "COM1");
        stale.RequestId = "prior-request";
        bus.Emit("agent.config.agent.snapshot.B", stale);
        Assert.False(loadB.IsCompleted);
        Assert.False(vm.IsLoaded);
        bus.Emit("agent.config.agent.snapshot.B", Snapshot(b, "B-folder", "COM22"));
        await loadB;
        Assert.Equal("B-folder", vm.StoragePath);
        Assert.Equal("COM22", Assert.Single(vm.Cameras).SerialPortName);

        Task applyB = vm.ApplyCommand.ExecuteAsync(null);
        AgentConfigApplyMessage command = Assert.IsType<AgentConfigApplyMessage>(applied);
        Assert.Equal("B", command.AgentId);
        Assert.Equal("B-folder", command.Config.StoragePath);
        Assert.Equal("COM22", Assert.Single(command.Config.Cameras).SerialPortName);
        bus.Emit("agent.config.agent.ack.B", new AgentConfigAckMessage { AgentId = "B", RequestId = a.RequestId, IsSuccess = true });
        Assert.False(applyB.IsCompleted);
        bus.Emit("agent.config.agent.ack.B", new AgentConfigAckMessage { AgentId = "B", RequestId = command.RequestId, IsSuccess = true });
        await applyB;
        await vm.DisposeAsync();
        Assert.Equal(0, bus.ActiveCount);
    }

    [Fact]
    public async Task SettingsTimeout_LeavesApplyDisabled_AndDisposeReleasesSubscriptions()
    {
        var bus = new NatsScopeFixtureDeepAudit();
        bus.Nats.Setup(service => service.PublishAgentConfigRequestAsync(It.IsAny<AgentConfigRequestMessage>())).Returns(Task.CompletedTask);
        await using var vm = new AgentSettingsViewModel(bus.Nats.Object, TimeSpan.FromMilliseconds(25));
        vm.SelectedAgent = "A";
        await vm.LoadCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsLoaded);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        await vm.DisposeAsync();
        Assert.Equal(0, bus.ActiveCount);
    }

    private static AgentStatusMessage Status(string agent, int index, string[]? inventory) => new()
    {
        AgentId = agent, Alias = $"alias-{agent}", HostName = "host", CameraIndex = index,
        CameraStatus = CameraStatus.Connected, HostAgentIds = inventory?.ToList(), Timestamp = DateTime.UtcNow
    };

    private static AgentConfigSnapshotMessage Snapshot(AgentConfigRequestMessage request, string storage, string port) => new()
    {
        AgentId = request.AgentId, RequestId = request.RequestId,
        Config = new AgentConfigSnapshot { StoragePath = storage, Cameras = new() { new CameraDescriptor(request.AgentId, 4, request.AgentId, port) } }
    };
}
