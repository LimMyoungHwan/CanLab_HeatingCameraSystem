namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 한 번 촬영에서 생성된 파일 묶음. 메타데이터와 원시/미리보기 파일 경로를 담는다.
    /// </summary>
    /// <param name="Metadata">촬영 메타데이터.</param>
    /// <param name="Y16Path">Y16 원시 파일 경로.</param>
    /// <param name="JsonPath">JSON 메타데이터 파일 경로.</param>
    /// <param name="PngPath">PNG 미리보기 파일 경로. null이면 미리보기 없음.</param>
    public sealed record CaptureFiles(
        CaptureMetadata Metadata,
        string Y16Path,
        string JsonPath,
        string? PngPath);
}
