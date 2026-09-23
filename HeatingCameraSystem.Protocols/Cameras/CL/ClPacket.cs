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
        /// 카메라 설정 레지스터(<see cref="ClMainId.UserConfig"/>/<see cref="ClUserConfigSubId.Camera"/>)에서
        /// 출력 포맷 2비트를 꺼낸다. 비트 배치는 하드웨어 계약이다 — <c>참고/util/Viewer.cpp:1463,1479-1483</c>:
        /// <code>
        /// bit 7    autoStart
        /// bit 6-4  dispMode
        /// bit 3-2  outFormat   (OUT_UYVY=0, OUT_Y16=1)
        /// bit 1    captureType
        /// bit 0    opMode      (NORMAL=0, FACTORY=1)
        /// </code>
        /// </summary>
        public static byte ExtractOutputFormat(byte cameraConfig) => (byte)((cameraConfig >> 2) & 0x03);

        /// <summary>
        /// 출력 포맷 2비트만 갈아끼우고 나머지 네 필드는 읽은 값 그대로 보존한다. 0xF3은 bit 3-2만
        /// 지우는 마스크다. 이 헬퍼를 건너뛰고 레지스터를 통째로 쓰면 autoStart·dispMode·
        /// captureType·opMode가 전부 0이 된다.
        /// </summary>
        public static byte ReplaceOutputFormat(byte cameraConfig, byte outputFormat)
            => (byte)((cameraConfig & 0xF3) | ((outputFormat & 0x03) << 2));

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
            double v = DecodeFpaTemperatureRaw(msb, lsb) / 32768.0 * 4.096;
            return -188.65 * v + 415.48;
        }

        /// <summary>
        /// FPA 온도 레지스터를 부호 있는 16비트 원시값 그대로 돌려준다. 생산 저장 규칙의 <c>.raw</c>
        /// 파일은 ℃가 아니라 이 값을 픽셀(0,0)에 담으며, ℃에서 역산하면 반올림 오차가 생긴다.
        /// </summary>
        public static short DecodeFpaTemperatureRaw(byte msb, byte lsb)
        {
            int raw = (msb << 8) | lsb;
            if (raw > 32767)
            {
                raw -= 65536;
            }

            return (short)raw;
        }
    }
}
