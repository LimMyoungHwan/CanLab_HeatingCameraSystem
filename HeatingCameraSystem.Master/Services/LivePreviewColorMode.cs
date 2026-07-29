using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// App-wide live-preview colormap toggle. The NATS live stream arrives as an iron-palette
    /// color JPEG; grayscale mode converts it to <see cref="PixelFormats.Gray8"/> at display time.
    /// The iron ramp is luminance-monotonic by design, so the Gray8 luma recovers the pre-palette
    /// AGC grayscale — no Y16 round-trip and no agent command needed (Master-side only).
    /// Views subscribe <see cref="Changed"/> to keep their selection menus in sync.
    /// </summary>
    public static class LivePreviewColorMode
    {
        private static bool _grayscale;

        public static bool Grayscale => _grayscale;

        public static event Action? Changed;

        public static void SetGrayscale(bool value)
        {
            if (_grayscale == value) return;
            _grayscale = value;
            Changed?.Invoke();
        }

        /// <summary>Returns a frozen grayscale copy when grayscale mode is on, else the source unchanged.</summary>
        public static BitmapSource Apply(BitmapSource src)
        {
            if (!_grayscale) return src;
            var gray = new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0);
            gray.Freeze();
            return gray;
        }
    }
}
