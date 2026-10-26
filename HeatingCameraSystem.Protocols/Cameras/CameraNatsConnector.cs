using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// AgentUI 프로세스의 선택적 NATS 브리지. 로컬 카메라마다 논리적
    /// <see cref="CameraDescriptor.AgentId"/>를 유지하므로 기존 Master 계약은 그대로다:
    /// 카메라별로 <c>master.cmd.capture.{AgentId}</c>(그리고 공유 <c>master.cmd.capture.all</c> —
    /// 프로세스 내 팬아웃)를 구독하고, 라이브 루프를 스냅샷하고(tee — 카메라를 다시 열지 않는다),
    /// 방사 측정 <c>.y16</c>을 로컬에 저장하며, 볼 수 있는 JPG를 실은
    /// <c>agent.result.capture.{AgentId}</c>와 주기적 <c>agent.status.{AgentId}</c> 하트비트를
    /// 발행한다. NATS는 절대 시작 의존성이 아니다: 접속은 백그라운드에서 재시도로 돌고,
    /// NATS가 없어도 로컬 런타임은 동작한다.
    /// </summary>
    public sealed class CameraNatsConnector : IAsyncDisposable
    {
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

        private readonly INatsCommunicationService _nats;
        private readonly CameraRuntimeManager _manager;
        private readonly CaptureStore _store;
        private readonly IReadOnlyList<CameraDescriptor> _cameras;
        private readonly int _heartbeatSeconds;
        private readonly int _captureBurstCount;
        private readonly IReadOnlyDictionary<string, ThermalNucCorrector>? _nucs;
        private readonly Func<AgentConfigSnapshot>? _getConfigSnapshot;
        private readonly Action<AgentConfigSnapshot>? _applyConfigSnapshot;
        private readonly Func<CameraDescriptor, CameraControlMessage, Task<(bool Success, string Message)>>? _cameraControlHandler;
        private readonly Func<CameraDescriptor, bool>? _serialHealth;
        private readonly Func<CameraDescriptor, Task<double?>>? _readCameraTemperature;
        private readonly Func<CameraDescriptor, Task<short?>>? _readFpaRaw;
        private readonly Func<CameraDescriptor, string?>? _readBiasJson;
        private readonly Action<CameraDescriptor, string>? _reportCaptureWarning;
        private readonly Func<CameraDescriptor, AppliedBiasResult?>? _readAppliedBias;
        private readonly ProductionCaptureSink? _productionSink;
        private readonly Func<CameraDescriptor, CameraControlMessage, CancellationToken, Task>? _biasControlHandler;
        private readonly Action<string>? _diagnosticLog;
        private readonly Func<string>? _readLogDirectory;
        private readonly Func<CameraDescriptor, CancellationToken, Task<ICameraOperationLease>>? _acquireCaptureLease;
        private readonly Func<CameraDescriptor, SerialConfigMessage, Task<(bool Success, string Message)>>? _serialConfigHandler;

        private sealed record VerifiedBias(string RequestId, string Json, string FilePath, int AttemptTimeoutSeconds,
            string ApplicationId, string SourceOperation);
        private sealed class ActiveCameraOperation : IDisposable
        {
            public ActiveCameraOperation(string requestId, CancellationToken token)
            {
                RequestId = requestId;
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            }
            public string RequestId { get; }
            public CancellationTokenSource Cancellation { get; }
            public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public void Dispose() => Cancellation.Dispose();
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ActiveCameraOperation> _activeBias = new(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ActiveCameraOperation> _activeCaptures = new(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, VerifiedBias> _verifiedBias = new(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _cameraOperationGates = new(StringComparer.Ordinal);
        private sealed record CaptureOutcome(bool Success, string ImagePath, byte[]? ImageBytes, double? CameraTemperature,
            string FailureCode = "", string Message = "", string ProductionRawPath = "");

        // 전송 지연으로 취소가 원본보다 먼저 도착해도 뒤늦은 작업을 시작하지 않는다.
        // 최대 BIAS 실행 예산(4시간)보다 길게 보관하되 개수와 수명을 모두 제한한다.
        private const int MaximumCancelledRequests = 1024;
        private static readonly TimeSpan CancelledRequestRetention = TimeSpan.FromHours(5);
        private readonly object _cancelledRequestsSync = new();
        private readonly Dictionary<(string AgentId, string Operation, string RequestId), DateTime> _cancelledRequests = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _subscriptionGate = new(1, 1);
        private sealed record CameraSubscription(CameraDescriptor Descriptor, List<IAsyncDisposable> Leases);
        private readonly Dictionary<string, CameraSubscription> _subscriptions = new(StringComparer.Ordinal);
        private readonly object _lifetimeSync = new();
        private readonly HashSet<Task> _inFlight = new();
        private Task? _lifetimeTask;
        private Task? _disposeTask;
        private volatile bool _connected;

        public CameraNatsConnector(
            INatsCommunicationService nats,
            CameraRuntimeManager manager,
            CaptureStore store,
            IReadOnlyList<CameraDescriptor> cameras,
            int heartbeatSeconds = 5,
            IReadOnlyDictionary<string, ThermalNucCorrector>? nucs = null,
            int captureBurstCount = 1,
            Func<AgentConfigSnapshot>? getConfigSnapshot = null,
            Action<AgentConfigSnapshot>? applyConfigSnapshot = null,
            Func<CameraDescriptor, CameraControlMessage, Task<(bool Success, string Message)>>? cameraControlHandler = null,
            Func<CameraDescriptor, bool>? serialHealth = null,
            Func<CameraDescriptor, Task<double?>>? readCameraTemperature = null,
            Func<CameraDescriptor, Task<short?>>? readFpaRaw = null,
            Func<CameraDescriptor, string?>? readBiasJson = null,
            ProductionCaptureSink? productionSink = null,
            Action<CameraDescriptor, string>? reportCaptureWarning = null,
            Func<CameraDescriptor, CameraControlMessage, CancellationToken, Task>? biasControlHandler = null,
            Action<string>? diagnosticLog = null,
            Func<CameraDescriptor, AppliedBiasResult?>? readAppliedBias = null,
            Func<string>? readLogDirectory = null,
            Func<CameraDescriptor, SerialConfigMessage, Task<(bool Success, string Message)>>? serialConfigHandler = null,
            Func<CameraDescriptor, CancellationToken, Task<ICameraOperationLease>>? acquireCaptureLease = null)
        {
            _nats = nats ?? throw new ArgumentNullException(nameof(nats));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
            _heartbeatSeconds = heartbeatSeconds > 0 ? heartbeatSeconds : 5;
            _captureBurstCount = captureBurstCount > 0 ? captureBurstCount : 1;
            _nucs = nucs;
            _getConfigSnapshot = getConfigSnapshot;
            _applyConfigSnapshot = applyConfigSnapshot;
            _cameraControlHandler = cameraControlHandler;
            _serialHealth = serialHealth;
            _readCameraTemperature = readCameraTemperature;
            _readFpaRaw = readFpaRaw;
            _readBiasJson = readBiasJson;
            _reportCaptureWarning = reportCaptureWarning;
            _readAppliedBias = readAppliedBias;
            _productionSink = productionSink;
            _biasControlHandler = biasControlHandler;
            _diagnosticLog = diagnosticLog;
            _readLogDirectory = readLogDirectory;
            _serialConfigHandler = serialConfigHandler;
            _acquireCaptureLease = acquireCaptureLease;
        }

        public bool IsConnected => _connected;

        /// <summary>백그라운드에서 접속 재시도를 시작한다. 접속에 실패해도 호출자를 막지 않는다.</summary>
        public void Start(string natsUrl)
        {
            lock (_lifetimeSync)
            {
                if (_lifetimeTask is not null || _cts.IsCancellationRequested) return;
                _lifetimeTask = Task.Run(() => ConnectWithRetryAsync(natsUrl, _cts.Token));
            }
        }

        private CameraDescriptor[] CameraSnapshot()
        {
            lock (_cameras) return _cameras.ToArray();
        }

        private CameraDescriptor ResolveCurrentDescriptor(string agentId)
        {
            CameraDescriptor[] matches = CameraSnapshot().Where(cam =>
                string.Equals(cam.AgentId, agentId, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"카메라 대상이 없거나 중복되었습니다: {agentId}");
            return matches[0];
        }

        private static bool SameBinding(CameraDescriptor left, CameraDescriptor right)
            => left.OpenCvIndex == right.OpenCvIndex
                && string.Equals(left.SerialPortName, right.SerialPortName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.CameraSerialNumber, right.CameraSerialNumber, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.UsbContainerId, right.UsbContainerId, StringComparison.OrdinalIgnoreCase);

        private void EnsureBindingCurrent(CameraDescriptor expected, ICameraRuntime? expectedRuntime = null)
        {
            CameraDescriptor current = ResolveCurrentDescriptor(expected.AgentId);
            if (current.OpenCvIndex < 0 || !SameBinding(expected, current))
                throw new InvalidOperationException("카메라 연결 대상이 변경되었거나 오프라인입니다. 현재 장치를 다시 확인해 주세요.");
            if (expectedRuntime is not null && (!_manager.TryGet(expected.AgentId, out var currentRuntime)
                    || !ReferenceEquals(expectedRuntime, currentRuntime)))
                throw new InvalidOperationException("촬영 중 카메라 런타임이 교체되었습니다.");
        }

        private void CancelForBindingChange(string agentId)
        {
            _verifiedBias.TryRemove(agentId, out _);
            if (_activeBias.TryGetValue(agentId, out var bias))
                try { bias.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
            if (_activeCaptures.TryGetValue(agentId, out var capture))
                try { capture.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }

        private void Track(Func<Task> work)
        {
            Task task;
            lock (_lifetimeSync)
            {
                if (_cts.IsCancellationRequested) return;
                task = Task.Run(work);
                _inFlight.Add(task);
            }
            _ = ObserveAsync(task);
        }

        private async Task ObserveAsync(Task task)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
            catch (Exception ex) { LogBias($"[CameraNats] background operation failed: {ex}"); }
            finally { lock (_lifetimeSync) _inFlight.Remove(task); }
        }

        private async Task ConnectWithRetryAsync(string natsUrl, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _nats.ConnectAsync(natsUrl).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    _connected = true;
                    await SyncSubscriptionsAsync().ConfigureAwait(false);
                    await Task.WhenAll(HeartbeatLoopAsync(ct), LiveStreamLoopAsync(ct)).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    LogBias($"[CameraNats] connection/startup failed, retrying: {ex}");
                    try { await Task.Delay(ReconnectDelay, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(_heartbeatSeconds), ct).ConfigureAwait(false);
                // 부분 구독 실패도 다음 주기에 다시 채운다. 완료되지 않은 구독은 먼저 전부 정리한다.
                await SyncSubscriptionsAsync().ConfigureAwait(false);
            }
        }

        /// <summary>구독을 현재 AgentId 목록에 맞추고 변경된 물리 바인딩의 작업·BIAS 증거를 무효화한다.</summary>
        public async Task SyncSubscriptionsAsync()
        {
            if (!_connected || _cts.IsCancellationRequested) return;
            try { await _subscriptionGate.WaitAsync(_cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            try
            {
                CameraDescriptor[] snapshot = CameraSnapshot();
                var current = snapshot.GroupBy(cam => cam.AgentId, StringComparer.Ordinal)
                    .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
                foreach (string removed in _subscriptions.Keys.Where(id => !current.ContainsKey(id)).ToArray())
                {
                    CancelForBindingChange(removed);
                    CameraSubscription old = _subscriptions[removed];
                    _subscriptions.Remove(removed);
                    await DisposeLeasesAsync(old.Leases).ConfigureAwait(false);
                }
                foreach (CameraDescriptor descriptor in current.Values)
                {
                    if (_subscriptions.TryGetValue(descriptor.AgentId, out var subscribed))
                    {
                        if (!SameBinding(subscribed.Descriptor, descriptor))
                        {
                            CancelForBindingChange(descriptor.AgentId);
                            _subscriptions[descriptor.AgentId] = subscribed with { Descriptor = descriptor };
                            LogBias($"[CameraNats] camera binding changed agent={descriptor.AgentId} index={descriptor.OpenCvIndex}; BIAS invalidated");
                        }
                        continue;
                    }
                    List<IAsyncDisposable>? leases = await SubscribeCameraAsync(descriptor.AgentId).ConfigureAwait(false);
                    if (leases is not null) _subscriptions.Add(descriptor.AgentId, new(descriptor, leases));
                }
            }
            finally { _subscriptionGate.Release(); }
            await PublishHeartbeatsAsync().ConfigureAwait(false);
        }

        private async Task<List<IAsyncDisposable>?> SubscribeCameraAsync(string agentId)
        {
            var leases = new List<IAsyncDisposable>();
            try
            {
                CancellationToken ct = _cts.Token;
                leases.Add(await _nats.SubscribeCaptureCommandScopedAsync(agentId,
                    cmd => Track(() => HandleCaptureForAgentAsync(agentId, cmd)), ct).ConfigureAwait(false));
                leases.Add(await _nats.SubscribeCameraControlScopedAsync(agentId,
                    msg => Track(() => HandleControlForAgentAsync(agentId, msg)), ct).ConfigureAwait(false));
                leases.Add(await _nats.SubscribeRawImageRequestScopedAsync(agentId,
                    req => { if (req.AgentId == agentId) Track(() => HandleRawImageRequestAsync(req)); }, ct).ConfigureAwait(false));
                if (_getConfigSnapshot is not null)
                    leases.Add(await _nats.SubscribeAgentConfigRequestScopedAsync(agentId,
                        req => { if (req.AgentId == agentId) Track(() => PublishConfigSnapshotAsync(agentId, req.RequestId)); }, ct).ConfigureAwait(false));
                if (_applyConfigSnapshot is not null)
                    leases.Add(await _nats.SubscribeAgentConfigApplyScopedAsync(agentId,
                        msg => { if (msg.AgentId == agentId) Track(() => ApplyConfigAsync(msg)); }, ct).ConfigureAwait(false));
                if (_serialConfigHandler is not null)
                    leases.Add(await _nats.SubscribeSerialConfigScopedAsync(agentId,
                        msg => { if (msg.AgentId == agentId) Track(() => HandleSerialConfigAsync(agentId, msg)); }, ct).ConfigureAwait(false));
                return leases;
            }
            catch (Exception ex)
            {
                await DisposeLeasesAsync(leases).ConfigureAwait(false);
                LogBias($"[CameraNats] subscription setup failed agent={agentId}: {ex}");
                return null;
            }
        }

        private static async Task DisposeLeasesAsync(IEnumerable<IAsyncDisposable> leases)
        {
            foreach (var lease in leases)
            {
                try { await lease.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { Debug.WriteLine($"[CameraNats] unsubscribe failed: {ex}"); }
            }
        }

        private Task HandleCaptureForAgentAsync(string agentId, CaptureCommandMessage cmd)
        {
            // 콜백에는 초기 descriptor를 가두지 않는다. 재연결 후의 현재 라우팅을 사용한다.
            CameraDescriptor? current = CameraSnapshot().SingleOrDefault(cam => cam.AgentId == agentId);
            return current is null ? Task.CompletedTask : HandleCaptureAsync(current, cmd);
        }

        private Task HandleControlForAgentAsync(string agentId, CameraControlMessage msg)
        {
            CameraDescriptor? current = CameraSnapshot().SingleOrDefault(cam => cam.AgentId == agentId);
            return current is null ? Task.CompletedTask : HandleCameraControlAsync(current, msg);
        }

        /// <summary>
        /// 캡처 명령 처리: 라이브 루프를 스냅샷해 NUC 보정 후 저장하고 결과를 발행한다.
        /// 장수는 명령의 <see cref="CaptureCommandMessage.ShotCount"/>가 우선이고 없으면 로컬 설정을 쓴다.
        /// Master가 장수를 세어 스텝 완료를 판정하므로 실패한 장도 <c>IsSuccess=false</c>로 반드시 1건 발행한다.
        /// </summary>
        public async Task HandleCaptureAsync(CameraDescriptor descriptor, CaptureCommandMessage cmd)
        {
            try
            {
                if (cmd.TargetAgentId != "all" && !string.Equals(cmd.TargetAgentId, descriptor.AgentId, StringComparison.Ordinal))
                    throw new InvalidOperationException("촬영 명령의 대상 AgentId가 일치하지 않습니다.");
                descriptor = ResolveCurrentDescriptor(descriptor.AgentId);
                EnsureBindingCurrent(descriptor);
                _cts.Token.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                await PublishCaptureResultAsync(descriptor, cmd, false, string.Empty, null, null,
                    RequiresBiasFile(cmd) ? BiasExecutionPolicy.FileFailureCode : "cameraBindingChanged", $"NG: {ex.Message}").ConfigureAwait(false);
                return;
            }
            SemaphoreSlim gate = _cameraOperationGates.GetOrAdd(descriptor.AgentId, _ => new SemaphoreSlim(1, 1));
            using var active = new ActiveCameraOperation(cmd.RecipeStepId, _cts.Token);
            if (!_activeCaptures.TryAdd(descriptor.AgentId, active))
            {
                await PublishCaptureResultAsync(descriptor, cmd, false, string.Empty, null, null,
                    RequiresBiasFile(cmd) ? BiasExecutionPolicy.FileFailureCode : string.Empty,
                    "NG: 이전 촬영 작업이 종료되지 않았습니다.").ConfigureAwait(false);
                return;
            }

            bool entered = false;
            CaptureOutcome? outcome = null;
            ICameraOperationLease? localLease = null;
            CancellationTokenRegistration bindingCancellation = default;
            try
            {
                if (IsRequestCancelled(descriptor.AgentId, CameraControlOps.Capture, cmd.RecipeStepId))
                    active.Cancellation.Cancel();
                entered = await gate.WaitAsync(TimeSpan.FromSeconds(3), active.Cancellation.Token).ConfigureAwait(false);
                if (!entered)
                    throw new InvalidOperationException("이전 BIAS 또는 촬영 작업이 종료되지 않았습니다.");

                active.Cancellation.Token.ThrowIfCancellationRequested();
                EnsureBindingCurrent(descriptor);
                if (_acquireCaptureLease is not null)
                {
                    localLease = await _acquireCaptureLease(descriptor, active.Cancellation.Token).ConfigureAwait(false);
                    bindingCancellation = localLease.CancellationToken.Register(() =>
                    {
                        try { active.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
                    });
                    active.Cancellation.Token.ThrowIfCancellationRequested();
                }
                outcome = await HandleCaptureCoreAsync(descriptor, cmd, active.Cancellation.Token).ConfigureAwait(false);
                active.Cancellation.Token.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                LogBias($"[CAPTURE] NG agent={descriptor.AgentId} step={cmd.RecipeStepId} reason={ex}");
                try { _reportCaptureWarning?.Invoke(descriptor, $"NG: {ex.Message}"); }
                catch (Exception reportingError)
                {
                    LogBias($"[CAPTURE] NG notification failed agent={descriptor.AgentId} step={cmd.RecipeStepId} reason={reportingError.Message}");
                }
                outcome = new CaptureOutcome(false, string.Empty, null, null,
                    RequiresBiasFile(cmd) ? BiasExecutionPolicy.FileFailureCode : string.Empty,
                    $"NG: {ex.Message}");
            }
            finally
            {
                bindingCancellation.Dispose();
                try
                {
                    if (localLease is not null) await localLease.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    outcome = new CaptureOutcome(false, string.Empty, null, null, Message: $"NG: 카메라 작업 해제 실패: {ex.Message}");
                    LogBias($"[CAPTURE] lease release failed agent={descriptor.AgentId}: {ex}");
                }
                finally
                {
                    _activeCaptures.TryRemove(descriptor.AgentId, out _);
                    if (entered) gate.Release();
                    active.Finished.TrySetResult();
                }
            }
            if (outcome is not null)
                await PublishCaptureResultAsync(descriptor, cmd, outcome.Success, outcome.ImagePath, outcome.ImageBytes,
                    outcome.CameraTemperature, outcome.FailureCode, outcome.Message, outcome.ProductionRawPath).ConfigureAwait(false);
        }

        private static bool RequiresBiasFile(CaptureCommandMessage cmd)
            => cmd.WriteBiasJson || !string.IsNullOrWhiteSpace(cmd.RequiredBiasRequestId);

        private async Task<CaptureOutcome?> HandleCaptureCoreAsync(CameraDescriptor descriptor, CaptureCommandMessage cmd, CancellationToken cancellationToken)
        {
            int shots = cmd.ShotCount > 0 ? cmd.ShotCount : _captureBurstCount;
            if (shots < 1) shots = 1;

            double? cameraTemperature = null;
            if (_manager.TryGet(descriptor.AgentId, out ICameraRuntime runtime) && _readCameraTemperature is not null)
            {
                try { cameraTemperature = await _readCameraTemperature(descriptor).ConfigureAwait(false); }
                catch (Exception ex) { Debug.WriteLine($"[CameraNats] camera info read failed for {descriptor.AgentId}: {ex.Message}"); }
            }

            bool production = _productionSink is not null && ProductionCaptureSink.IsEnabled(cmd);

            // 필수 결과를 먼저 확인한다. JSON 없이 장수만 채워 성공을 보내지 않는다.
            if (RequiresBiasFile(cmd))
                await EnsureBiasForCaptureAsync(descriptor, cmd, production, cancellationToken).ConfigureAwait(false);

            // FPA는 시리얼 왕복이라 장마다 읽으면 촬영이 멈춘다. AISEN 원본처럼 배치당 한 번만 읽어
            // 모든 장의 픽셀(0,0)에 같은 값을 심는다.
            short? fpaRaw = null;
            if (production && _readFpaRaw is not null)
            {
                try { fpaRaw = await _readFpaRaw(descriptor).ConfigureAwait(false); }
                catch (Exception ex) { Debug.WriteLine($"[CameraNats] FPA raw read failed for {descriptor.AgentId}: {ex.Message}"); }
            }

            // 규칙 저장에서는 .raw가 방사 측정 원본이므로 .y16을 장마다 또 쓰지 않는다. 결과 화면용으로
            // 마지막 시도 프레임 한 장만 남기고, 결과도 배치당 1건만 발행한다(장마다 JPEG을 붙이면
            // 100장 x 카메라수 만큼의 미리보기가 Master 메모리에 쌓인다).
            ThermalFrame? representative = null;
            int representativeShotIndex = -1;
            int captured = 0;
            DateTimeOffset? lastFrameTimestamp = null;
            string captureFailure = string.Empty;

            for (int i = 0; i < shots; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Debug.WriteLine($"[CameraNats] capture aborted for {descriptor.AgentId} at shot {i + 1}/{shots}");
                    break;
                }

                EnsureBindingCurrent(descriptor);
                EnsureRequiredBiasIsCurrent(descriptor, cmd);

                bool success = false;
                string imagePath = string.Empty;
                byte[]? bytes = null;

                try
                {
                    if (_manager.TryGet(descriptor.AgentId, out ICameraRuntime shotRuntime))
                    {
                        EnsureBindingCurrent(descriptor, shotRuntime);
                        bool forceFreshFrame = production || i > 0;
                        DateTimeOffset requestedAt = DateTimeOffset.UtcNow;
                        ThermalFrame? snap = await shotRuntime.CaptureSnapshotAsync(
                            maxAge: forceFreshFrame ? TimeSpan.Zero : TimeSpan.FromSeconds(1),
                            nextFrameTimeout: TimeSpan.FromSeconds(2),
                            ct: cancellationToken).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        EnsureBindingCurrent(descriptor, shotRuntime);
                        if (snap is null)
                            throw new InvalidOperationException("카메라에서 새 프레임을 받지 못했습니다.");
                        if ((forceFreshFrame && snap.Timestamp < requestedAt)
                            || (lastFrameTimestamp is not null && snap.Timestamp <= lastFrameTimestamp.Value))
                            throw new InvalidOperationException("이전 프레임이 반환되었습니다. 새 촬영으로 저장할 수 없습니다.");
                        lastFrameTimestamp = snap.Timestamp;

                        if (snap is not null)
                        {
                            ThermalFrame frame = snap;
                            if (_nucs is not null && _nucs.TryGetValue(descriptor.AgentId, out ThermalNucCorrector? nuc) && nuc is not null)
                            {
                                frame = nuc.Apply(frame);
                            }

                            if (production)
                            {
                                // WriteShot보다 먼저 잡는다. Raw 런이 방사 측정 없는 프레임으로 실패해도
                                // 아래에서 .y16과 .json이 남아 pixelFormat으로 원인을 볼 수 있어야 한다.
                                representative = frame;
                                representativeShotIndex = i;

                                // 후처리 툴이 캘리브레이션을 직접 하므로 .raw는 NUC 미보정 원본(snap)이어야 한다.
                                // .jpg는 육안 검사용이라 반대로 NUC 보정 프레임을 쓴다.
                                _productionSink!.WriteShot(
                                    descriptor,
                                    cmd,
                                    cmd.SaveFormat == ProductionCaptureFormat.Jpeg ? frame : snap,
                                    fpaRaw,
                                    i);
                            }
                            else
                            {
                                CaptureRecord record = _store.Save(frame, descriptor.AgentId, descriptor.OpenCvIndex, cmd.RecipeStepId);
                                imagePath = record.Y16Path;
                                bytes = ThermalPreviewEncoder.EncodeJpeg(frame);
                            }

                            captured++;
                            success = true;
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    captureFailure = ex.Message;
                    LogBias($"[CAPTURE] NG agent={descriptor.AgentId} shot={i + 1}/{shots}: {ex.Message}");
                    success = false;
                }

                if (!production) await PublishCaptureResultAsync(descriptor, cmd, success, imagePath, bytes, cameraTemperature,
                    message: success ? string.Empty : captureFailure).ConfigureAwait(false);
                else if (!success) break;
            }
            if (production)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureBindingCurrent(descriptor);
                EnsureRequiredBiasIsCurrent(descriptor, cmd);
                string imagePath = string.Empty;
                byte[]? bytes = null;
                string productionRawPath = string.Empty;
                if (representative is not null)
                {
                    CaptureRecord record = _store.Save(representative, descriptor.AgentId, descriptor.OpenCvIndex, cmd.RecipeStepId);
                    imagePath = record.Y16Path;
                    bytes = ThermalPreviewEncoder.EncodeJpeg(representative);
                    if (cmd.SaveFormat == ProductionCaptureFormat.Raw)
                    {
                        productionRawPath = Path.Combine(
                            cmd.StorageRootUnc,
                            ProductionCaptureSink.RelativeDirectory(descriptor, cmd),
                            CaptureNamingRule.FileName(cmd.FilePrefix, Math.Max(0, cmd.ShotNumberOffset) + representativeShotIndex, ProductionCaptureFormat.Raw));
                    }
                }

                // 같은 카메라의 다음 작업은 파일 전송 루틴까지 종료한 뒤에만 시작할 수 있다.
                // BIAS는 앞선 strict 경로에서 저장·검증했다. 남아 있던 과거 JSON을 일반 전송으로 덮지 않는다.
                await _productionSink!.SyncFolderAsync(descriptor, cmd, cancellationToken, syncBiasJson: false).ConfigureAwait(false);
                EnsureRequiredBiasIsCurrent(descriptor, cmd);
                EnsureBindingCurrent(descriptor);
                return new CaptureOutcome(captured == shots, imagePath, bytes, cameraTemperature,
                    Message: captured == shots ? string.Empty : $"NG: 촬영 {captured}/{shots}. {captureFailure}", ProductionRawPath: productionRawPath);
            }
            return null;
        }

        private async Task EnsureBiasForCaptureAsync(CameraDescriptor cam, CaptureCommandMessage cmd, bool production, CancellationToken ct)
        {
            VerifiedBias? verified = null;
            string? prerequisiteError = null;
            bool isRecipe = cmd.Source == CaptureSource.Recipe || !string.IsNullOrWhiteSpace(cmd.RecipeStepId);
            if (!production)
                prerequisiteError = "BIAS 필수 파일의 생산 저장 경로가 없습니다.";
            else if (!string.IsNullOrWhiteSpace(cmd.RequiredBiasRequestId))
            {
                if (!_verifiedBias.TryGetValue(cam.AgentId, out verified)
                    || !string.Equals(verified.RequestId, cmd.RequiredBiasRequestId, StringComparison.Ordinal))
                    prerequisiteError = "이번 레시피의 BIAS 성공 결과가 없습니다. 보정부터 다시 실행해 주세요.";
                else
                {
                    string expected = Path.GetFullPath(Path.Combine(cmd.StorageRootUnc,
                        ProductionCaptureSink.ConditionRelativeDirectory(cam, cmd), "bias.json"));
                    if (!string.Equals(expected, Path.GetFullPath(verified.FilePath), StringComparison.OrdinalIgnoreCase))
                        prerequisiteError = "BIAS 결과의 센서·제품·온도 조건 또는 저장 경로가 이번 촬영과 다릅니다.";
                }
            }
            else if (isRecipe)
                prerequisiteError = "레시피의 필수 BIAS 요청 식별자가 없습니다. Master와 AgentUI를 함께 업데이트해 주세요.";

            int timeout = verified?.AttemptTimeoutSeconds ?? BiasExecutionPolicy.DefaultAttemptTimeoutSeconds;
            string context = $"agent={cam.AgentId} step={cmd.RecipeStepId} biasRequest={cmd.RequiredBiasRequestId}";
            // 이미 보정한 촬영의 파일 복구는 보정을 다시 하지 않는다. 해당 요청의 불변 결과만 저장한다.
            BiasOperationResult result = await BiasOperationRunner.RunAsync(
                _ => prerequisiteError is null ? Task.CompletedTask : Task.FromException(new InvalidOperationException(prerequisiteError)),
                () => verified is not null ? ReadCurrentVerifiedBiasJson(cam, verified) : (isRecipe ? null : _readBiasJson?.Invoke(cam)),
                async (json, token) =>
                {
                    if (verified is not null)
                    {
                        ReadCurrentVerifiedBiasJson(cam, verified);
                        bool matches = false;
                        try
                        {
                            matches = string.Equals(await File.ReadAllTextAsync(verified.FilePath, token).ConfigureAwait(false), json, StringComparison.Ordinal);
                            if (!matches)
                                LogBias($"[BIAS FILE] REWRITE {context} path={verified.FilePath} reason=내용 불일치");
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { LogBias($"[BIAS FILE] REWRITE {context} path={verified.FilePath} reason={ex.Message}"); }
                        ReadCurrentVerifiedBiasJson(cam, verified);
                        if (matches)
                        {
                            LogBias($"[BIAS FILE] VERIFIED {context} path={verified.FilePath}");
                            return verified.FilePath;
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    if (_productionSink is null)
                        throw new InvalidOperationException("생산 저장 처리가 연결되지 않았습니다.");
                    string path = await _productionSink.WriteAndVerifyBiasJsonAsync(cam, cmd, json, token).ConfigureAwait(false);
                    if (verified is not null) ReadCurrentVerifiedBiasJson(cam, verified);
                    return path;
                },
                true, timeout, context, LogBias, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.Message);
        }

        private string ReadCurrentVerifiedBiasJson(CameraDescriptor cam, VerifiedBias verified)
        {
            if (!_verifiedBias.TryGetValue(cam.AgentId, out VerifiedBias? current) || !ReferenceEquals(current, verified))
                throw new InvalidOperationException("이번 레시피의 BIAS 결과가 무효화되었습니다. 보정부터 다시 실행해 주세요.");
            try
            {
                if (!string.IsNullOrWhiteSpace(verified.ApplicationId))
                {
                    AppliedBiasResult applied = ReadCurrentAppliedBias(cam, verified.SourceOperation);
                    EnsureSameBiasApplication(applied, verified.ApplicationId, verified.SourceOperation, verified.Json);
                }
                else EnsureBiasJsonMatchesCurrent(cam, verified.Json);
            }
            catch
            {
                _verifiedBias.TryRemove(cam.AgentId, out _);
                throw;
            }
            return verified.Json;
        }

        private void EnsureBiasJsonMatchesCurrent(CameraDescriptor cam, string json)
        {
            // 수동 BIAS 또는 카메라 재연결은 패널 결과를 변경/초기화한다. 디스크의 이전 JSON만으로
            // 통과시키면 실제 카메라 설정과 다른 보정 결과로 후속 촬영을 진행하게 된다.
            if (!string.Equals(_readBiasJson?.Invoke(cam), json, StringComparison.Ordinal))
                throw new InvalidOperationException("카메라의 현재 BIAS 결과가 이번 보정 결과와 다릅니다. 수동 보정 또는 재연결 후 보정을 다시 실행해 주세요.");
        }

        private AppliedBiasResult ReadCurrentAppliedBias(CameraDescriptor cam, string sourceOperation)
        {
            if (!BiasExecutionPolicy.IsBiasOperation(sourceOperation))
                throw new InvalidOperationException("현재 BIAS를 사용할 대역이 올바르지 않습니다.");
            AppliedBiasResult applied = _readAppliedBias?.Invoke(cam)
                ?? throw new InvalidOperationException("현재 카메라에 적용하고 확인한 BIAS가 없습니다. BIAS를 먼저 적용해 주세요.");
            if (string.IsNullOrWhiteSpace(applied.ApplicationId) || string.IsNullOrWhiteSpace(applied.Json))
                throw new InvalidOperationException("현재 BIAS의 적용 식별자 또는 결과가 없습니다.");
            if (!string.Equals(applied.Operation, sourceOperation, StringComparison.Ordinal))
                throw new InvalidOperationException($"현재 BIAS 대역({applied.Operation})이 요청 대역({sourceOperation})과 다릅니다.");

            EnsureBiasJsonMatchesCurrent(cam, applied.Json);
            // 패널 조회와 JSON 조회 사이에 수동 적용이 시작/완료된 경우도 이전 세대로 통과시키지 않는다.
            AppliedBiasResult confirmed = _readAppliedBias?.Invoke(cam)
                ?? throw new InvalidOperationException("BIAS 확인 중 현재 적용 상태가 초기화되었습니다.");
            EnsureSameBiasApplication(confirmed, applied.ApplicationId, applied.Operation, applied.Json);
            return applied;
        }

        private static void EnsureSameBiasApplication(AppliedBiasResult applied, string applicationId, string sourceOperation, string json)
        {
            if (!string.Equals(applied.ApplicationId, applicationId, StringComparison.Ordinal)
                || !string.Equals(applied.Operation, sourceOperation, StringComparison.Ordinal)
                || !string.Equals(applied.Json, json, StringComparison.Ordinal))
                throw new InvalidOperationException("BIAS 적용 세대가 변경되었습니다. 현재 적용값 확인부터 다시 실행해 주세요.");
        }

        private void EnsureBiasApplicationMatchesCurrent(CameraDescriptor cam, AppliedBiasResult? applied, string json)
        {
            if (applied is null)
            {
                if (_readAppliedBias is not null)
                    throw new InvalidOperationException("이번 BIAS 처리의 적용 상태를 확인하지 못했습니다.");
                EnsureBiasJsonMatchesCurrent(cam, json); // readBiasJson만 제공하는 기존 자동 연결의 호환 경로
                return;
            }
            AppliedBiasResult current = ReadCurrentAppliedBias(cam, applied.Operation);
            EnsureSameBiasApplication(current, applied.ApplicationId, applied.Operation, json);
        }

        private void EnsureRequiredBiasIsCurrent(CameraDescriptor cam, CaptureCommandMessage cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd.RequiredBiasRequestId)) return;
            if (!_verifiedBias.TryGetValue(cam.AgentId, out VerifiedBias? verified)
                || !string.Equals(verified.RequestId, cmd.RequiredBiasRequestId, StringComparison.Ordinal))
                throw new InvalidOperationException("이번 레시피의 BIAS 성공 결과가 없습니다. 보정부터 다시 실행해 주세요.");
            ReadCurrentVerifiedBiasJson(cam, verified);
        }

        private async Task PublishCaptureResultAsync(
            CameraDescriptor descriptor,
            CaptureCommandMessage cmd,
            bool success,
            string imagePath,
            byte[]? bytes,
            double? cameraTemperature,
            string failureCode = "",
            string message = "",
            string productionRawPath = "")
        {
            try
            {
                await _nats.PublishCaptureResultAsync(new CaptureResultMessage
                {
                    AgentId = descriptor.AgentId,
                    Alias = descriptor.Alias,
                    CameraIndex = descriptor.OpenCvIndex,
                    RecipeStepId = cmd.RecipeStepId,
                    Source = cmd.Source != CaptureSource.Unknown
                        ? cmd.Source
                        : (string.IsNullOrEmpty(cmd.RecipeStepId) ? CaptureSource.Manual : CaptureSource.Recipe),
                    CaptureId = Guid.NewGuid().ToString(),
                    IsSuccess = success,
                    FailureCode = failureCode,
                    Message = message,
                    ImagePath = imagePath,
                    ProductionRawPath = productionRawPath,
                    ImageBytes = bytes,
                    Timestamp = DateTime.UtcNow,
                    CameraTemperature = cameraTemperature
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] publish result failed for {descriptor.AgentId}: {ex.Message}");
            }
        }

        private void RememberCancelledRequest(string agentId, string operation, string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId)) return;
            DateTime now = DateTime.UtcNow;
            var key = (agentId, operation, requestId);
            lock (_cancelledRequestsSync)
            {
                foreach (var expired in _cancelledRequests.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                    _cancelledRequests.Remove(expired);

                if (!_cancelledRequests.ContainsKey(key) && _cancelledRequests.Count >= MaximumCancelledRequests)
                    _cancelledRequests.Remove(_cancelledRequests.OrderBy(pair => pair.Value).First().Key);
                _cancelledRequests[key] = now + CancelledRequestRetention;
            }
        }

        private bool IsRequestCancelled(string agentId, string operation, string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId)) return false;
            var key = (agentId, operation, requestId);
            lock (_cancelledRequestsSync)
            {
                if (!_cancelledRequests.TryGetValue(key, out DateTime expires)) return false;
                if (expires > DateTime.UtcNow) return true;
                _cancelledRequests.Remove(key);
                return false;
            }
        }

        /// <summary>요청을 특정한 취소는 그 요청만 끝낸다. 빈 요청은 기존 수동 캡처 중단에만 허용한다.</summary>
        private async Task<bool> AbortCameraOperationAsync(
            System.Collections.Concurrent.ConcurrentDictionary<string, ActiveCameraOperation> operations,
            string agentId, string operation, string targetRequestId, bool allowCurrentWithoutTarget)
        {
            // 먼저 기록하고 그 뒤 현재 작업을 읽는다. 등록 직전·직후 어느 쪽으로 경합해도
            // 작업의 시작 검사 또는 아래 Cancel 중 하나가 반드시 해당 요청을 취소한다.
            RememberCancelledRequest(agentId, operation, targetRequestId);
            if (!operations.TryGetValue(agentId, out ActiveCameraOperation? active)) return false;
            bool matches = (!string.IsNullOrWhiteSpace(targetRequestId)
                    && string.Equals(active.RequestId, targetRequestId, StringComparison.Ordinal))
                || (allowCurrentWithoutTarget && string.IsNullOrWhiteSpace(targetRequestId));
            if (!matches) return false;

            RememberCancelledRequest(agentId, operation, active.RequestId);
            try { active.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
            await active.Finished.Task.ConfigureAwait(false);
            return true;
        }

        /// <summary>카메라 제어 명령을 주입된 핸들러에 위임하고 성패를 ACK로 발행한다.</summary>
        public async Task HandleCameraControlAsync(CameraDescriptor cam, CameraControlMessage msg)
        {
            bool abort = msg.Op == CameraControlOps.BiasAbort || msg.Op == CameraControlOps.CaptureAbort;
            try
            {
                if (!string.Equals(msg.AgentId, cam.AgentId, StringComparison.Ordinal))
                    throw new InvalidOperationException("제어 명령의 대상 AgentId가 일치하지 않습니다.");
                cam = ResolveCurrentDescriptor(cam.AgentId);
                if (!abort)
                {
                    EnsureBindingCurrent(cam);
                    if (msg.CameraIndex != cam.OpenCvIndex)
                        throw new InvalidOperationException($"카메라 인덱스가 변경되었습니다: 요청={msg.CameraIndex}, 현재={cam.OpenCvIndex}");
                    _cts.Token.ThrowIfCancellationRequested();
                }
            }
            catch (Exception ex)
            {
                await PublishControlAckAsync(cam, msg, false, $"NG: {ex.Message}").ConfigureAwait(false);
                return;
            }
            if (msg.Op == CameraControlOps.BiasAbort)
            {
                bool matched = await AbortCameraOperationAsync(_activeBias, cam.AgentId, CameraControlOps.Bias,
                    msg.TargetRequestId, allowCurrentWithoutTarget: false).ConfigureAwait(false);
                await PublishControlAckAsync(cam, msg, true, matched ? "BIAS 작업 종료 확인" : "해당 BIAS 작업이 실행 중이지 않습니다.").ConfigureAwait(false);
                return;
            }

            if (BiasExecutionPolicy.IsBiasOperation(msg.Op) || msg.Op == CameraControlOps.BiasUseCurrent)
            {
                await HandleBiasAsync(cam, msg).ConfigureAwait(false);
                return;
            }

            bool success = false;
            string message = "control handler not wired";

            // 중단은 패널 명령이 아니라 커넥터가 직접 처리한다 — 진행 중인 버스트 루프를 아는 곳이 여기다.
            if (msg.Op == CameraControlOps.CaptureAbort)
            {
                bool aborted = await AbortCameraOperationAsync(_activeCaptures, cam.AgentId, CameraControlOps.Capture,
                    msg.TargetRequestId, allowCurrentWithoutTarget: true).ConfigureAwait(false);
                await PublishControlAckAsync(cam, msg, true, aborted ? "capture aborted" : "no capture in progress").ConfigureAwait(false);
                return;
            }

            SemaphoreSlim gate = _cameraOperationGates.GetOrAdd(cam.AgentId, _ => new SemaphoreSlim(1, 1));
            bool entered = false;
            try
            {
                bool changesRuntime = msg.Op == CameraControlOps.RuntimeLoad || msg.Op == CameraControlOps.RuntimeUnload;
                if (changesRuntime) CancelForBindingChange(cam.AgentId);
                entered = changesRuntime
                    ? await gate.WaitAsync(TimeSpan.FromSeconds(5), _cts.Token).ConfigureAwait(false)
                    : await gate.WaitAsync(0, _cts.Token).ConfigureAwait(false);
                if (!entered) throw new InvalidOperationException("카메라가 BIAS 또는 촬영 작업 중입니다.");
                EnsureBindingCurrent(cam);
                if (_cameraControlHandler is not null)
                {
                    (success, message) = await _cameraControlHandler(cam, msg).ConfigureAwait(false);
                    _cts.Token.ThrowIfCancellationRequested();
                }
            }
            catch (Exception ex)
            {
                success = false;
                message = $"NG: {ex.Message}";
                LogBias($"[CameraNats] control failed agent={cam.AgentId} request={msg.RequestId} op={msg.Op}: {ex.Message}");
            }
            finally { if (entered) gate.Release(); }

            await PublishControlAckAsync(cam, msg, success, message).ConfigureAwait(false);
        }

        private async Task HandleBiasAsync(CameraDescriptor cam, CameraControlMessage msg)
        {
            SemaphoreSlim gate = _cameraOperationGates.GetOrAdd(cam.AgentId, _ => new SemaphoreSlim(1, 1));
            using var active = new ActiveCameraOperation(msg.RequestId, _cts.Token);
            if (!_activeBias.TryAdd(cam.AgentId, active))
            {
                await PublishControlAckAsync(cam, msg, false, "NG: 이전 BIAS 작업이 아직 종료되지 않았습니다.").ConfigureAwait(false);
                return;
            }

            bool entered = false;
            BiasOperationResult result;
            bool useCurrent = msg.Op == CameraControlOps.BiasUseCurrent;
            string sourceOperation = useCurrent ? msg.BiasSourceOperation : msg.Op;
            AppliedBiasResult? appliedForAttempt = null;
            string context = $"agent={cam.AgentId} request={msg.RequestId} op={msg.Op} sourceOp={sourceOperation} target={msg.BiasTargetLevel}";
            try
            {
                if (IsRequestCancelled(cam.AgentId, CameraControlOps.Bias, msg.RequestId))
                    active.Cancellation.Cancel();
                entered = await gate.WaitAsync(TimeSpan.FromSeconds(3), active.Cancellation.Token).ConfigureAwait(false);
                if (!entered)
                    throw new InvalidOperationException("이전 BIAS 또는 촬영 작업이 아직 종료되지 않았습니다.");

                active.Cancellation.Token.ThrowIfCancellationRequested();
                EnsureBindingCurrent(cam);
                _verifiedBias.TryRemove(cam.AgentId, out _);
                AppliedBiasResult? selectedApplication = null;
                string? prerequisiteError = null;
                if (useCurrent)
                {
                    if (!msg.RequireBiasFile)
                        prerequisiteError = "현재 BIAS 사용 요청은 필수 파일 저장을 포함해야 합니다.";
                    else if (_readAppliedBias is null)
                        prerequisiteError = "현재 BIAS 적용 상태 조회가 연결되지 않았습니다. AgentUI를 업데이트해 주세요.";
                    else
                    {
                        try { selectedApplication = ReadCurrentAppliedBias(cam, sourceOperation); }
                        catch (Exception ex) { prerequisiteError = ex.Message; }
                    }
                }
                result = await BiasOperationRunner.RunAsync(
                    async ct =>
                    {
                        if (prerequisiteError is not null)
                            throw new InvalidOperationException(prerequisiteError);
                        if (_biasControlHandler is null)
                            throw new InvalidOperationException("취소 가능한 BIAS 처리기가 없습니다. AgentUI를 업데이트해 주세요.");

                        EnsureBindingCurrent(cam);
                        appliedForAttempt = null;
                        if (useCurrent && selectedApplication is not null)
                            EnsureBiasApplicationMatchesCurrent(cam, selectedApplication, selectedApplication.Json);
                        // biasUseCurrent는 Op를 그대로 전달한다. App은 현재 레지스터 읽기만 검증하며
                        // 자동 보정 탐색으로 바꾸지 않는다. 파일 재시도도 선택한 적용 세대를 유지한다.
                        await _biasControlHandler(cam, msg, ct).ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        EnsureBindingCurrent(cam);
                        if (_readAppliedBias is not null)
                        {
                            AppliedBiasResult applied = ReadCurrentAppliedBias(cam, sourceOperation);
                            if (useCurrent && selectedApplication is not null)
                                EnsureSameBiasApplication(applied, selectedApplication.ApplicationId, selectedApplication.Operation, selectedApplication.Json);
                            appliedForAttempt = applied;
                            LogBias($"[BIAS APPLIED] VERIFIED {context} applicationId={applied.ApplicationId} source={applied.Source}");
                        }
                    },
                    () =>
                    {
                        string? json = appliedForAttempt?.Json ?? _readBiasJson?.Invoke(cam);
                        if (json is not null) EnsureBiasApplicationMatchesCurrent(cam, appliedForAttempt, json);
                        return json;
                    },
                    async (json, ct) =>
                    {
                        if (_productionSink is null || msg.BiasStorage is null)
                            throw new InvalidOperationException("BIAS 저장 경로 또는 생산 저장 설정이 없습니다.");
                        EnsureBiasApplicationMatchesCurrent(cam, appliedForAttempt, json);
                        string path = await _productionSink.WriteAndVerifyBiasJsonAsync(cam, msg.BiasStorage, json, ct).ConfigureAwait(false);
                        EnsureBiasApplicationMatchesCurrent(cam, appliedForAttempt, json);
                        return path;
                    },
                    msg.RequireBiasFile,
                    msg.BiasAttemptTimeoutSeconds,
                    context,
                    LogBias,
                    active.Cancellation.Token).ConfigureAwait(false);
                active.Cancellation.Token.ThrowIfCancellationRequested();
                if (result.IsSuccess)
                {
                    EnsureBindingCurrent(cam);
                    EnsureBiasApplicationMatchesCurrent(cam, appliedForAttempt, result.BiasJson);
                    if (msg.RequireBiasFile)
                        _verifiedBias[cam.AgentId] = new VerifiedBias(msg.RequestId, result.BiasJson, result.FilePath, msg.BiasAttemptTimeoutSeconds,
                            appliedForAttempt?.ApplicationId ?? string.Empty, sourceOperation);
                }
            }
            catch (OperationCanceledException)
            {
                result = new BiasOperationResult(false, 0, "NG: BIAS 작업이 취소되었습니다.", string.Empty, string.Empty);
                LogBias($"[BIAS] CANCELLED {context}");
            }
            catch (Exception ex)
            {
                result = new BiasOperationResult(false, 0, $"NG: BIAS 처리 오류: {ex.Message}", string.Empty, string.Empty);
                LogBias($"[BIAS] NG {context} error={ex}");
            }
            finally
            {
                _activeBias.TryRemove(cam.AgentId, out _);
                if (entered) gate.Release();
                active.Finished.TrySetResult();
            }

            // 작업이 끝나고 파일 확인 결과가 확정된 뒤에만 ACK한다.
            await PublishControlAckAsync(cam, msg, result.IsSuccess, result.Message,
                result.IsSuccess && msg.RequireBiasFile, result.FilePath, result.Attempts,
                result.IsSuccess ? appliedForAttempt?.ApplicationId ?? string.Empty : string.Empty,
                result.IsSuccess ? sourceOperation : string.Empty).ConfigureAwait(false);
        }

        private void LogBias(string message)
        {
            try { _diagnosticLog?.Invoke(message); }
            catch (Exception ex) { Debug.WriteLine($"[BIAS] diagnostic log failed: {ex.Message}"); }
            Debug.WriteLine(message);
        }

        private async Task PublishControlAckAsync(CameraDescriptor cam, CameraControlMessage msg, bool success, string message,
            bool biasFileSaved = false, string biasFilePath = "", int attempts = 0,
            string biasApplicationId = "", string biasSourceOperation = "")
        {
            try
            {
                await _nats.PublishCameraControlAckAsync(new CameraControlAckMessage
                {
                    AgentId = cam.AgentId,
                    CameraIndex = cam.OpenCvIndex,
                    Op = msg.Op,
                    RequestId = msg.RequestId,
                    IsSuccess = success,
                    Message = message,
                    BiasFileSaved = biasFileSaved,
                    BiasFilePath = biasFilePath,
                    Attempts = attempts,
                    BiasApplicationId = biasApplicationId,
                    BiasSourceOperation = biasSourceOperation,
                    Timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogBias($"[CameraNats] camera control ack publish failed agent={cam.AgentId} request={msg.RequestId}: {ex.Message}");
            }
        }

        private async Task PublishConfigSnapshotAsync(string agentId, string requestId)
        {
            if (_getConfigSnapshot is null) return;
            try
            {
                await _nats.PublishAgentConfigSnapshotAsync(new AgentConfigSnapshotMessage
                {
                    AgentId = agentId,
                    RequestId = requestId,
                    Config = _getConfigSnapshot(),
                    Timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] config snapshot publish failed for {agentId}: {ex.Message}");
            }
        }

        private async Task ApplyConfigAsync(AgentConfigApplyMessage msg)
        {
            bool success = true;
            string message = "저장됨. AgentUI 재시작 후 적용됩니다.";
            try
            {
                if (_applyConfigSnapshot is null) throw new InvalidOperationException("설정 저장 처리기가 없습니다.");
                ResolveCurrentDescriptor(msg.AgentId);
                _applyConfigSnapshot(msg.Config);
                message = (_getConfigSnapshot?.Invoke().RestartRequired ?? true)
                    ? "저장됨. AgentUI 재시작 후 적용됩니다." : "설정이 적용되었습니다.";
            }
            catch (Exception ex)
            {
                success = false;
                message = ex.Message;
                Debug.WriteLine($"[CameraNats] config apply failed for {msg.AgentId}: {ex.Message}");
            }

            try
            {
                await _nats.PublishAgentConfigAckAsync(new AgentConfigAckMessage
                {
                    AgentId = msg.AgentId,
                    RequestId = msg.RequestId,
                    IsSuccess = success,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] config ack publish failed for {msg.AgentId}: {ex.Message}");
            }
            if (success) await PublishConfigSnapshotAsync(msg.AgentId, msg.RequestId).ConfigureAwait(false);
        }

        private async Task HandleSerialConfigAsync(string agentId, SerialConfigMessage msg)
        {
            bool success = false;
            string message;
            SemaphoreSlim gate = _cameraOperationGates.GetOrAdd(agentId, _ => new SemaphoreSlim(1, 1));
            bool entered = false;
            try
            {
                entered = await gate.WaitAsync(0, _cts.Token).ConfigureAwait(false);
                if (!entered) throw new InvalidOperationException("카메라가 BIAS 또는 촬영 작업 중입니다.");
                CameraDescriptor current = ResolveCurrentDescriptor(agentId);
                EnsureBindingCurrent(current);
                if (_serialConfigHandler is null) throw new InvalidOperationException("시리얼 설정 처리기가 없습니다.");
                CancelForBindingChange(agentId);
                (success, message) = await _serialConfigHandler(current, msg).ConfigureAwait(false);
            }
            catch (Exception ex) { message = $"NG: {ex.Message}"; }
            finally { if (entered) gate.Release(); }
            await _nats.PublishSerialConfigAckAsync(new SerialConfigAckMessage
            {
                AgentId = agentId, IsSuccess = success, ErrorMessage = success ? string.Empty : message,
                Timestamp = DateTime.UtcNow
            }).ConfigureAwait(false);
        }

        private async Task PublishHeartbeatsAsync()
        {
            CameraDescriptor[] cameras = CameraSnapshot();
            var live = new List<CameraDescriptor>();
            foreach (CameraDescriptor cam in cameras)
            {
                if (_manager.TryGet(cam.AgentId, out _))
                {
                    live.Add(cam);
                }
            }

            var inventory = new List<string>(live.Count);
            foreach (CameraDescriptor cam in live) inventory.Add(cam.AgentId);

            // 마지막 카메라까지 빠지면 인벤토리를 실어 보낼 카메라가 없다. 호스트 이름으로 빈
            // 인벤토리를 한 번 보고해야 Master가 그 PC의 카메라를 지운다(침묵은 신호가 아니다).
            if (live.Count == 0)
            {
                await PublishHostInventoryAsync(inventory).ConfigureAwait(false);
            }

            foreach (CameraDescriptor cam in cameras)
            {
                if (cam.OpenCvIndex < 0) continue; // 물리 영상 대상 미확정: 자동 load의 근거로 쓰지 않는다.
                CameraStatus status = _manager.TryGet(cam.AgentId, out ICameraRuntime runtime)
                    ? MapStatus(runtime.Status) : CameraStatus.Offline;
                // runtime을 unload한 카메라도 신원이 확정되어 있으면 Offline을 보고한다.
                // Manager는 이 실제 인덱스를 받아 원하는 로드 상태를 복원할 수 있다.
                await PublishStatusAsync(cam, status, inventory).ConfigureAwait(false);
            }
        }

        private async Task PublishHostInventoryAsync(List<string> inventory)
        {
            try
            {
                await _nats.PublishAgentStatusAsync(new AgentStatusMessage
                {
                    AgentId = Environment.MachineName,
                    HostName = Environment.MachineName,
                    CameraStatus = CameraStatus.Offline,
                    Timestamp = DateTime.UtcNow,
                    HostAgentIds = inventory
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] host inventory publish failed: {ex.Message}");
            }
        }

        private async Task PublishStatusAsync(CameraDescriptor cam, CameraStatus status, List<string> inventory)
        {
            try
            {
                await _nats.PublishAgentStatusAsync(new AgentStatusMessage
                {
                    AgentId = cam.AgentId,
                    Alias = cam.Alias,
                    HostName = Environment.MachineName,
                    CameraIndex = cam.OpenCvIndex,
                    UsbContainerId = cam.UsbContainerId ?? string.Empty,
                    CameraSerialNumber = cam.CameraSerialNumber ?? string.Empty,
                    LogDirectory = _readLogDirectory?.Invoke() ?? string.Empty,
                    CameraStatus = status,
                    Timestamp = DateTime.UtcNow,
                    HostAgentIds = inventory,
                    IsSerialConnected = ReadSerialHealth(cam),
                    CameraTemperature = await ReadCameraTemperatureSafeAsync(cam).ConfigureAwait(false),
                    PendingSyncFiles = _productionSink?.PendingFiles
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] heartbeat failed for {cam.AgentId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Master의 온디맨드 원본 요청에 응답한다. 요청 경로의 .y16을 그대로 실어 보내고,
        /// 가로/세로는 옆에 있는 .json 사이드카에서 최선으로 읽는다(없어도 히스토그램은 그린다).
        /// Master가 타임아웃까지 붙잡히지 않도록 실패도 반드시 응답한다.
        /// </summary>
        private async Task HandleRawImageRequestAsync(RawImageRequestMessage request)
        {
            var response = new RawImageResponseMessage
            {
                AgentId = request.AgentId,
                RequestId = request.RequestId
            };

            try
            {
                if (string.IsNullOrWhiteSpace(request.RawPath) || !File.Exists(request.RawPath))
                {
                    response.Message = $"원본 파일을 찾을 수 없습니다: {request.RawPath}";
                }
                else
                {
                    response.Pixels = await File.ReadAllBytesAsync(request.RawPath).ConfigureAwait(false);
                    response.IsSuccess = true;

                    string sidecar = Path.ChangeExtension(request.RawPath, ".json");
                    if (File.Exists(sidecar))
                    {
                        try
                        {
                            var meta = JsonSerializer.Deserialize<CaptureMetadata>(
                                await File.ReadAllTextAsync(sidecar).ConfigureAwait(false));
                            if (meta is not null)
                            {
                                response.Width = meta.Width;
                                response.Height = meta.Height;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[CameraNats] raw sidecar parse failed for {sidecar}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.Message = ex.Message;
                Debug.WriteLine($"[CameraNats] raw read failed for {request.RawPath}: {ex.Message}");
            }

            try { await _nats.PublishRawImageResponseAsync(response).ConfigureAwait(false); }
            catch (Exception ex) { Debug.WriteLine($"[CameraNats] raw response publish failed: {ex.Message}"); }
        }

        /// <summary>하트비트가 카메라 시리얼 고장으로 끊기면 안 되므로 실패는 null로 흡수한다.</summary>
        private async Task<double?> ReadCameraTemperatureSafeAsync(CameraDescriptor cam)
        {
            if (_readCameraTemperature is null) return null;
            try { return await _readCameraTemperature(cam).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] heartbeat temperature read failed for {cam.AgentId}: {ex.Message}");
                return null;
            }
        }

        private bool? ReadSerialHealth(CameraDescriptor cam)
        {
            if (_serialHealth is null) return null;
            try { return _serialHealth(cam); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] serial health probe failed for {cam.AgentId}: {ex.Message}");
                return false;
            }
        }

        private static CameraStatus MapStatus(CameraRuntimeStatus status) => status switch
        {
            CameraRuntimeStatus.Running => CameraStatus.Connected,
            _ => CameraStatus.Offline
        };

        // ponytail: 카메라당 NATS로 ~10fps 컬러 JPEG 미리보기. 대역폭 상한 — 여러 Agent가 링크를
        // 포화시키면 지연을 늘리거나 해상도를 낮출 것.
        private async Task LiveStreamLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (CameraDescriptor cam in CameraSnapshot())
                {
                    if (!_manager.TryGet(cam.AgentId, out ICameraRuntime runtime)) continue;

                    // 라이브는 무조건 원본(LatestFrame)을 본다 — NUC·AGC 같은 표시 처리는
                    // Master 측에서 한다. 여기서 보정 프레임을 본면 Master는 원본을 볼 수 없다.
                    ThermalFrame? frame = runtime.LatestFrame;
                    if (frame is null || runtime.Status != CameraRuntimeStatus.Running
                        || DateTimeOffset.UtcNow - frame.Timestamp > TimeSpan.FromSeconds(2)) continue;

                    try
                    {
                        byte[] jpeg = ThermalPreviewEncoder.EncodeJpeg(frame);
                        await _nats.PublishLiveFrameAsync(new LiveFrameMessage
                        {
                            AgentId = cam.AgentId,
                            CameraIndex = cam.OpenCvIndex,
                            ImageBytes = jpeg,
                            Width = frame.Width,
                            Height = frame.Height,
                            Timestamp = DateTime.UtcNow
                        }).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CameraNats] live publish failed for {cam.AgentId}: {ex.Message}");
                    }
                }

                try { await Task.Delay(100, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_lifetimeSync)
            {
                _disposeTask ??= DisposeCoreAsync();
                return new ValueTask(_disposeTask);
            }
        }

        private async Task DisposeCoreAsync()
        {
            _cts.Cancel();
            _connected = false;
            Task? lifetime;
            Task[] work;
            lock (_lifetimeSync)
            {
                lifetime = _lifetimeTask;
                work = _inFlight.ToArray();
            }
            if (lifetime is not null)
            {
                try { await lifetime.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch (Exception ex) { Debug.WriteLine($"[CameraNats] startup/loop shutdown: {ex.Message}"); }
            }
            await _subscriptionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var subscription in _subscriptions.Values)
                    await DisposeLeasesAsync(subscription.Leases).ConfigureAwait(false);
                _subscriptions.Clear();
            }
            finally { _subscriptionGate.Release(); }
            Task drain = Task.WhenAll(work.Concat(_activeBias.Values.Select(active => active.Finished.Task))
                .Concat(_activeCaptures.Values.Select(active => active.Finished.Task)));
            try { await drain.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (Exception ex) { Debug.WriteLine($"[CameraNats] command shutdown: {ex.Message}"); }
            // Gate/CTS는 늦게 끝나는 핸들러가 Release/Token을 사용할 수 있다. 종료 경합에서
            // 이를 먼저 Dispose해 실제 장치 종료 예외를 덮어쓰지 않는다.
        }
    }
}
