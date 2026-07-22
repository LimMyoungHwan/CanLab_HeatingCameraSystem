using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Management;
using System.Runtime.Versioning;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// System.IO.Ports 로 COM 포트를 열거하고 WMI(System.Management)로
    /// FriendlyName + PNPDeviceID 메타데이터를 조인한다.
    /// Windows 전용. WmiCameraEnumerator 와 동일한 WMI 접근 방식.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WmiUsbSerialEnumerator : IUsbSerialEnumerator
    {
        public IReadOnlyList<DiscoveredSerialPort> Enumerate()
        {
            // WMI: Name에 "(COMx)"를 포함하는 PnP 엔터티만 수집 → PortName 기준 인덱싱
            var wmiByPort = new Dictionary<string, (string Name, string Pnp)>(StringComparer.OrdinalIgnoreCase);

            using var searcher = new ManagementObjectSearcher(
                @"SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%)'");

            foreach (ManagementBaseObject obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? string.Empty;
                string pnp  = obj["PNPDeviceID"]?.ToString() ?? string.Empty;

                string? port = ExtractComPort(name);
                if (port is null) continue;

                wmiByPort[port] = (name, pnp);
            }

            // SerialPort.GetPortNames()를 권위 소스로, WMI 메타데이터를 조인
            var results = new List<DiscoveredSerialPort>();
            foreach (string port in SerialPort.GetPortNames())
            {
                string friendly;
                string hardwareId;
                if (wmiByPort.TryGetValue(port, out var meta))
                {
                    friendly   = string.IsNullOrEmpty(meta.Name) ? port : meta.Name;
                    hardwareId = meta.Pnp;
                }
                else
                {
                    friendly   = port;
                    hardwareId = string.Empty;
                }

                // ponytail: ContainerID 우선(하나의 물리 복합 장치 = UVC + USB-serial 동일 키),
                // 조회 불가 시 PNPDeviceID 정규화 폴백. WmiCameraEnumerator와 공유.
                results.Add(new DiscoveredSerialPort(
                    port,
                    friendly,
                    hardwareId,
                    UsbTopology.DeriveContainerId(hardwareId)));
            }

            return results;
        }

        /// <summary>WMI Name 문자열에서 "(COMx)"의 COMx 부분을 뽑아낸다.</summary>
        private static string? ExtractComPort(string name)
        {
            int open = name.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
            if (open < 0) return null;
            int close = name.IndexOf(')', open);
            if (close < 0) return null;
            return name.Substring(open + 1, close - open - 1);
        }
    }
}
