using HeatingCameraSystem.Simulator.Memory;
using VagabondK.Protocols.LSElectric;

namespace HeatingCameraSystem.Tests;

/// <summary>
/// Locks the pure XGT device-memory primitive that T6 plugs into the FEnet simulation
/// service: byte-addressed per-DeviceType store, bit/byte/word/dword/lword access, logical
/// tokens, dotted D-word-bit read-modify-write, UseHexBitIndex XGB semantics, continuous
/// byte ranges, thread-safety, and typed rejection of bad addresses/areas. No business
/// behavior is asserted here — that is T6.
/// </summary>
public class FEnetDeviceMemoryTests
{
    private static DeviceVariable Var(DeviceType type, DataType dataType, uint index) =>
        new(type, dataType, index, Array.Empty<byte>());

    // (a) single bit set / clear / read
    [Fact]
    public void SingleBit_SetClearRead()
    {
        var mem = new FEnetDeviceMemory();

        Assert.False(mem.ReadBitToken("M10"));
        mem.WriteBitToken("M10", true);
        Assert.True(mem.ReadBitToken("M10"));
        mem.WriteBitToken("M10", false);
        Assert.False(mem.ReadBitToken("M10"));
    }

    // (b) dotted D-word bit set leaves the other 15 bits of the word intact (RMW)
    [Fact]
    public void DottedDWordBit_PreservesOtherBits()
    {
        var mem = new FEnetDeviceMemory();
        mem.WriteWordToken("D2520", unchecked((short)0xF0F0));

        mem.WriteBitToken("D2520.0", true);   // only add bit 0
        Assert.Equal(unchecked((short)0xF0F1), mem.ReadWordToken("D2520"));
        Assert.True(mem.ReadBitToken("D2520.0"));

        mem.WriteBitToken("D2520.4", false);  // only clear bit 4
        Assert.Equal(unchecked((short)0xF0E1), mem.ReadWordToken("D2520"));
        Assert.False(mem.ReadBitToken("D2520.4"));
        Assert.True(mem.ReadBitToken("D2520.0")); // untouched
    }

    // (c) word write / read (incl. signed round-trip)
    [Fact]
    public void Word_WriteRead()
    {
        var mem = new FEnetDeviceMemory();

        mem.WriteWordToken("D100", 1234);
        Assert.Equal((short)1234, mem.ReadWordToken("D100"));

        mem.WriteWordToken("D102", -5);
        Assert.Equal((short)-5, mem.ReadWordToken("D102"));
    }

    // (d) continuous byte-range write then read returns the same bytes, coherent with word access
    [Fact]
    public void Continuous_WriteThenRead_RoundTrips()
    {
        var mem = new FEnetDeviceMemory();
        byte[] payload = { 1, 2, 3, 4, 5, 6, 7, 8 };

        mem.WriteContinuous(DeviceType.D, 400, payload);
        byte[] read = mem.ReadContinuous(DeviceType.D, 400, payload.Length);

        Assert.Equal(payload, read);
        // Byte offset 400 == word index 200; little-endian {1,2} => 0x0201.
        Assert.Equal((short)0x0201, mem.ReadWordToken("D200"));
    }

    // (e) RMW preservation across overlapping single-bit writes to the same word
    [Fact]
    public void OverlappingBitWrites_PreserveEachOther()
    {
        var mem = new FEnetDeviceMemory();

        mem.WriteBitToken("D2520.0", true);
        mem.WriteBitToken("D2520.5", true);
        Assert.Equal((short)0x0021, mem.ReadWordToken("D2520")); // 1 | 32
        Assert.True(mem.ReadBitToken("D2520.0"));
        Assert.True(mem.ReadBitToken("D2520.5"));

        mem.WriteBitToken("D2520.0", false);
        Assert.Equal((short)0x0020, mem.ReadWordToken("D2520"));
        Assert.False(mem.ReadBitToken("D2520.0"));
        Assert.True(mem.ReadBitToken("D2520.5"));
    }

    // (f) hex P/M bit indexing under UseHexBitIndex=true maps to the expected word+bit
    [Fact]
    public void HexBitIndex_MapsToExpectedWordAndBit()
    {
        var hex = new FEnetDeviceMemory(useHexBitIndex: true);
        hex.WriteBitToken("M10", true);   // 0x10 = 16 => word 1, bit 0
        Assert.Equal((short)0x0001, hex.ReadWordToken("M1"));
        Assert.Equal((short)0, hex.ReadWordToken("M0"));

        hex.WriteBitToken("P20", true);   // 0x20 = 32 => word 2, bit 0
        Assert.Equal((short)0x0001, hex.ReadWordToken("P2"));

        // Decimal semantics differ: "M10" => bit 10 of word 0.
        var dec = new FEnetDeviceMemory(useHexBitIndex: false);
        dec.WriteBitToken("M10", true);
        Assert.Equal((short)0x0400, dec.ReadWordToken("M0"));
        Assert.Equal((short)0, dec.ReadWordToken("M1"));
    }

