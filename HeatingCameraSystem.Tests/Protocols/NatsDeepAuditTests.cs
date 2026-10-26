using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using Moq;
using NATS.Client.Core;
using System.Threading.Channels;

namespace HeatingCameraSystem.Tests.Protocols;

public class NatsDeepAuditTests
{
    [Fact]
    public async Task Subscribe_WaitsForRegistrationAndServerPong()
    {
        var channel = Channel.CreateUnbounded<NatsMsg<int>>();
        var sub = CreateSub(channel);
        var registered = new TaskCompletionSource<INatsSub<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pong = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new Mock<INatsConnection>();
        connection.Setup(c => c.SubscribeCoreAsync<int>("audit", null, null, null, It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<INatsSub<int>>(registered.Task));
        connection.Setup(c => c.PingAsync(It.IsAny<CancellationToken>()))
            .Callback(() => pingStarted.TrySetResult())
            .Returns(() => new ValueTask<TimeSpan>(pong.Task));
        await using var service = new NatsCommunicationService(connection.Object);
        Task<IAsyncDisposable> pending = service.SubscribeAsync<int>("audit", _ => { });
        Assert.False(pending.IsCompleted);
        registered.SetResult(sub.Object);
        await pingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(pending.IsCompleted);
        pong.SetResult(TimeSpan.FromMilliseconds(1));
        await using var lease = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, service.ActiveSubscriptionCount);
    }

    [Fact]
    public async Task ScopedDispose_UnsubscribesAndStopsLateCallbacks()
    {
        var channel = Channel.CreateUnbounded<NatsMsg<int>>();
        var sub = CreateSub(channel);
        var connection = ConnectionFor(sub.Object);
        await using var service = new NatsCommunicationService(connection.Object);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbacks = 0;
        var lease = await service.SubscribeAsync<int>("audit", _ =>
        {
            Interlocked.Increment(ref callbacks);
            first.TrySetResult();
        });
        channel.Writer.TryWrite(new NatsMsg<int> { Subject = "audit", Data = 1 });
        await first.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await lease.DisposeAsync();
        channel.Writer.TryWrite(new NatsMsg<int> { Subject = "audit", Data = 2 });
        Assert.Equal(1, callbacks);
        Assert.Equal(0, service.ActiveSubscriptionCount);
        sub.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task CancelledScope_IsRemovedWithoutDisposingWholeService()
    {
        var channel = Channel.CreateUnbounded<NatsMsg<int>>();
        var sub = CreateSub(channel);
        var connection = ConnectionFor(sub.Object);
        await using var service = new NatsCommunicationService(connection.Object);
        using var cts = new CancellationTokenSource();
        var lease = await service.SubscribeAsync<int>("audit", _ => { }, cts.Token);
        cts.Cancel();
        await lease.DisposeAsync();
        Assert.Equal(0, service.ActiveSubscriptionCount);
        connection.Verify(c => c.DisposeAsync(), Times.Never);
    }

    [Fact]
    public async Task RegistrationFailure_DoesNotLeaveSubscriptionOrCallbackBehind()
    {
        var connection = new Mock<INatsConnection>();
        connection.Setup(c => c.SubscribeCoreAsync<int>("audit", null, null, null, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromException<INatsSub<int>>(new IOException("SUB failed")));
        await using var service = new NatsCommunicationService(connection.Object);
        await Assert.ThrowsAsync<IOException>(() => service.SubscribeAsync<int>("audit", _ => { }));
        Assert.Equal(0, service.ActiveSubscriptionCount);
    }

    [Fact]
    public async Task LegacyScopedFallback_DetachesCallbackOnDispose()
    {
        var service = new Mock<INatsCommunicationService>();
        Action<AgentStatusMessage>? callback = null;
        service.Setup(n => n.SubscribeAgentStatusAsync(It.IsAny<Action<AgentStatusMessage>>()))
            .Callback<Action<AgentStatusMessage>>(value => callback = value).Returns(Task.CompletedTask);
        int delivered = 0;
        var lease = await service.Object.SubscribeAgentStatusScopedAsync(_ => delivered++);
        Assert.NotNull(callback);
        callback(new AgentStatusMessage());
        await lease.DisposeAsync();
        callback(new AgentStatusMessage());
        Assert.Equal(1, delivered);
    }

    [Fact]
    public async Task RepeatedScopes_DoNotAccumulateServiceEntries()
    {
        var connection = new Mock<INatsConnection>();
        connection.Setup(c => c.PingAsync(It.IsAny<CancellationToken>())).Returns(new ValueTask<TimeSpan>(TimeSpan.Zero));
        connection.Setup(c => c.SubscribeCoreAsync<int>("audit", null, null, null, It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<INatsSub<int>>(CreateSub(Channel.CreateUnbounded<NatsMsg<int>>()).Object));
        await using var service = new NatsCommunicationService(connection.Object);
        for (int i = 0; i < 20; i++)
        {
            var lease = await service.SubscribeAsync<int>("audit", _ => { });
            await lease.DisposeAsync();
            Assert.Equal(0, service.ActiveSubscriptionCount);
        }
    }

    [Fact]
    public async Task CallbackFailure_DoesNotEndNativeSubscription()
    {
        var channel = Channel.CreateUnbounded<NatsMsg<int>>();
        var connection = ConnectionFor(CreateSub(channel).Object);
        await using var service = new NatsCommunicationService(connection.Object);
        var second = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var lease = await service.SubscribeAsync<int>("audit", value =>
        {
            if (value == 1) throw new InvalidOperationException("subscriber failed");
            second.TrySetResult(value);
        });
        channel.Writer.TryWrite(new NatsMsg<int> { Subject = "audit", Data = 1 });
        channel.Writer.TryWrite(new NatsMsg<int> { Subject = "audit", Data = 2 });
        Assert.Equal(2, await second.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private static Mock<INatsSub<int>> CreateSub(Channel<NatsMsg<int>> channel)
    {
        var sub = new Mock<INatsSub<int>>();
        sub.SetupGet(s => s.Msgs).Returns(channel.Reader);
        sub.Setup(s => s.DisposeAsync()).Callback(() => channel.Writer.TryComplete()).Returns(ValueTask.CompletedTask);
        return sub;
    }

    private static Mock<INatsConnection> ConnectionFor(INatsSub<int> sub)
    {
        var connection = new Mock<INatsConnection>();
        connection.Setup(c => c.SubscribeCoreAsync<int>("audit", null, null, null, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<INatsSub<int>>(sub));
        connection.Setup(c => c.PingAsync(It.IsAny<CancellationToken>())).Returns(new ValueTask<TimeSpan>(TimeSpan.Zero));
        return connection;
    }
}
