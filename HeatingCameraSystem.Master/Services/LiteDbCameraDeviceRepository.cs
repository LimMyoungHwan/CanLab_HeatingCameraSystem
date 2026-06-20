using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using LiteDB;

namespace HeatingCameraSystem.Master.Services
{
    public class LiteDbCameraDeviceRepository : ICameraDeviceRepository
    {
        private readonly ILiteCollection<CameraDevice> _col;

        public LiteDbCameraDeviceRepository(LiteDatabase db)
        {
            _col = db.GetCollection<CameraDevice>("CameraDevice");
            _col.EnsureIndex(d => d.HardwareId, unique: true);
        }

        public Task<IEnumerable<CameraDevice>> GetAllAsync() =>
            Task.FromResult(_col.FindAll().AsEnumerable());

        public Task<CameraDevice?> GetByHardwareIdAsync(string hardwareId) =>
            Task.FromResult<CameraDevice?>(_col.FindOne(d => d.HardwareId == hardwareId));

        public Task<CameraDevice?> GetByAliasAsync(string alias) =>
            Task.FromResult<CameraDevice?>(_col.FindOne(d => d.Alias == alias));

        public Task<IEnumerable<CameraDevice>> GetByPCIdAsync(string pcId) =>
            Task.FromResult(_col.Find(d => d.PCId == pcId).AsEnumerable());

        public Task UpsertAsync(CameraDevice device)
        {
            _col.Upsert(device);
            return Task.CompletedTask;
        }

        public Task DeleteByHardwareIdAsync(string hardwareId)
        {
            _col.DeleteMany(d => d.HardwareId == hardwareId);
            return Task.CompletedTask;
        }
    }
}
