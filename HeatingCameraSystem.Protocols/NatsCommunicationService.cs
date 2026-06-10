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

        public async Task SubscribeCaptureCommandAsync(string agentId, Action<CaptureCommandMessage> onMessageReceived)
        {
            CheckConnection();
            string subject = $"master.cmd.capture.{agentId}";
            
            // Subscribing in the background
            _ = Task.Run(async () =>
            {
                await foreach (var msg in _connection!.SubscribeAsync<CaptureCommandMessage>(subject))
                {
                    if (msg.Data != null)
                    {
                        onMessageReceived(msg.Data);
                    }
                }
            });
            
            // Also subscribe to 'all' for broadcasts
            string broadcastSubject = "master.cmd.capture.all";
            _ = Task.Run(async () =>
            {
                await foreach (var msg in _connection!.SubscribeAsync<CaptureCommandMessage>(broadcastSubject))
                {
                    if (msg.Data != null)
                    {
                        onMessageReceived(msg.Data);
                    }
                }
            });

            await Task.CompletedTask;
        }

        public async Task PublishAgentStatusAsync(AgentStatusMessage message)
        {
            CheckConnection();
            string subject = $"agent.status.{message.AgentId}";
            await _connection!.PublishAsync(subject, message);
        }

        public async Task SubscribeAgentStatusAsync(Action<AgentStatusMessage> onMessageReceived)
        {
            CheckConnection();
            string subject = "agent.status.>"; // Receive from all agents
            
            _ = Task.Run(async () =>
            {
                await foreach (var msg in _connection!.SubscribeAsync<AgentStatusMessage>(subject))
                {
                    if (msg.Data != null)
                    {
                        onMessageReceived(msg.Data);
                    }
                }
            });

            await Task.CompletedTask;
        }

        public async Task PublishCaptureResultAsync(CaptureResultMessage message)
        {
            CheckConnection();
            string subject = $"agent.result.capture.{message.AgentId}";
            await _connection!.PublishAsync(subject, message);
        }

        public async Task SubscribeCaptureResultAsync(Action<CaptureResultMessage> onMessageReceived)
        {
            CheckConnection();
            string subject = "agent.result.capture.>"; // Receive from all agents
            
            _ = Task.Run(async () =>
            {
                await foreach (var msg in _connection!.SubscribeAsync<CaptureResultMessage>(subject))
                {
                    if (msg.Data != null)
                    {
                        onMessageReceived(msg.Data);
                    }
                }
            });

            await Task.CompletedTask;
        }

        private void CheckConnection()
        {
            if (_connection == null)
            {
                throw new InvalidOperationException("NATS connection is not initialized. Call ConnectAsync first.");
            }
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
