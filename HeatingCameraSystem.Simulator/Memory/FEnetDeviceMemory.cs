using System.Buffers.Binary;
using VagabondK.Protocols.LSElectric;

namespace HeatingCameraSystem.Simulator.Memory;

/// <summary>
/// Thrown when a device request targets an unsupported area or an address outside the
/// allocated range. T6 catches this to set a FEnet NAK code; this pure-memory class never
/// references event args or NAK codes itself.
/// </summary>
public sealed class DeviceMemoryException : Exception
{
    public DeviceMemoryException(string message) : base(message) { }
}

/// <summary>
/// Thread-safe general XGT device memory: one little-endian byte array per supported
/// <see cref="DeviceType"/> (D/M/P/K/L/F). Every access maps a <see cref="DeviceVariable"/>
/// (or logical token) to a byte offset and, for bits, a bit-in-byte:
/// Bit → Index/8 &amp; Index%8, Byte → Index, Word → Index*2, DoubleWord → Index*4,
/// LongWord → Index*8. Pure memory + addressing only — no chamber/servo/blackbody behavior.
/// All reads and writes take one lock, so concurrent callers never observe torn state.
/// </summary>
public sealed partial class FEnetDeviceMemory
{
    // Generous fixed areas. D is word-heavy (largest HardwareSettings token ≈ D4004 word +
    // point coords up to ≈ D3200). The bit areas cover every P/M/K/L/F hex bit token
    // (e.g. M2000→byte 1024, P745→byte 232) with headroom. Anything beyond → rejected.
    private const int DAreaBytes = 131_072;      // 65_536 words: D0..D65535
    private const int BitAreaBytes = 16_384;     // 131_072 bits per P/M/K/L/F area

    private readonly object _gate = new();
    private readonly bool _useHexBitIndex;
    private readonly IReadOnlyDictionary<DeviceType, byte[]> _areas;

    /// <param name="useHexBitIndex">XGB semantics (true): P/M/L/K/F bit tokens parse their
    /// index as hex (matches <c>PlcSettings.UseHexBitIndex</c> and the real client).</param>
    public FEnetDeviceMemory(bool useHexBitIndex = true)
    {
        _useHexBitIndex = useHexBitIndex;
        _areas = new Dictionary<DeviceType, byte[]>
        {
            [DeviceType.D] = new byte[DAreaBytes],
            [DeviceType.M] = new byte[BitAreaBytes],
            [DeviceType.P] = new byte[BitAreaBytes],
            [DeviceType.K] = new byte[BitAreaBytes],
            [DeviceType.L] = new byte[BitAreaBytes],
            [DeviceType.F] = new byte[BitAreaBytes],
        };
    }

    /// <summary>First byte occupied by a variable (bits floor to their containing byte).</summary>
    public static int ByteOffsetOf(DeviceVariable variable) => variable.DataType switch
    {
        DataType.Bit => (int)(variable.Index / 8),
        DataType.Byte => (int)variable.Index,
        DataType.Word => (int)(variable.Index * 2),
        DataType.DoubleWord => (int)(variable.Index * 4),
        DataType.LongWord => (int)(variable.Index * 8),
        _ => throw Unsupported(variable),
    };

    // ── Individual access by DeviceVariable (dispatch on DataType) ──

    public DeviceValue ReadValue(DeviceVariable variable)
    {
        lock (_gate)
        {
            byte[] area = Area(variable.DeviceType);
            return variable.DataType switch
            {
                DataType.Bit => new DeviceValue(GetBit(area, variable)),
                DataType.Byte => new DeviceValue(Slice(area, variable, 1)[0]),
                DataType.Word => new DeviceValue(BinaryPrimitives.ReadInt16LittleEndian(Slice(area, variable, 2))),
                DataType.DoubleWord => new DeviceValue(BinaryPrimitives.ReadInt32LittleEndian(Slice(area, variable, 4))),
                DataType.LongWord => new DeviceValue(BinaryPrimitives.ReadInt64LittleEndian(Slice(area, variable, 8))),
                _ => throw Unsupported(variable),
            };
        }
    }

    public void WriteValue(DeviceVariable variable, DeviceValue value)
    {
        lock (_gate)
        {
            byte[] area = Area(variable.DeviceType);
            switch (variable.DataType)
            {
                case DataType.Bit:
                    SetBit(area, variable, value.BitValue);
                    break;
                case DataType.Byte:
                    Slice(area, variable, 1)[0] = value.ByteValue;
                    break;
                case DataType.Word:
                    BinaryPrimitives.WriteInt16LittleEndian(Slice(area, variable, 2), value.WordValue);
                    break;
                case DataType.DoubleWord:
                    BinaryPrimitives.WriteInt32LittleEndian(Slice(area, variable, 4), value.DoubleWordValue);
                    break;
                case DataType.LongWord:
                    BinaryPrimitives.WriteInt64LittleEndian(Slice(area, variable, 8), value.LongWordValue);
                    break;
                default:
                    throw Unsupported(variable);
            }
        }
    }

