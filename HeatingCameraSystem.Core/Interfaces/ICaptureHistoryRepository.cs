using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 촬영 이력(캡처 결과, 실패 여부, 파일 경로 등)을 저장·조회·정리한다.
    /// </summary>
    public interface ICaptureHistoryRepository
    {
        /// <summary>한 건의 촬영 이력을 저장한다.</summary>
        Task InsertAsync(CaptureHistoryRecord record);

        /// <summary>지정 기간의 촬영 이력을 페이지 단위로 조회한다.</summary>
        Task<IEnumerable<CaptureHistoryRecord>> QueryAsync(
            DateTime from,
            DateTime to,
            string? cameraId = null,
            int page = 1,
            int pageSize = 10);

        /// <summary>지정 기간의 촬영 이력 건수를 반환한다.</summary>
        Task<int> CountAsync(DateTime from, DateTime to, string? cameraId = null);

        /// <summary>한 레시피 실행 회차의 캡처를 시간 오름차순으로 모두 반환한다(결과 화면용).</summary>
        Task<IEnumerable<CaptureHistoryRecord>> QueryByRunAsync(string runId);

        /// <summary>지정 시점보다 오래된 이력을 삭제한다.</summary>
        Task DeleteOlderThanAsync(DateTime cutoff);
    }
}
