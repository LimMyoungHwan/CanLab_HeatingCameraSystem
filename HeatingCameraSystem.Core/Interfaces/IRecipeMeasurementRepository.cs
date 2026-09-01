using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>레시피 실행 중 남긴 측정 기록을 저장·조회·정리한다.</summary>
    public interface IRecipeMeasurementRepository
    {
        Task InsertAsync(RecipeMeasurementRecord record);

        /// <summary>한 실행 회차의 측정을 시간 오름차순으로 모두 반환한다(차트용).</summary>
        Task<IEnumerable<RecipeMeasurementRecord>> QueryByRunAsync(string runId);

        /// <summary>측정이 남아 있는 실행 회차의 RunId를 최신순으로 반환한다.</summary>
        Task<IEnumerable<string>> ListRunIdsAsync(int limit = 100);

        Task DeleteOlderThanAsync(DateTime cutoff);
    }
}
