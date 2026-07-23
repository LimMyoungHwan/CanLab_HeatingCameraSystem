using System;
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

        private void RunSubscriptionLoop<T>(string subject, Action<T> onMessageReceived)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var msg in _connection!.SubscribeAsync<T>(subject))
                    {
                        if (msg.Data == null) continue;
                        try
                        {
                            onMessageReceived(msg.Data);
                        }
                        catch (Exception cbEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[NATS] subscriber callback threw on {subject}: {cbEx.GetType().Name}: {cbEx.Message}");
                        }
                    }
                }
                catch (Exception loopEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[NATS] subscription loop ended on {subject}: {loopEx.GetType().Name}: {loopEx.Message}");
                }
            });
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
            if (_connection != null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
    }
}
