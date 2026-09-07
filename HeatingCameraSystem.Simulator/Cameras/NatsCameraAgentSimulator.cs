using System.Collections.Concurrent;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Simulator;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.State;

namespace HeatingCameraSystem.Simulator.Cameras;

/// <summary>
/// 설정된 카메라 전부를 NATS Agent처럼 흉내 내는 단일 엔드포인트. 카메라별 캡처 명령을 구독하고,
/// 하트비트(<c>agent.status.{AgentId}</c>)와 라이브 프레임을 주기 발행하며, 캡처 결과는
/// 합성 프레임을 JPEG로 저장해 응답한다. <see cref="SimulatorState"/>의 카메라 모드에 따라
/// Offline이면 무응답, Faulted면 <c>IsSuccess=false</c>로 응답해 장애 시나리오를 재현한다.
/// </summary>
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

    /// <summary>NATS 연결 후 카메라별 캡처 구독을 걸고 하트비트 타이머와 라이브 프레임 루프를 기동한다.</summary>
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

    /// <summary>
    /// 캡처 명령 1건 처리. Offline이면 무응답, Faulted면 실패 응답, Online이면 합성 프레임을
    /// 저장하고 경로와 JPEG 바이트를 담아 성공 응답한다. public: 테스트가 직접 호출한다.
    /// </summary>
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

    // Offline이 아닌 카메라마다 하트비트를 발행한다. 발행 실패는 다음 주기가 만회하므로 삼킨다.
    private void PublishStatuses()
    {
        foreach (CameraSettings camera in _settings.Cameras)
        {
            CameraMode mode = _state.GetCameraMode(camera.AgentId);
            if (mode == CameraMode.Offline) continue;
            _ = PublishStatusSafeAsync(camera);
        }
    }

    private async Task PublishStatusSafeAsync(CameraSettings camera)
    {
        try
        {
            await _nats.PublishAgentStatusAsync(new AgentStatusMessage
            {
                AgentId = camera.AgentId,
                CameraIndex = camera.CameraIndex,
                CameraStatus = CameraStatus.Connected,
                Timestamp = DateTime.UtcNow,
                CameraTemperature = SyntheticFpaTemperature(camera.AgentId)
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Simulator] status publish dropped: {ex.Message}");
        }
    }

    /// <summary>
    /// 실 검출기 FPA는 상온에서 대략 29~33℃다. 카메라마다 다르되 호출마다 같은 값을 주어야
    /// 대시보드가 값을 받는지와 값이 흔들리는지를 구분할 수 있다.
    /// </summary>
    private static double SyntheticFpaTemperature(string agentId)
    {
        int sum = 0;
        foreach (char c in agentId) sum += c;
        return 29.0 + (sum % 40) / 10.0;
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
                try
                {
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
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"[Simulator] live frame publish dropped: {ex.Message}");
                }
            }
        }
    }
}
