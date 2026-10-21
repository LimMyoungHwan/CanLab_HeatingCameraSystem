namespace HeatingCameraSystem.Core.Models;

/// <summary>순서대로 연결한 단일 카메라 스텝 레시피를 선택 카메라마다 전체 실행한다.</summary>
public sealed class CapturePlan
{
    // Missing Entries identifies the retired matrix format; never interpret its cached Steps.
    public List<CapturePlanEntry>? Entries { get; set; }
}

public sealed class CapturePlanEntry
{
    public string Key { get; set; } = Guid.NewGuid().ToString("N");
    public string TemplateRecipeId { get; set; } = string.Empty;
    public List<CameraIdentity> Cameras { get; set; } = new();
}

/// <summary>Heartbeat의 실제 AgentId와 Agent 내부 카메라 번호(0 포함).</summary>
public sealed class CameraIdentity
{
    private string _agentId = string.Empty;
    // LiteDB represents empty strings as null; old/empty receipts must remain readable.
    public string AgentId { get => _agentId; set => _agentId = value ?? string.Empty; }
    public int CameraIndex { get; set; }
    public string Key => $"{AgentId.Length}:{AgentId}/{CameraIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    public string Label => $"{AgentId} (CAM-{CameraIndex:D2})";
    public CameraIdentity Copy() => new() { AgentId = AgentId, CameraIndex = CameraIndex };
}

public enum CameraPositionKind { Coordinate, Point }

public sealed class CameraPosition
{
    public CameraPositionKind Kind { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int PointNumber { get; set; }
}

public sealed class CameraPositionEntry
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public int CameraIndex { get; set; }
    public BlackBodyRole Role { get; set; }
    public CameraPosition Position { get; set; } = new();
    public CameraIdentity Camera => new() { AgentId = AgentId, CameraIndex = CameraIndex };
}
