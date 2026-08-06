using System;
using System.Collections.Generic;
using System.Linq;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    // Rewrites each camera's OpenCvIndex to the current DirectShow index by matching the (already
    // serial-reconciled) UsbContainerId against the live video enumeration. Run AFTER the serial/S-N
    // reconcile refreshes UsbContainerId, at startup and on every hotplug, so OpenCV opens the right
    // physical camera even after a replug shuffles the DShow enumeration order.
    public static class VideoIndexReconciler
    {
        // Mutates the list in place; returns how many OpenCvIndex values changed.
        public static int Reconcile(IList<CameraDescriptor> cameras, IReadOnlyList<VideoDevice> devices)
        {
            int changed = 0;
            for (int i = 0; i < cameras.Count; i++)
            {
                CameraDescriptor cam = cameras[i];
                if (string.IsNullOrWhiteSpace(cam.UsbContainerId))
                {
                    continue;
                }

                VideoDevice? dev = devices.FirstOrDefault(
                    d => string.Equals(d.ContainerId, cam.UsbContainerId, StringComparison.OrdinalIgnoreCase));
                if (dev is null || dev.Index == cam.OpenCvIndex)
                {
                    continue;
                }

                cameras[i] = cam with { OpenCvIndex = dev.Index };
                changed++;
            }

            return changed;
        }
    }
}
