using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly Func<CameraDescriptor, string, Task<(bool Success, string Message)>>? _cameraControlHandler;
        private readonly Func<CameraDescriptor, bool>? _serialHealth;

        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _subscriptionGate = new(1, 1);
        private readonly HashSet<string> _subscribedAgentIds = new(StringComparer.Ordinal);
        private Timer? _heartbeat;
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
            Func<CameraDescriptor, string, Task<(bool Success, string Message)>>? cameraControlHandler = null,
            Func<CameraDescriptor, bool>? serialHealth = null)
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
        }

        public bool IsConnected => _connected;

        /// <summary>백그라운드에서 접속 재시도를 시작한다. 접속에 실패해도 호출자를 막지 않는다.</summary>
        public void Start(string natsUrl)
        {
            _ = Task.Run(() => ConnectWithRetryAsync(natsUrl, _cts.Token));
        }

        private async Task ConnectWithRetryAsync(string natsUrl, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_connected)
            {
                try
                {
                    await _nats.ConnectAsync(natsUrl).ConfigureAwait(false);
                    _connected = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CameraNats] connect failed, retrying: {ex.Message}");
                    try { await Task.Delay(ReconnectDelay, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }

            if (!_connected || ct.IsCancellationRequested)
            {
                return;
            }

            await SyncSubscriptionsAsync().ConfigureAwait(false);

            _heartbeat = new Timer(_ => PublishHeartbeats(), null, TimeSpan.Zero, TimeSpan.FromSeconds(_heartbeatSeconds));

            _ = Task.Run(() => LiveStreamLoopAsync(_cts.Token));
        }

        /// <summary>
        /// 아직 구독하지 않은 카메라의 NATS 구독을 채운다. 핫플러그로 카메라가 늘어난 뒤 호출하지
        /// 않으면 새 카메라는 캡처 명령을 받지 못한다.
        /// </summary>
        public async Task SyncSubscriptionsAsync()
        {
            if (!_connected || _cts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await _subscriptionGate.WaitAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                foreach (CameraDescriptor descriptor in new List<CameraDescriptor>(_cameras))
                {
                    if (!_subscribedAgentIds.Add(descriptor.AgentId))
                    {
                        continue;
                    }

                    if (!await SubscribeCameraAsync(descriptor).ConfigureAwait(false))
                    {
                        _subscribedAgentIds.Remove(descriptor.AgentId);
                    }
                }
            }
            finally
            {
                _subscriptionGate.Release();
            }

            // 카메라 구성이 바뀐 직후(핫플러그/재구성) 즉시 인벤토리를 알린다. 하트비트 주기를
            // 기다리면 Master가 최대 HeartbeatSeconds 동안 사라진 카메라를 계속 보여준다.
            PublishHeartbeats();
        }

        private async Task<bool> SubscribeCameraAsync(CameraDescriptor descriptor)
        {
            bool success = true;
            try
            {
                await _nats.SubscribeCaptureCommandAsync(
                    descriptor.AgentId,
                    cmd => _ = HandleCaptureAsync(descriptor, cmd)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                success = false;
                Debug.WriteLine($"[CameraNats] subscribe failed for {descriptor.AgentId}: {ex.Message}");
            }

            try
            {
                await _nats.SubscribeCameraControlAsync(
                    descriptor.AgentId,
                    msg => _ = HandleCameraControlAsync(descriptor, msg)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                success = false;
                Debug.WriteLine($"[CameraNats] camera control subscribe failed for {descriptor.AgentId}: {ex.Message}");
            }

            if (_getConfigSnapshot is not null || _applyConfigSnapshot is not null)
            {
                try
                {
                    if (_getConfigSnapshot is not null)
                        await _nats.SubscribeAgentConfigRequestAsync(descriptor.AgentId, req => _ = PublishConfigSnapshotAsync(descriptor.AgentId)).ConfigureAwait(false);
                    if (_applyConfigSnapshot is not null)
                        await _nats.SubscribeAgentConfigApplyAsync(descriptor.AgentId, msg => _ = ApplyConfigAsync(msg)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    success = false;
                    Debug.WriteLine($"[CameraNats] config subscribe failed for {descriptor.AgentId}: {ex.Message}");
                }
            }

            return success;
        }

        /// <summary>
        /// 캡처 명령 처리: 라이브 루프를 스냅샷해 NUC 보정 후 저장하고 결과를 발행한다.
        /// 캡처가 실패해도 <c>IsSuccess=false</c>로 결과는 반드시 발행한다.
        /// </summary>
        public async Task HandleCaptureAsync(CameraDescriptor descriptor, CaptureCommandMessage cmd)
        {
            bool success = false;
            string imagePath = string.Empty;
            byte[]? bytes = null;

            try
            {
                if (_manager.TryGet(descriptor.AgentId, out ICameraRuntime runtime))
                {
                    for (int i = 0; i < _captureBurstCount; i++)
                    {
                        bool forceFreshFrame = i > 0;
                        ThermalFrame? snap = await runtime.CaptureSnapshotAsync(
                            maxAge: forceFreshFrame ? TimeSpan.Zero : TimeSpan.FromSeconds(1),
                            nextFrameTimeout: TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                        if (snap is not null)
                        {
                            ThermalFrame frame = snap;
                            if (_nucs is not null && _nucs.TryGetValue(descriptor.AgentId, out ThermalNucCorrector? nuc) && nuc is not null)
                            {
                                frame = nuc.Apply(frame);
                            }
                            CaptureRecord record = _store.Save(frame, descriptor.AgentId, descriptor.OpenCvIndex, cmd.RecipeStepId);
                            imagePath = record.Y16Path;
                            bytes = ThermalPreviewEncoder.EncodeJpeg(frame);
                            success = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] capture failed for {descriptor.AgentId}: {ex.Message}");
                success = false;
            }

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
                    ImagePath = imagePath,
                    ImageBytes = bytes,
                    Timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] publish result failed for {descriptor.AgentId}: {ex.Message}");
            }
        }

        /// <summary>카메라 제어 명령을 주입된 핸들러에 위임하고 성패를 ACK로 발행한다.</summary>
        public async Task HandleCameraControlAsync(CameraDescriptor cam, CameraControlMessage msg)
        {
            bool success = false;
            string message = "control handler not wired";

            try
            {
                if (_cameraControlHandler is not null)
                {
                    (success, message) = await _cameraControlHandler(cam, msg.Op).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Debug.WriteLine($"[CameraNats] camera control failed for {cam.AgentId}: {ex.Message}");
            }

            try
            {
                await _nats.PublishCameraControlAckAsync(new CameraControlAckMessage
                {
                    AgentId = cam.AgentId,
                    CameraIndex = msg.CameraIndex,
                    Op = msg.Op,
                    IsSuccess = success,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] camera control ack publish failed for {cam.AgentId}: {ex.Message}");
            }
        }

        private async Task PublishConfigSnapshotAsync(string agentId)
        {
            if (_getConfigSnapshot is null) return;
            try
            {
                await _nats.PublishAgentConfigSnapshotAsync(new AgentConfigSnapshotMessage
                {
                    AgentId = agentId,
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
                _applyConfigSnapshot?.Invoke(msg.Config);
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
                    IsSuccess = success,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] config ack publish failed for {msg.AgentId}: {ex.Message}");
            }
        }

        private void PublishHeartbeats()
        {
            var live = new List<CameraDescriptor>();
            foreach (CameraDescriptor cam in new List<CameraDescriptor>(_cameras))
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
                _ = PublishHostInventoryAsync(inventory);
                return;
            }

            foreach (CameraDescriptor cam in live)
            {
                if (_manager.TryGet(cam.AgentId, out ICameraRuntime runtime))
                {
                    _ = PublishStatusAsync(cam, MapStatus(runtime.Status), inventory);
                }
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
                    CameraStatus = status,
                    Timestamp = DateTime.UtcNow,
                    HostAgentIds = inventory,
                    IsSerialConnected = ReadSerialHealth(cam)
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] heartbeat failed for {cam.AgentId}: {ex.Message}");
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
                foreach (CameraDescriptor cam in _cameras)
                {
                    if (!_manager.TryGet(cam.AgentId, out ICameraRuntime runtime)) continue;

                    ThermalFrame? frame = runtime.LatestFrame;
                    if (frame is null) continue;

                    if (_nucs is not null && _nucs.TryGetValue(cam.AgentId, out ThermalNucCorrector? nuc) && nuc is not null)
                    {
                        frame = nuc.Apply(frame);
                    }

                    try
                    {
                        byte[] jpeg = ThermalPreviewEncoder.EncodeColorJpeg(frame);
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

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();

            if (_heartbeat is not null)
            {
                await _heartbeat.DisposeAsync().ConfigureAwait(false);
            }

            _subscriptionGate.Dispose();
            _cts.Dispose();
        }
    }
}
