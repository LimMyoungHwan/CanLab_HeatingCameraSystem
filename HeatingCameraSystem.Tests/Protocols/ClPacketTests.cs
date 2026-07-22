using System;
using System.Linq;
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
    }
}
