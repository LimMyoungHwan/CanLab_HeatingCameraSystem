using System;

namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    /// <summary>
    /// 벤더 카메라 CL 시리얼 프로토콜의 패킷 조립·해석 헬퍼. 7바이트 고정 프레임이며
    /// 앞 2바이트는 매직 "CL"(0x43 0x4C)이다. 레이아웃과 디코딩 수식은 하드웨어 계약이다.
    /// </summary>
    public static class ClPacket
    {
        /// <summary>7바이트 요청 프레임을 만든다: 'C' 'L' mainId subId rw 0x00 data.</summary>
        public static byte[] BuildRequest(byte mainId, byte subId, ClRw rw, byte data = 0)
        {
            return new byte[] { 0x43, 0x4C, mainId, subId, (byte)rw, 0x00, data };
        }

        /// <summary>응답에서 페이로드(7번째 바이트)를 꺼낸다. 7바이트 미만이거나 매직이 다르면 예외.</summary>
        public static byte ExtractPayload(ReadOnlySpan<byte> rx)
        {
            if (rx.Length < 7 || rx[0] != 0x43 || rx[1] != 0x4C)
            {
                throw new ArgumentException("CL response must be at least 7 bytes and start with CL.", nameof(rx));
            }

            return rx[6];
        }

        /// <summary>
        /// S/N 레지스터 4바이트(SerialNbA~D)를 13비트/5비트/10비트 필드로 풀어 각각 4·2·3자리
        /// 십진수로 이어 붙인 9자리 문자열을 만든다. 비트 배치는 하드웨어 계약이다.
        /// </summary>
        public static string DecodeSerialNumber(byte a, byte b, byte c, byte d)
        {
            int n1 = (((a & 0x1F) << 8) | b) & 0x1FFF;
            int n2 = (c >> 2) & 0x1F;
            int n3 = (((c & 0x03) << 8) | d) & 0x3FF;

            return $"{n1:D4}{n2:D2}{n3:D3}";
        }

        /// <summary>
        /// FPA 온도 레지스터 MSB/LSB를 부호 있는 16비트로 합쳐 전압(4.096 스케일)으로 바꾼 뒤
        /// 선형 변환(-188.65·V + 415.48)으로 온도 값을 얻는다. 계수는 하드웨어 계약이다.
        /// </summary>
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
