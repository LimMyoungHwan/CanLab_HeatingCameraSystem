using HeatingCameraSystem.Protocols;

namespace HeatingCameraSystem.Tests;

public class SrProtocolTests
{
    [Fact]
    public void SetTemperature_100C_MatchesSpec_6057060_Section_3_1_2()
    {
        byte[] expected = { 0xAA, 0x01, 0x00, 0x0A, 0x06, 0x07, 0xF1, 0x00, 0x04, 0x42, 0xC8, 0x00, 0x00, 0x3F };
        Assert.Equal(expected, SrProtocol.SetTemperature(100f));
    }

    [Fact]
    public void SetMode_Absolute_MatchesSpec_6057060_Section_3_1_1()
    {
        byte[] expected = { 0xAA, 0x01, 0x00, 0x07, 0x06, 0x07, 0xF0, 0x00, 0x01, 0x01, 0x4F };
        Assert.Equal(expected, SrProtocol.SetMode(1));
    }

    [Fact]
    public void GetTemperature_BuildsSingleParameterGetFrame()
    {
        byte[] frame = SrProtocol.GetTemperature();
        byte[] expected = { 0xAA, 0x01, 0x00, 0x06, 0x08, 0x07, 0xD7, 0x00, 0x00, frame[^1] };
        Assert.Equal(expected, frame);
        Assert.Equal(0, FrameByteSum(frame) & 0xFF);
    }

    [Fact]
    public void ParseFloat_RoundTripsBuiltFrame()
    {
        byte[] frame = SrProtocol.BuildSetFloat(SrProtocol.ParamCurrentTemperature, 123.5f);
        Assert.Equal(123.5f, SrProtocol.ParseFloat(frame, SrProtocol.ParamCurrentTemperature), precision: 3);
    }

    [Fact]
    public void ParseFloat_UnknownParameter_Throws()
    {
        byte[] frame = SrProtocol.BuildSetFloat(SrProtocol.ParamCurrentTemperature, 10f);
        Assert.Throws<System.FormatException>(() => SrProtocol.ParseFloat(frame, SrProtocol.ParamCurrentSetPoint));
    }

    [Fact]
    public void Checksum_MakesWholeFrameSumZeroMod256()
    {
        Assert.Equal(0, FrameByteSum(SrProtocol.SetTemperature(55.5f)) & 0xFF);
        Assert.Equal(0, FrameByteSum(SrProtocol.SetMode(2)) & 0xFF);
        Assert.Equal(0, FrameByteSum(SrProtocol.GetTargetTemperature()) & 0xFF);
    }

    private static int FrameByteSum(byte[] frame)
    {
        int sum = 0;
        foreach (byte b in frame) sum += b;
        return sum;
    }
}
