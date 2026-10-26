using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Protocols.Cameras.CL;
using HeatingCameraSystem.Protocols.Simulation;

namespace HeatingCameraSystem.Tests;

[Trait("Category", "Integration")]
public class NatsCameraIntegrationDeepAuditTests
{
    [Fact]
    public async Task RealNats_SerialErrorReachesMaster_ThenNextRequestSucceedsWithoutStartupDelay()
    {
        string? url = Environment.GetEnvironmentVariable("HCS_TEST_NATS_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            Console.WriteLine("[SKIP] Set HCS_TEST_NATS_URL to an isolated NATS server for the live camera bridge test.");
            return;
        }
        string root = Path.Combine(Path.GetTempPath(), "hcs_live_nats_audit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var agent = new NatsCommunicationService();
            await using var master = new NatsCommunicationService();
            await agent.ConnectAsync(url);
            await master.ConnectAsync(url);
            var camera = new CameraDescriptor("audit_" + Guid.NewGuid().ToString("N"), 4, "isolated test", "TEST");
            using var manager = new CameraRuntimeManager(d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource()));
            using var store = new CaptureStore(root, new LiteDbCaptureIndex(Path.Combine(root, "index.db")));
            using var serial = new ClSerialCameraClient("TEST", new ErrorThenSuccessTransport());
            await serial.InitializeAsync();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var heartbeatLease = await master.SubscribeAgentStatusScopedAsync(status =>
            {
                if (status.AgentId == camera.AgentId) ready.TrySetResult();
            });
            await using var connector = new CameraNatsConnector(agent, manager, store, new[] { camera },
                cameraControlHandler: async (_, _) =>
                {
                    await serial.SetShutterAsync(false);
                    return (true, "ok");
                });
            connector.Start(url);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await heartbeatLease.DisposeAsync();

            await using var client = new CameraCommandClient(master, commandTimeout: TimeSpan.FromSeconds(5));
            CameraControlAckMessage rejected = await client.SendAsync(camera.AgentId, 4, CameraControlOps.ShutterClose);
            Assert.False(rejected.IsSuccess);
            Assert.Contains("CL", rejected.Message);
            Assert.Equal(0, master.ActiveSubscriptionCount);
            for (int i = 0; i < 10; i++)
            {
                // No arbitrary subscription warm-up delay: SUB+PONG must make the first ACK reliable.
                CameraControlAckMessage accepted = await client.SendAsync(camera.AgentId, 4, CameraControlOps.ShutterClose);
                Assert.True(accepted.IsSuccess);
                Assert.NotEqual(rejected.RequestId, accepted.RequestId);
                Assert.Equal(0, master.ActiveSubscriptionCount);
            }
            await connector.DisposeAsync();
            Assert.Equal(0, agent.ActiveSubscriptionCount);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    private sealed class ErrorThenSuccessTransport : IClSerialTransport
    {
        private byte[]? _reply;
        private int _writes;
        public bool IsOpen { get; private set; }
        public void Open() => IsOpen = true;
        public void DiscardInBuffer() => _reply = null;
        public void Write(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            _writes++;
            _reply = new byte[] { 0x43, 0x4C, buffer[offset + 2], buffer[offset + 3], buffer[offset + 4],
                _writes == 1 ? (byte)0xFF : (byte)0, 0 };
        }
        public int Read(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            byte[] reply = _reply ?? throw new TimeoutException();
            Array.Copy(reply, 0, buffer, offset, reply.Length);
            _reply = null;
            return reply.Length;
        }
        public void Dispose() => IsOpen = false;
    }
}
