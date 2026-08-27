using VagabondK.Protocols.LSElectric;

namespace HeatingCameraSystem.Simulator.Memory;

/// <summary>
/// 논리 토큰(PlcSettings 스타일: "D100", "M10", "P000", "D2520.0") 편의 접근.
/// 토큰→DeviceVariable 변환은 <c>PlcXgtClient</c>와 똑같이 VagabondK의
/// <see cref="DeviceVariable.Parse(string, bool)"/>에 위임한다:
/// 워드 토큰은 <c>%{area}W{n}</c>, 비트 토큰은 <c>%{area}X{n}</c>이 된다(UseHexBitIndex 준수).
/// 점 붙은 D 토큰("D2520.0")은 비트-오브-워드다: 읽기 = 워드 읽기 + 마스크,
/// 쓰기 = 나머지 15비트를 보존하는 read-modify-write(저장소 락 아래에서 원자적).
/// </summary>
public sealed partial class FEnetDeviceMemory
{
    public short ReadWordToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return ReadValue(ParseWord(token)).WordValue;
    }

    public void WriteWordToken(string token, short value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        WriteValue(ParseWord(token), new DeviceValue(value));
    }

    public bool ReadBitToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (TrySplitDotted(token, out string wordToken, out int bit))
        {
            short word = ReadValue(ParseWord(wordToken)).WordValue;
            return (word & (1 << bit)) != 0;
        }
        return ReadValue(ParseBit(token)).BitValue;
    }

    public void WriteBitToken(string token, bool on)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (TrySplitDotted(token, out string wordToken, out int bit))
        {
            DeviceVariable wordVar = ParseWord(wordToken);
            // read-modify-write는 원자적이어야 한다: 읽기 + 쓰기 전 구간에서 저장소 락을 잡아
            // 같은 워드에 대한 동시 RMW가 이웃 비트를 덮어쓰지 못하게 한다.
            lock (_gate)
            {
                ushort word = (ushort)ReadValueLocked(wordVar).WordValue;
                word = on ? (ushort)(word | (1 << bit)) : (ushort)(word & ~(1 << bit));
                WriteValueLocked(wordVar, new DeviceValue((short)word));
            }
            return;
        }
        WriteValue(ParseBit(token), new DeviceValue(on));
    }

    private DeviceVariable ParseWord(string token)
    {
        (string area, string suffix) = SplitToken(token);
        return DeviceVariable.Parse($"%{area}W{suffix}", _useHexBitIndex);
    }

    private DeviceVariable ParseBit(string token)
    {
        (string area, string suffix) = SplitToken(token);
        return DeviceVariable.Parse($"%{area}X{suffix}", _useHexBitIndex);
    }

    private static (string Area, string Suffix) SplitToken(string token)
    {
        int i = 0;
        while (i < token.Length && char.IsLetter(token[i])) i++;
        return (token[..i], token[i..]);
    }

    private static bool TrySplitDotted(string token, out string wordToken, out int bit)
    {
        int dot = token.IndexOf('.');
        if (dot < 0) { wordToken = token; bit = 0; return false; }
        wordToken = token[..dot];
        bit = int.Parse(token[(dot + 1)..]);
        return true;
    }
}
