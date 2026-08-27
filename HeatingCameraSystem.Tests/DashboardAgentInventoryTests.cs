using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.ViewModels;
using Moq;

namespace HeatingCameraSystem.Tests;

/// <summary>
/// Master가 AgentUI의 카메라 추가/제거를 하트비트 타임아웃 없이 즉시 반영하는지에 대한 회귀 테스트.
/// </summary>
public class DashboardAgentInventoryTests
{
    private static DashboardViewModel CreateVm(out Action<AgentStatusMessage> publishStatus)
    {
        Action<AgentStatusMessage>? captured = null;
        var nats = new Mock<INatsCommunicationService>();
        nats.Setup(n => n.SubscribeAgentStatusAsync(It.IsAny<Action<AgentStatusMessage>>()))
            .Callback<Action<AgentStatusMessage>>(cb => captured = cb)
            .Returns(Task.CompletedTask);
        nats.Setup(n => n.SubscribeLiveFrameAsync(It.IsAny<Action<LiveFrameMessage>>()))
            .Returns(Task.CompletedTask);

        var vm = new DashboardViewModel(null, nats.Object, null, startTimers: false);
        Assert.NotNull(captured);
        publishStatus = captured!;
        return vm;
    }

    private static AgentStatusMessage Status(string agentId, string host, int cameraIndex, params string[] inventory) =>
        new AgentStatusMessage
        {
            AgentId = agentId,
            HostName = host,
            CameraIndex = cameraIndex,
            CameraStatus = CameraStatus.Connected,
            Timestamp = DateTime.UtcNow,
            HostAgentIds = inventory.ToList()
        };

    private static AgentStatusMessage LegacyStatus(string agentId, string host, int cameraIndex) =>
        new AgentStatusMessage
        {
            AgentId = agentId,
            HostName = host,
            CameraIndex = cameraIndex,
            CameraStatus = CameraStatus.Connected,
            Timestamp = DateTime.UtcNow,
            HostAgentIds = null
        };

    private static AgentStatusMessage HostReport(string host, params string[] inventory) =>
        Status(host, host, 0, inventory);

    [Fact]
    public void RemovedCamera_DisappearsOnNextInventory()
    {
        var vm = CreateVm(out var publish);
        publish(Status("PC1_Agent_0", "PC1", 0, "PC1_Agent_0", "PC1_Agent_1"));
        publish(Status("PC1_Agent_1", "PC1", 1, "PC1_Agent_0", "PC1_Agent_1"));
        Assert.Equal(2, vm.Agents.Count);

        publish(Status("PC1_Agent_1", "PC1", 1, "PC1_Agent_1"));

        Assert.Single(vm.Agents);
        Assert.Equal("PC1_Agent_1", vm.Agents[0].Name);
    }

    [Fact]
    public void AddedCamera_AppearsImmediately()
    {
        var vm = CreateVm(out var publish);
        publish(Status("PC1_Agent_0", "PC1", 0, "PC1_Agent_0"));
        Assert.Single(vm.Agents);

        publish(Status("PC1_Agent_1", "PC1", 1, "PC1_Agent_0", "PC1_Agent_1"));

        Assert.Equal(2, vm.Agents.Count);
    }

    [Fact]
    public void Inventory_OnlyReconcilesItsOwnHost()
    {
        var vm = CreateVm(out var publish);
        publish(Status("PC2_Agent_0", "PC2", 0, "PC2_Agent_0"));
        publish(Status("PC1_Agent_0", "PC1", 0, "PC1_Agent_0", "PC1_Agent_1"));
        publish(Status("PC1_Agent_1", "PC1", 1, "PC1_Agent_0", "PC1_Agent_1"));
        Assert.Equal(3, vm.Agents.Count);

        publish(Status("PC1_Agent_1", "PC1", 1, "PC1_Agent_1"));

        Assert.Equal(2, vm.Agents.Count);
        Assert.Contains(vm.Agents, a => a.Name == "PC2_Agent_0");
        Assert.Contains(vm.Agents, a => a.Name == "PC1_Agent_1");
    }

