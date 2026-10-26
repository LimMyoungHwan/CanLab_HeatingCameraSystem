using System;
using System.Collections.Generic;
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
    public class NatsCommunicationService : INatsCommunicationService, INatsScopedSubscriptions
    {
        private INatsConnection? _connection;
        private readonly NatsOpts _baseOpts;

        // 구독 수명: Dispose 시 취소되어 모든 자체 복구 재시도 루프가 깨끗하게 멈춘다.
        private readonly CancellationTokenSource _subscriptionCts = new();
        private readonly HashSet<IAsyncDisposable> _subscriptions = new();
        private readonly SemaphoreSlim _connectionGate = new(1, 1);
        private int _disposed;
        private Task? _disposeTask;
        private string? _connectedUrl;
        private readonly object _subscriptionLock = new();

        public NatsCommunicationService()
        {
            // 기본값: 로컬 NATS 서버 + JSON 직렬화
            _baseOpts = NatsOpts.Default with { SerializerRegistry = NatsJsonSerializerRegistry.Default };
        }

        internal NatsCommunicationService(INatsConnection connection) : this()
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        internal int ActiveSubscriptionCount
        {
            get { lock (_subscriptionLock) return _subscriptions.Count; }
        }

        public async Task ConnectAsync(string natsUrl = "nats://127.0.0.1:4222")
        {
            await _connectionGate.WaitAsync(_subscriptionCts.Token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                if (_connection is not null)
                {
                    if (_connectedUrl is not null && !string.Equals(_connectedUrl, natsUrl, StringComparison.Ordinal))
                        throw new InvalidOperationException("NATS 주소 변경은 서비스 재시작이 필요합니다.");
                    return;
                }
                var candidate = new NatsConnection(_baseOpts with { Url = natsUrl });
                try
                {
                    await candidate.ConnectAsync().ConfigureAwait(false);
                    _subscriptionCts.Token.ThrowIfCancellationRequested();
                    _connection = candidate;
                    _connectedUrl = natsUrl;
                }
                catch
                {
                    await candidate.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            finally { _connectionGate.Release(); }
        }

        public async Task PublishCaptureCommandAsync(CaptureCommandMessage message)
        {
            string subject = $"master.cmd.capture.{message.TargetAgentId}";
            await GetConnection().PublishAsync(subject, message);
        }

        public async Task SubscribeCaptureCommandAsync(string agentId, Action<CaptureCommandMessage> onMessageReceived)
        {
            await this.SubscribeCaptureCommandScopedAsync(agentId, onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishAgentStatusAsync(AgentStatusMessage message)
        {
            string subject = $"agent.status.{message.AgentId}";
            await GetConnection().PublishAsync(subject, message);
        }

        public async Task SubscribeAgentStatusAsync(Action<AgentStatusMessage> onMessageReceived)
        {
            await this.SubscribeAgentStatusScopedAsync(onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishCaptureResultAsync(CaptureResultMessage message)
        {
            string subject = $"agent.result.capture.{message.AgentId}";
            await GetConnection().PublishAsync(subject, message);
        }

        public async Task SubscribeCaptureResultAsync(Action<CaptureResultMessage> onMessageReceived)
        {
            await this.SubscribeCaptureResultScopedAsync(onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishLiveFrameAsync(LiveFrameMessage message)
        {
            await GetConnection().PublishAsync($"agent.live.{message.AgentId}", message);
        }

        public async Task SubscribeLiveFrameAsync(Action<LiveFrameMessage> onMessageReceived)
        {
            await this.SubscribeLiveFrameScopedAsync(onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishSerialConfigAsync(SerialConfigMessage message)
        {
            await GetConnection().PublishAsync($"master.config.serial.{message.AgentId}", message);
        }

        public async Task SubscribeSerialConfigAsync(string agentId, Action<SerialConfigMessage> onMessageReceived)
        {
            await this.SubscribeSerialConfigScopedAsync(agentId, onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishSerialConfigAckAsync(SerialConfigAckMessage message)
        {
            await GetConnection().PublishAsync($"agent.config.serial.ack.{message.AgentId}", message);
        }

        public async Task SubscribeSerialConfigAckAsync(string agentId, Action<SerialConfigAckMessage> onMessageReceived)
        {
            await this.SubscribeSerialConfigAckScopedAsync(agentId, onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishAgentConfigRequestAsync(AgentConfigRequestMessage message)
        {
            await GetConnection().PublishAsync($"master.config.agent.get.{message.AgentId}", message);
        }

        public async Task SubscribeAgentConfigRequestAsync(string agentId, Action<AgentConfigRequestMessage> onMessageReceived)
        {
            await this.SubscribeAgentConfigRequestScopedAsync(agentId, onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishAgentConfigSnapshotAsync(AgentConfigSnapshotMessage message)
        {
            await GetConnection().PublishAsync($"agent.config.agent.snapshot.{message.AgentId}", message);
        }

        public async Task SubscribeAgentConfigSnapshotAsync(string agentId, Action<AgentConfigSnapshotMessage> onMessageReceived)
        {
            await this.SubscribeAgentConfigSnapshotScopedAsync(agentId, onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishAgentConfigApplyAsync(AgentConfigApplyMessage message)
        {
            await GetConnection().PublishAsync($"master.config.agent.set.{message.AgentId}", message);
        }

        public async Task SubscribeAgentConfigApplyAsync(string agentId, Action<AgentConfigApplyMessage> onMessageReceived)
        {
            await this.SubscribeAgentConfigApplyScopedAsync(agentId, onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishAgentConfigAckAsync(AgentConfigAckMessage message)
        {
            await GetConnection().PublishAsync($"agent.config.agent.ack.{message.AgentId}", message);
        }

        public async Task SubscribeAgentConfigAckAsync(string agentId, Action<AgentConfigAckMessage> onMessageReceived)
        {
            await this.SubscribeAgentConfigAckScopedAsync(agentId, onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishCameraControlAsync(CameraControlMessage message)
        {
            await GetConnection().PublishAsync($"master.cmd.camera.{message.AgentId}", message);
        }

        public async Task SubscribeCameraControlAsync(string agentId, Action<CameraControlMessage> onMessageReceived)
        {
            await this.SubscribeCameraControlScopedAsync(agentId, onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishCameraControlAckAsync(CameraControlAckMessage message)
        {
            await GetConnection().PublishAsync($"agent.ack.camera.{message.AgentId}", message);
        }

        public async Task SubscribeCameraControlAckAsync(string agentId, Action<CameraControlAckMessage> onMessageReceived)
        {
            await this.SubscribeCameraControlAckScopedAsync(agentId, onMessageReceived).ConfigureAwait(false);
        }

        /// <summary>SUB와 PONG 확인까지 기다려 첫 ACK가 구독 시작 전에 유실되지 않게 한다.</summary>
        public async Task<IAsyncDisposable> SubscribeAsync<T>(string subject, Action<T> onMessageReceived,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(onMessageReceived);
            cancellationToken.ThrowIfCancellationRequested();
            INatsConnection connection = GetConnection();
            Subscription<T> subscription;
            lock (_subscriptionLock)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                subscription = new Subscription<T>(connection, subject, onMessageReceived,
                    _subscriptionCts.Token, cancellationToken, RemoveSubscription);
                _subscriptions.Add(subscription);
                subscription.Start();
            }
            try
            {
                await subscription.Ready.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                return subscription;
            }
            catch
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private void RemoveSubscription(IAsyncDisposable subscription)
        {
            lock (_subscriptionLock) _subscriptions.Remove(subscription);
        }

        private sealed class Subscription<T> : IAsyncDisposable
        {
            private readonly INatsConnection _connection;
            private readonly string _subject;
            private Action<T>? _callback;
            private readonly CancellationTokenSource _cts;
            private readonly Action<IAsyncDisposable> _removed;
            private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private Task _loop = Task.CompletedTask;
            private int _disposed;

            public Subscription(INatsConnection connection, string subject, Action<T> callback,
                CancellationToken lifetime, CancellationToken caller, Action<IAsyncDisposable> removed)
            {
                _connection = connection;
                _subject = subject;
                _callback = callback;
                _cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime, caller);
                _removed = removed;
            }
            public Task Ready => _ready.Task;
            public void Start() => _loop = RunAsync();

            private async Task RunAsync()
            {
                CancellationToken ct = _cts.Token;
                int attempt = 0;
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            await using var sub = await _connection.SubscribeCoreAsync<T>(
                                _subject, cancellationToken: ct).ConfigureAwait(false);
                            // 서버가 SUB 다음의 PING을 처리한 뒤 반환한다. 단순 Task.Run/로컬 enqueue는
                            // 다른 연결에서 즉시 오는 응답의 준비 완료 증거가 아니다.
                            await _connection.PingAsync(ct).ConfigureAwait(false);
                            ct.ThrowIfCancellationRequested();
                            _ready.TrySetResult();
                            await foreach (var msg in sub.Msgs.ReadAllAsync(ct).ConfigureAwait(false))
                            {
                                if (ct.IsCancellationRequested) break;
                                if (msg.Data is not T item) continue;
                                try { Volatile.Read(ref _callback)?.Invoke(item); }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[NATS] callback {_subject}: {ex}");
                                }
                                attempt = 0;
                            }
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                        catch (Exception ex)
                        {
                            if (!_ready.Task.IsCompleted)
                            {
                                _ready.TrySetException(ex);
                                return;
                            }
                            System.Diagnostics.Debug.WriteLine($"[NATS] resubscribe {_subject}: {ex}");
                        }
                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(500 * (1 << Math.Min(attempt, 4)), 8000)), ct).ConfigureAwait(false);
                        attempt++;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                finally
                {
                    _ready.TrySetCanceled(ct.IsCancellationRequested ? ct : new CancellationToken(true));
                    Interlocked.Exchange(ref _callback, null);
                    _removed(this);
                    _cts.Dispose();
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    Interlocked.Exchange(ref _callback, null);
                    try { _cts.Cancel(); } catch (ObjectDisposedException) { }
                }
                try { await _loop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                catch (TimeoutException) { /* 연결 종료 경로에서도 취소·참조 해제는 이미 완료됐다. */ }
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

        private INatsConnection GetConnection()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _connection ?? throw new InvalidOperationException("NATS connection is not initialized. Call ConnectAsync first.");
        }

        public async Task PublishRawImageRequestAsync(RawImageRequestMessage message)
        {
            await GetConnection().PublishAsync($"master.req.raw.{message.AgentId}", message);
        }

        public async Task SubscribeRawImageRequestAsync(string agentId, Action<RawImageRequestMessage> onMessageReceived)
        {
            await this.SubscribeRawImageRequestScopedAsync(agentId, onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishRawImageResponseAsync(RawImageResponseMessage message)
        {
            await GetConnection().PublishAsync($"agent.res.raw.{message.AgentId}", message);
        }

        public async Task SubscribeRawImageResponseAsync(string agentId, Action<RawImageResponseMessage> onMessageReceived)
        {
            await this.SubscribeRawImageResponseScopedAsync(agentId, onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishCameraInventoryAsync(CameraInventoryMessage message)
        {
            await GetConnection().PublishAsync($"agent-mgr.inventory.{message.PCId}", message);
        }

        public async Task SubscribeCameraInventoryAsync(Action<CameraInventoryMessage> onMessageReceived)
        {
            await this.SubscribeCameraInventoryScopedAsync(onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishManagerCommandAsync(ManagerCommandMessage message)
        {
            await GetConnection().PublishAsync($"server.cmd.mgr.{message.PCId}", message);
        }

        public async Task SubscribeManagerCommandAsync(string pcId, Action<ManagerCommandMessage> onMessageReceived)
        {
            await this.SubscribeManagerCommandScopedAsync(pcId, onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishLogAlertAsync(LogAlertMessage message)
        {
            await GetConnection().PublishAsync($"agent-mgr.log.alert.{message.PCId}", message);
        }

        public async Task SubscribeLogAlertAsync(Action<LogAlertMessage> onMessageReceived)
        {
            await this.SubscribeLogAlertScopedAsync(onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishLogDumpRequestAsync(LogDumpRequestMessage message)
        {
            await GetConnection().PublishAsync($"server.req.log.{message.PCId}", message);
        }

        public async Task SubscribeLogDumpRequestAsync(string pcId, Action<LogDumpRequestMessage> onMessageReceived)
        {
            await this.SubscribeLogDumpRequestScopedAsync(pcId, onMessageReceived).ConfigureAwait(false);
        }

        public async Task PublishLogDumpAsync(LogDumpMessage message)
        {
            await GetConnection().PublishAsync($"agent-mgr.log.dump.{message.PCId}", message);
        }

        public async Task SubscribeLogDumpAsync(string pcId, Action<LogDumpMessage> onMessageReceived)
        {
            await this.SubscribeLogDumpScopedAsync(pcId, onMessageReceived).ConfigureAwait(false);
        }

        /// <summary>구독 수명을 먼저 종료한 뒤 연결을 해제한다. 중복 Dispose도 같은 완료를 기다린다.</summary>
        public ValueTask DisposeAsync()
        {
            lock (_subscriptionLock)
            {
                if (_disposeTask is not null) return new ValueTask(_disposeTask);
                Volatile.Write(ref _disposed, 1);
                _disposeTask = DisposeCoreAsync();
                return new ValueTask(_disposeTask);
            }
        }

        private async Task DisposeCoreAsync()
        {
            _subscriptionCts.Cancel();
            IAsyncDisposable[] pending;
            lock (_subscriptionLock) pending = _subscriptions.ToArray();
            try
            {
                await Task.WhenAll(pending.Select(item => item.DisposeAsync().AsTask()))
                    .WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NATS] subscription shutdown: {ex.Message}");
            }
            await _connectionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                INatsConnection? connection = _connection;
                _connection = null;
                if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            }
            finally { _connectionGate.Release(); }
            _subscriptionCts.Dispose();
        }
    }
}
