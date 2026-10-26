using System;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// USB 복합 장치의 부모 식별 키를 유도한다. Windows ContainerID(하나의 물리
    /// 장치의 모든 기능 — UVC 비디오 + USB-serial — 이 공유)만 사용한다.
    /// 조회 불가 시 빈 값을 반환해 다른 물리 장치를 자동으로 짝짓지 않는다.
    /// WmiCameraEnumerator 와 WmiUsbSerialEnumerator 가 공유.
    /// </summary>
    public static class UsbTopology
    {
        private static readonly Regex MiToken = new(@"&MI_[0-9A-Fa-f]{2}", RegexOptions.Compiled);

        /// <summary>
        /// PNPDeviceID로 레지스트리 ContainerID를 조회한다. 유효한 non-zero GUID면
        /// 대문자로 반환(하나의 물리 복합 장치의 모든 기능이 동일 → 올바른 페어링 키).
        /// 없거나 실패하면 빈 값을 반환한다. VID/PID만 같은 장치를 물리적 동일성의 근거로
        /// 사용하지 않는다. 절대 예외를 던지지 않는다.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static string DeriveContainerId(string pnpDeviceId)
            => ResolvePhysicalContainerId(pnpDeviceId, instanceId =>
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Enum\{instanceId}");
                return key?.GetValue("ContainerID") as string;
            });

        // 조회 실패와 GUID 검증을 같은 경로로 처리한다. 내부 조회 seam은 실제 레지스트리나
        // 카메라를 건드리지 않고 물리 식별자 실패 처리를 검증하는 데도 사용한다.
        internal static string ResolvePhysicalContainerId(string pnpDeviceId, Func<string, string?> readContainerId)
        {
            if (string.IsNullOrWhiteSpace(pnpDeviceId)) return string.Empty;

            try
            {
                string? value = readContainerId(pnpDeviceId);
                if (Guid.TryParse(value, out Guid containerId) && containerId != Guid.Empty)
                {
                    return containerId.ToString("B").ToUpperInvariant();
                }
            }
            catch
            {
                // 레지스트리 접근 실패(권한/경로) → 물리 식별자를 확정할 수 없음
            }

            return string.Empty;
        }

        /// <summary>
        /// 순수(레지스트리 무관, 이식 가능) PNPDeviceID 정규화: 대문자화 →
        /// 인터페이스 토큰(&amp;MI_xx) 제거 → 마지막 백슬래시 인스턴스 구획 삭제.
        /// 복합 장치의 여러 인터페이스가 동일 키로 수렴한다.
        /// </summary>
        // 기존 표시/분류용 정규화 API다. 동일 VID/PID인 서로 다른 물리 장치를 구분하지 못하므로
        // 자동 페어링이나 영상 인덱스 바인딩의 식별자로 사용하지 않는다.
        public static string NormalizeParent(string pnpDeviceId)
        {
            if (string.IsNullOrEmpty(pnpDeviceId)) return string.Empty;

            string upper = pnpDeviceId.ToUpperInvariant();
            string noMi = MiToken.Replace(upper, string.Empty);

            int lastSlash = noMi.LastIndexOf('\\');
            return lastSlash <= 0 ? noMi : noMi.Substring(0, lastSlash);
        }

        /// <summary>
        /// DirectShow 모니커 DevicePath를 Enum 레지스트리 키로 쓰는 PnP 인스턴스 id로 변환한다.
        /// </summary>
        // 실제 DShow 경로는 "@device:pnp:" 접두사를 달고 오므로 "\\?\" 뒤 구간을 취한다:
        // @device:pnp:\\?\usb#vid_xxxx&pid_yyyy&mi_00#<instance>#{iface-guid}\...  ->  USB\VID_XXXX&PID_YYYY&MI_00\<instance>
        public static string DevicePathToInstanceId(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return string.Empty;

            string s = devicePath;
            int root = s.IndexOf(@"\\?\", StringComparison.Ordinal);
            if (root >= 0) s = s[(root + 4)..];

            int iface = s.IndexOf("#{", StringComparison.Ordinal);
            if (iface >= 0) s = s[..iface];

            return s.Replace('#', '\\').ToUpperInvariant();
        }
    }
}
