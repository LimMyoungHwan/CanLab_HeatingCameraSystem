using OpenCvSharp;

namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    /// <summary>
    /// CLTC 카메라를 raw Y16 모드로 여는 공통 설정. 촬영 경로(<see cref="CltcThermalFrameSource"/>)와
    /// 라이브 경로(<see cref="CltcLiveThermalCamera"/>)가 같은 순서를 쓰도록 한 곳에 둔다 —
    /// 두 곳에 복사해 두면 한쪽만 고쳐져 라이브와 촬영의 픽셀 포맷이 갈린다.
    /// </summary>
    internal static class ClCaptureSetup
    {
        /// <summary>
        /// <c>ConvertRgb=0</c>을 <c>FourCC</c>보다 <b>먼저</b> 건다. 벤더 레퍼런스
        /// (<c>참고/util/Capture.cpp:19-22</c>)가 쓰는 순서이며, 뒤집으면 DSHOW가 Y16 요청을
        /// 흘려버리고 드라이버 기본 포맷(8비트 BGR)이 남는다. 그 경우
        /// <see cref="ClThermalMatDecoder"/>가 <c>CV_8UC3</c>을 받아 방사 측정 데이터가 없는
        /// 프레임을 만들고, 생산 저장은 <c>.raw</c>를 쓸 수 없게 된다.
        /// </summary>
        // ponytail: 해상도(FRAME_WIDTH/HEIGHT)는 일부러 강제하지 않는다 — 레퍼런스는 자기 설정값을
        // 넣지만 우리는 네이티브 해상도를 그대로 쓰는 편이 안전하다. 모델별 해상도 강제가 필요해지면
        // CameraModelSpec.Width/Height를 여기로 넘긴다.
        public static void OpenRawY16(VideoCapture capture)
        {
            capture.Set(VideoCaptureProperties.ConvertRgb, 0);
            capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('Y', '1', '6', ' '));
        }
    }
}
