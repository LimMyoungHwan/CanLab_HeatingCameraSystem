namespace HeatingCameraSystem.Core.Models
{
    /// <summary>Agent가 Master로 보내는 캡처 이미지 형식.</summary>
    public enum CaptureImageFormat
    {
        /// <summary>16비트 원시 열화상 데이터(Y16).</summary>
        Y16Raw,

        /// <summary>16비트 TIFF.</summary>
        Tiff16
    }
}
