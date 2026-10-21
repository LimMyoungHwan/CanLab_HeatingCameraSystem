namespace HeatingCameraSystem.Core.Models;

/// <summary>공유 위치표와 촬영 계획 컴파일러가 함께 사용하는 하드웨어 독립 검증.</summary>
public static class CameraPositionRules
{
    public static void Validate(CameraPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (position.Kind is not CameraPositionKind.Coordinate and not CameraPositionKind.Point)
            throw new ArgumentException("Unknown camera position kind.", nameof(position));
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            throw new ArgumentException("Camera position coordinates must be finite.", nameof(position));
        // Point 1 is the PLC's coordinate-move scratch point. The actual upper limit is hardware-specific.
        if (position.Kind == CameraPositionKind.Point && position.PointNumber < 2)
            throw new ArgumentException("Camera positions require Point 2 or higher; Point 1 is reserved.", nameof(position));
    }

    public static void ValidateEntry(CameraPositionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CapturePlanCompiler.ValidateCamera(entry.Camera);
        if (!Enum.IsDefined(entry.Role))
            throw new ArgumentException("위치 역할은 Hot/Cold/Room이어야 합니다.", nameof(entry));
        Validate(entry.Position);
    }
}
