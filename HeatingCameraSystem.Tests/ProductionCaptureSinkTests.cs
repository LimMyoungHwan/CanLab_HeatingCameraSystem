using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class ProductionCaptureSinkTests : IDisposable
    {
        private readonly string _buffer = Path.Combine(Path.GetTempPath(), "prodsink_" + Guid.NewGuid().ToString("N"));
        private readonly string _destination = Path.Combine(Path.GetTempPath(), "prodsink_dest_" + Guid.NewGuid().ToString("N"));
        private const string ValidBiasJson = "{\"CINT\":\"93\",\"TINT\":\"273\",\"GSK_MSB\":1,\"GSK_LSB\":\"42\",\"GFID\":\"AE\"}";

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
        public void PendingFiles_CountsRawAndBiasFiles()
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("RPP40", "hot", "BB80");

            sink.WriteShot(Camera, cmd, Frame(), null, 0);
            sink.WriteShot(Camera, cmd, Frame(), null, 1);
            sink.WriteBiasJson(Camera, cmd, "{}");

            Assert.Equal(3, sink.PendingFiles);
        }

        [Fact]
        public async Task WriteAndVerifyBiasJson_CompletesOnlyAfterFinalContentsMatch()
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("LNN30", "cold", "BB10", bias: true);
            cmd.StorageRootUnc = _destination;

            string path = await sink.WriteAndVerifyBiasJsonAsync(Camera, cmd, ValidBiasJson);

            Assert.Equal(Path.Combine(_destination, "544112136_ABC", "LNN30", "bias.json"), path);
            Assert.Equal(ValidBiasJson, await File.ReadAllTextAsync(path));
            Assert.False(File.Exists(Path.Combine(_destination, "544112136_ABC", "LNN30", "cold", "bias.json")));
            Assert.Equal(0, sink.PendingFiles);
            Assert.Empty(Directory.EnumerateFiles(_destination, "*.tmp", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task WriteAndVerifyBiasJson_ReplacesPreviousContents()
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("LNN30", "cold", "BB10", bias: true);
            cmd.StorageRootUnc = _destination;
            string directory = Path.Combine(_destination, "544112136_ABC", "LNN30");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "bias.json");
            await File.WriteAllTextAsync(path, ValidBiasJson.Replace("\"42\"", "\"01\""));

            string result = await sink.WriteAndVerifyBiasJsonAsync(Camera, cmd, ValidBiasJson);

            Assert.Equal(path, result);
            Assert.Equal(ValidBiasJson, await File.ReadAllTextAsync(path));
        }

        [Fact]
        public async Task WriteAndVerifyBiasJson_LockedPreviousFileDoesNotCountAsSuccess()
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("LNN30", "cold", "BB10", bias: true);
            cmd.StorageRootUnc = _destination;
            string directory = Path.Combine(_destination, "544112136_ABC", "LNN30");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "bias.json");
            string previous = ValidBiasJson.Replace("\"42\"", "\"01\"");
            await File.WriteAllTextAsync(path, previous);
            using var lockedFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            Exception? error = await Record.ExceptionAsync(() => sink.WriteAndVerifyBiasJsonAsync(Camera, cmd, ValidBiasJson));
            // Windows는 잠긴 파일의 교체를 접근 거부로도 보고한다. 저장 실패를 전파해야 한다.
            Assert.True(error is IOException or UnauthorizedAccessException,
                $"파일 잠금 오류가 필요합니다. 실제: {error?.GetType().Name ?? "예외 없음"}");

            Assert.Equal(previous, await File.ReadAllTextAsync(path));
            Assert.Equal(1, sink.PendingFiles);
            Assert.Empty(Directory.EnumerateFiles(_destination, "*.tmp", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task WriteAndVerifyBiasJson_UnwritableDestinationKeepsPendingBiasAndFails()
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("LNN30", "cold", "BB10", bias: true);
            cmd.StorageRootUnc = _destination;
            await File.WriteAllTextAsync(_destination, "폴더로 사용할 수 없는 경로");

            await Assert.ThrowsAnyAsync<IOException>(() => sink.WriteAndVerifyBiasJsonAsync(Camera, cmd, ValidBiasJson));

            Assert.Equal(1, sink.PendingFiles);
            Assert.Equal(ValidBiasJson, await File.ReadAllTextAsync(Path.Combine(_buffer, "544112136_ABC", "LNN30", "bias.json")));
        }

        [Fact]
        public async Task WriteAndVerifyBiasJson_CancelledRequestDoesNotWriteFiles()
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("LNN30", "cold", "BB10", bias: true);
            cmd.StorageRootUnc = _destination;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sink.WriteAndVerifyBiasJsonAsync(Camera, cmd, ValidBiasJson, cancellation.Token));

            Assert.False(Directory.Exists(_buffer));
            Assert.False(Directory.Exists(_destination));
        }

        [Fact]
        public async Task WriteAndVerifyBiasJson_SameBufferAndDestinationPreservesFinalFile()
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("LNN30", "cold", "BB10", bias: true);
            cmd.StorageRootUnc = _buffer;

            string path = await sink.WriteAndVerifyBiasJsonAsync(Camera, cmd, ValidBiasJson);

            Assert.Equal(ValidBiasJson, await File.ReadAllTextAsync(path));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("{\"CINT\":\"ZZ\",\"TINT\":\"273\",\"GSK_MSB\":1,\"GSK_LSB\":\"42\",\"GFID\":\"AE\"}")]
        public async Task WriteAndVerifyBiasJson_RejectsInvalidPayloadBeforeWriting(string payload)
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("LNN30", "cold", "BB10", bias: true);
            cmd.StorageRootUnc = _destination;

            await Assert.ThrowsAsync<InvalidDataException>(() => sink.WriteAndVerifyBiasJsonAsync(Camera, cmd, payload));

            Assert.False(Directory.Exists(_buffer));
            Assert.False(Directory.Exists(_destination));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task WriteAndVerifyBiasJson_RejectsMissingStorageContext(bool missingRoot)
        {
            var sink = new ProductionCaptureSink(_buffer);
            CaptureCommandMessage cmd = Command("LNN30", "cold", "BB10", bias: true);
            cmd.StorageRootUnc = missingRoot ? "" : _destination;
            if (!missingRoot) cmd.ConditionFolder = "";

            await Assert.ThrowsAsync<InvalidOperationException>(() => sink.WriteAndVerifyBiasJsonAsync(Camera, cmd, ValidBiasJson));

            Assert.False(Directory.Exists(_buffer));
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

        public void Dispose()
        {
            if (Directory.Exists(_buffer)) Directory.Delete(_buffer, recursive: true);
            if (Directory.Exists(_destination)) Directory.Delete(_destination, recursive: true);
            if (File.Exists(_destination)) File.Delete(_destination);
        }
    }
}
