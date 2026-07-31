using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Simulator.Memory;
using HeatingCameraSystem.Simulator.Plc;

namespace HeatingCameraSystem.Tests;

/// <summary>
/// Deterministic jog dynamics: one <see cref="PlcDynamicsEngine.JogStep"/> is invoked directly
/// (no wall-clock timer) and the quantized position delta is asserted. With rate 50mm/s and a
/// 100ms tick the step is 50 word units (50 × 10 × 100 / 1000).
/// </summary>
public class PlcDynamicsEngineTests
{
    private const double JogRate = 50.0;
    private const int TickMs = 100;
    private const short ExpectedStep = 50;

    [Fact]
    public void JogStep_XPlusHeld_IncreasesServoX()
    {
        var memory = new FEnetDeviceMemory();
        var plc = new PlcSettings();
        memory.WriteWordToken(plc.ServoXPos, 100);
        memory.WriteBitToken(plc.BitJogXPlus, true);

        PlcDynamicsEngine.JogStep(memory, plc, JogRate, TickMs);

        Assert.Equal((short)(100 + ExpectedStep), memory.ReadWordToken(plc.ServoXPos));
    }

    [Fact]
    public void JogStep_XMinusHeld_DecreasesServoX()
    {
        var memory = new FEnetDeviceMemory();
        var plc = new PlcSettings();
        memory.WriteWordToken(plc.ServoXPos, 500);
        memory.WriteBitToken(plc.BitJogXMinus, true);

        PlcDynamicsEngine.JogStep(memory, plc, JogRate, TickMs);

        Assert.Equal((short)(500 - ExpectedStep), memory.ReadWordToken(plc.ServoXPos));
    }

    [Fact]
    public void JogStep_YPlusHeld_IncreasesServoY()
    {
        var memory = new FEnetDeviceMemory();
        var plc = new PlcSettings();
        memory.WriteWordToken(plc.ServoYPos, 200);
        memory.WriteBitToken(plc.BitJogYPlus, true);

        PlcDynamicsEngine.JogStep(memory, plc, JogRate, TickMs);

        Assert.Equal((short)(200 + ExpectedStep), memory.ReadWordToken(plc.ServoYPos));
    }

    [Fact]
    public void JogStep_NoBitsHeld_LeavesPositionsUnchanged()
    {
        var memory = new FEnetDeviceMemory();
        var plc = new PlcSettings();
        memory.WriteWordToken(plc.ServoXPos, 321);
        memory.WriteWordToken(plc.ServoYPos, 654);

        PlcDynamicsEngine.JogStep(memory, plc, JogRate, TickMs);

        Assert.Equal((short)321, memory.ReadWordToken(plc.ServoXPos));
        Assert.Equal((short)654, memory.ReadWordToken(plc.ServoYPos));
    }

    [Fact]
    public void JogStep_BothXBitsHeld_LeavesServoXUnchanged()
    {
        var memory = new FEnetDeviceMemory();
        var plc = new PlcSettings();
        memory.WriteWordToken(plc.ServoXPos, 111);
        memory.WriteBitToken(plc.BitJogXPlus, true);
        memory.WriteBitToken(plc.BitJogXMinus, true);

        PlcDynamicsEngine.JogStep(memory, plc, JogRate, TickMs);

        Assert.Equal((short)111, memory.ReadWordToken(plc.ServoXPos));
    }
}
