using VagabondK.Protocols.LSElectric;

namespace HeatingCameraSystem.Simulator.Memory;

/// <summary>
/// Logical-token convenience access (PlcSettings style: "D100", "M10", "P000", "D2520.0").
/// Token→DeviceVariable mapping is delegated to VagabondK's own
/// <see cref="DeviceVariable.Parse(string, bool)"/> exactly as <c>PlcXgtClient</c> does:
/// word tokens become <c>%{area}W{n}</c>, bit tokens <c>%{area}X{n}</c> (honoring
/// UseHexBitIndex). A dotted D token ("D2520.0") is a bit-of-word: read = word read + mask,
/// write = read-modify-write preserving the other 15 bits (atomic under the store lock).
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
            // Read-modify-write must be atomic: hold the store lock across read + write so a
            // concurrent RMW on the same word cannot clobber sibling bits.
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
