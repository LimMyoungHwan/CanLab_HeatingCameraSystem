using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.AgentUI.Services
{
    /// <summary>
    /// Converts a 14-bit Y16 <see cref="ThermalFrame"/> to a frozen 8-bit grayscale
    /// <see cref="BitmapSource"/> for live display (min/max normalized). The result is frozen,
    /// so it may be produced on the camera loop thread and assigned on the UI thread.
    /// NOTE: preview only — the 14-bit radiometric data is NOT preserved here (S4 persists raw).
    /// Duplicated from Master by design (AgentUI must not reference the Master project).
    /// </summary>
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
