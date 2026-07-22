using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Threading;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// WMI(System.Management)로 USB 카메라를 열거하고 PnP 이벤트를 감시한다.
    /// Windows 전용. LocalSystem 또는 관리자 권한 필요.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WmiCameraEnumerator : ICameraEnumerator
    {
        private ManagementEventWatcher? _arrivalWatcher;
        private ManagementEventWatcher? _removalWatcher;
        private Timer? _debounceTimer;
        private readonly TimeSpan _debounce = TimeSpan.FromSeconds(1);
        private readonly object _debounceLock = new();

        public event Action<PnpChange>? Changed;

        public IReadOnlyList<DiscoveredCamera> Enumerate()
        {
            var results = new List<DiscoveredCamera>();
            int index = 0;

            using var searcher = new ManagementObjectSearcher(
                @"SELECT * FROM Win32_PnPEntity WHERE (PNPClass = 'Image' OR PNPClass = 'Camera')");

            foreach (ManagementBaseObject obj in searcher.Get())
            {
                string hardwareId = obj["DeviceID"]?.ToString() ?? string.Empty;
                string name       = obj["Name"]?.ToString() ?? $"Camera {index}";

                if (string.IsNullOrEmpty(hardwareId)) continue;

                results.Add(new DiscoveredCamera
                {
                    HardwareId   = hardwareId,
                    FriendlyName = name,
                    OpenCvIndex  = index++,
                    // ponytail: ContainerID 우선(동일 CLTC_T_VGA 두 대를 물리 장치로 구분),
                    // 조회 불가 시 PNPDeviceID 정규화 폴백. WmiUsbSerialEnumerator와 공유.
                    UsbParentId  = UsbTopology.DeriveContainerId(hardwareId),
                });
            }

            return results;
        }

        public IReadOnlyList<DiscoveredCamera> EnumerateThermal(string friendlyNamePrefix = "CLTC_T_VGA") =>
            Enumerate().Where(c => c.FriendlyName.StartsWith(friendlyNamePrefix, StringComparison.OrdinalIgnoreCase)).ToList();

        public void StartWatching()
        {
            var query = new WqlEventQuery(
                "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");
            _arrivalWatcher = new ManagementEventWatcher(query);
            _arrivalWatcher.EventArrived += (_, _) => ScheduleDebounce(PnpChangeType.Arrival);
            _arrivalWatcher.Start();

            var removeQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");
            _removalWatcher = new ManagementEventWatcher(removeQuery);
            _removalWatcher.EventArrived += (_, _) => ScheduleDebounce(PnpChangeType.Removal);
            _removalWatcher.Start();
        }

        public void StopWatching()
        {
            _arrivalWatcher?.Stop();
            _removalWatcher?.Stop();
            _debounceTimer?.Dispose();
        }

        private void ScheduleDebounce(PnpChangeType changeType)
        {
            lock (_debounceLock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(_ =>
                {
                    var cameras = Enumerate();
                    // PnP 이벤트 후 전체 재열거 → 첫 번째 변경된 카메라 추정 (근사)
                    foreach (var cam in cameras)
                    {
                        Changed?.Invoke(new PnpChange { ChangeType = changeType, Camera = cam });
                    }
                }, null, _debounce, Timeout.InfiniteTimeSpan);
            }
        }

        public void Dispose()
        {
            StopWatching();
            _arrivalWatcher?.Dispose();
            _removalWatcher?.Dispose();
        }
    }
}
