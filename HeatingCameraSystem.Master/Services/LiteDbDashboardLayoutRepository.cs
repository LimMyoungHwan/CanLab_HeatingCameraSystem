using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// LiteDB 저장용 문서. 뷰 모드당 한 건이며 <c>"mode-{mode}"</c> 형식의 DocId가 _id다.
    /// </summary>
    internal class DashboardLayoutDocument
    {
        [BsonId]
        public string DocId { get; set; } = string.Empty;
        public List<DashboardLayoutSlot> Slots { get; set; } = new();
    }

    /// <summary>
    /// 대시보드 슬롯 배치를 LiteDB <c>dashboard_layout</c> 컬렉션에 보관하는 저장소.
    /// 뷰 모드별로 문서 한 건에 슬롯 목록 전체를 통째로 저장한다.
    /// </summary>
    public class LiteDbDashboardLayoutRepository : IDashboardLayoutRepository
    {
        private readonly ILiteCollection<DashboardLayoutDocument> _col;

        public LiteDbDashboardLayoutRepository(LiteDatabase db)
        {
            _col = db.GetCollection<DashboardLayoutDocument>("dashboard_layout");
        }

        /// <summary>해당 모드의 배치를 돌려준다. 저장된 적이 없으면 빈 목록이다.</summary>
        public Task<IReadOnlyList<DashboardLayoutSlot>> GetForModeAsync(int mode)
        {
            var doc = _col.FindById($"mode-{mode}");
            IReadOnlyList<DashboardLayoutSlot> result =
                doc?.Slots ?? new List<DashboardLayoutSlot>();
            return Task.FromResult(result);
        }

        /// <summary>해당 모드의 배치를 통째로 덮어쓴다.</summary>
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
