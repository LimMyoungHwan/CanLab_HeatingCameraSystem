using System.Buffers.Binary;
using VagabondK.Protocols.LSElectric;

namespace HeatingCameraSystem.Simulator.Memory;

/// <summary>
/// 디바이스 요청이 미지원 영역이나 할당 범위 밖 주소를 겨냥하면 던진다. T6(FEnetPlcSimulator)가
/// 이를 받아 FEnet NAK 코드로 바꾼다 — 이 순수 메모리 클래스 자신은 이벤트 인자나 NAK 코드를
/// 전혀 참조하지 않는다.
/// </summary>
public sealed class DeviceMemoryException : Exception
{
    public DeviceMemoryException(string message) : base(message) { }
}

/// <summary>
/// 스레드 안전한 범용 XGT 디바이스 메모리: 지원 <see cref="DeviceType"/>(D/M/P/K/L/F)마다
/// little-endian 바이트 배열 하나를 둔다. 모든 접근은 <see cref="DeviceVariable"/>(또는 논리
/// 토큰)을 바이트 오프셋으로, 비트는 바이트 내 비트 위치로 매핑한다:
/// Bit → Index/8 &amp; Index%8, Byte → Index, Word → Index*2, DoubleWord → Index*4,
/// LongWord → Index*8. 순수 메모리 + 주소 해석만 담당하며 챔버/서보/흑체 거동은 없다.
/// 읽기와 쓰기 전부가 락 하나를 잡으므로 동시 호출자가 찢어진 상태를 볼 수 없다.
/// </summary>
public sealed partial class FEnetDeviceMemory
{
    // 넉넉한 고정 영역. D는 워드 위주(HardwareSettings 최대 토큰 ≈ D4004 워드 +
    // 포인트 좌표 최대 ≈ D3200). 비트 영역은 모든 P/M/K/L/F hex 비트 토큰
    // (예: M2000→byte 1024, P745→byte 232)을 여유 있게 덮는다. 그 밖은 → 거부.
    private const int DAreaBytes = 131_072;      // 65_536 words: D0..D65535
    private const int BitAreaBytes = 16_384;     // 131_072 bits per P/M/K/L/F area

    private readonly object _gate = new();
    private readonly bool _useHexBitIndex;
    private readonly IReadOnlyDictionary<DeviceType, byte[]> _areas;

    /// <param name="useHexBitIndex">XGB 의미론(true): P/M/L/K/F 비트 토큰의 인덱스를 hex로
    /// 해석한다(<c>PlcSettings.UseHexBitIndex</c> 및 실 클라이언트와 일치).</param>
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

    /// <summary>변수가 차지하는 첫 바이트(비트는 자신이 속한 바이트로 내림).</summary>
    public static int ByteOffsetOf(DeviceVariable variable) => variable.DataType switch
    {
        DataType.Bit => (int)(variable.Index / 8),
        DataType.Byte => (int)variable.Index,
        DataType.Word => (int)(variable.Index * 2),
        DataType.DoubleWord => (int)(variable.Index * 4),
        DataType.LongWord => (int)(variable.Index * 8),
        _ => throw Unsupported(variable),
    };

    // ── DeviceVariable 단위 개별 접근 (DataType으로 분기) ──

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

    /// <summary>개별 읽기: 각 응답 값을 저장소에서 채운다(T6가 e.ResponseValues를 넘긴다).</summary>
    public void ReadIndividual(IEnumerable<DeviceVariableValue> responseValues)
    {
        ArgumentNullException.ThrowIfNull(responseValues);
        lock (_gate)
        {
            foreach (DeviceVariableValue item in responseValues)
                item.DeviceValue = ReadValueLocked(item.DeviceVariable);
        }
    }

    /// <summary>개별 쓰기: DeviceVariable→DeviceValue 맵을 적용한다(T6가 e.Values를 넘긴다).</summary>
    public void WriteIndividual(IReadOnlyDictionary<DeviceVariable, DeviceValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (_gate)
        {
            foreach (KeyValuePair<DeviceVariable, DeviceValue> kv in values)
                WriteValueLocked(kv.Key, kv.Value);
        }
    }

    // ── 단일 DeviceType 내 연속 바이트 범위 접근 ──

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

    // ── 내부 구현 (호출자가 이미 _gate를 잡고 있다) ──

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
