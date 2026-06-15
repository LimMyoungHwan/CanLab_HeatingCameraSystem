using System.Collections.Generic;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface ICameraMappingRepository
    {
        Task<IEnumerable<CameraMappingConfig>> GetAllAsync();
        Task SaveAllAsync(IEnumerable<CameraMappingConfig> mappings);
    }
}
