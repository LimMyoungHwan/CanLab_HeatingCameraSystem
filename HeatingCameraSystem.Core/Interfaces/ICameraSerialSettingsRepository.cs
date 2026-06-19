using System.Collections.Generic;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface ICameraSerialSettingsRepository
    {
        Task<IEnumerable<CameraSerialSettings>> GetAllAsync();
        Task<CameraSerialSettings?>             GetByCameraIndexAsync(int cameraIndex);
        Task                                    UpsertAsync(CameraSerialSettings settings);
    }
}
