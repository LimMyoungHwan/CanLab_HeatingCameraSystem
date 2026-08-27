using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 챔버(온/습도) 측정 이력을 저장·조회·정리한다.
    /// </summary>
    public interface IChamberHistoryRepository
    {
        /// <summary>한 건의 챔버 이력을 저장한다.</summary>
        Task InsertAsync(ChamberHistoryRecord record);

        /// <summary>지정 기간의 챔버 이력을 페이지 단위로 조회한다.</summary>
        Task<IEnumerable<ChamberHistoryRecord>> QueryAsync(
            DateTime from,
            DateTime to,
            int page = 1,
            int pageSize = 10);

        /// <summary>지정 기간의 챔버 이력 건수를 반환한다.</summary>
        Task<int> CountAsync(DateTime from, DateTime to);

        /// <summary>지정 시점보다 오래된 챔버 이력을 삭제한다.</summary>
        Task DeleteOlderThanAsync(DateTime cutoff);
    }
}
