using System;
using System.Threading.Tasks;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface ISerialShutterController : IDisposable
    {
        bool IsConnected { get; }
        Task ConnectAsync();
        void Disconnect();
        Task OpenShutterAsync(int cameraIndex);
        Task CloseShutterAsync(int cameraIndex);
        Task<bool> GetShutterStateAsync(int cameraIndex);
    }
}