    /// <summary>Individual read: fills each response value from the store (T6 feeds e.ResponseValues).</summary>
    public void ReadIndividual(IEnumerable<DeviceVariableValue> responseValues)
    {
        ArgumentNullException.ThrowIfNull(responseValues);
        lock (_gate)
        {
            foreach (DeviceVariableValue item in responseValues)
                item.DeviceValue = ReadValueLocked(item.DeviceVariable);
        }
    }

    /// <summary>Individual write: applies a DeviceVariable→DeviceValue map (T6 feeds e.Values).</summary>
    public void WriteIndividual(IReadOnlyDictionary<DeviceVariable, DeviceValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (_gate)
        {
            foreach (KeyValuePair<DeviceVariable, DeviceValue> kv in values)
                WriteValueLocked(kv.Key, kv.Value);
        }
    }

    // ── Continuous byte-range access over a single DeviceType ──

    public byte[] ReadContinuous(DeviceType type, int byteOffset, int length)
    {
        if (length < 0)
            throw new DeviceMemoryException($"Continuous length {length} must be non-negative.");
        lock (_gate)
        {
            byte[] area = Area(type);
            EnsureRange(area, byteOffset, length, type, byteOffset);
            var result = new byte[length];
            Array.Copy(area, byteOffset, result, 0, length);
            return result;
        }
    }

    public void WriteContinuous(DeviceType type, int byteOffset, ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            byte[] area = Area(type);
            EnsureRange(area, byteOffset, data.Length, type, byteOffset);
            data.CopyTo(area.AsSpan(byteOffset, data.Length));
        }
    }

    // ── Internals (callers already hold _gate) ──

    private DeviceValue ReadValueLocked(DeviceVariable variable)
    {
        byte[] area = Area(variable.DeviceType);
        return variable.DataType switch
        {
            DataType.Bit => new DeviceValue(GetBit(area, variable)),
            DataType.Byte => new DeviceValue(Slice(area, variable, 1)[0]),
            DataType.Word => new DeviceValue(BinaryPrimitives.ReadInt16LittleEndian(Slice(area, variable, 2))),
            DataType.DoubleWord => new DeviceValue(BinaryPrimitives.ReadInt32LittleEndian(Slice(area, variable, 4))),
            DataType.LongWord => new DeviceValue(BinaryPrimitives.ReadInt64LittleEndian(Slice(area, variable, 8))),
            _ => throw Unsupported(variable),
        };
    }

    private void WriteValueLocked(DeviceVariable variable, DeviceValue value)
    {
        byte[] area = Area(variable.DeviceType);
        switch (variable.DataType)
        {
            case DataType.Bit: SetBit(area, variable, value.BitValue); break;
            case DataType.Byte: Slice(area, variable, 1)[0] = value.ByteValue; break;
            case DataType.Word: BinaryPrimitives.WriteInt16LittleEndian(Slice(area, variable, 2), value.WordValue); break;
            case DataType.DoubleWord: BinaryPrimitives.WriteInt32LittleEndian(Slice(area, variable, 4), value.DoubleWordValue); break;
            case DataType.LongWord: BinaryPrimitives.WriteInt64LittleEndian(Slice(area, variable, 8), value.LongWordValue); break;
            default: throw Unsupported(variable);
        }
    }

    private byte[] Area(DeviceType type)
    {
        if (!_areas.TryGetValue(type, out byte[]? area))
            throw new DeviceMemoryException($"Unsupported device area '{type}'.");
        return area;
    }

    private static Span<byte> Slice(byte[] area, DeviceVariable variable, int length)
    {
        int offset = ByteOffsetOf(variable);
        EnsureRange(area, offset, length, variable.DeviceType, variable.Index);
        return area.AsSpan(offset, length);
    }

    private static bool GetBit(byte[] area, DeviceVariable variable)
    {
        int offset = ByteOffsetOf(variable);
        EnsureRange(area, offset, 1, variable.DeviceType, variable.Index);
        return (area[offset] & (1 << (int)(variable.Index % 8))) != 0;
    }

    private static void SetBit(byte[] area, DeviceVariable variable, bool on)
    {
        int offset = ByteOffsetOf(variable);
        EnsureRange(area, offset, 1, variable.DeviceType, variable.Index);
        int mask = 1 << (int)(variable.Index % 8);
        area[offset] = on ? (byte)(area[offset] | mask) : (byte)(area[offset] & ~mask);
    }

    private static void EnsureRange(byte[] area, int offset, int length, DeviceType type, long index)
    {
        if (offset < 0 || length < 0 || offset + length > area.Length)
            throw new DeviceMemoryException(
                $"Address {type}{index} (byte offset {offset}, length {length}) is outside the {type} area (size {area.Length}).");
    }

    private static DeviceMemoryException Unsupported(DeviceVariable variable) =>
        new($"Unsupported data type '{variable.DataType}' for {variable.DeviceType}{variable.Index}.");
}
