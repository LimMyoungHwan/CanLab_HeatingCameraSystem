using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces;

public interface ICapturePlanRunRepository
{
    Task SaveAsync(CapturePlanRunRecord run);
    Task<CapturePlanRunRecord?> GetByIdAsync(string runId);
    Task<IReadOnlyList<CapturePlanRunRecord>> GetLatestChainAsync(string recipeId);
    /// <summary>프로그램 시작 시 한 번만 호출. 중단된 Running 영수증을 복구한다.</summary>
    Task RecoverInterruptedAsync();
}
