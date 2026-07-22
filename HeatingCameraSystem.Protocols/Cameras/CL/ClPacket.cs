using System;

namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    public static class ClPacket
    {
        public static byte[] BuildRequest(byte mainId, byte subId, ClRw rw, byte data = 0)
        {
            return new byte[] { 0x43, 0x4C, mainId, subId, (byte)rw, 0x00, data };
        }

        public static byte ExtractPayload(ReadOnlySpan<byte> rx)
        {
            if (rx.Length < 7 || rx[0] != 0x43 || rx[1] != 0x4C)
            {
                throw new ArgumentException("CL response must be at least 7 bytes and start with CL.", nameof(rx));
            }

            return rx[6];
        }

        public static string DecodeSerialNumber(byte a, byte b, byte c, byte d)
        {
            int n1 = (((a & 0x1F) << 8) | b) & 0x1FFF;
            int n2 = (c >> 2) & 0x1F;
            int n3 = (((c & 0x03) << 8) | d) & 0x3FF;

            return $"{n1:D4}{n2:D2}{n3:D3}";
        }

        public static double DecodeFpaTemperature(byte msb, byte lsb)
        {
            int raw = (msb << 8) | lsb;
            if (raw > 32767)
            {
                raw -= 65536;
            }

            double v = raw / 32768.0 * 4.096;
            return -188.65 * v + 415.48;
        }
    }
}
