using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 알람 이력을 LiteDB <c>alarm_history</c> 컬렉션에 보관하는 저장소.
    /// Timestamp 인덱스를 만들고 조회는 항상 최신순(Timestamp 내림차순)으로 돌려준다.
    /// </summary>
    public sealed class LiteDbAlarmHistoryRepository : IAlarmHistoryRepository
    {
        private readonly ILiteCollection<AlarmHistoryRecord> _collection;

        public LiteDbAlarmHistoryRepository(LiteDatabase db)
        {
            _collection = db.GetCollection<AlarmHistoryRecord>("alarm_history");
            _collection.EnsureIndex(x => x.Timestamp);
        }

        public Task InsertAsync(AlarmHistoryRecord record)
        {
            _collection.Insert(record);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 기간 내 알람을 최신순으로 페이징 조회한다. 최소 심각도 필터는 기간 레코드를
        /// 전부 읽은 뒤 메모리에서 적용한다.
        /// </summary>
        public Task<IEnumerable<AlarmHistoryRecord>> QueryAsync(
            DateTime from,
            DateTime to,
            AlarmSeverity? minimumSeverity = null,
            int page = 1,
            int pageSize = 10)
        {
            var records = _collection.Query()
                .Where(x => x.Timestamp >= from && x.Timestamp <= to)
                .OrderByDescending(x => x.Timestamp)
                .ToList();

            if (minimumSeverity.HasValue)
                records = records.Where(x => x.Severity >= minimumSeverity.Value).ToList();

            var pageRecords = records
                .Skip(Math.Max(0, page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Task.FromResult<IEnumerable<AlarmHistoryRecord>>(pageRecords);
        }

        /// <summary><see cref="QueryAsync"/>와 같은 조건으로 걸러진 알람 건수를 센다.</summary>
        public Task<int> CountAsync(
            DateTime from,
            DateTime to,
            AlarmSeverity? minimumSeverity = null)
        {
            var query = _collection.Query()
                .Where(x => x.Timestamp >= from && x.Timestamp <= to)
                .ToList();

            if (minimumSeverity.HasValue)
                query = query.Where(x => x.Severity >= minimumSeverity.Value).ToList();

            return Task.FromResult(query.Count);
        }
    }
}
