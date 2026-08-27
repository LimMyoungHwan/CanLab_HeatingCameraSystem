using System.Collections.Concurrent;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.ManagerE2EDriver;

/// <summary>
/// [S8] WPF AgentUI의 NATS 표면을 인프로세스로 대체하는 스탠드인. WPF 실행 없이 Manager E2E가
/// 재정의된 카메라별 런타임 IPC를 증명할 수 있게 한다. 시작 시 모든 카메라를 열고("load") —
/// 실행 시 설정된 카메라를 모두 여는 AgentUI를 흉내낸다 — 카메라가 로드된 동안
/// <c>agent.status.{AgentId}</c> 로 하트비트하며, <c>master.cmd.camera.{AgentId}</c> 의
/// Manager <c>runtimeLoad</c>/<c>runtimeUnload</c> 명령을 따른다.
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
