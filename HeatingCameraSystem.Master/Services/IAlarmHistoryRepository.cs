using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HeatingCameraSystem.Master.Services
{
    public interface IAlarmHistoryRepository
    {
        Task InsertAsync(AlarmHistoryRecord record);

        Task<IEnumerable<AlarmHistoryRecord>> QueryAsync(
            DateTime from,
            DateTime to,
            AlarmSeverity? minimumSeverity = null,
            int page = 1,
            int pageSize = 10);

        Task<int> CountAsync(
            DateTime from,
            DateTime to,
            AlarmSeverity? minimumSeverity = null);
    }
}
