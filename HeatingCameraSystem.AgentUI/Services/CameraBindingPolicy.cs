using System;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.AgentUI.Services
{
    internal static class CameraBindingPolicy
    {
        internal static void ValidateSerialConfig(CameraDescriptor camera, CameraSerialSettings settings)
        {
            if (settings.CameraIndex != camera.OpenCvIndex || camera.OpenCvIndex < 0)
                throw new InvalidOperationException("시리얼 설정의 카메라 인덱스가 현재 장치와 다릅니다.");
            if (string.IsNullOrWhiteSpace(settings.PortName)) throw new ArgumentException("COM 포트가 비어 있습니다.");
            if (settings.BaudRate != 115200 || settings.DataBits != 8
                || !string.Equals(settings.Parity, "None", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(settings.StopBits, "One", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("CL 카메라는 115200 bps, 8 data bits, parity None, stop bit One을 사용합니다.");
        }

        // Alias edits preserve the actual device and current calibration. Any physical/COM/index
        // change recreates the panel and invalidates the previous generation.
        internal static bool SameBinding(CameraDescriptor left, CameraDescriptor right) =>
            string.Equals(left.AgentId, right.AgentId, StringComparison.Ordinal)
            && left.OpenCvIndex == right.OpenCvIndex
            && string.Equals(left.SerialPortName, right.SerialPortName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.CameraSerialNumber, right.CameraSerialNumber, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.UsbContainerId, right.UsbContainerId, StringComparison.OrdinalIgnoreCase);
    }
}
