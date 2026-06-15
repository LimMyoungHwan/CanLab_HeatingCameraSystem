using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    // LiteDB에 기본 키를 주기 위한 내부 래퍼
    internal class MappingDocument
    {
        [BsonId]
        public string DocId { get; set; } = "current";
        public List<CameraMappingConfig> Mappings { get; set; } = new();
    }

    public class LiteDbCameraMappingRepository : ICameraMappingRepository
    {
        private readonly ILiteCollection<MappingDocument> _col;

        public LiteDbCameraMappingRepository(LiteDatabase db)
        {
            _col = db.GetCollection<MappingDocument>("camera_mapping");
        }

        public Task<IEnumerable<CameraMappingConfig>> GetAllAsync()
        {
            var doc = _col.FindById("current");
            IEnumerable<CameraMappingConfig> result =
                doc?.Mappings ?? Enumerable.Empty<CameraMappingConfig>();
            return Task.FromResult(result);
        }

        public Task SaveAllAsync(IEnumerable<CameraMappingConfig> mappings)
        {
            var doc = new MappingDocument { Mappings = mappings.ToList() };
            _col.Upsert(doc);
            return Task.CompletedTask;
        }
    }
}
