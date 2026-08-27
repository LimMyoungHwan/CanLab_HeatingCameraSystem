using System;
using System.Collections.Generic;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using LiteDB;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// 저장된 캡처를 빠르게 탐색·조회·정리하기 위한 로컬 LiteDB 인덱스. Master의 캡처 이력
    /// 패턴을 그대로 따른다. 자체 데이터베이스 파일을 소유해 AgentUI가 Master 프로젝트에
    /// 의존하지 않는다. 이식 가능한 원본 진실은 여전히 <c>.json</c> 사이드카다.
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
