namespace HeatingCameraSystem.Simulator.State;

/// <summary>Simulator가 보고하는 카메라별 런타임 모드.</summary>
public enum CameraMode
{
    Online,
    Faulted,
    Offline
}

/// <summary>
/// Simulator 런타임 상태의 단일 스레드 안전 보관소: PLC 온라인 플래그, 장애 비트 0-19,
/// AgentId별 카메라 모드. 읽기와 쓰기 전부가 락 하나를 잡으므로 동시 호출자가
/// 찢어진 상태를 볼 수 없다.
/// </summary>
public sealed class SimulatorState
{
    public const int FaultCount = 20;

    private readonly object _gate = new();
    private readonly bool[] _faults = new bool[FaultCount];
    private readonly Dictionary<string, CameraMode> _cameras;
    private bool _plcOnline;

    /// <summary>AgentId마다 <see cref="CameraMode.Online"/> 항목 하나씩을 심는다.</summary>
    public SimulatorState(IEnumerable<string> agentIds)
    {
        ArgumentNullException.ThrowIfNull(agentIds);
        _cameras = new Dictionary<string, CameraMode>(StringComparer.Ordinal);
        foreach (string id in agentIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("AgentId must be non-blank.", nameof(agentIds));
            _cameras[id] = CameraMode.Online;
        }
    }

    public bool PlcOnline
    {
        get { lock (_gate) return _plcOnline; }
    }

    public void SetPlcOnline(bool online)
    {
        lock (_gate) _plcOnline = online;
    }

    public void SetFault(int index)
    {
        GuardFault(index);
        lock (_gate) _faults[index] = true;
    }

    public void ClearFault(int index)
    {
        GuardFault(index);
        lock (_gate) _faults[index] = false;
    }

    public bool GetFault(int index)
    {
        GuardFault(index);
        lock (_gate) return _faults[index];
    }

    /// <summary>락 아래에서 뜬 전체 장애 비트의 일관된 복사본.</summary>
    public IReadOnlyList<bool> SnapshotFaults()
    {
        lock (_gate) return (bool[])_faults.Clone();
    }

    public void SetCameraMode(string agentId, CameraMode mode)
    {
        lock (_gate)
        {
            if (!_cameras.ContainsKey(agentId))
                throw new ArgumentException($"Unknown AgentId '{agentId}'.", nameof(agentId));
            _cameras[agentId] = mode;
        }
    }

    public CameraMode GetCameraMode(string agentId)
    {
        lock (_gate)
        {
            if (!_cameras.TryGetValue(agentId, out CameraMode mode))
                throw new ArgumentException($"Unknown AgentId '{agentId}'.", nameof(agentId));
            return mode;
        }
    }

    private static void GuardFault(int index)
    {
        if (index is < 0 or >= FaultCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Fault index must be 0-{FaultCount - 1}.");
    }
}
