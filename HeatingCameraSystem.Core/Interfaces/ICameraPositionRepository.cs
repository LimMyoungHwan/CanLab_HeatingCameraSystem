using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces;

public interface ICameraPositionRepository
{
    Task<IReadOnlyList<CameraPositionEntry>> GetAllAsync();
    Task SaveAsync(CameraPositionEntry entry);
    Task DeleteAsync(CameraIdentity camera, BlackBodyRole role);
}
