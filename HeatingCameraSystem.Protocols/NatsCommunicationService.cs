using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;

namespace HeatingCameraSystem.Protocols
{
    public class NatsCommunicationService : INatsCommunicationService
    {
        private INatsConnection? _connection;
        private readonly NatsOpts _baseOpts;

        // Subscription lifetime: cancelled on dispose so every self-healing retry loop stops cleanly.
        private readonly CancellationTokenSource _subscriptionCts = new();
        private readonly List<Task> _subscriptionTasks = new();
        private readonly object _subscriptionLock = new();

        public NatsCommunicationService()
        {
            // By default, use local NATS server and JSON serialization
            _baseOpts = NatsOpts.Default with { SerializerRegistry = NatsJsonSerializerRegistry.Default };
        }

        public async Task ConnectAsync(string natsUrl = "nats://127.0.0.1:4222")
        {
            var opts = _baseOpts with { Url = natsUrl };
            _connection = new NatsConnection(opts);
            await _connection.ConnectAsync();
        }

        public async Task PublishCaptureCommandAsync(CaptureCommandMessage message)
        {
            CheckConnection();
            string subject = $"master.cmd.capture.{message.TargetAgentId}";
            await _connection!.PublishAsync(subject, message);
        }

        public Task SubscribeCaptureCommandAsync(string agentId, Action<CaptureCommandMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"master.cmd.capture.{agentId}", onMessageReceived);
            RunSubscriptionLoop("master.cmd.capture.all", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishAgentStatusAsync(AgentStatusMessage message)
        {
            CheckConnection();
            string subject = $"agent.status.{message.AgentId}";
            await _connection!.PublishAsync(subject, message);
        }

        public Task SubscribeAgentStatusAsync(Action<AgentStatusMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop("agent.status.>", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishCaptureResultAsync(CaptureResultMessage message)
        {
            CheckConnection();
            string subject = $"agent.result.capture.{message.AgentId}";
            await _connection!.PublishAsync(subject, message);
        }

        public Task SubscribeCaptureResultAsync(Action<CaptureResultMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop("agent.result.capture.>", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishLiveFrameAsync(LiveFrameMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"agent.live.{message.AgentId}", message);
        }

        public Task SubscribeLiveFrameAsync(Action<LiveFrameMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop("agent.live.>", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishSerialConfigAsync(SerialConfigMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"master.config.serial.{message.AgentId}", message);
        }

        public Task SubscribeSerialConfigAsync(string agentId, Action<SerialConfigMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"master.config.serial.{agentId}", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishSerialConfigAckAsync(SerialConfigAckMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"agent.config.serial.ack.{message.AgentId}", message);
        }

        public Task SubscribeSerialConfigAckAsync(string agentId, Action<SerialConfigAckMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"agent.config.serial.ack.{agentId}", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishAgentConfigRequestAsync(AgentConfigRequestMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"master.config.agent.get.{message.AgentId}", message);
        }

        public Task SubscribeAgentConfigRequestAsync(string agentId, Action<AgentConfigRequestMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"master.config.agent.get.{agentId}", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishAgentConfigSnapshotAsync(AgentConfigSnapshotMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"agent.config.agent.snapshot.{message.AgentId}", message);
        }

        public Task SubscribeAgentConfigSnapshotAsync(string agentId, Action<AgentConfigSnapshotMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"agent.config.agent.snapshot.{agentId}", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishAgentConfigApplyAsync(AgentConfigApplyMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"master.config.agent.set.{message.AgentId}", message);
        }

        public Task SubscribeAgentConfigApplyAsync(string agentId, Action<AgentConfigApplyMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"master.config.agent.set.{agentId}", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishAgentConfigAckAsync(AgentConfigAckMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"agent.config.agent.ack.{message.AgentId}", message);
        }

        public Task SubscribeAgentConfigAckAsync(string agentId, Action<AgentConfigAckMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"agent.config.agent.ack.{agentId}", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishCameraControlAsync(CameraControlMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"master.cmd.camera.{message.AgentId}", message);
        }

        public Task SubscribeCameraControlAsync(string agentId, Action<CameraControlMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"master.cmd.camera.{agentId}", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishCameraControlAckAsync(CameraControlAckMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"agent.ack.camera.{message.AgentId}", message);
        }

        public Task SubscribeCameraControlAckAsync(string agentId, Action<CameraControlAckMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"agent.ack.camera.{agentId}", onMessageReceived);
            return Task.CompletedTask;
        }

        private void RunSubscriptionLoop<T>(string subject, Action<T> onMessageReceived)
        {
            CancellationToken ct = _subscriptionCts.Token;
            Task task = Task.Run(() => RunSubscribeWithRetryAsync<T>(
                token => UnwrapAsync(_connection!.SubscribeAsync<T>(subject, cancellationToken: token), token),
                onMessageReceived,
                attempt => TimeSpan.FromMilliseconds(Math.Min(500 * (1 << Math.Min(attempt, 4)), 8000)),
                ct));

            lock (_subscriptionLock)
            {
                _subscriptionTasks.Add(task);
            }
        }

        // Self-healing subscription loop. A thrown enumerator (transient NATS disconnect) or a natural
        // enumerator completion (connection drop) is NOT auto-re-issued by NATS.Net, so we re-issue it here
        // with backoff and keep delivering until ct is cancelled (service disposed). attempt resets to 0 after
        // any delivered message so unrelated later blips restart backoff from the bottom instead of compounding.
        internal static async Task RunSubscribeWithRetryAsync<T>(
            Func<CancellationToken, IAsyncEnumerable<T>> subscribeFactory,
            Action<T> onMessage,
            Func<int, TimeSpan> backoff,
            CancellationToken ct)
        {
            int attempt = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await foreach (T item in subscribeFactory(ct).WithCancellation(ct).ConfigureAwait(false))
                    {
                        try
                        {
                            onMessage(item);
                        }
                        catch (Exception cbEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[NATS] subscriber callback threw: {cbEx.GetType().Name}: {cbEx.Message}");
                        }
                        attempt = 0;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception loopEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[NATS] subscription attempt failed, will re-subscribe: {loopEx.GetType().Name}: {loopEx.Message}");
                }

                try
                {
                    await Task.Delay(backoff(attempt), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                attempt++;
            }
        }

        private static async IAsyncEnumerable<T> UnwrapAsync<T>(
            IAsyncEnumerable<NatsMsg<T>> src,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (NatsMsg<T> msg in src.WithCancellation(ct).ConfigureAwait(false))
            {
                T? data = msg.Data;
                if (data is null) continue;
                yield return data;
            }
        }

        private void CheckConnection()
        {
            if (_connection == null)
            {
                throw new InvalidOperationException("NATS connection is not initialized. Call ConnectAsync first.");
            }
        }

        public async Task PublishCameraInventoryAsync(CameraInventoryMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"agent-mgr.inventory.{message.PCId}", message);
        }

        public Task SubscribeCameraInventoryAsync(Action<CameraInventoryMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop("agent-mgr.inventory.>", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishManagerCommandAsync(ManagerCommandMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"server.cmd.mgr.{message.PCId}", message);
        }

        public Task SubscribeManagerCommandAsync(string pcId, Action<ManagerCommandMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"server.cmd.mgr.{pcId}", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishLogAlertAsync(LogAlertMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"agent-mgr.log.alert.{message.PCId}", message);
        }

        public Task SubscribeLogAlertAsync(Action<LogAlertMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop("agent-mgr.log.alert.>", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishLogDumpRequestAsync(LogDumpRequestMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"server.req.log.{message.PCId}", message);
        }

        public Task SubscribeLogDumpRequestAsync(string pcId, Action<LogDumpRequestMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"server.req.log.{pcId}", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishLogDumpAsync(LogDumpMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"agent-mgr.log.dump.{message.PCId}", message);
        }

        public Task SubscribeLogDumpAsync(string pcId, Action<LogDumpMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"agent-mgr.log.dump.{pcId}", onMessageReceived);
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            _subscriptionCts.Cancel();

            Task[] pending;
            lock (_subscriptionLock)
            {
                pending = _subscriptionTasks.ToArray();
            }

            try
            {
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // Best-effort: a loop may exceed the bound or surface a stray error on shutdown;
                // dispose the connection regardless so we never hang or leak on exit.
            }

            if (_connection != null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            _subscriptionCts.Dispose();
        }
    }
}
