using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Moq;

namespace HeatingCameraSystem.Tests;

public sealed class CameraCommandClientDeepAuditTests
{
    [Fact]
    public async Task Send_IgnoresOtherRequestAgentCameraOrOperation_AndDisposesResponseSubscription()
    {
        var bus = new NatsScopeFixtureDeepAudit();
        CameraControlMessage? sent = null;
        bus.Nats.Setup(service => service.PublishCameraControlAsync(It.IsAny<CameraControlMessage>()))
            .Callback<CameraControlMessage>(message => sent = message).Returns(Task.CompletedTask);
        await using var client = new CameraCommandClient(bus.Nats.Object);
        Task<CameraControlAckMessage> waiting = client.SendAsync("A", 4, CameraControlOps.Nuc);
        CameraControlMessage command = Assert.IsType<CameraControlMessage>(sent);
        Assert.NotEmpty(command.RequestId);
        Assert.Equal(1, bus.ActiveCount);
        foreach (string field in new[] { "request", "agent", "camera", "operation" })
        {
            CameraControlAckMessage ack = Ack(command);
            switch (field)
            {
                case "request": ack.RequestId = "old-run"; break;
                case "agent": ack.AgentId = "B"; break;
                case "camera": ack.CameraIndex++; break;
                default: ack.Op = CameraControlOps.Run; break;
            }
            bus.Emit("agent.ack.camera.A", ack);
            Assert.False(waiting.IsCompleted);
        }
        bus.Emit("agent.ack.camera.A", Ack(command));
        Assert.True((await waiting.WaitAsync(TimeSpan.FromSeconds(2))).IsSuccess);
        Assert.Equal(0, bus.ActiveCount);
    }

    [Fact]
    public async Task Timeout_StopsOnlyTheTimedOutCapture_AndReleasesSubscription()
    {
        var bus = new NatsScopeFixtureDeepAudit();
        var sent = new List<CameraControlMessage>();
        bus.Nats.Setup(service => service.PublishCameraControlAsync(It.IsAny<CameraControlMessage>()))
            .Callback<CameraControlMessage>(sent.Add).Returns(Task.CompletedTask);
        await using var client = new CameraCommandClient(bus.Nats.Object, commandTimeout: TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAsync<TimeoutException>(() => client.SendAsync("A", 4, CameraControlOps.Capture));
        Assert.Equal(2, sent.Count);
        Assert.Equal(CameraControlOps.CaptureAbort, sent[1].Op);
        Assert.Equal(sent[0].RequestId, sent[1].TargetRequestId);
        Assert.NotEqual(sent[0].RequestId, sent[1].RequestId);
        Assert.Equal(0, bus.ActiveCount);
    }

    [Fact]
    public async Task Stop_WaitsForBiasAbortBeforeDraining_AndRejectsNewWork()
    {
        var bus = new NatsScopeFixtureDeepAudit();
        var sent = new List<CameraControlMessage>();
        var abortPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishAbort = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bus.Nats.Setup(service => service.PublishCameraControlAsync(It.IsAny<CameraControlMessage>()))
            .Returns<CameraControlMessage>(message =>
            {
                sent.Add(message);
                if (message.Op != CameraControlOps.BiasAbort) return Task.CompletedTask;
                abortPublished.TrySetResult();
                return finishAbort.Task;
            });
        await using var client = new CameraCommandClient(bus.Nats.Object);
        Task<CameraControlAckMessage> work = client.SendAsync("A", 4, CameraControlOps.BiasLow);
        Task drain = client.StopAsync(TimeSpan.FromSeconds(2));
        await abortPublished.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(drain.IsCompleted);
        Assert.Equal(sent[0].RequestId, sent[1].TargetRequestId);
        finishAbort.TrySetResult();
        await drain;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync("A", 4, CameraControlOps.Nuc));
        Assert.Equal(0, bus.ActiveCount);
    }

    [Fact]
    public async Task RawRequest_CorrelatesAgentAndRequest_AndDisposesAfterReply()
    {
        var bus = new NatsScopeFixtureDeepAudit();
        RawImageRequestMessage? sent = null;
        bus.Nats.Setup(service => service.PublishRawImageRequestAsync(It.IsAny<RawImageRequestMessage>()))
            .Callback<RawImageRequestMessage>(message => sent = message).Returns(Task.CompletedTask);
        Task<RawImageResponseMessage> waiting = new RawImageClient(bus.Nats.Object).RequestAsync("A", "frame.y16");
        RawImageRequestMessage request = Assert.IsType<RawImageRequestMessage>(sent);
        bus.Emit("agent.res.raw.A", new RawImageResponseMessage { AgentId = "B", RequestId = request.RequestId, IsSuccess = true });
        Assert.False(waiting.IsCompleted);
        bus.Emit("agent.res.raw.A", new RawImageResponseMessage { AgentId = "A", RequestId = request.RequestId, IsSuccess = true });
        Assert.True((await waiting.WaitAsync(TimeSpan.FromSeconds(1))).IsSuccess);
        Assert.Equal(0, bus.ActiveCount);
    }

    private static CameraControlAckMessage Ack(CameraControlMessage command) => new()
    {
        AgentId = command.AgentId, CameraIndex = command.CameraIndex, Op = command.Op,
        RequestId = command.RequestId, IsSuccess = true
    };
}
