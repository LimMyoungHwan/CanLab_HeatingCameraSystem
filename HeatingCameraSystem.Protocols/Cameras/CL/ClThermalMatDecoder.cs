using System;
using HeatingCameraSystem.Core.Models;
using OpenCvSharp;

namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    /// <summary>
    /// UVC 프레임 <see cref="Mat"/>을 <see cref="ThermalFrame"/>으로 변환한다.
    /// <para>
    /// 제품의 카메라 출력 포맷 레지스터(<c>참고/util/DataPacket.h</c>: <c>OUT_Y16=0x01</c>,
    /// <c>OUT_UYVY=0x00</c>)에 따라 드라이버가 주는 Mat 타입이 달라진다. FourCC로 Y16을 요청해도
    /// 제품이 UYVY 모드면 8비트 프레임이 오므로 두 경우를 모두 받는다 — CV_16UC1만 받고 나머지를
    /// 버리면 UYVY 모드 제품이 프레임 기아에 빠져 저장이 통째로 실패한다.
    /// </para>
    /// </summary>
    public static class ClThermalMatDecoder
    {
        private const ushort ThermalMask = 0x3FFF;

        /// <summary>지원하지 않는 포맷이거나 버퍼가 프레임 크기보다 작으면 null을 반환한다.</summary>
        public static ThermalFrame? Decode(Mat? mat, DateTimeOffset timestamp)
        {
            if (mat is null || mat.Empty()) return null;

            MatType type = mat.Type();
            if (type == MatType.CV_16UC1) return DecodeY16(mat, timestamp);
            if (type == MatType.CV_8UC2) return DecodeVideo(mat, ColorConversionCodes.YUV2BGR_UYVY, timestamp);
            if (type == MatType.CV_8UC3) return DecodeVideo(mat, null, timestamp);
            return null;
        }

        private static ThermalFrame? DecodeY16(Mat mat, DateTimeOffset timestamp)
        {
            var pixels = new ushort[mat.Width * mat.Height];
            Span<ushort> source = mat.AsSpan<ushort>();
            if (source.Length < pixels.Length) return null;

            source[..pixels.Length].CopyTo(pixels);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] &= ThermalMask;
            }

            return new ThermalFrame(pixels, mat.Width, mat.Height, timestamp);
        }

        // UYVY 모드에는 온도 정보가 없다. 카메라 출력을 그대로 남기려고 BGR24를 싣고, Pixels에는
        // 휘도를 채워 단일 채널 소비자(.y16 메타데이터, 표시 컨버터, FPA 평균 계산)가 깨지지 않게 한다.
        private static ThermalFrame? DecodeVideo(Mat mat, ColorConversionCodes? toBgr, DateTimeOffset timestamp)
        {
            using var converted = new Mat();
            Mat bgr = mat;
            if (toBgr is ColorConversionCodes code)
            {
                Cv2.CvtColor(mat, converted, code);
                bgr = converted;
            }

            if (bgr.Empty() || bgr.Type() != MatType.CV_8UC3) return null;

            int width = bgr.Width;
            int height = bgr.Height;

            var bgrBytes = new byte[width * height * 3];
            Span<byte> bgrSource = bgr.AsSpan<byte>();
            if (bgrSource.Length < bgrBytes.Length) return null;
            bgrSource[..bgrBytes.Length].CopyTo(bgrBytes);

            using var gray = new Mat();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

            var pixels = new ushort[width * height];
            Span<byte> luma = gray.AsSpan<byte>();
            if (luma.Length < pixels.Length) return null;
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = luma[i];
            }

            return new ThermalFrame(pixels, width, height, timestamp) { Bgr24 = bgrBytes };
        }
    }
}
