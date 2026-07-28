using System;
using HeatingCameraSystem.Protocols;

namespace HeatingCameraSystem.Tests;

public class SrProtocolTests
{
    [Fact]
    public void SetTemperature_BuildsCommandWithSpaceAndCr()
        => Assert.Equal("SETTEMPERATURE 35.5\r", SrProtocol.SetTemperature(35.5f));

    [Fact]
    public void SetTemperature_TrimsTrailingZeros()
        => Assert.Equal("SETTEMPERATURE 40\r", SrProtocol.SetTemperature(40f));

    [Fact]
    public void SetMode_BuildsAbsoluteModeCommand()
        => Assert.Equal("SETMODE 1\r", SrProtocol.SetMode(1));

    [Fact]
    public void GetTemperature_HasNoOperand()
        => Assert.Equal("GETTEMPERATURE\r", SrProtocol.GetTemperature());

    [Fact]
    public void GetTargetTemperature_HasNoOperand()
        => Assert.Equal("GETTARGETTEMPERATURE\r", SrProtocol.GetTargetTemperature());

    [Theory]
    [InlineData("35.000\r\n", 35.0f)]
    [InlineData("*36.250*\r\n", 36.25f)]
    [InlineData("  -5.5 ", -5.5f)]
    public void ParseTemperature_ExtractsNumericFromReply(string reply, float expected)
        => Assert.Equal(expected, SrProtocol.ParseTemperature(reply), precision: 3);

    [Fact]
    public void ParseTemperature_ThrowsOnInvalidOperand()
        => Assert.Throws<InvalidOperationException>(() => SrProtocol.ParseTemperature("*InvalidOperand*\r\n"));

    [Fact]
    public void ParseTemperature_ThrowsOnEmpty()
        => Assert.Throws<FormatException>(() => SrProtocol.ParseTemperature("  "));
}