    [Fact]
    public void LegacySenderWithoutInventory_NeverEvicts()
    {
        var vm = CreateVm(out var publish);
        publish(LegacyStatus("LegacyAgent_0", "PC1", 0));
        publish(LegacyStatus("LegacyAgent_1", "PC1", 1));

        Assert.Equal(2, vm.Agents.Count);
    }

    [Fact]
    public void LastCameraRemoved_HostReportsEmptyInventory_DropsItsCameras()
    {
        var vm = CreateVm(out var publish);
        publish(Status("PC1_Agent_0", "PC1", 0, "PC1_Agent_0"));
        Assert.Single(vm.Agents);

        publish(HostReport("PC1"));

        Assert.Empty(vm.Agents);
    }

    [Fact]
    public void InventoryOnlyReport_NeverCreatesAnAgentNode()
    {
        var vm = CreateVm(out var publish);

        publish(HostReport("PC1"));

        Assert.Empty(vm.Agents);
    }

    [Fact]
    public void EmptyInventoryFromOneHost_LeavesOtherHostsAlone()
    {
        var vm = CreateVm(out var publish);
        publish(Status("PC2_Agent_0", "PC2", 0, "PC2_Agent_0"));
        publish(Status("PC1_Agent_0", "PC1", 0, "PC1_Agent_0"));

        publish(HostReport("PC1"));

        var remaining = Assert.Single(vm.Agents);
        Assert.Equal("PC2_Agent_0", remaining.Name);
    }

    [Fact]
    public void ReslottedCamera_ReplacesStaleCameraNodeInPlace()
    {
        var vm = CreateVm(out var publish);
        publish(Status("PC1_Agent_0", "PC1", 0, "PC1_Agent_0"));
        publish(Status("PC1_Agent_0", "PC1", 3, "PC1_Agent_0"));

        var agent = Assert.Single(vm.Agents);
        var camera = Assert.Single(agent.Cameras);
        Assert.Equal("CAM-03", camera.Id);
    }

    [Fact]
    public void SerialFault_IsShownWithoutAffectingTheVideoStatus()
    {
        var vm = CreateVm(out var publish);
        var msg = Status("PC1_Agent_0", "PC1", 0, "PC1_Agent_0");
        msg.IsSerialConnected = false;

        publish(msg);

        var camera = Assert.Single(vm.Agents).Cameras[0];
        Assert.False(camera.IsSerialConnected);
        Assert.Equal(CameraStatus.Connected, camera.CameraStatus);
    }

    [Fact]
    public void SerialRecovery_ClearsTheFault()
    {
        var vm = CreateVm(out var publish);
        var down = Status("PC1_Agent_0", "PC1", 0, "PC1_Agent_0");
        down.IsSerialConnected = false;
        publish(down);

        var up = Status("PC1_Agent_0", "PC1", 0, "PC1_Agent_0");
        up.IsSerialConnected = true;
        publish(up);

        Assert.True(Assert.Single(vm.Agents).Cameras[0].IsSerialConnected);
    }

    [Fact]
    public void LegacySenderThatCannotReportSerial_RaisesNoSerialFault()
    {
        var vm = CreateVm(out var publish);

        publish(LegacyStatus("PC1_Agent_0", "PC1", 0));

        Assert.True(Assert.Single(vm.Agents).Cameras[0].IsSerialConnected);
    }

    [Fact]
    public void RemovedCamera_ReleasesItsDashboardSlot()
    {
        var vm = CreateVm(out var publish);
        publish(Status("PC1_Agent_0", "PC1", 0, "PC1_Agent_0", "PC1_Agent_1"));
        publish(Status("PC1_Agent_1", "PC1", 1, "PC1_Agent_0", "PC1_Agent_1"));

        vm.SetViewModeCommand.Execute("4");
        var doomed = vm.Agents.First(a => a.Name == "PC1_Agent_0").Cameras[0];
        vm.AssignCameraToDashboardSlotCommand.Execute(Tuple.Create(doomed, vm.CameraFeeds[0]));
        Assert.NotNull(vm.CameraFeeds[0].Camera);

        publish(Status("PC1_Agent_1", "PC1", 1, "PC1_Agent_1"));

        Assert.DoesNotContain(vm.CameraFeeds, slot => ReferenceEquals(slot.Camera, doomed));
    }
}
