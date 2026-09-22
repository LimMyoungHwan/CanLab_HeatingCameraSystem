using System;
using System.IO;
using System.Linq;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Protocols.Cameras.CL;
using OpenCvSharp;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    /// <summary>
    /// 제품이 UYVY(YUV422) 출력 모드일 때의 촬영 저장 계약. 예전에는 CV_16UC1이 아닌 프레임을
    /// 전부 버려 프레임 기아로 저장이 통째로 실패했고, UYVY에는 방사 측정 데이터가 없으므로
    /// 지금도 <c>.raw</c>로는 내려가지 않아야 한다.
    /// </summary>
    public class UyvyModeCaptureTests : IDisposable
    {
        private readonly string _buffer = Path.Combine(Path.GetTempPath(), "uyvy_" + Guid.NewGuid().ToString("N"));

        private static readonly CameraDescriptor Camera =
            new("Agent_1", 0, "CAM-01", CameraSerialNumber: "544112136");

        private static CaptureCommandMessage RawRun() => new()
        {
            StorageRootUnc = @"\\MASTER-PC\HeatingData",
            ProductNumber = "ABC",
            ConditionFolder = "RPP40",
            BlackBodyFolder = "cold",
            FilePrefix = "BB20",
            SaveFormat = ProductionCaptureFormat.Raw
        };

        // UYVY는 픽셀 두 개가 U Y0 V Y1 네 바이트를 공유하므로 너비는 짝수여야 한다.
        // U=V=128(무채색), 휘도는 200/60을 교대로 둬 변환 결과에 명암 차이가 남게 한다.
        private static Mat UyvyMat(int width, int height)
        {
            var bytes = new byte[width * height * 2];
            for (int i = 0; i < bytes.Length; i += 4)
            {
                bytes[i] = 128;
                bytes[i + 1] = 200;
                bytes[i + 2] = 128;
                bytes[i + 3] = 60;
            }

            return Mat.FromPixelData(height, width, MatType.CV_8UC2, bytes);
        }

        private static ThermalFrame UyvyFrame(int width = 4, int height = 2)
        {
            using Mat mat = UyvyMat(width, height);
            return ClThermalMatDecoder.Decode(mat, DateTimeOffset.Now)
                   ?? throw new InvalidOperationException("UYVY decode returned null.");
        }

        [Fact]
        public void Decode_UyvyMat_YieldsNonRadiometricFrameCarryingBgr24()
        {
            ThermalFrame frame = UyvyFrame();

            Assert.False(frame.IsRadiometric);
            Assert.Equal(4, frame.Width);
            Assert.Equal(2, frame.Height);
            Assert.Equal(4 * 2, frame.Pixels.Length);
            Assert.Equal(4 * 2 * 3, frame.Bgr24!.Length);
            Assert.True(frame.Pixels.Max() > frame.Pixels.Min());
        }

        [Fact]
        public void Decode_Y16Mat_StaysRadiometricAndMasksTo14Bit()
        {
            var pixels = new ushort[] { 0xFFFF, 0x0001, 0x4000, 0x3FFF };
            using Mat mat = Mat.FromPixelData(2, 2, MatType.CV_16UC1, pixels);

            ThermalFrame? frame = ClThermalMatDecoder.Decode(mat, DateTimeOffset.Now);

            Assert.NotNull(frame);
            Assert.True(frame!.IsRadiometric);
            Assert.Null(frame.Bgr24);
            Assert.Equal(new ushort[] { 0x3FFF, 0x0001, 0x0000, 0x3FFF }, frame.Pixels);
        }

        [Fact]
        public void Decode_UnsupportedMatType_ReturnsNull()
        {
            using var mat = new Mat(2, 2, MatType.CV_32FC1, Scalar.All(0));

            Assert.Null(ClThermalMatDecoder.Decode(mat, DateTimeOffset.Now));
        }

        [Fact]
        public void WriteShot_RawRunWithUyvyFrame_WritesJpegInstead()
        {
            var sink = new ProductionCaptureSink(_buffer);

            string path = sink.WriteShot(Camera, RawRun(), UyvyFrame(), fpaRaw: 16560, shotIndex: 3);

            Assert.Equal(
                Path.Combine(_buffer, "544112136_ABC", "RPP40", "cold", "BB20_003.jpg"),
                path);
            Assert.True(new FileInfo(path).Length > 0);
        }

        [Fact]
        public void RawCaptureWriter_RefusesUyvyFrame()
        {
            Assert.Throws<ArgumentException>(
                () => RawCaptureWriter.Write(_buffer, "BB20_000.raw", UyvyFrame(), fpaRaw: 16560));
        }

        [Fact]
        public void NucCorrector_LeavesUyvyFrameUntouched()
        {
            var nuc = new ThermalNucCorrector();
            nuc.CaptureFromFlat(new ThermalFrame(new ushort[] { 100, 101, 99, 100 }, 2, 2, DateTimeOffset.Now));

            ThermalFrame uyvy = UyvyFrame(2, 2);

            Assert.Same(uyvy, nuc.Apply(uyvy));
        }

        public void Dispose()
        {
            if (Directory.Exists(_buffer))
            {
                Directory.Delete(_buffer, recursive: true);
            }
        }
    }
}
