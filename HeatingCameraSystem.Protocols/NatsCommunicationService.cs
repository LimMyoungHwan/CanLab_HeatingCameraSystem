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
    /// <summary>
    /// <see cref="INatsCommunicationService"/>의 NATS.Net 구현. 토픽 문자열은 루트 AGENTS.md의
    /// Master/Agent 계약을 그대로 따른다 — 한쪽만 바꾸면 통신이 조용히 끊긴다.
    /// 구독은 자체 복구 루프(<see cref="RunSubscribeWithRetryAsync"/>)로 돌며 Dispose 시 일괄 취소된다.
    /// 접속 재연결은 NATS.Net이 내부에서 처리한다.
    /// </summary>
    public class NatsCommunicationService : INatsCommunicationService
    {
        private INatsConnection? _connection;
        private readonly NatsOpts _baseOpts;

        // 구독 수명: Dispose 시 취소되어 모든 자체 복구 재시도 루프가 깨끗하게 멈춘다.
        private readonly CancellationTokenSource _subscriptionCts = new();
        private readonly List<Task> _subscriptionTasks = new();
        private readonly object _subscriptionLock = new();

        public NatsCommunicationService()
        {
            // 기본값: 로컬 NATS 서버 + JSON 직렬화
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

        /// <summary>구독 하나를 자체 복구 루프로 백그라운드 실행하고 Dispose 대기 목록에 등록한다.</summary>
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

        // 자체 복구 구독 루프. 열거자가 예외로 죽거나(일시적 NATS 단절) 자연 종료되면(연결 끊김)
        // NATS.Net이 구독을 다시 걸어주지 않으므로, 여기서 백오프를 두고 재구독하며 ct가 취소될 때까지
        // (서비스 Dispose) 계속 전달한다. attempt는 메시지가 하나라도 전달되면 0으로 리셋되어,
        // 한참 뒤의 무관한 순단이 백오프를 누적하지 않고 바닥부터 다시 시작한다.
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

        /// <summary>NatsMsg 껍데기를 벗겨 Data만 흘린다. 역직렬화 결과가 null인 메시지는 버린다.</summary>
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

        public async Task PublishRawImageRequestAsync(RawImageRequestMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"master.req.raw.{message.AgentId}", message);
        }

        public Task SubscribeRawImageRequestAsync(string agentId, Action<RawImageRequestMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"master.req.raw.{agentId}", onMessageReceived);
            return Task.CompletedTask;
        }

        public async Task PublishRawImageResponseAsync(RawImageResponseMessage message)
        {
            CheckConnection();
            await _connection!.PublishAsync($"agent.res.raw.{message.AgentId}", message);
        }

        public Task SubscribeRawImageResponseAsync(string agentId, Action<RawImageResponseMessage> onMessageReceived)
        {
            CheckConnection();
            RunSubscriptionLoop($"agent.res.raw.{agentId}", onMessageReceived);
            return Task.CompletedTask;
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

        /// <summary>구독 루프를 모두 취소하고 최대 3초 대기한 뒤, 성패와 무관하게 연결을 정리한다.</summary>
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
                // 최선 노력: 종료 중 루프가 제한 시간을 넘기거나 잡음 오류를 낼 수 있다.
                // 어느 쪽이든 연결은 반드시 정리해 종료 시 멈추거나 누수되지 않게 한다.
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
