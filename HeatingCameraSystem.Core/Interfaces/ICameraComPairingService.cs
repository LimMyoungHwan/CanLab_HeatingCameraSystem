using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface ICameraComPairingService
    {
        Task<IReadOnlyList<CameraComPair>> GetPairsAsync(CancellationToken ct = default);
        void SetManualOverride(string cameraUsbParentId, string portName);
    }
}
