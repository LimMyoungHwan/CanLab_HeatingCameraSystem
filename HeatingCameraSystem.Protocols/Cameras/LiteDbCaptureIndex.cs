using System;
using System.Collections.Generic;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using LiteDB;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// Local LiteDB index over saved captures (fast browse/query/retention), mirroring the
    /// Master capture-history pattern. Owns its own database file so AgentUI stays independent
    /// of the Master project. The <c>.json</c> sidecars remain the portable source of truth.
    /// </summary>
    public sealed class LiteDbCaptureIndex : ICaptureIndex
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<CaptureRecord> _col;

        public LiteDbCaptureIndex(string dbPath)
        {
            _db = new LiteDatabase(dbPath);
            _col = _db.GetCollection<CaptureRecord>("captures");
            _col.EnsureIndex(x => x.TimestampUtc);
            _col.EnsureIndex(x => x.AgentId);
        }

        public void Add(CaptureRecord record)
        {
            if (record.Id == Guid.Empty)
            {
                record.Id = Guid.NewGuid();
            }

            _col.Insert(record);
        }

        public IReadOnlyList<CaptureRecord> Query(string? agentId = null, int limit = 200)
        {
            var q = _col.Query();
            if (!string.IsNullOrEmpty(agentId))
            {
                q = q.Where(r => r.AgentId == agentId);
            }

            return q.OrderByDescending(r => r.TimestampUtc).Limit(limit).ToList();
        }

        public CaptureRecord? Get(Guid id) => _col.FindById(id);

        public bool Delete(Guid id) => _col.Delete(id);

        public IReadOnlyList<CaptureRecord> FindOlderThan(DateTime cutoffUtc)
            => _col.Query().Where(r => r.TimestampUtc < cutoffUtc).ToList();

        public void Dispose() => _db.Dispose();
    }
}
