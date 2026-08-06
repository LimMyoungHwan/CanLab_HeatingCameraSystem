using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using DirectShowLib;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    // Enumerates DirectShow video-input devices via DsDevice.GetDevicesOfCat — the SAME
    // ICreateDevEnum / IEnumMoniker sequence OpenCV's cap_dshow walks (no sorting) — so the Nth device
    // here is exactly VideoCapture(N, VideoCaptureAPIs.DSHOW). This alignment ONLY holds for the DSHOW
    // backend; opening with MSMF would break it. ContainerId is derived from each DevicePath through the
    // same registry lookup WmiCameraEnumerator uses, so it matches CameraDescriptor.UsbContainerId.
    [SupportedOSPlatform("windows")]
    public sealed class DirectShowVideoDeviceEnumerator : IVideoDeviceEnumerator
    {
        public IReadOnlyList<VideoDevice> Enumerate()
        {
            DsDevice[] devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            var results = new List<VideoDevice>(devices.Length);

            try
            {
                for (int index = 0; index < devices.Length; index++)
                {
                    string devicePath = devices[index].DevicePath ?? string.Empty;
                    string containerId = string.IsNullOrEmpty(devicePath)
                        ? string.Empty
                        : UsbTopology.DeriveContainerId(UsbTopology.DevicePathToInstanceId(devicePath));

                    results.Add(new VideoDevice(index, devicePath, containerId, devices[index].Name ?? $"Camera {index}"));
                }
            }
            finally
            {
                foreach (DsDevice device in devices)
                {
                    device.Dispose();
                }
            }

            return results;
        }
    }
}
