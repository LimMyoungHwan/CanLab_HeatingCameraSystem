using System;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 열화상 프레임 한 장. <see cref="Pixels"/>는 <see cref="Width"/>×<see cref="Height"/> 개의
    /// 16bit 원시값을 담는다.
    /// </summary>
    /// <param name="Pixels">16bit 원시 열화상 픽셀 배열.</param>
    /// <param name="Width">프레임 너비(픽셀).</param>
    /// <param name="Height">프레임 높이(픽셀).</param>
    /// <param name="Timestamp">프레임 캡처 시각.</param>
    public sealed record ThermalFrame(ushort[] Pixels, int Width, int Height, DateTimeOffset Timestamp)
    {
        /// <summary>
        /// 카메라가 UYVY(YUV422) 모드로 출력할 때만 채워지는 BGR24 바이트. 이 모드에는 방사 측정
        /// 데이터가 없다 — 카메라가 AGC와 컬러맵을 이미 적용한 8비트 영상만 나온다. 그래서
        /// <see cref="Pixels"/>에는 휘도(Y)가 들어가 단일 채널 소비자가 그대로 동작하지만, 그 값을
        /// 온도로 환산하거나 <c>.raw</c>로 저장해선 안 된다. null이면 14비트 방사 측정 프레임이다.
        /// </summary>
        public byte[]? Bgr24 { get; init; }

        /// <summary>온도 환산과 <c>.raw</c> 저장이 유효한 프레임이면 true.</summary>
        public bool IsRadiometric => Bgr24 is null;
    }
}
