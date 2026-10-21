namespace HeatingCameraSystem.Core.Models;

public static class CapturePlanLabels
{
    public static string Operation(string operation) => operation switch
    {
        CameraControlOps.Capture => "캡처", CameraControlOps.ShutterOpen => "셔터 열기",
        CameraControlOps.ShutterClose => "셔터 닫기", CameraControlOps.Bias => "BIAS (카메라 대역 따름)",
        CameraControlOps.BiasLow => "BIAS LOW", CameraControlOps.BiasMid => "BIAS MID",
        CameraControlOps.BiasHigh => "BIAS HIGH", CameraControlOps.Nuc => "NUC 실행",
        CameraControlOps.Run => "카메라 RUN", CameraControlOps.Stop => "카메라 STOP",
        CameraControlOps.SaveConfig => "설정 저장", CameraControlOps.RefreshInfo => "정보 갱신", _ => operation
    };
    public static string Duration(int seconds)
    {
        seconds = Math.Max(0, seconds);
        var parts = new List<string>(3);
        if (seconds >= 3600) parts.Add($"{seconds / 3600}시간");
        if (seconds % 3600 >= 60) parts.Add($"{seconds % 3600 / 60}분");
        if (seconds % 60 > 0 || parts.Count == 0) parts.Add($"{seconds % 60}초");
        return string.Join(" ", parts);
    }
    public static string Entry(CapturePlan plan, string entryKey, IReadOnlyList<Recipe> templates)
    {
        int index = plan.Entries?.FindIndex(e => e.Key == entryKey) ?? -1;
        if (index < 0 || plan.Entries == null) return "항목";
        var entry = plan.Entries[index];
        string name = templates.FirstOrDefault(t => t.Id == entry.TemplateRecipeId)?.Name ?? "연결된 레시피 없음";
        return $"항목 {index + 1} ({name})";
    }
    public static string ValidationReason(CapturePlan plan, string reason) => reason;
}
