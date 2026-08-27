using System;
using System.Collections.Generic;
using System.Linq;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// 감지된 카메라 페어 중 목록에 아직 없는 것마다 CameraDescriptor를 추가한다. 키는 안정적
    /// 정체성(앱 수준 S/N 우선, USB ContainerId 폴백)이다. AgentId는 "{host}_Agent_{n}"으로 자동
    /// 번호가 붙고, 이 호스트의 기존 최대 번호 다음부터 이어진다. 목록을 제자리에서 수정하고
    /// 추가된 개수를 반환한다. 시작 시와 핫플러그 때, 시리얼/비디오 리컨사일이 COM 포트와
    /// OpenCvIndex를 다듬기 전에 실행한다. 쓸 만한 S/N도 ContainerId도 없는 장치는 건너뛴다 —
    /// 중복 제거에 쓸 안정적 키가 없어, 등록하면 실행할 때마다 중복이 생기기 때문이다.
    /// </summary>
    public static class CameraAutoRegistrar
    {
        public static int Register(IList<CameraDescriptor> cameras, IReadOnlyList<CameraComPair> pairs, string host)
        {
            int nextNumber = NextAgentNumber(cameras, host);
            int added = 0;

            foreach (CameraComPair pair in pairs)
            {
                string? serial = IsUsableSerial(pair.CameraSerialNumber) ? pair.CameraSerialNumber : null;
                string? containerId = string.IsNullOrWhiteSpace(pair.Camera.UsbParentId) ? null : pair.Camera.UsbParentId;

                if (serial is null && containerId is null)
                {
                    continue;
                }

                if (IsAlreadyRegistered(cameras, serial, containerId))
                {
                    continue;
                }

                string agentId = $"{host}_Agent_{nextNumber++}";
                cameras.Add(new CameraDescriptor(
                    agentId,
                    pair.Camera.OpenCvIndex,
                    pair.Camera.FriendlyName,
                    pair.SerialPort?.PortName,
                    pair.Camera.FriendlyName,
                    serial,
                    containerId));
                added++;
            }

            return added;
        }

        /// <summary>
        /// 시리얼 페어링이 페어를 못 만들 때(COM 포트 고장/부재, WMI 오동작)의 폴백: 비디오 열거만으로
        /// 열화상 카메라를 등록해 패널과 라이브 영상이라도 뜨게 한다. 키는 UsbContainerId뿐이며
        /// (여기엔 시리얼 S/N이 없다), 열화상이 아닌 이름과 안정적 ContainerId가 없는 장치는 건너뛴다.
        /// Register의 호스트 번호 매기기와 중복 제거를 재사용한다.
        /// </summary>
        public static int RegisterVideoOnly(
            IList<CameraDescriptor> cameras,
            IReadOnlyList<VideoDevice> devices,
            string host,
            string friendlyNamePrefix = "CLTC_T_VGA")
        {
            int nextNumber = NextAgentNumber(cameras, host);
            int added = 0;

            foreach (VideoDevice device in devices)
            {
                if (string.IsNullOrWhiteSpace(device.ContainerId))
                {
                    continue;
                }

                if (!device.FriendlyName.StartsWith(friendlyNamePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsAlreadyRegistered(cameras, serial: null, containerId: device.ContainerId))
                {
                    continue;
                }

                string agentId = $"{host}_Agent_{nextNumber++}";
                cameras.Add(new CameraDescriptor(
                    agentId,
                    device.Index,
                    device.FriendlyName,
                    null,
                    device.FriendlyName,
                    null,
                    device.ContainerId));
                added++;
            }

            return added;
        }

        private static int NextAgentNumber(IEnumerable<CameraDescriptor> cameras, string host)
        {
            string prefix = host + "_Agent_";
            int max = 0;
            foreach (CameraDescriptor cam in cameras)
            {
                if (cam.AgentId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(cam.AgentId.AsSpan(prefix.Length), out int n))
                {
                    max = Math.Max(max, n);
                }
            }

            return max + 1;
        }

        private static bool IsAlreadyRegistered(IEnumerable<CameraDescriptor> cameras, string? serial, string? containerId)
        {
            foreach (CameraDescriptor cam in cameras)
            {
                if (serial is not null && string.Equals(cam.CameraSerialNumber, serial, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (containerId is not null && string.Equals(cam.UsbContainerId, containerId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // 비어 있거나 전부 0인 S/N은 프로그래밍되지 않은 테스트 카메라 — 실제 정체성 키가 아니므로 ContainerId로 폴백한다.
        private static bool IsUsableSerial(string? serial) =>
            !string.IsNullOrWhiteSpace(serial) && serial.Any(c => c is >= '1' and <= '9');
    }
}
