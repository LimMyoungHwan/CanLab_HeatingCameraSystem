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
    public sealed record ThermalFrame(ushort[] Pixels, int Width, int Height, DateTimeOffset Timestamp);
}
