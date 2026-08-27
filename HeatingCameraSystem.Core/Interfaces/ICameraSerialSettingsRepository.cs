using System.Collections.Generic;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 각 카메라의 시리얼 통신 설정(COM 포트, 페어링 등)을 저장·조회한다.
    /// </summary>
    public interface ICameraSerialSettingsRepository
    {
        /// <summary>모든 카메라의 시리얼 설정을 반환한다.</summary>
        Task<IEnumerable<CameraSerialSettings>> GetAllAsync();

        /// <summary>지정 카메라의 시리얼 설정을 조회한다. 없으면 null.</summary>
        Task<CameraSerialSettings?>             GetByCameraIndexAsync(int cameraIndex);

        /// <summary>카메라 시리얼 설정을 삽입 또는 갱신한다.</summary>
        Task                                    UpsertAsync(CameraSerialSettings settings);
    }
}
