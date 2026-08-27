using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 대시보드 화면의 카메라 배치 레이아웃을 모드별로 저장·조회한다.
    /// </summary>
    public interface IDashboardLayoutRepository
    {
        /// <summary>지정 모드에 저장된 레이아웃 슬롯 목록을 반환한다.</summary>
        Task<IReadOnlyList<DashboardLayoutSlot>> GetForModeAsync(int mode);

        /// <summary>지정 모드의 레이아웃 슬롯을 저장한다.</summary>
        Task SaveForModeAsync(int mode, IEnumerable<DashboardLayoutSlot> slots);
    }
}
