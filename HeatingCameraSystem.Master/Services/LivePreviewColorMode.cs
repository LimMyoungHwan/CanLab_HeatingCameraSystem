using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HeatingCameraSystem.Protocols.Cameras;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 앱 전역 라이브 미리보기 컬러맵 토글. NATS 라이브 스트림은 min/max 정규화 그레이 JPEG로 도착한다.
    /// 그레이 모드: 그대로 표시. 컬러 모드: gray8 픽셀에 iron LUT를 적용해 BGR24 BitmapSource로 변환.
    /// 화면들은 선택 메뉴를 동기화하기 위해 <see cref="Changed"/>를 구독한다.
    /// </summary>
    public static class LivePreviewColorMode
    {
        private static bool _grayscale;

        public static bool Grayscale => _grayscale;

        public static event Action? Changed;

        /// <summary>모드를 바꾼다. 실제로 값이 달라졌을 때만 <see cref="Changed"/>를 발생시킨다.</summary>
        public static void SetGrayscale(bool value)
        {
            if (_grayscale == value) return;
            _grayscale = value;
            Changed?.Invoke();
        }

        /// <summary>
        /// 그레이 모드면 원본을 그대로, 컬러 모드면 iron LUT를 적용한 BGR24 BitmapSource를 반환한다.
        /// </summary>
        public static BitmapSource Apply(BitmapSource src)
        {
            if (_grayscale) return src;

            // 수신 JPEG는 Gray8 또는 Bgr24로 디코딩될 수 있다. Gray8로 통일 후 iron 적용.
            BitmapSource gray8Src = src.Format == PixelFormats.Gray8
                ? src
                : new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0);

            int w = gray8Src.PixelWidth;
            int h = gray8Src.PixelHeight;
            var gray8 = new byte[w * h];
            gray8Src.CopyPixels(new Int32Rect(0, 0, w, h), gray8, w, 0);

            byte[] bgr = ThermalColorizer.Gray8ToBgr24(gray8);

            var bmp = new WriteableBitmap(w, h, src.DpiX, src.DpiY, PixelFormats.Bgr24, null);
            bmp.WritePixels(new Int32Rect(0, 0, w, h), bgr, w * 3, 0);
            bmp.Freeze();
            return bmp;
        }
    }
}
