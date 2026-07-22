using System;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// USB 복합 장치의 부모 식별 키를 유도한다. Windows ContainerID(하나의 물리
    /// 장치의 모든 기능 — UVC 비디오 + USB-serial — 이 공유)를 우선 사용하고,
    /// 조회 불가 시 PNPDeviceID 정규화로 폴백한다.
    /// WmiCameraEnumerator 와 WmiUsbSerialEnumerator 가 공유.
    /// </summary>
    public static class UsbTopology
    {
        private static readonly Regex MiToken = new(@"&MI_[0-9A-Fa-f]{2}", RegexOptions.Compiled);
        private const string ZeroGuid = "{00000000-0000-0000-0000-000000000000}";

        /// <summary>
        /// PNPDeviceID로 레지스트리 ContainerID를 조회한다. 유효한 non-zero GUID면
        /// 대문자로 반환(하나의 물리 복합 장치의 모든 기능이 동일 → 올바른 페어링 키).
        /// 없거나 실패하면 <see cref="NormalizeParent"/> 폴백. 절대 예외를 던지지 않는다.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static string DeriveContainerId(string pnpDeviceId)
        {
            if (string.IsNullOrEmpty(pnpDeviceId)) return string.Empty;

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Enum\{pnpDeviceId}");
                var value = key?.GetValue("ContainerID") as string;
                if (!string.IsNullOrEmpty(value)
                    && Guid.TryParse(value, out _)
                    && !string.Equals(value, ZeroGuid, StringComparison.OrdinalIgnoreCase))
                {
                    return value.ToUpperInvariant();
                }
            }
            catch
            {
                // 레지스트리 접근 실패(권한/경로) → 폴백
            }

            return NormalizeParent(pnpDeviceId);
        }

        /// <summary>
        /// 순수(레지스트리 무관, 이식 가능) PNPDeviceID 정규화: 대문자화 →
        /// 인터페이스 토큰(&amp;MI_xx) 제거 → 마지막 백슬래시 인스턴스 구획 삭제.
        /// 복합 장치의 여러 인터페이스가 동일 키로 수렴한다.
        /// </summary>
        // ponytail: ContainerID를 얻을 수 없을 때의 폴백. 동일 VID/PID인 두 물리 장치를
        // 구분하지 못한다 — 실제 하드웨어 페어링은 DeriveContainerId(ContainerID)에 의존한다.
        public static string NormalizeParent(string pnpDeviceId)
        {
            if (string.IsNullOrEmpty(pnpDeviceId)) return string.Empty;

            string upper = pnpDeviceId.ToUpperInvariant();
            string noMi = MiToken.Replace(upper, string.Empty);

            int lastSlash = noMi.LastIndexOf('\\');
            return lastSlash <= 0 ? noMi : noMi.Substring(0, lastSlash);
        }
    }
}
