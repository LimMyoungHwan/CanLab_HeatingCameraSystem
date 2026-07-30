using System.Collections.Concurrent;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.ManagerE2EDriver;

/// <summary>
/// [S8] In-process stand-in for the WPF AgentUI's NATS surface, so the Manager E2E can prove the
/// redefined per-camera runtime IPC without launching WPF. It opens ("loads") every camera on
/// start — mirroring AgentUI, which opens all of its configured cameras at launch — heartbeats on
/// <c>agent.status.{AgentId}</c> while a camera is loaded, and honours the Manager's
/// <c>runtimeLoad</c>/<c>runtimeUnload</c> commands on <c>master.cmd.camera.{AgentId}</c>.
/// </summary>
internal sealed class FakeAgentUiRuntime : IAsyncDisposable
{
    private readonly INatsCommunicationService _nats;
    private readonly ConcurrentDictionary<string, bool> _loaded = new();
    private readonly CancellationTokenSource _cts = new();

    public FakeAgentUiRuntime(INatsCommunicationService nats) => _nats = nats;

    public async Task StartAsync(IEnumerable<string> agentIds, int heartbeatMs = 500)
    {
        foreach (var agentId in agentIds)
        {
            _loaded[agentId] = true;
            var id = agentId;
            await _nats.SubscribeCameraControlAsync(id, msg =>
            {
                if (msg.Op == CameraControlOps.RuntimeUnload) _loaded[id] = false;
                else if (msg.Op == CameraControlOps.RuntimeLoad) _loaded[id] = true;
            });
        }

        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                foreach (var kv in _loaded)
                {
                    if (!kv.Value) continue;
                    await _nats.PublishAgentStatusAsync(new AgentStatusMessage
                    {
                        AgentId = kv.Key,
                        CameraStatus = CameraStatus.Connected,
                        Timestamp = DateTime.UtcNow,
                    });
                }

                try { await Task.Delay(heartbeatMs, _cts.Token); }
                catch (OperationCanceledException) { return; }
            }
        });
    }

    public bool IsHeartbeating(string agentId) => _loaded.TryGetValue(agentId, out var v) && v;

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
