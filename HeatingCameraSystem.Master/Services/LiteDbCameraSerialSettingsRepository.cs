using System.Collections.Generic;
using System.Threading.Tasks;
using LiteDB;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// LiteDB 저장용 래퍼 문서. CameraIndex를 문서 _id로 그대로 사용한다(자동 증가 없음).
    /// </summary>
    internal class CameraSerialDocument
    {
        [BsonId(false)]
        public int                  CameraIndex { get; set; }
        public CameraSerialSettings Settings    { get; set; } = new();
    }

    /// <summary>
    /// 카메라별 시리얼 설정을 LiteDB <c>camera_serial_settings</c> 컬렉션에 보관하는 저장소.
    /// CameraIndex가 문서 _id이므로 카메라당 설정은 항상 한 건이다.
    /// </summary>
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
