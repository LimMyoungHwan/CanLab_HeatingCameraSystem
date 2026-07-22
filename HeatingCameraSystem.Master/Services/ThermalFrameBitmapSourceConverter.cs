using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    public static class ThermalFrameBitmapSourceConverter
    {
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
