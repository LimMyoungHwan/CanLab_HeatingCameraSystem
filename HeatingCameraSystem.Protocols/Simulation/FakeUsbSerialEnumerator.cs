using System.Collections.Generic;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
    /// <summary>
    /// 하드웨어 없이 동작하는 가짜 USB-Serial 열거자.
    /// SimulationMode=true 시 WmiUsbSerialEnumerator 대신 사용.
    /// UsbParentId 규약은 FakeCameraEnumerator와 공유되어 카메라↔COM 페어링 조인이 가능하다.
    /// </summary>
    public class FakeUsbSerialEnumerator : IUsbSerialEnumerator
    {
        private static readonly IReadOnlyList<DiscoveredSerialPort> _ports = new[]
        {
            new DiscoveredSerialPort("COM7", "USB Serial Device (COM7)", "USB\\VID_0483&PID_5740\\CAMA_IF01", "USB\\VID_0483&PID_5740\\CAMA"),
            new DiscoveredSerialPort("COM8", "USB Serial Device (COM8)", "USB\\VID_0483&PID_5740\\CAMB_IF01", "USB\\VID_0483&PID_5740\\CAMB"),
        };

        public IReadOnlyList<DiscoveredSerialPort> Enumerate() => _ports;
    }
}
