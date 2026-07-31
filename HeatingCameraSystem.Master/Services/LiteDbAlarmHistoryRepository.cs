using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;

namespace HeatingCameraSystem.Master.Services
{
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
