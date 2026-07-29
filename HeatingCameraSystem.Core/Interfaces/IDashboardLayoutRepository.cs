using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface IDashboardLayoutRepository
    {
        Task<IReadOnlyList<DashboardLayoutSlot>> GetForModeAsync(int mode);
        Task SaveForModeAsync(int mode, IEnumerable<DashboardLayoutSlot> slots);
    }
}
