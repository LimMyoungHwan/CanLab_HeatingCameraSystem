using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Simulator.Config;

namespace HeatingCameraSystem.Simulator.Cameras;

public sealed class SyntheticThermalScene
{
    private readonly FrameSettings _frame;
    private readonly Func<long> _getTick;

    public SyntheticThermalScene(FrameSettings frame, Func<long>? getTick = null)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _getTick = getTick ?? (() => Environment.TickCount64);
    }

    public ThermalFrame NextFrame(int cameraIndex)
    {
        long tick = _getTick();
        int width = _frame.Width;
        int height = _frame.Height;
        var pixels = new ushort[width * height];

        int seed = cameraIndex * 997;
        int centerX = (int)((tick * 11 + seed * 3) % width + width) % width;
        int centerY = (int)((tick * 7 + seed * 5) % height + height) % height;
        int radius = Math.Max(12, Math.Min(width, height) / 14);
        int radiusSquared = radius * radius;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int value = (x * 7 + y * 5 + (int)tick * 31 + seed) & 0x0FFF;
                int dx = x - centerX;
                int dy = y - centerY;
                int distanceSquared = dx * dx + dy * dy;

                if (distanceSquared <= radiusSquared)
                    value = 0x3FFF - distanceSquared * 0x1000 / radiusSquared;

                pixels[y * width + x] = (ushort)Math.Clamp(value, 0, 0x3FFF);
            }
        }

        return new ThermalFrame(pixels, width, height, DateTimeOffset.FromUnixTimeMilliseconds(tick));
    }
}
