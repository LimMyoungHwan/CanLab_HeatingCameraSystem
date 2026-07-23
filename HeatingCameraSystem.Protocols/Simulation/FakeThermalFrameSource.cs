using System;
using System.Threading;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
    /// <summary>
    /// Deterministic synthetic thermal frame source for SimulationMode and tests.
    /// Produces a 640x480 14-bit frame with a moving hot spot on each read
    /// (same pattern as <see cref="FakeLiveThermalCamera"/>).
    /// </summary>
    public sealed class FakeThermalFrameSource : IThermalFrameSource
    {
        private const int Width = 640;
        private const int Height = 480;
        private int _tick;

        public void Open()
        {
        }

        public ThermalFrame? Read() => CreateFrame(Interlocked.Increment(ref _tick));

        public void Close()
        {
        }

        public void Dispose()
        {
        }

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
