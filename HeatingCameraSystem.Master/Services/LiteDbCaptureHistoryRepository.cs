using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiteDB;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 촬영 이력을 LiteDB <c>capture_history</c> 컬렉션에 보관하는 저장소.
    /// Timestamp·CameraId 인덱스를 만들고 조회는 항상 최신순(Timestamp 내림차순)이다.
    /// </summary>
    public class LiteDbCaptureHistoryRepository : ICaptureHistoryRepository
    {
        private readonly ILiteCollection<CaptureHistoryRecord> _col;

        public LiteDbCaptureHistoryRepository(LiteDatabase db)
        {
            _col = db.GetCollection<CaptureHistoryRecord>("capture_history");
            _col.EnsureIndex(x => x.Timestamp);
            _col.EnsureIndex(x => x.CameraId);
            _col.EnsureIndex(x => x.RunId);
        }

        public Task InsertAsync(CaptureHistoryRecord record)
        {
            _col.Insert(record);
            return Task.CompletedTask;
        }

        /// <summary>기간 내 이력을 최신순으로 페이징 조회한다. cameraId를 주면 해당 카메라로 한정한다.</summary>
        public Task<IEnumerable<CaptureHistoryRecord>> QueryAsync(
            DateTime from, DateTime to, string? cameraId = null, int page = 1, int pageSize = 10)
        {
            List<CaptureHistoryRecord> results;

            if (!string.IsNullOrEmpty(cameraId))
            {
                results = _col.Query()
                    .Where(r => r.Timestamp >= from && r.Timestamp <= to && r.CameraId == cameraId)
                    .OrderByDescending(r => r.Timestamp)
                    .Skip((page - 1) * pageSize)
                    .Limit(pageSize)
                    .ToList();
            }
            else
            {
                results = _col.Query()
                    .Where(r => r.Timestamp >= from && r.Timestamp <= to)
                    .OrderByDescending(r => r.Timestamp)
                    .Skip((page - 1) * pageSize)
                    .Limit(pageSize)
                    .ToList();
            }

            return Task.FromResult<IEnumerable<CaptureHistoryRecord>>(results);
        }

        /// <summary><see cref="QueryAsync"/>와 같은 조건에 해당하는 이력 건수를 센다.</summary>
        public Task<int> CountAsync(DateTime from, DateTime to, string? cameraId = null)
        {
            int count;

            if (!string.IsNullOrEmpty(cameraId))
            {
                count = _col.Query()
                    .Where(r => r.Timestamp >= from && r.Timestamp <= to && r.CameraId == cameraId)
                    .Count();
            }
            else
            {
                count = _col.Query()
                    .Where(r => r.Timestamp >= from && r.Timestamp <= to)
                    .Count();
            }

            return Task.FromResult(count);
        }

        public Task<IEnumerable<CaptureHistoryRecord>> QueryByRunAsync(string runId)
        {
            var results = _col.Query()
                .Where(r => r.RunId == runId)
                .OrderBy(r => r.Timestamp)
                .ToList();

            return Task.FromResult<IEnumerable<CaptureHistoryRecord>>(results);
        }

        public Task DeleteOlderThanAsync(DateTime cutoff)
        {
            _col.DeleteMany(r => r.Timestamp < cutoff);
            return Task.CompletedTask;
        }
    }
}
