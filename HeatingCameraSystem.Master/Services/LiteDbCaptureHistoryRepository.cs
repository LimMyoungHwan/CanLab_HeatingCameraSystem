using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiteDB;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    public class LiteDbCaptureHistoryRepository : ICaptureHistoryRepository
    {
        private readonly ILiteCollection<CaptureHistoryRecord> _col;

        public LiteDbCaptureHistoryRepository(LiteDatabase db)
        {
            _col = db.GetCollection<CaptureHistoryRecord>("capture_history");
            _col.EnsureIndex(x => x.Timestamp);
            _col.EnsureIndex(x => x.CameraId);
        }

        public Task InsertAsync(CaptureHistoryRecord record)
        {
            _col.Insert(record);
            return Task.CompletedTask;
        }

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

        public Task DeleteOlderThanAsync(DateTime cutoff)
        {
            _col.DeleteMany(r => r.Timestamp < cutoff);
            return Task.CompletedTask;
        }
    }
}
