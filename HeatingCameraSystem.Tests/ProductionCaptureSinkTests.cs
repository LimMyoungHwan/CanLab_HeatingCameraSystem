using System;
using System.IO;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class ProductionCaptureSinkTests : IDisposable
    {
        private readonly string _buffer = Path.Combine(Path.GetTempPath(), "prodsink_" + Guid.NewGuid().ToString("N"));

        private static readonly CameraDescriptor Camera =
            new("Agent_1", 0, "CAM-01", CameraSerialNumber: "544112136");

        private static CaptureCommandMessage Command(string condition, string blackBody, string prefix, bool bias = false) => new()
        {
            StorageRootUnc = @"\\MASTER-PC\HeatingData",
            ProductNumber = "ABC",
            ConditionFolder = condition,
            BlackBodyFolder = blackBody,
            FilePrefix = prefix,
            WriteBiasJson = bias
        };

        private static ThermalFrame Frame() => new(new ushort[] { 1, 2, 3, 4 }, 2, 2, DateTimeOffset.Now);

        [Fact]
        public void WriteShot_BuildsCustomerFolderTree()
        {
            var sink = new ProductionCaptureSink(_buffer);

            string path = sink.WriteShot(Camera, Command("RPP40", "cold", "BB20"), Frame(), fpaRaw: 16560, shotIndex: 7);

            Assert.Equal(
                Path.Combine(_buffer, "544112136_ABC", "RPP40", "cold", "BB20_007.raw"),
                path);
        }

        [Fact]
        public void WriteShot_NoSerialNumber_UsesUnknownFolder()
        {
            var sink = new ProductionCaptureSink(_buffer);
            var camera = Camera with { CameraSerialNumber = null };

            string path = sink.WriteShot(camera, Command("LNN30", "room", "BBroom"), Frame(), fpaRaw: null, shotIndex: 0);

            Assert.Contains(Path.Combine("UNKNOWN_ABC", "LNN30", "room"), path);
        }

        [Fact]
        public void WriteBiasJson_LandsOnConditionFolderNotBlackBodyFolder()
        {
            var sink = new ProductionCaptureSink(_buffer);

            string path = sink.WriteBiasJson(Camera, Command("RPP40", "cold", "BB20", bias: true), "{}");

            Assert.Equal(Path.Combine(_buffer, "544112136_ABC", "RPP40", "bias.json"), path);
        }

        [Fact]
        public void PendingFiles_CountsOnlyRawFiles()
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("RPP40", "hot", "BB80");

            sink.WriteShot(Camera, cmd, Frame(), null, 0);
            sink.WriteShot(Camera, cmd, Frame(), null, 1);
            sink.WriteBiasJson(Camera, cmd, "{}");

            Assert.Equal(2, sink.PendingFiles);
        }

        [Fact]
        public void WriteShot_JpegFormat_KeepsFolderRuleAndOnlyChangesExtension()
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("RPP40", "cold", "BB20");
            cmd.SaveFormat = ProductionCaptureFormat.Jpeg;

            string path = sink.WriteShot(Camera, cmd, Frame(), fpaRaw: 16560, shotIndex: 7);

            Assert.Equal(
                Path.Combine(_buffer, "544112136_ABC", "RPP40", "cold", "BB20_007.jpg"),
                path);
            Assert.True(new FileInfo(path).Length > 0);
        }

        [Fact]
        public void PendingFiles_CountsJpegRuns_SoFailedTransfersAreNotReportedComplete()
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("RPP40", "hot", "BB80");
            cmd.SaveFormat = ProductionCaptureFormat.Jpeg;

            sink.WriteShot(Camera, cmd, Frame(), null, 0);
            sink.WriteShot(Camera, cmd, Frame(), null, 1);

            Assert.Equal(2, sink.PendingFiles);
        }

        [Theory]
        [InlineData("", "RPP40", "cold", "BB20")]
        [InlineData(@"\\M\D", "", "cold", "BB20")]
        [InlineData(@"\\M\D", "RPP40", "", "BB20")]
        [InlineData(@"\\M\D", "RPP40", "cold", "")]
        public void IsEnabled_RequiresEveryStorageField(string root, string condition, string blackBody, string prefix)
        {
            var cmd = new CaptureCommandMessage
            {
                StorageRootUnc = root,
                ConditionFolder = condition,
                BlackBodyFolder = blackBody,
                FilePrefix = prefix
            };

            Assert.False(ProductionCaptureSink.IsEnabled(cmd));
        }

        [Fact]
        public void IsEnabled_AllFieldsPresent()
        {
            Assert.True(ProductionCaptureSink.IsEnabled(Command("RPP40", "cold", "BB20")));
        }

        [Fact]
        public async Task SameFolderReshoot_OverwritesNumberZeroButKeepsOldTailAfterRealRobocopy()
        {
            string master = Path.Combine(Path.GetTempPath(), "prodsink_master_" + Guid.NewGuid().ToString("N"));
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("RPP40", "hot", "BB80");
            cmd.StorageRootUnc = master;
            try
            {
                sink.WriteShot(Camera, cmd, Frame(), null, 0);
                sink.WriteShot(Camera, cmd, Frame(), null, 1);
                await sink.SyncFolderAsync(Camera, cmd);
                string destination = Path.Combine(master, ProductionCaptureSink.RelativeDirectory(Camera, cmd));
                Assert.Equal(2, Directory.GetFiles(destination, "*.raw").Length);
                // Different byte length also makes robocopy's changed-file decision deterministic.
                var replacement = new ThermalFrame(new ushort[] { 99, 100 }, 2, 1, DateTimeOffset.Now);
                sink.WriteShot(Camera, cmd, replacement, null, 0);
                await sink.SyncFolderAsync(Camera, cmd);
                Assert.Equal(2, Directory.GetFiles(destination, "*.raw").Length);
                Assert.Equal(new byte[] { 99, 0, 100, 0 }, File.ReadAllBytes(Path.Combine(destination, "BB80_000.raw")));
                Assert.Equal(new byte[] { 1, 0, 2, 0, 3, 0, 4, 0 }, File.ReadAllBytes(Path.Combine(destination, "BB80_001.raw")));
            }
            finally
            {
                if (Directory.Exists(master)) Directory.Delete(master, true);
            }
        }

        [Theory]
        [InlineData(0, 0, "BB80_000.raw")]
        [InlineData(100, 0, "BB80_100.raw")]
        [InlineData(100, 99, "BB80_199.raw")]
        [InlineData(-5, 2, "BB80_002.raw")]
        public void WriteShot_AppliesShotNumberOffset(int offset, int shotIndex, string expectedName)
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("RPP40", "hot", "BB80");
            cmd.ShotNumberOffset = offset;

            string path = sink.WriteShot(Camera, cmd, Frame(), fpaRaw: null, shotIndex: shotIndex);

            Assert.Equal(expectedName, Path.GetFileName(path));
            Assert.Equal(1, sink.PendingFiles);
        }

        public void Dispose()
        {
            if (Directory.Exists(_buffer)) Directory.Delete(_buffer, recursive: true);
        }
    }
}
