using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Tests;

/// <summary>Engine 요청의 공개 상관 키에서 fake Agent가 응답할 실제 카메라 번호를 읽는다.</summary>
internal static class CaptureReplyFixtureDeepAudit
{
    public static int CameraIndex(CaptureCommandMessage command)
    {
        string[] parts = command.RecipeStepId.Split(':');
        return int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
    }
}
