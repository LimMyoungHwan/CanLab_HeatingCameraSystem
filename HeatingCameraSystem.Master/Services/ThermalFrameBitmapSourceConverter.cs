using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>Y16 열화상 프레임(<see cref="ThermalFrame"/>)을 WPF에서 표시 가능한 비트맵으로 바꾸는 헬퍼.</summary>
    public static class ThermalFrameBitmapSourceConverter
    {
        /// <summary>
        /// 프레임의 min–max로 선형 정규화해 Gray8 비트맵을 만든다. 모든 픽셀이 같은 값이면 전부
        /// 0(검정)이 된다. 결과는 Freeze되어 만든 스레드와 무관하게 바인딩에 쓸 수 있다.
        /// 크기와 픽셀 수가 맞지 않으면 ArgumentException을 던진다.
        /// </summary>
        public static BitmapSource ToBitmapSource(ThermalFrame f)
        {
            if (f.Width <= 0 || f.Height <= 0 || f.Pixels.Length != f.Width * f.Height)
            {
                throw new ArgumentException("Thermal frame dimensions do not match pixel data.", nameof(f));
            }

            ushort min = ushort.MaxValue;
            ushort max = ushort.MinValue;
            foreach (ushort pixel in f.Pixels)
            {
                if (pixel < min) min = pixel;
                if (pixel > max) max = pixel;
            }

            var bytes = new byte[f.Pixels.Length];
            if (max > min)
            {
                double scale = 255.0 / (max - min);
                for (int i = 0; i < f.Pixels.Length; i++)
                {
                    bytes[i] = (byte)((f.Pixels[i] - min) * scale);
                }
            }

            var bmp = new WriteableBitmap(f.Width, f.Height, 96, 96, PixelFormats.Gray8, null);
            bmp.WritePixels(new Int32Rect(0, 0, f.Width, f.Height), bytes, f.Width, 0);
            bmp.Freeze();
            return bmp;
        }
    }
}
