namespace HeatingCameraSystem.Core.Models
{
    public sealed record CaptureFiles(
        CaptureMetadata Metadata,
        string Y16Path,
        string JsonPath,
        string? PngPath);
}
