using System;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// Master·Agent·Manager를 잇는 NATS 메시징 계약. 토픽 문자열은 프로세스 간 약속이므로
    /// 루트 <c>AGENTS.md</c>의 규칙과 정확히 일치해야 하며, 한쪽만 바꾸면 통신이 조용히 끊긴다.
    /// 재연결은 NATS 라이브러리가 자체 처리한다(<c>ConnectionMonitorService</c> 대상이 아니다).
    /// </summary>
    public interface INatsCommunicationService : IAsyncDisposable
    {
        /// <summary>NATS 서버에 접속한다. 이후 발행·구독은 접속이 끝난 뒤에만 유효하다.</summary>
        Task ConnectAsync(string natsUrl = "nats://127.0.0.1:4222");

        // ── 캡처 명령: Master → Agent ──

        /// <summary>캡처를 지시한다. <c>TargetAgentId</c>가 "all"이면 전체 브로드캐스트다.</summary>
        Task PublishCaptureCommandAsync(CaptureCommandMessage message);

        /// <summary>지정 Agent 앞으로 온 캡처 명령을 구독한다.</summary>
        Task SubscribeCaptureCommandAsync(string agentId, Action<CaptureCommandMessage> onMessageReceived);

        // ── 상태 하트비트: Agent → Master ──

        /// <summary>카메라 상태 하트비트를 보낸다(호스트 카메라 인벤토리도 함께 실린다).</summary>
        Task PublishAgentStatusAsync(AgentStatusMessage message);

        /// <summary>모든 Agent의 상태 하트비트를 구독한다.</summary>
        Task SubscribeAgentStatusAsync(Action<AgentStatusMessage> onMessageReceived);

        // ── 캡처 결과: Agent → Master ──

        /// <summary>캡처 결과를 보고한다. 실패한 캡처도 <c>IsSuccess=false</c>로 보고한다.</summary>
        Task PublishCaptureResultAsync(CaptureResultMessage message);

        /// <summary>모든 Agent의 캡처 결과를 구독한다.</summary>
        Task SubscribeCaptureResultAsync(Action<CaptureResultMessage> onMessageReceived);

        // ── 라이브 미리보기 프레임: Agent → Master (agent.live.{AgentId}) ──

        /// <summary>미리보기 프레임을 보낸다. 대역폭을 먹으므로 발행 주기에 주의한다.</summary>
        Task PublishLiveFrameAsync(LiveFrameMessage message);

        /// <summary>모든 Agent의 라이브 프레임을 구독한다.</summary>
        Task SubscribeLiveFrameAsync(Action<LiveFrameMessage> onMessageReceived);

        // ── 시리얼 설정: Master → Agent (master.config.serial.{AgentId}) ──

        Task PublishSerialConfigAsync(SerialConfigMessage message);
        Task SubscribeSerialConfigAsync(string agentId, Action<SerialConfigMessage> onMessageReceived);

        // ── 시리얼 설정 응답: Agent → Master (agent.config.serial.ack.{AgentId}) ──

        Task PublishSerialConfigAckAsync(SerialConfigAckMessage message);
        Task SubscribeSerialConfigAckAsync(string agentId, Action<SerialConfigAckMessage> onMessageReceived);

        // ── Agent 설정 조회 요청: Master → Agent (master.config.agent.get.{AgentId}) ──

        Task PublishAgentConfigRequestAsync(AgentConfigRequestMessage message);
        Task SubscribeAgentConfigRequestAsync(string agentId, Action<AgentConfigRequestMessage> onMessageReceived);

        // ── Agent 설정 스냅샷: Agent → Master (agent.config.agent.snapshot.{AgentId}) ──

        Task PublishAgentConfigSnapshotAsync(AgentConfigSnapshotMessage message);
        Task SubscribeAgentConfigSnapshotAsync(string agentId, Action<AgentConfigSnapshotMessage> onMessageReceived);

        // ── Agent 설정 적용: Master → Agent (master.config.agent.set.{AgentId}) ──

        Task PublishAgentConfigApplyAsync(AgentConfigApplyMessage message);
        Task SubscribeAgentConfigApplyAsync(string agentId, Action<AgentConfigApplyMessage> onMessageReceived);

        // ── Agent 설정 적용 응답: Agent → Master (agent.config.agent.ack.{AgentId}) ──

        Task PublishAgentConfigAckAsync(AgentConfigAckMessage message);
        Task SubscribeAgentConfigAckAsync(string agentId, Action<AgentConfigAckMessage> onMessageReceived);

        // ── 카메라 인벤토리: Manager → 서버 (agent-mgr.inventory.{PCId}) ──

        Task PublishCameraInventoryAsync(CameraInventoryMessage message);
        Task SubscribeCameraInventoryAsync(Action<CameraInventoryMessage> onMessageReceived);

        // ── Manager 명령: 서버 → Manager (server.cmd.mgr.{PCId}) ──

        Task PublishManagerCommandAsync(ManagerCommandMessage message);
        Task SubscribeManagerCommandAsync(string pcId, Action<ManagerCommandMessage> onMessageReceived);

        // ── 로그 알림: Manager → 서버 (agent-mgr.log.alert.{PCId}) ──

        Task PublishLogAlertAsync(LogAlertMessage message);
        Task SubscribeLogAlertAsync(Action<LogAlertMessage> onMessageReceived);

        // ── 로그 덤프 요청: 서버 → Manager (server.req.log.{PCId}) ──

        Task PublishLogDumpRequestAsync(LogDumpRequestMessage message);
        Task SubscribeLogDumpRequestAsync(string pcId, Action<LogDumpRequestMessage> onMessageReceived);

        // ── 로그 덤프: Manager → 서버 (agent-mgr.log.dump.{PCId}) ──

        Task PublishLogDumpAsync(LogDumpMessage message);
        Task SubscribeLogDumpAsync(string pcId, Action<LogDumpMessage> onMessageReceived);

        // ── 카메라 제어: Master → Agent (master.cmd.camera.{AgentId}) ──

        Task PublishCameraControlAsync(CameraControlMessage message);
        Task SubscribeCameraControlAsync(string agentId, Action<CameraControlMessage> onMessageReceived);

        // ── 카메라 제어 응답: Agent → Master (agent.ack.camera.{AgentId}) ──

        Task PublishCameraControlAckAsync(CameraControlAckMessage message);
        Task SubscribeCameraControlAckAsync(string agentId, Action<CameraControlAckMessage> onMessageReceived);

        // ── 원본 프레임 온디맨드 조회: Master → Agent (master.req.raw.{AgentId}) / Agent → Master (agent.res.raw.{AgentId}) ──

        Task PublishRawImageRequestAsync(RawImageRequestMessage message);
        Task SubscribeRawImageRequestAsync(string agentId, Action<RawImageRequestMessage> onMessageReceived);
        Task PublishRawImageResponseAsync(RawImageResponseMessage message);
        Task SubscribeRawImageResponseAsync(string agentId, Action<RawImageResponseMessage> onMessageReceived);
    }
}
