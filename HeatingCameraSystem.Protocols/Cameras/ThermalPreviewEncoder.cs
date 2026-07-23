using HeatingCameraSystem.Core.Models;
using OpenCvSharp;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// Encodes a 14-bit Y16 <see cref="ThermalFrame"/> into a viewable 8-bit image (min/max
    /// normalized) via OpenCV imgcodecs. Used for the NATS capture-result payload so the Master
    /// keeps receiving displayable bytes exactly as it did from the console Agent's JPG. The
    /// radiometric data is preserved separately in the local .y16 files.
    /// </summary>
    public static class ThermalPreviewEncoder
    {
        public static byte[] EncodeJpeg(ThermalFrame frame) => Encode(frame, ".jpg");

        public static byte[] EncodePng(ThermalFrame frame) => Encode(frame, ".png");

        /// <summary>
        /// Encodes a frame as a false-color JPEG (plateau AGC + iron via <see cref="ThermalColorizer"/>)
        /// for the NATS live-preview stream, so Master shows the same thermal look as the AgentUI preview.
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
