namespace HeatingCameraSystem.Simulator.State;

/// <summary>Per-camera runtime mode reported by the simulator.</summary>
public enum CameraMode
{
    Online,
    Faulted,
    Offline
}

/// <summary>
/// Single thread-safe holder for simulator runtime: PLC online flag, fault bits 0-19,
/// and per-AgentId camera mode. Every read and write takes one lock, so concurrent
/// callers never observe torn state.
/// </summary>
public sealed class SimulatorState
{
    public const int FaultCount = 20;

    private readonly object _gate = new();
    private readonly bool[] _faults = new bool[FaultCount];
    private readonly Dictionary<string, CameraMode> _cameras;
    private bool _plcOnline;

    /// <summary>Seeds one <see cref="CameraMode.Online"/> entry per AgentId.</summary>
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

    /// <summary>Consistent copy of all fault bits taken under the lock.</summary>
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
