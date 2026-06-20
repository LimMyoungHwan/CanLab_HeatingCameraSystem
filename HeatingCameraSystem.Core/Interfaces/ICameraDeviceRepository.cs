using System.Collections.Generic;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface ICameraDeviceRepository
    {
        Task<IEnumerable<CameraDevice>> GetAllAsync();
        Task<CameraDevice?>             GetByHardwareIdAsync(string hardwareId);
        Task<CameraDevice?>             GetByAliasAsync(string alias);
        Task<IEnumerable<CameraDevice>> GetByPCIdAsync(string pcId);
        Task                            UpsertAsync(CameraDevice device);
        Task                            DeleteByHardwareIdAsync(string hardwareId);
    }
}
