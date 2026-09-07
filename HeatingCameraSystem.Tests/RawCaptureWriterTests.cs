using System;
using System.IO;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class RawCaptureWriterTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "rawcap_" + Guid.NewGuid().ToString("N"));

        private static ThermalFrame Frame(params ushort[] pixels)
            => new(pixels, pixels.Length, 1, DateTimeOffset.Now);

        [Fact]
        public void Write_StoresPixelsAsLittleEndianWords()
        {
            ThermalFrame frame = Frame(0x0102, 0x0304);

            string path = RawCaptureWriter.Write(_dir, "BB80_000.raw", frame, fpaRaw: null);

            Assert.Equal(new byte[] { 0x02, 0x01, 0x04, 0x03 }, File.ReadAllBytes(path));
        }

        [Fact]
        public void Write_PutsFpaRawInFirstPixel()
        {
            ThermalFrame frame = Frame(0x0102, 0x0304);

            string path = RawCaptureWriter.Write(_dir, "BB80_000.raw", frame, fpaRaw: 16560);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(16560, BitConverter.ToInt16(bytes, 0));
            Assert.Equal(0x0304, BitConverter.ToUInt16(bytes, 2));
        }

        [Fact]
        public void Write_NegativeFpaRaw_RoundTrips()
        {
            string path = RawCaptureWriter.Write(_dir, "BB80_000.raw", Frame(0, 0), fpaRaw: -1234);

            Assert.Equal(-1234, BitConverter.ToInt16(File.ReadAllBytes(path), 0));
        }

        [Fact]
        public void Write_DoesNotMutateFrame()
        {
            ThermalFrame frame = Frame(0x0102, 0x0304);

            RawCaptureWriter.Write(_dir, "BB80_000.raw", frame, fpaRaw: 16560);

            Assert.Equal(0x0102, frame.Pixels[0]);
        }

        [Fact]
        public void Write_SameFileName_Overwrites()
        {
            RawCaptureWriter.Write(_dir, "BB80_000.raw", Frame(1, 2, 3), fpaRaw: null);
            string path = RawCaptureWriter.Write(_dir, "BB80_000.raw", Frame(9, 9), fpaRaw: null);

            Assert.Single(Directory.GetFiles(_dir));
            Assert.Equal(4, new FileInfo(path).Length);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
    }
}
