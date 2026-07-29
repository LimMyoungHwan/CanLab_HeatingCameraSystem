using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    internal class DashboardLayoutDocument
    {
        [BsonId]
        public string DocId { get; set; } = string.Empty;
        public List<DashboardLayoutSlot> Slots { get; set; } = new();
    }

    public class LiteDbDashboardLayoutRepository : IDashboardLayoutRepository
    {
        private readonly ILiteCollection<DashboardLayoutDocument> _col;

        public LiteDbDashboardLayoutRepository(LiteDatabase db)
        {
            _col = db.GetCollection<DashboardLayoutDocument>("dashboard_layout");
        }

        public Task<IReadOnlyList<DashboardLayoutSlot>> GetForModeAsync(int mode)
        {
            var doc = _col.FindById($"mode-{mode}");
            IReadOnlyList<DashboardLayoutSlot> result =
                doc?.Slots ?? new List<DashboardLayoutSlot>();
            return Task.FromResult(result);
        }

        public Task SaveForModeAsync(int mode, IEnumerable<DashboardLayoutSlot> slots)
        {
            var doc = new DashboardLayoutDocument
            {
                DocId = $"mode-{mode}",
                Slots = slots.ToList()
            };
            _col.Upsert(doc);
            return Task.CompletedTask;
        }
    }
}
