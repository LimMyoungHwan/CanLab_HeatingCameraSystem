using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface ICaptureHistoryRepository
    {
        Task InsertAsync(CaptureHistoryRecord record);
        Task<IEnumerable<CaptureHistoryRecord>> QueryAsync(
            DateTime from,
            DateTime to,
            string? cameraId = null,
            int page = 1,
            int pageSize = 10);
        Task<int> CountAsync(DateTime from, DateTime to, string? cameraId = null);
        Task DeleteOlderThanAsync(DateTime cutoff);
    }
}
