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

    /// <summary>
    /// 생산 저장 규칙(<c>.raw</c> 트리)의 파일 포맷. 폴더 구조·파일명·장수는 포맷과 무관하게 같고
    /// 확장자만 달라진다.
    /// </summary>
    public enum ProductionCaptureFormat
    {
        /// <summary>16비트 원본. 픽셀(0,0)에 FPA 원시값이 들어가는 후처리 툴 계약이다.</summary>
        Raw,

        /// <summary>
        /// 8비트 JPEG. 열 데이터가 남지 않으므로 캘리브레이션에 쓸 수 없고, 육안 검사·보고서용
        /// 재촬영에만 쓴다.
        /// </summary>
        Jpeg
    }
}
