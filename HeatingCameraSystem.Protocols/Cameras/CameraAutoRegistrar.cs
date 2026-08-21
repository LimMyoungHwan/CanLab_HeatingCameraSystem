using System;
using System.Collections.Generic;
using System.Linq;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    // Adds a CameraDescriptor for each detected camera pair that isn't already in the list, keyed by a
    // stable identity (app-level S/N first, USB ContainerId fallback). AgentId is auto-numbered
    // "{host}_Agent_{n}", continuing past the highest existing number for this host. Mutates the list in
    // place and returns how many were added. Run at startup and on hotplug BEFORE the serial/video
    // reconcile refines COM ports and OpenCvIndex. Devices with neither a usable S/N nor a ContainerId are
    // skipped — there is no stable key to dedupe them on, so registering would duplicate on every launch.
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

        // A blank or all-zeros S/N is an unprogrammed test camera — not a real identity key; fall back to ContainerId.
        private static bool IsUsableSerial(string? serial) =>
            !string.IsNullOrWhiteSpace(serial) && serial.Any(c => c is >= '1' and <= '9');
    }
}
