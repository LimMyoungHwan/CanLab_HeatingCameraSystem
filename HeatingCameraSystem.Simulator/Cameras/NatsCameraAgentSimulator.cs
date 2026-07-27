using System.Collections.Concurrent;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Simulator;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.State;

namespace HeatingCameraSystem.Simulator.Cameras;

public sealed class NatsCameraAgentSimulator : ICameraAgentEndpoint
{
    private readonly SimulatorSettings _settings;
    private readonly SimulatorState _state;
    private readonly INatsCommunicationService _nats;
    private readonly SyntheticThermalScene _scene;
    private readonly SyntheticCaptureStore _store;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, long> _sequences = new(StringComparer.Ordinal);
    private Timer? _statusTimer;
    private Task? _liveLoop;
    private bool _ownsNats;

    public NatsCameraAgentSimulator(SimulatorSettings settings, SimulatorState? state = null, INatsCommunicationService? nats = null)
    {
        _settings = settings;
        _state = state ?? new SimulatorState(settings.Cameras.Select(c => c.AgentId));
        _nats = nats ?? new NatsCommunicationService();
        _ownsNats = nats == null;
        _scene = new SyntheticThermalScene(settings.Frame);
        _store = new SyntheticCaptureStore(settings.OutputPath);
    }

    public async Task StartAsync()
    {
        await _nats.ConnectAsync(_settings.Endpoint.NatsUrl).ConfigureAwait(false);
        foreach (CameraSettings camera in _settings.Cameras)
        {
            CameraSettings local = camera;
            await _nats.SubscribeCaptureCommandAsync(local.AgentId, cmd => _ = HandleCaptureAsync(local, cmd)).ConfigureAwait(false);
        }

        _statusTimer = new Timer(_ => PublishStatuses(), null, TimeSpan.Zero, TimeSpan.FromSeconds(_settings.Dynamics.HeartbeatSeconds));
        _liveLoop = Task.Run(() => PublishLiveFramesAsync(_cts.Token));
    }

    public async Task HandleCaptureAsync(CameraSettings camera, CaptureCommandMessage command)
    {
        CameraMode mode = _state.GetCameraMode(camera.AgentId);
        if (mode == CameraMode.Offline) return;

        var result = new CaptureResultMessage
        {
            AgentId = camera.AgentId,
            RecipeStepId = command.RecipeStepId,
            Timestamp = DateTime.UtcNow
        };

        if (mode == CameraMode.Faulted)
        {
            result.IsSuccess = false;
            await _nats.PublishCaptureResultAsync(result).ConfigureAwait(false);
            return;
        }

        long sequence = _sequences.AddOrUpdate(camera.AgentId, 1, (_, current) => current + 1);
        ThermalFrame frame = _scene.NextFrame(camera.CameraIndex);
        var saved = _store.Persist(camera.CameraIndex, sequence, frame);
        result.IsSuccess = true;
        result.ImagePath = saved.Path;
        result.ImageBytes = saved.Bytes;
        await _nats.PublishCaptureResultAsync(result).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _statusTimer?.Dispose();
        if (_liveLoop != null)
        {
            try { await _liveLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
        if (_ownsNats) await _nats.DisposeAsync().ConfigureAwait(false);
    }

    private void PublishStatuses()
    {
        foreach (CameraSettings camera in _settings.Cameras)
        {
            CameraMode mode = _state.GetCameraMode(camera.AgentId);
            if (mode == CameraMode.Offline) continue;
            _ = _nats.PublishAgentStatusAsync(new AgentStatusMessage
            {
                AgentId = camera.AgentId,
                CameraIndex = camera.CameraIndex,
                CameraStatus = CameraStatus.Connected,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    private async Task PublishLiveFramesAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.Dynamics.LiveFrameIntervalMs));
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            foreach (CameraSettings camera in _settings.Cameras)
            {
                if (_state.GetCameraMode(camera.AgentId) != CameraMode.Online) continue;
                ThermalFrame frame = _scene.NextFrame(camera.CameraIndex);
                byte[] jpeg = ThermalPreviewEncoder.EncodeColorJpeg(frame);
                await _nats.PublishLiveFrameAsync(new LiveFrameMessage
                {
                    AgentId = camera.AgentId,
                    CameraIndex = camera.CameraIndex,
                    ImageBytes = jpeg,
                    Width = frame.Width,
                    Height = frame.Height,
                    Timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
        }
    }
}
