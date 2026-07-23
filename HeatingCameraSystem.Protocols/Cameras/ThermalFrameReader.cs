using System;
using System.IO;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    public static class ThermalFrameReader
    {
        public static ThermalFrame Read(CaptureRecord record)
        {
            byte[] bytes = File.ReadAllBytes(record.Y16Path);
            var pixels = new ushort[record.Width * record.Height];
            int copyBytes = Math.Min(bytes.Length, pixels.Length * sizeof(ushort));
            Buffer.BlockCopy(bytes, 0, pixels, 0, copyBytes);

            return new ThermalFrame(
                pixels,
                record.Width,
                record.Height,
                new DateTimeOffset(DateTime.SpecifyKind(record.TimestampUtc, DateTimeKind.Utc)));
        }
    }
}
