using System.Collections.Generic;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// PC에 연결된 USB-Serial COM 포트를 열거한다.
    /// FriendlyName + UsbParentId를 노출해 나중에 카메라↔COM 페어링에 사용한다.
    /// </summary>
    public interface IUsbSerialEnumerator
    {
        /// <summary>현재 연결된 COM 포트 목록을 동기 열거한다.</summary>
        IReadOnlyList<DiscoveredSerialPort> Enumerate();
    }
}
