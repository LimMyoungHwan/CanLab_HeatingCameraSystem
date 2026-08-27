using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 알람 이력 저장소 계약. <see cref="AlarmSink"/>가 저장하고 알람 이력 화면이 조회한다.
    /// </summary>
    public interface IAlarmHistoryRepository
    {
        Task InsertAsync(AlarmHistoryRecord record);

        /// <summary>기간 내 알람을 페이징 조회한다. <paramref name="minimumSeverity"/>가 있으면 그 이상 심각도만 포함한다.</summary>
        Task<IEnumerable<AlarmHistoryRecord>> QueryAsync(
            DateTime from,
            DateTime to,
            AlarmSeverity? minimumSeverity = null,
            int page = 1,
            int pageSize = 10);

        /// <summary><see cref="QueryAsync"/>와 같은 필터 조건의 전체 건수를 돌려준다.</summary>
        Task<int> CountAsync(
            DateTime from,
            DateTime to,
            AlarmSeverity? minimumSeverity = null);
    }
}
