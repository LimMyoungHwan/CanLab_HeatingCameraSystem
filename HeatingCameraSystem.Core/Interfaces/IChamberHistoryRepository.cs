using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface IChamberHistoryRepository
    {
        Task InsertAsync(ChamberHistoryRecord record);
        Task<IEnumerable<ChamberHistoryRecord>> QueryAsync(
            DateTime from,
            DateTime to,
            int page = 1,
            int pageSize = 10);
        Task<int> CountAsync(DateTime from, DateTime to);
        Task DeleteOlderThanAsync(DateTime cutoff);
    }
}
