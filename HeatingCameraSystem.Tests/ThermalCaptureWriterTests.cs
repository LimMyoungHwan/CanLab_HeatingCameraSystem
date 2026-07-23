using System;
using System.IO;
using System.Text.Json;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class ThermalCaptureWriterTests
    {
        [Fact]
        public void Write_PersistsRadiometricY16_AndSelfDescribingSidecar()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_capture_" + Guid.NewGuid().ToString("N"));
            try
            {
                var pixels = new ushort[] { 0, 100, 0x3FFF, 250 };
                var frame = new ThermalFrame(pixels, 2, 2, DateTimeOffset.Now);
                var writer = new ThermalCaptureWriter(dir);

                CaptureFiles files = writer.Write(frame, "PC_cam0", cameraIndex: 0, recipeStepId: "step1");

                Assert.True(File.Exists(files.Y16Path));
                Assert.True(File.Exists(files.JsonPath));

                byte[] raw = File.ReadAllBytes(files.Y16Path);
                Assert.Equal(pixels.Length * 2, raw.Length);

                var readBack = new ushort[pixels.Length];
                Buffer.BlockCopy(raw, 0, readBack, 0, raw.Length);
                Assert.Equal(pixels, readBack);

                CaptureMetadata? meta = JsonSerializer.Deserialize<CaptureMetadata>(File.ReadAllText(files.JsonPath));
                Assert.NotNull(meta);
                Assert.Equal(2, meta!.Width);
                Assert.Equal(2, meta.Height);
                Assert.Equal("PC_cam0", meta.AgentId);
                Assert.Equal(0, meta.CameraIndex);
                Assert.Equal("step1", meta.RecipeStepId);
                Assert.Equal((ushort)0, meta.Min);
                Assert.Equal((ushort)0x3FFF, meta.Max);
                Assert.Equal("Y16_14bit_LE", meta.PixelFormat);
                Assert.Equal(Path.GetFileName(files.Y16Path), meta.Y16File);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
