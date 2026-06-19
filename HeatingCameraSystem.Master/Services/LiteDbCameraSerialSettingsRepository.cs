using System.Collections.Generic;
using System.Threading.Tasks;
using LiteDB;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    internal class CameraSerialDocument
    {
        [BsonId(false)]
        public int                  CameraIndex { get; set; }
        public CameraSerialSettings Settings    { get; set; } = new();
    }

    public class LiteDbCameraSerialSettingsRepository : ICameraSerialSettingsRepository
    {
        private readonly ILiteCollection<CameraSerialDocument> _col;

        public LiteDbCameraSerialSettingsRepository(LiteDatabase db)
        {
            _col = db.GetCollection<CameraSerialDocument>("camera_serial_settings");
        }

        public Task<IEnumerable<CameraSerialSettings>> GetAllAsync()
        {
            var result = new List<CameraSerialSettings>();
            foreach (var doc in _col.FindAll())
                result.Add(doc.Settings);
            return Task.FromResult<IEnumerable<CameraSerialSettings>>(result);
        }

        public Task<CameraSerialSettings?> GetByCameraIndexAsync(int cameraIndex)
        {
            var doc = _col.FindById(cameraIndex);
            return Task.FromResult(doc?.Settings);
        }

        public Task UpsertAsync(CameraSerialSettings settings)
        {
            _col.Upsert(new CameraSerialDocument
            {
                CameraIndex = settings.CameraIndex,
                Settings    = settings
            });
            return Task.CompletedTask;
        }
    }
}
