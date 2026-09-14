using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.AgentUI.Services
{
    /// <summary>
    /// 카메라 루프 스레드에서 만들어 UI 스레드에서 할당할 수 있도록 Freeze한다.
    /// 미리보기 전용 — 14비트 방사 측정 데이터는 보존되지 않는다
    /// (원본은 따로 저장).
    /// </summary>
    public static class ThermalFrameBitmapSourceConverter
    {
        public static BitmapSource ToBitmapSource(ThermalFrame f)
        {
            if (f.Width <= 0 || f.Height <= 0 || f.Pixels.Length != f.Width * f.Height)
            {
                throw new ArgumentException("Thermal frame dimensions do not match pixel data.", nameof(f));
            }

            ushort[] px = f.Pixels;
            ushort min = ushort.MaxValue, max = ushort.MinValue;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i] < min) min = px[i];
                if (px[i] > max) max = px[i];
            }

            int w = f.Width, h = f.Height;
            var bytes = new byte[px.Length];
            if (max > min)
            {
                double scale = 255.0 / (max - min);
                for (int i = 0; i < px.Length; i++)
                {
                    bytes[i] = (byte)((px[i] - min) * scale);
                }
            }

            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Gray8, null);
            bmp.WritePixels(new Int32Rect(0, 0, w, h), bytes, w, 0);
            bmp.Freeze();
            return bmp;
        }
    }
}
