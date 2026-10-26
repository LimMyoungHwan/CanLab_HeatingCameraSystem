using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>구독 준비를 기다리고 화면·실행 수명이 끝나면 구독을 해제한다.</summary>
    public static class NatsSubscriptionExtensions
    {
        public static Task<IAsyncDisposable> SubscribeCaptureCommandScopedAsync(
            this INatsCommunicationService service, string agentId, Action<CaptureCommandMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"master.cmd.capture.{agentId}", "master.cmd.capture.all" }, onMessageReceived,
                callback => service.SubscribeCaptureCommandAsync(agentId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeAgentStatusScopedAsync(
            this INatsCommunicationService service, Action<AgentStatusMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { "agent.status.>" }, onMessageReceived,
                callback => service.SubscribeAgentStatusAsync(callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeCaptureResultScopedAsync(
            this INatsCommunicationService service, Action<CaptureResultMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { "agent.result.capture.>" }, onMessageReceived,
                callback => service.SubscribeCaptureResultAsync(callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeLiveFrameScopedAsync(
            this INatsCommunicationService service, Action<LiveFrameMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { "agent.live.>" }, onMessageReceived,
                callback => service.SubscribeLiveFrameAsync(callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeSerialConfigScopedAsync(
            this INatsCommunicationService service, string agentId, Action<SerialConfigMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"master.config.serial.{agentId}" }, onMessageReceived,
                callback => service.SubscribeSerialConfigAsync(agentId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeSerialConfigAckScopedAsync(
            this INatsCommunicationService service, string agentId, Action<SerialConfigAckMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"agent.config.serial.ack.{agentId}" }, onMessageReceived,
                callback => service.SubscribeSerialConfigAckAsync(agentId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeAgentConfigRequestScopedAsync(
            this INatsCommunicationService service, string agentId, Action<AgentConfigRequestMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"master.config.agent.get.{agentId}" }, onMessageReceived,
                callback => service.SubscribeAgentConfigRequestAsync(agentId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeAgentConfigSnapshotScopedAsync(
            this INatsCommunicationService service, string agentId, Action<AgentConfigSnapshotMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"agent.config.agent.snapshot.{agentId}" }, onMessageReceived,
                callback => service.SubscribeAgentConfigSnapshotAsync(agentId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeAgentConfigApplyScopedAsync(
            this INatsCommunicationService service, string agentId, Action<AgentConfigApplyMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"master.config.agent.set.{agentId}" }, onMessageReceived,
                callback => service.SubscribeAgentConfigApplyAsync(agentId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeAgentConfigAckScopedAsync(
            this INatsCommunicationService service, string agentId, Action<AgentConfigAckMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"agent.config.agent.ack.{agentId}" }, onMessageReceived,
                callback => service.SubscribeAgentConfigAckAsync(agentId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeCameraControlScopedAsync(
            this INatsCommunicationService service, string agentId, Action<CameraControlMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"master.cmd.camera.{agentId}" }, onMessageReceived,
                callback => service.SubscribeCameraControlAsync(agentId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeCameraControlAckScopedAsync(
            this INatsCommunicationService service, string agentId, Action<CameraControlAckMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"agent.ack.camera.{agentId}" }, onMessageReceived,
                callback => service.SubscribeCameraControlAckAsync(agentId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeRawImageRequestScopedAsync(
            this INatsCommunicationService service, string agentId, Action<RawImageRequestMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"master.req.raw.{agentId}" }, onMessageReceived,
                callback => service.SubscribeRawImageRequestAsync(agentId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeRawImageResponseScopedAsync(
            this INatsCommunicationService service, string agentId, Action<RawImageResponseMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"agent.res.raw.{agentId}" }, onMessageReceived,
                callback => service.SubscribeRawImageResponseAsync(agentId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeCameraInventoryScopedAsync(
            this INatsCommunicationService service, Action<CameraInventoryMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { "agent-mgr.inventory.>" }, onMessageReceived,
                callback => service.SubscribeCameraInventoryAsync(callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeManagerCommandScopedAsync(
            this INatsCommunicationService service, string pcId, Action<ManagerCommandMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"server.cmd.mgr.{pcId}" }, onMessageReceived,
                callback => service.SubscribeManagerCommandAsync(pcId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeLogAlertScopedAsync(
            this INatsCommunicationService service, Action<LogAlertMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { "agent-mgr.log.alert.>" }, onMessageReceived,
                callback => service.SubscribeLogAlertAsync(callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeLogDumpRequestScopedAsync(
            this INatsCommunicationService service, string pcId, Action<LogDumpRequestMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"server.req.log.{pcId}" }, onMessageReceived,
                callback => service.SubscribeLogDumpRequestAsync(pcId, callback), cancellationToken);

        public static Task<IAsyncDisposable> SubscribeLogDumpScopedAsync(
            this INatsCommunicationService service, string pcId, Action<LogDumpMessage> onMessageReceived, CancellationToken cancellationToken = default)
            => SubscribeScopedAsync(service, new[] { $"agent-mgr.log.dump.{pcId}" }, onMessageReceived,
                callback => service.SubscribeLogDumpAsync(pcId, callback), cancellationToken);

        private static async Task<IAsyncDisposable> SubscribeScopedAsync<T>(
            INatsCommunicationService service, string[] subjects, Action<T> callback,
            Func<Action<T>, Task> legacySubscribe, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(service);
            ArgumentNullException.ThrowIfNull(callback);
            ct.ThrowIfCancellationRequested();
            if (service is INatsScopedSubscriptions scoped)
            {
                var leases = new List<IAsyncDisposable>();
                try
                {
                    foreach (string subject in subjects.Distinct(StringComparer.Ordinal))
                        leases.Add(await scoped.SubscribeAsync(subject, callback, ct).ConfigureAwait(false));
                    ct.ThrowIfCancellationRequested();
                    return new SubscriptionGroup(leases);
                }
                catch
                {
                    foreach (var lease in leases)
                        await lease.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            // 이전 구현/테스트 더블에는 물리적 UNSUB가 없다. 콜백 참조를 끊어 화면과 실행을
            // 보유하거나 종료된 실행에 늦은 메시지를 전달하지 않도록 한다.
            var detached = new DetachedCallback<T>(callback, ct);
            try
            {
                await legacySubscribe(detached.Deliver).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return detached;
            }
            catch
            {
                await detached.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private sealed class SubscriptionGroup : IAsyncDisposable
        {
            private List<IAsyncDisposable>? _leases;
            public SubscriptionGroup(List<IAsyncDisposable> leases) => _leases = leases;
            public async ValueTask DisposeAsync()
            {
                var leases = Interlocked.Exchange(ref _leases, null);
                if (leases is null) return;
                List<Exception>? errors = null;
                foreach (var lease in leases)
                {
                    try { await lease.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception ex) { (errors ??= new()).Add(ex); }
                }
                if (errors is not null) throw new AggregateException(errors);
            }
        }

        private sealed class DetachedCallback<T> : IAsyncDisposable
        {
            private Action<T>? _callback;
            private readonly CancellationTokenRegistration _registration;
            public DetachedCallback(Action<T> callback, CancellationToken ct)
            {
                _callback = callback;
                _registration = ct.Register(() => Interlocked.Exchange(ref _callback, null));
            }
            public void Deliver(T item) => Volatile.Read(ref _callback)?.Invoke(item);
            public ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref _callback, null);
                _registration.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
