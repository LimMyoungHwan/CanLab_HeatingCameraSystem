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
        private readonly IReadOnlyDictionary<string, ThermalNucCorrector>? _nucs;

        private readonly CancellationTokenSource _cts = new();
        private Timer? _heartbeat;
        private volatile bool _connected;

        public CameraNatsConnector(
            INatsCommunicationService nats,
            CameraRuntimeManager manager,
            CaptureStore store,
            IReadOnlyList<CameraDescriptor> cameras,
            int heartbeatSeconds = 5,
            IReadOnlyDictionary<string, ThermalNucCorrector>? nucs = null)
        {
            _nats = nats ?? throw new ArgumentNullException(nameof(nats));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
            _heartbeatSeconds = heartbeatSeconds > 0 ? heartbeatSeconds : 5;
            _nucs = nucs;
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
                    ThermalFrame? snap = await runtime.CaptureSnapshotAsync(
                        maxAge: TimeSpan.FromSeconds(1),
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
