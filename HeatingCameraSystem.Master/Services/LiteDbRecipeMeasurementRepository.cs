using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 레시피 측정 기록을 LiteDB <c>recipe_measurement</c> 컬렉션에 보관한다.
    /// 회차 조회는 시간 오름차순(차트가 그대로 그릴 수 있게), 회차 목록은 최신순이다.
    /// </summary>
    public class LiteDbRecipeMeasurementRepository : IRecipeMeasurementRepository
    {
        private readonly ILiteCollection<RecipeMeasurementRecord> _col;

        public LiteDbRecipeMeasurementRepository(LiteDatabase db)
        {
            _col = db.GetCollection<RecipeMeasurementRecord>("recipe_measurement");
            _col.EnsureIndex(x => x.Timestamp);
            _col.EnsureIndex(x => x.RunId);
        }

        public Task InsertAsync(RecipeMeasurementRecord record)
        {
            _col.Insert(record);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<RecipeMeasurementRecord>> QueryByRunAsync(string runId)
        {
            var results = _col.Query()
                .Where(r => r.RunId == runId)
                .OrderBy(r => r.Timestamp)
                .ToList();

            return Task.FromResult<IEnumerable<RecipeMeasurementRecord>>(results);
        }

        public Task<IEnumerable<string>> ListRunIdsAsync(int limit = 100)
        {
            var runIds = _col.Query()
                .OrderByDescending(r => r.Timestamp)
                .ToEnumerable()
                .Select(r => r.RunId)
                .Distinct(StringComparer.Ordinal)
                .Take(limit)
                .ToList();

            return Task.FromResult<IEnumerable<string>>(runIds);
        }

        public Task DeleteOlderThanAsync(DateTime cutoff)
        {
            _col.DeleteMany(r => r.Timestamp < cutoff);
            return Task.CompletedTask;
        }
    }
}
