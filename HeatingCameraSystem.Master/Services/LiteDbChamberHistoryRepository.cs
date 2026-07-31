using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiteDB;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    public class LiteDbChamberHistoryRepository : IChamberHistoryRepository
    {
        private readonly ILiteCollection<ChamberHistoryRecord> _col;

        public LiteDbChamberHistoryRepository(LiteDatabase db)
        {
            _col = db.GetCollection<ChamberHistoryRecord>("chamber_history");
            _col.EnsureIndex(x => x.Timestamp);
        }

        public Task InsertAsync(ChamberHistoryRecord record)
        {
            _col.Insert(record);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<ChamberHistoryRecord>> QueryAsync(
            DateTime from, DateTime to, int page = 1, int pageSize = 10)
        {
            var results = _col.Query()
                .Where(r => r.Timestamp >= from && r.Timestamp <= to)
                .OrderByDescending(r => r.Timestamp)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToList();

            return Task.FromResult<IEnumerable<ChamberHistoryRecord>>(results);
        }

        public Task<int> CountAsync(DateTime from, DateTime to)
        {
            int count = _col.Query()
                .Where(r => r.Timestamp >= from && r.Timestamp <= to)
                .Count();

            return Task.FromResult(count);
        }

        public Task DeleteOlderThanAsync(DateTime cutoff)
        {
            _col.DeleteMany(r => r.Timestamp < cutoff);
            return Task.CompletedTask;
        }
    }
}
