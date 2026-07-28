using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// CI Systems SR-800R RS-232 명령 문자열 조립/파싱 (순수 로직, I/O 없음 → 단위 테스트 대상).
    /// 규격(매뉴얼 Chapter 6): Host→기기 EOM = CR(0x0D), 명령 + Space + 피연산자, 대소문자 무시.
    /// 응답은 3자리 정확도 숫자이며, 오류 시 *InvalidCommand* / *InvalidOperand* 문자열을 반환한다.
    /// </summary>
    public static class SrProtocol
    {
        /// <summary>Host → controller end-of-message: ASCII CR.</summary>
        public const char HostEom = '\r';

        public static string Build(string command, string? operand = null)
            => operand is null ? command + HostEom : command + " " + operand + HostEom;

        public static string SetMode(int mode)
            => Build("SETMODE", mode.ToString(CultureInfo.InvariantCulture));

        public static string SetTemperature(float celsius)
            => Build("SETTEMPERATURE", celsius.ToString("0.###", CultureInfo.InvariantCulture));

        public static string GetTemperature() => Build("GETTEMPERATURE");

        public static string GetTargetTemperature() => Build("GETTARGETTEMPERATURE");

        private static readonly Regex NumberPattern = new(@"[-+]?\d+(\.\d+)?", RegexOptions.Compiled);

        public static float ParseTemperature(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                throw new FormatException("SR-800R returned an empty reply.");
            if (reply.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SR-800R error reply: {reply.Trim()}");

            Match m = NumberPattern.Match(reply);
            if (!m.Success)
                throw new FormatException($"No numeric value in SR-800R reply '{reply.Trim()}'.");
            return float.Parse(m.Value, CultureInfo.InvariantCulture);
        }
    }
}
