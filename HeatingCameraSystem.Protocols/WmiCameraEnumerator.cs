using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly object _inventoryLock = new();
        private IReadOnlyList<DiscoveredCamera> _previousCameras = Array.Empty<DiscoveredCamera>();
        private IReadOnlyList<DiscoveredSerialPort> _previousPorts = Array.Empty<DiscoveredSerialPort>();
        private bool _watching;

        /// <summary>PnP 장치 증감 알림. 디바운스 후 전체 재열거 결과를 카메라별로 발화한다.</summary>
        public event Action<PnpChange>? Changed;

        /// <summary>
        /// PNPClass가 Image 또는 Camera인 PnP 엔터티를 열거한다.
        /// OpenCvIndex는 WMI 열거 순서대로 0부터 부여한다.
        /// </summary>
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

        /// <summary>Win32_PnPEntity 생성/삭제 이벤트 감시를 시작한다. 이벤트 폭주는 1초 디바운스로 묶는다.</summary>
        public void StartWatching()
        {
            if (Volatile.Read(ref _watching)) return;
            lock (_inventoryLock)
            {
                try { _previousCameras = Enumerate(); }
                catch (Exception ex) { Debug.WriteLine($"[CameraInventory] initial enumeration failed: {ex.Message}"); }
                _previousPorts = ReadPortsOrPrevious();
            }
            Volatile.Write(ref _watching, true);
            var query = new WqlEventQuery(
                "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");
            _arrivalWatcher = new ManagementEventWatcher(query);
            _arrivalWatcher.EventArrived += (_, _) => ScheduleDebounce();
            _arrivalWatcher.Start();

            var removeQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");
            _removalWatcher = new ManagementEventWatcher(removeQuery);
            _removalWatcher.EventArrived += (_, _) => ScheduleDebounce();
            _removalWatcher.Start();
        }

        public void StopWatching()
        {
            Volatile.Write(ref _watching, false);
            _arrivalWatcher?.Stop();
            _removalWatcher?.Stop();
            lock (_debounceLock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }
            _arrivalWatcher?.Dispose();
            _removalWatcher?.Dispose();
            _arrivalWatcher = null;
            _removalWatcher = null;
        }

        /// <summary>타이머를 재장전해 마지막 이벤트로부터 1초 뒤 한 번만 재열거·통지한다.</summary>
        private void ScheduleDebounce()
        {
            lock (_debounceLock)
            {
                if (!Volatile.Read(ref _watching)) return;
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(_ => RefreshInventory(), null, _debounce, Timeout.InfiniteTimeSpan);
            }
        }

        private void RefreshInventory()
        {
            try
            {
                IReadOnlyList<PnpChange> changes;
                lock (_inventoryLock)
                {
                    if (!Volatile.Read(ref _watching)) return;
                    var cameras = Enumerate();
                    var ports = ReadPortsOrPrevious();
                    changes = CameraInventoryDelta.Calculate(_previousCameras, cameras, _previousPorts, ports);
                    _previousCameras = cameras;
                    _previousPorts = ports;
                }
                foreach (PnpChange change in changes)
                {
                    if (!Volatile.Read(ref _watching)) return;
                    Changed?.Invoke(change);
                }
            }
            catch (Exception ex)
            {
                // Enumeration failure is not evidence that every camera was removed.
                Debug.WriteLine($"[CameraInventory] refresh failed: {ex.Message}");
            }
        }

        private IReadOnlyList<DiscoveredSerialPort> ReadPortsOrPrevious()
        {
            try { return new WmiUsbSerialEnumerator().Enumerate(); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraInventory] serial enumeration failed: {ex.Message}");
                return _previousPorts;
            }
        }

        public void Dispose()
        {
            StopWatching();
        }
    }
}
