using System;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface INatsCommunicationService : IAsyncDisposable
    {
        Task ConnectAsync(string natsUrl = "nats://127.0.0.1:4222");

        // Master -> Agent (Command)
        Task PublishCaptureCommandAsync(CaptureCommandMessage message);
        Task SubscribeCaptureCommandAsync(string agentId, Action<CaptureCommandMessage> onMessageReceived);

        // Agent -> Master (Status)
        Task PublishAgentStatusAsync(AgentStatusMessage message);
        Task SubscribeAgentStatusAsync(Action<AgentStatusMessage> onMessageReceived);

        // Agent -> Master (Result)
        Task PublishCaptureResultAsync(CaptureResultMessage message);
        Task SubscribeCaptureResultAsync(Action<CaptureResultMessage> onMessageReceived);

        // Master -> Agent (Serial Config): master.config.serial.{AgentId}
        Task PublishSerialConfigAsync(SerialConfigMessage message);
        Task SubscribeSerialConfigAsync(string agentId, Action<SerialConfigMessage> onMessageReceived);

        // Agent -> Master (Serial Config ACK): agent.config.serial.ack.{AgentId}
        Task PublishSerialConfigAckAsync(SerialConfigAckMessage message);
        Task SubscribeSerialConfigAckAsync(string agentId, Action<SerialConfigAckMessage> onMessageReceived);
    }
}
