using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>Pure inventory comparison, including COM-only changes and removal of the last camera.</summary>
    internal static class CameraInventoryDelta
    {
        public static IReadOnlyList<PnpChange> Calculate(
            IReadOnlyList<DiscoveredCamera> previous, IReadOnlyList<DiscoveredCamera> current,
            IReadOnlyList<DiscoveredSerialPort> previousPorts, IReadOnlyList<DiscoveredSerialPort> currentPorts)
        {
            var oldById = previous.Where(camera => !string.IsNullOrWhiteSpace(camera.HardwareId))
                .GroupBy(camera => camera.HardwareId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var newById = current.Where(camera => !string.IsNullOrWhiteSpace(camera.HardwareId))
                .GroupBy(camera => camera.HardwareId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var changes = new List<PnpChange>();
            foreach (var entry in oldById)
                if (!newById.ContainsKey(entry.Key))
                    changes.Add(new PnpChange { ChangeType = PnpChangeType.Removal, Camera = entry.Value });

            foreach (var entry in newById)
            {
                DiscoveredCamera camera = entry.Value;
                if (!oldById.TryGetValue(entry.Key, out DiscoveredCamera? old)
                    || old.OpenCvIndex != camera.OpenCvIndex
                    || !string.Equals(old.UsbParentId, camera.UsbParentId, StringComparison.OrdinalIgnoreCase)
                    || !PortsFor(old, previousPorts).SetEquals(PortsFor(camera, currentPorts)))
                    changes.Add(new PnpChange { ChangeType = PnpChangeType.Arrival, Camera = camera });
            }
            return changes;
        }

        private static HashSet<string> PortsFor(DiscoveredCamera camera, IReadOnlyList<DiscoveredSerialPort> ports)
            => ports.Where(port => !string.IsNullOrWhiteSpace(camera.UsbParentId)
                    && string.Equals(port.UsbParentId, camera.UsbParentId, StringComparison.OrdinalIgnoreCase))
                .Select(port => port.PortName + "|" + port.HardwareId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
