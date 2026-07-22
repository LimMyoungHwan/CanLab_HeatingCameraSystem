using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface ILiveThermalCamera : IDisposable
    {
        event EventHandler<ThermalFrame>? FrameReady;
        bool IsRunning { get; }
        Task StartAsync(int cameraIndex, CancellationToken ct = default);
        Task StopAsync();
    }
}
