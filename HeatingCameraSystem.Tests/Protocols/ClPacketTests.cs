using System;
using System.Linq;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Protocols.Cameras.CL;
using Xunit;

namespace HeatingCameraSystem.Tests.Protocols
{
    public class ClPacketTests
    {
        [Fact]
        public void BuildRequest_ShutterOpenWrite_BuildsGoldenPacket()
        {
            byte[] request = ClPacket.BuildRequest(
                (byte)ClMainId.OperateCtrl,
                (byte)ClOperateCtrlSubId.Shutter,
                ClRw.Write,
                1);

            Assert.True(request.SequenceEqual(new byte[] { 0x43, 0x4C, 0x30, 0x01, 0x00, 0x00, 0x01 }));
        }

        [Fact]
        public void BuildRequest_BiasGskLsbWrite_BuildsGoldenPacket()
        {
            byte[] request = ClPacket.BuildRequest(
                (byte)ClMainId.Detector,
                (byte)ClDetectorSubId.GskLsb,
                ClRw.Write,
                0x7F);

            Assert.True(request.SequenceEqual(new byte[] { 0x43, 0x4C, 0x00, 0x06, 0x00, 0x00, 0x7F }));
        }

        [Fact]
        public void ExtractPayload_ValidClPacket_ReturnsLastByte()
        {
            byte payload = ClPacket.ExtractPayload(new byte[] { 0x43, 0x4C, 0, 0, 0, 0, 0x2A });

            Assert.Equal(0x2A, payload);
        }

        [Fact]
        public void ExtractPayload_InvalidHeader_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                ClPacket.ExtractPayload(new byte[] { 0x00, 0x00, 0, 0, 0, 0, 0 }));
        }

        [Fact]
        public void DecodeSerialNumber_GoldenBytes_ReturnsGoldenSerial()
        {
            string serial = ClPacket.DecodeSerialNumber(0x00, 0x01, 0x00, 0x14);

            Assert.Equal("000100020", serial);
        }

        [Fact]
        public void DecodeSerialNumber_MixedFields_ReturnsFormattedSerial()
        {
            string serial = ClPacket.DecodeSerialNumber(0x01, 0x02, 0x08, 0x03);

            Assert.Equal("025802003", serial);
        }

        [Fact]
        public void DecodeFpaTemperature_GoldenBytes_ReturnsExpectedTemperature()
        {
            double temperature = ClPacket.DecodeFpaTemperature(0x40, 0x00);

            Assert.Equal(29.12, temperature, 2);
        }

        [Fact]
        public void DecodeFpaTemperature_NegativeRaw_ReturnsExpectedTemperature()
        {
            double temperature = ClPacket.DecodeFpaTemperature(0xC0, 0x00);

            Assert.Equal(801.84, temperature, 2);
        }

        [Fact]
        public void DecodeFpaTemperatureRaw_PositiveBytes_ReturnsSignedWord()
        {
            Assert.Equal((short)0x4000, ClPacket.DecodeFpaTemperatureRaw(0x40, 0x00));
        }

        [Fact]
        public void DecodeFpaTemperatureRaw_HighBitSet_ReturnsNegativeWord()
        {
            Assert.Equal((short)-16384, ClPacket.DecodeFpaTemperatureRaw(0xC0, 0x00));
        }

        // 0xB7 = 1011 0111 : autoStart=1, dispMode=3, outFormat=1(Y16), captureType=1, opMode=1
        [Theory]
        [InlineData(0xB7, 0x01)]
        [InlineData(0xB3, 0x00)]
        [InlineData(0x00, 0x00)]
        [InlineData(0xFF, 0x03)]
        public void ExtractOutputFormat_ReadsBits3And2(int cameraConfig, int expected)
        {
            Assert.Equal((byte)expected, ClPacket.ExtractOutputFormat((byte)cameraConfig));
        }

        [Fact]
        public void ReplaceOutputFormat_UyvyToY16_KeepsEveryOtherField()
        {
            const byte uyvy = 0xB3; // autoStart=1, dispMode=3, outFormat=0, captureType=1, opMode=1

            byte updated = ClPacket.ReplaceOutputFormat(uyvy, (byte)CameraOutputFormat.Y16);

            Assert.Equal((byte)0xB7, updated);
            Assert.Equal(uyvy & 0xF3, updated & 0xF3);
        }

        [Fact]
        public void ReplaceOutputFormat_AllOtherBitsSet_OnlyClearsOutputFormat()
        {
            byte updated = ClPacket.ReplaceOutputFormat(0xFF, (byte)CameraOutputFormat.Uyvy);

            Assert.Equal((byte)0xF3, updated);
        }

        [Theory]
        [InlineData(0x00)]
        [InlineData(0x5A)]
        [InlineData(0xB3)]
        [InlineData(0xFF)]
        public void ReplaceOutputFormat_RoundTripsThroughExtract(int cameraConfig)
        {
            foreach (CameraOutputFormat format in new[] { CameraOutputFormat.Uyvy, CameraOutputFormat.Y16 })
            {
                byte updated = ClPacket.ReplaceOutputFormat((byte)cameraConfig, (byte)format);

                Assert.Equal((byte)format, ClPacket.ExtractOutputFormat(updated));
            }
        }
    }
}
