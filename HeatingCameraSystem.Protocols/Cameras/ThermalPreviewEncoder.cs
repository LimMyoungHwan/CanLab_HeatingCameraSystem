using HeatingCameraSystem.Core.Models;
using OpenCvSharp;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// 14비트 Y16 <see cref="ThermalFrame"/>을 OpenCV imgcodecs로 표시 가능한 8비트 이미지
    /// (min/max 정규화)로 인코딩한다. NATS 캡처 결과 페이로드에 쓰여 Master가 콘솔 Agent의
    /// JPG 시절과 똑같이 표시 가능한 바이트를 계속 받게 한다. 방사 측정 데이터는 로컬 .y16
    /// 파일에 따로 보존된다.
    /// </summary>
    public static class ThermalPreviewEncoder
    {
        public static byte[] EncodeJpeg(ThermalFrame frame) => Encode(frame, ".jpg");

        public static byte[] EncodePng(ThermalFrame frame) => Encode(frame, ".png");

        /// <summary>
        /// NATS 라이브 미리보기 스트림용 false-color JPEG(plateau AGC + iron, <see cref="ThermalColorizer"/> 경유)로
        /// 인코딩한다. Master가 AgentUI 미리보기와 같은 열화상 룩을 보게 하기 위해서다.
        /// </summary>
        public static byte[] EncodeColorJpeg(ThermalFrame frame)
        {
            byte[] bgr = ThermalColorizer.ToBgr24(frame);
            using var mat = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC3, bgr);
            Cv2.ImEncode(".jpg", mat, out byte[] buffer);
            return buffer;
        }

        private static byte[] Encode(ThermalFrame frame, string ext)
        {
            byte[] gray8 = Normalize8(frame);
            using var mat = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC1, gray8);
            Cv2.ImEncode(ext, mat, out byte[] buffer);
            return buffer;
        }

        private static byte[] Normalize8(ThermalFrame frame)
        {
            ushort min = ushort.MaxValue;
            ushort max = ushort.MinValue;
            foreach (ushort p in frame.Pixels)
            {
                if (p < min) min = p;
                if (p > max) max = p;
            }

            var bytes = new byte[frame.Pixels.Length];
            if (max > min)
            {
                double scale = 255.0 / (max - min);
                for (int i = 0; i < frame.Pixels.Length; i++)
                {
                    bytes[i] = (byte)((frame.Pixels[i] - min) * scale);
                }
            }

            return bytes;
        }
    }
}
