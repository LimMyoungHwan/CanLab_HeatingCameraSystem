using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 앱 전역 라이브 미리보기 컬러맵 토글. NATS 라이브 스트림은 iron 팔레트 컬러 JPEG로 도착하며,
    /// 그레이스케일 모드는 표시 시점에 <see cref="PixelFormats.Gray8"/>로 변환한다.
    /// iron 램프는 설계상 휘도 단조라서 Gray8 휘도만으로 팔레트 적용 전 AGC 그레이스케일이
    /// 복원된다 — Y16 왕복도, Agent 명령도 필요 없다(Master 측 전용).
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

        /// <summary>그레이스케일 모드면 frozen 그레이스케일 복사본을, 아니면 원본을 그대로 돌려준다.</summary>
        public static BitmapSource Apply(BitmapSource src)
        {
            if (!_grayscale) return src;
            var gray = new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0);
            gray.Freeze();
            return gray;
        }
    }
}
