using System;
using System.IO;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>저장된 .y16 원본을 <see cref="CaptureRecord"/>의 크기 정보로 <see cref="ThermalFrame"/>으로 복원한다.</summary>
    public static class ThermalFrameReader
    {
        /// <summary>
        /// <see cref="CaptureRecord.Y16Path"/>의 원본을 읽어 프레임으로 복원한다.
        /// 파일이 기대 크기보다 짧으면 있는 만큼만 복사하고 나머지 픽셀은 0으로 남는다.
        /// </summary>
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