    // (g) concurrent reads/writes from N tasks leave a consistent, expected final state
    [Fact]
    public async Task Concurrent_Access_StaysConsistent()
    {
        const int taskCount = 16;
        const int iterations = 500;
        var mem = new FEnetDeviceMemory();

        var tasks = Enumerable.Range(0, taskCount).Select(i => Task.Run(() =>
        {
            for (int n = 0; n < iterations; n++)
            {
                mem.WriteBitToken($"D5000.{i}", true);          // concurrent RMW on the SAME word
                mem.WriteWordToken($"D{6000 + i}", (short)(i * 7)); // disjoint words
                _ = mem.ReadWordToken("D5000");                 // concurrent readers
                _ = mem.ReadWordToken($"D{6000 + i}");
            }
        }));
        await Task.WhenAll(tasks);

        // All 16 bits survived (no lost RMW) => 0xFFFF.
        Assert.Equal(unchecked((short)0xFFFF), mem.ReadWordToken("D5000"));
        for (int i = 0; i < taskCount; i++)
            Assert.Equal((short)(i * 7), mem.ReadWordToken($"D{6000 + i}"));
    }

    // (h) out-of-range address rejected with the typed exception; process survives
    [Fact]
    public void OutOfRangeAddress_Rejected_AndSurvives()
    {
        var mem = new FEnetDeviceMemory();

        Assert.Throws<DeviceMemoryException>(() => mem.ReadWordToken("D70000"));
        Assert.Throws<DeviceMemoryException>(() => mem.WriteWordToken("D70000", 1));
        Assert.Throws<DeviceMemoryException>(() => mem.ReadValue(Var(DeviceType.D, DataType.Word, 100_000)));
        Assert.Throws<DeviceMemoryException>(() => mem.WriteContinuous(DeviceType.D, 131_000, new byte[1000]));

        // Not a crash/hang: a valid op after rejection still works.
        mem.WriteWordToken("D100", 77);
        Assert.Equal((short)77, mem.ReadWordToken("D100"));
    }

    // (i) unsupported DeviceType rejected with the typed exception; process survives
    [Fact]
    public void UnsupportedArea_Rejected_AndSurvives()
    {
        var mem = new FEnetDeviceMemory();

        Assert.Throws<DeviceMemoryException>(() => mem.ReadValue(Var(DeviceType.T, DataType.Word, 0)));
        Assert.Throws<DeviceMemoryException>(() => mem.WriteWordToken("T100", 1));
        Assert.Throws<DeviceMemoryException>(() => mem.ReadContinuous(DeviceType.C, 0, 2));

        mem.WriteWordToken("D100", 88);
        Assert.Equal((short)88, mem.ReadWordToken("D100"));
    }

    // Datatype dispatch: byte / doubleword / longword round-trip through DeviceVariable access.
    [Fact]
    public void DataTypeDispatch_Byte_DoubleWord_LongWord_RoundTrip()
    {
        var mem = new FEnetDeviceMemory();

        var b = Var(DeviceType.D, DataType.Byte, 10);
        mem.WriteValue(b, new DeviceValue((byte)0xAB));
        Assert.Equal((byte)0xAB, mem.ReadValue(b).ByteValue);

        var dw = Var(DeviceType.D, DataType.DoubleWord, 20);
        mem.WriteValue(dw, new DeviceValue(0x12345678));
        Assert.Equal(0x12345678, mem.ReadValue(dw).DoubleWordValue);

        var lw = Var(DeviceType.D, DataType.LongWord, 30);
        mem.WriteValue(lw, new DeviceValue(0x1122334455667788L));
        Assert.Equal(0x1122334455667788L, mem.ReadValue(lw).LongWordValue);
    }

    // Individual read/write helpers (the shape T6 feeds from the request event args).
    [Fact]
    public void Individual_ReadWrite_ThroughSharedStore()
    {
        var mem = new FEnetDeviceMemory();
        var d100 = Var(DeviceType.D, DataType.Word, 100);
        var d102 = Var(DeviceType.D, DataType.Word, 102);

        mem.WriteIndividual(new Dictionary<DeviceVariable, DeviceValue>
        {
            [d100] = new DeviceValue((short)4242),
            [d102] = new DeviceValue((short)-9),
        });

        var r100 = new DeviceVariableValue(d100);
        var r102 = new DeviceVariableValue(d102);
        mem.ReadIndividual(new[] { r100, r102 });

        Assert.Equal((short)4242, r100.DeviceValue.WordValue);
        Assert.Equal((short)-9, r102.DeviceValue.WordValue);
        // Coherent with token access.
        Assert.Equal((short)4242, mem.ReadWordToken("D100"));
    }
}
