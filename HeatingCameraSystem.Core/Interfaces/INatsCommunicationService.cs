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

        // Manager -> Server (Inventory): agent-mgr.inventory.{PCId}
        Task PublishCameraInventoryAsync(CameraInventoryMessage message);
        Task SubscribeCameraInventoryAsync(Action<CameraInventoryMessage> onMessageReceived);

        // Server -> Manager (Command): server.cmd.mgr.{PCId}
        Task PublishManagerCommandAsync(ManagerCommandMessage message);
        Task SubscribeManagerCommandAsync(string pcId, Action<ManagerCommandMessage> onMessageReceived);

        // Manager -> Server (Log Alert): agent-mgr.log.alert.{PCId}
        Task PublishLogAlertAsync(LogAlertMessage message);
        Task SubscribeLogAlertAsync(Action<LogAlertMessage> onMessageReceived);

        // Server -> Manager (Log Dump Request): server.req.log.{PCId}
        Task PublishLogDumpRequestAsync(LogDumpRequestMessage message);
        Task SubscribeLogDumpRequestAsync(string pcId, Action<LogDumpRequestMessage> onMessageReceived);

        // Manager -> Server (Log Dump): agent-mgr.log.dump.{PCId}
        Task PublishLogDumpAsync(LogDumpMessage message);
        Task SubscribeLogDumpAsync(string pcId, Action<LogDumpMessage> onMessageReceived);
    }
}
