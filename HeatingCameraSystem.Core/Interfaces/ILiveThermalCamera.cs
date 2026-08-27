using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 하나의 카메라에서 열화상 라이브 스트림을 가져온다.
    /// <see cref="ICameraRuntime"/>보다 하위 수준의 추상화이며, 직접 실행/정지만 다룬다.
    /// </summary>
    public interface ILiveThermalCamera : IDisposable
    {
        /// <summary>새 프레임이 준비되면 발생한다.</summary>
        event EventHandler<ThermalFrame>? FrameReady;

        /// <summary>라이브 스트림이 동작 중이면 true.</summary>
        bool IsRunning { get; }

        /// <summary>지정 카메라의 라이브 스트림을 시작한다.</summary>
        Task StartAsync(int cameraIndex, CancellationToken ct = default);

        /// <summary>라이브 스트림을 중지하고 리소스를 해제한다.</summary>
        Task StopAsync();
    }
}
