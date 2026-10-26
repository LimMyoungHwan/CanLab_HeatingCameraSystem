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

                if (serial is not null && pairs.Count(candidate =>
                    string.Equals(candidate.CameraSerialNumber, serial, StringComparison.OrdinalIgnoreCase)) > 1)
                {
                    for (int i = 0; i < cameras.Count; i++)
                        if (string.Equals(cameras[i].CameraSerialNumber, serial, StringComparison.OrdinalIgnoreCase))
                            cameras[i] = cameras[i] with { OpenCvIndex = -1, SerialPortName = null, UsbContainerId = null };
                    continue;
                }

                int existing = FindRegisteredIndex(cameras, serial, containerId);
                if (existing == -2) continue; // Ambiguous identity: do not silently pick a camera.
                if (serial is not null && containerId is not null)
                {
                    // A different programmed sensor at the old USB location is a different camera.
                    // Keep the old AgentId/SN offline so saved recipe targets cannot become the new sensor.
                    for (int i = 0; i < cameras.Count; i++)
                    {
                        CameraDescriptor other = cameras[i];
                        if (i != existing && IsUsableSerial(other.CameraSerialNumber)
                            && !string.Equals(other.CameraSerialNumber, serial, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(other.UsbContainerId, containerId, StringComparison.OrdinalIgnoreCase))
                            cameras[i] = other with { OpenCvIndex = -1, SerialPortName = null, UsbContainerId = null };
                    }
                }
                if (existing >= 0)
                {
                    // A unique S/N match survives a USB port move. Container fallback is only
                    // allowed where it cannot replace an existing different programmed S/N.
                    // 값이 있는 쪽만 덮어쓴다(못 읽은 쪽으로 기존 값을 지우지 않는다).
                    CameraDescriptor cam = cameras[existing];
                    string? port = pair.SerialPort?.PortName;
                    cameras[existing] = cam with
                    {
                        SerialPortName = port ?? cam.SerialPortName,
                        UsbContainerId = containerId ?? cam.UsbContainerId,
                        CameraSerialNumber = serial ?? cam.CameraSerialNumber,
                    };
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

        /// <summary>
        /// 현재 열거에 없는 카메라를 목록에서 제거한다. 키는 Register와 같은 안정 정체성
        /// (S/N 우선, UsbContainerId 폴백): 페어 S/N·컨테이너나 비디오 컨테이너 어디에도
        /// 걸리지 않는 항목을 지운다. 열거가 둘 다 비었으면 조회 실패로 보고 아무것도
        /// 지우지 않는다 — 전부 없는 것으로 오인하는 학살을 막는다. 안정 키 없는 항목은
        /// 판단 불가라 남긴다. 목록을 제자리에서 수정하고 제거 개수를 반환한다.
        /// </summary>
        public static int PruneDisconnected(
            IList<CameraDescriptor> cameras,
            IReadOnlyList<CameraComPair> pairs,
            IReadOnlyList<VideoDevice> devices)
        {
            if (pairs.Count == 0 && devices.Count == 0)
            {
                return 0;
            }

            var pairSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pairContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CameraComPair pair in pairs)
            {
                if (IsUsableSerial(pair.CameraSerialNumber))
                {
                    pairSerials.Add(pair.CameraSerialNumber!);
                }

                if (!string.IsNullOrWhiteSpace(pair.Camera.UsbParentId))
                {
                    pairContainers.Add(pair.Camera.UsbParentId);
                }
            }

            var videoContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (VideoDevice device in devices)
            {
                if (!string.IsNullOrWhiteSpace(device.ContainerId))
                {
                    videoContainers.Add(device.ContainerId);
                }
            }

            int removed = 0;
            for (int i = cameras.Count - 1; i >= 0; i--)
            {
                CameraDescriptor cam = cameras[i];
                bool hasSerial = IsUsableSerial(cam.CameraSerialNumber);
                bool hasContainer = !string.IsNullOrWhiteSpace(cam.UsbContainerId);
                if (!hasSerial && !hasContainer)
                {
                    continue;
                }

                bool present =
                    (hasSerial && pairSerials.Contains(cam.CameraSerialNumber!)) ||
                    (hasContainer && (pairContainers.Contains(cam.UsbContainerId!) || videoContainers.Contains(cam.UsbContainerId!)));
                if (!present)
                {
                    cameras.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// 같은 AgentId가 두 개 이상이면 첫 항목만 남긴다. 설정 화면 중복 추가의 잔해를
        /// 기동·핫플러그 때 쓸어낸다. 런타임·NATS가 AgentId 키라 둘째 이후는 어차피 무효다.
        /// 제거 개수를 반환한다.
        /// </summary>
        public static int DedupeByAgentId(IList<CameraDescriptor> cameras)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int removed = 0;
            for (int i = 0; i < cameras.Count; i++)
            {
                if (!seen.Add(cameras[i].AgentId))
                {
                    cameras.RemoveAt(i);
                    removed++;
                    i--;
                }
            }

            return removed;
        }

        private static int FindRegisteredIndex(IEnumerable<CameraDescriptor> cameras, string? serial, string? containerId)
        {
            var entries = cameras.Select((camera, index) => (Camera: camera, Index: index)).ToList();
            if (serial is not null)
            {
                var matches = entries.Where(entry => string.Equals(entry.Camera.CameraSerialNumber, serial, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count > 0) return matches.Count == 1 ? matches[0].Index : -2;
            }
            if (containerId is null) return -1;
            var byContainer = entries.Where(entry => string.Equals(entry.Camera.UsbContainerId, containerId, StringComparison.OrdinalIgnoreCase)
                && (serial is null || !IsUsableSerial(entry.Camera.CameraSerialNumber))).ToList();
            return byContainer.Count == 0 ? -1 : byContainer.Count == 1 ? byContainer[0].Index : -2;
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
