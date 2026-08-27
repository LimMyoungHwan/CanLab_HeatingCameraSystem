using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using DirectShowLib;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// DsDevice.GetDevicesOfCat로 DirectShow 비디오 입력 장치를 열거한다 — OpenCV cap_dshow가 걷는
    /// 것과 동일한 ICreateDevEnum / IEnumMoniker 순서(정렬 없음)라서, 여기의 N번째 장치가 정확히
    /// VideoCapture(N, VideoCaptureAPIs.DSHOW)다. 이 순서 일치는 DSHOW 백엔드에서만 성립하며
    /// MSMF로 열면 깨진다. ContainerId는 WmiCameraEnumerator와 같은 레지스트리 조회로 각
    /// DevicePath에서 파생되므로 CameraDescriptor.UsbContainerId와 맞아떨어진다.
    /// </summary>
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
