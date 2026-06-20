using System;
using System.Collections.Generic;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// PC에 연결된 USB 카메라를 열거하고 PnP 변경 이벤트를 통지한다.
    /// </summary>
    public interface ICameraEnumerator : IDisposable
    {
        /// <summary>현재 연결된 카메라 목록을 동기 열거한다.</summary>
        IReadOnlyList<DiscoveredCamera> Enumerate();

        /// <summary>USB 도착/제거 이벤트. 1초 디바운스 후 발행.</summary>
        event Action<PnpChange> Changed;

        /// <summary>PnP 이벤트 감시를 시작한다.</summary>
        void StartWatching();

        /// <summary>PnP 이벤트 감시를 중지한다.</summary>
        void StopWatching();
    }
}
