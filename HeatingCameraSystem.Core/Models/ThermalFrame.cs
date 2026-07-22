using System;

namespace HeatingCameraSystem.Core.Models
{
    public sealed record ThermalFrame(ushort[] Pixels, int Width, int Height, DateTimeOffset Timestamp);
}
