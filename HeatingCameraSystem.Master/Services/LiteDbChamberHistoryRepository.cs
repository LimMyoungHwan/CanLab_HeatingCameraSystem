using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiteDB;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 챔버 이력을 LiteDB <c>chamber_history</c> 컬렉션에 보관하는 저장소.
    /// Timestamp 인덱스를 만들고 조회는 항상 최신순(Timestamp 내림차순)이다.
    /// </summary>
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

        /// <summary>기간 내 이력을 최신순으로 페이징 조회한다.</summary>
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
