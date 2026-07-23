using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface ICameraSerialClient : IDisposable
    {
        string PortName { get; }
        bool IsOpen { get; }
        Task InitializeAsync(CancellationToken ct = default);
        Task<string> ReadSerialNumberAsync(CancellationToken ct = default);
        Task<double> ReadFpaTemperatureAsync(CancellationToken ct = default);
        Task SetShutterAsync(bool open, CancellationToken ct = default);
        Task SetCameraRunningAsync(bool running, CancellationToken ct = default);
        Task SaveConfigAsync(CancellationToken ct = default);
    }
}
