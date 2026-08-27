using System;
using System.Threading;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
    /// <summary>
    /// SimulationMode와 테스트를 위한 결정적 합성 열화상 프레임 소스.
    /// CltcThermalFrameSource(OpenCV 캡처) 대신 사용한다. 읽을 때마다 이동 핫스팟이 있는
    /// 640x480 14-bit 프레임을 생성한다(<see cref="FakeLiveThermalCamera"/>와 동일한 패턴).
    /// </summary>
    public sealed class FakeThermalFrameSource : IThermalFrameSource
    {
        private const int Width = 640;
        private const int Height = 480;
        private int _tick;

        /// <summary>열 물리 핸들이 없으므로 아무 동작도 하지 않는다.</summary>
        public void Open()
        {
        }

        /// <summary>호출마다 tick을 올려 새 합성 프레임을 만든다. 실제 하드웨어와 달리 null을 반환하거나 블록되는 일이 없다.</summary>
        public ThermalFrame? Read() => CreateFrame(Interlocked.Increment(ref _tick));

        /// <summary>해제할 물리 핸들이 없으므로 아무 동작도 하지 않는다.</summary>
        public void Close()
        {
        }

        public void Dispose()
        {
        }

        /// <summary>
        /// tick에서만 파생되는 결정적 합성 프레임을 만든다. 배경은 대각선 그라데이션,
        /// 그 위에 반지름 42px의 핫스팟이 tick마다 (11, 7)픽셀씩 이동한다. 값 범위는 0~0x3FFF(14-bit).
        /// </summary>
        private static ThermalFrame CreateFrame(int tick)
        {
            var pixels = new ushort[Width * Height];
            int centerX = tick * 11 % Width;
            int centerY = tick * 7 % Height;
            const int radius = 42;
            const int radiusSquared = radius * radius;

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int value = (x * 7 + y * 5 + tick * 31) & 0x0FFF;
                    int dx = x - centerX;
                    int dy = y - centerY;
                    int distanceSquared = dx * dx + dy * dy;

                    if (distanceSquared <= radiusSquared)
                    {
                        value = 0x3FFF - distanceSquared * 0x1000 / radiusSquared;
                    }

                    pixels[y * Width + x] = (ushort)Math.Clamp(value, 0, 0x3FFF);
                }
            }

            return new ThermalFrame(pixels, Width, Height, DateTimeOffset.Now);
        }
    }
}
