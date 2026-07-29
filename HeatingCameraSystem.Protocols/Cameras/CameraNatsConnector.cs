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
    /// Optional NATS bridge for an AgentUI process. Each local camera keeps its logical
    /// <see cref="CameraDescriptor.AgentId"/>, so the existing Master contract is unchanged:
    /// per camera it subscribes <c>master.cmd.capture.{AgentId}</c> (and the shared
    /// <c>master.cmd.capture.all</c>, giving in-process fan-out), snapshots the live loop
    /// (tee — never re-opens the camera), persists radiometric <c>.y16</c> locally, and
    /// publishes <c>agent.result.capture.{AgentId}</c> with a viewable JPG plus periodic
    /// <c>agent.status.{AgentId}</c> heartbeats. NATS is never a startup dependency: connect
    /// runs in the background with retry, and the local runtime works with NATS absent.
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

        private readonly CancellationTokenSource _cts = new();
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
            Func<CameraDescriptor, string, Task<(bool Success, string Message)>>? cameraControlHandler = null)
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
        }

        public bool IsConnected => _connected;

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

            foreach (CameraDescriptor cam in _cameras)
            {
                CameraDescriptor descriptor = cam;
                try
                {
                    await _nats.SubscribeCaptureCommandAsync(
                        descriptor.AgentId,
                        cmd => _ = HandleCaptureAsync(descriptor, cmd)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
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
                    Debug.WriteLine($"[CameraNats] camera control subscribe failed for {descriptor.AgentId}: {ex.Message}");
                }

                if (_getConfigSnapshot is not null || _applyConfigSnapshot is not null)
                {
                    string agentId = descriptor.AgentId;
                    try
                    {
                        if (_getConfigSnapshot is not null)
                            await _nats.SubscribeAgentConfigRequestAsync(agentId, req => _ = PublishConfigSnapshotAsync(agentId)).ConfigureAwait(false);
                        if (_applyConfigSnapshot is not null)
                            await _nats.SubscribeAgentConfigApplyAsync(agentId, msg => _ = ApplyConfigAsync(msg)).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CameraNats] config subscribe failed for {agentId}: {ex.Message}");
                    }
                }
            }

            _heartbeat = new Timer(_ => PublishHeartbeats(), null, TimeSpan.Zero, TimeSpan.FromSeconds(_heartbeatSeconds));

            _ = Task.Run(() => LiveStreamLoopAsync(_cts.Token));
        }

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
                            CaptureRecord record = _store.Save(snap, descriptor.AgentId, descriptor.OpenCvIndex, cmd.RecipeStepId);
                            imagePath = record.Y16Path;
                            bytes = ThermalPreviewEncoder.EncodeJpeg(snap);
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
                    RecipeStepId = cmd.RecipeStepId,
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
            foreach (CameraDescriptor cam in _cameras)
            {
                if (_manager.TryGet(cam.AgentId, out ICameraRuntime runtime))
                {
                    _ = PublishStatusAsync(cam, MapStatus(runtime.Status));
                }
            }
        }

        private async Task PublishStatusAsync(CameraDescriptor cam, CameraStatus status)
        {
            try
            {
                await _nats.PublishAgentStatusAsync(new AgentStatusMessage
                {
                    AgentId = cam.AgentId,
                    HostName = Environment.MachineName,
                    CameraIndex = cam.OpenCvIndex,
                    CameraStatus = status,
                    Timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] heartbeat failed for {cam.AgentId}: {ex.Message}");
            }
        }

        private static CameraStatus MapStatus(CameraRuntimeStatus status) => status switch
        {
            CameraRuntimeStatus.Running => CameraStatus.Connected,
            _ => CameraStatus.Offline
        };

        // ponytail: ~10fps color-JPEG preview per camera over NATS. Bandwidth ceiling — raise the
        // delay (or drop resolution) if many agents saturate the link.
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

            _cts.Dispose();
        }
    }
}
