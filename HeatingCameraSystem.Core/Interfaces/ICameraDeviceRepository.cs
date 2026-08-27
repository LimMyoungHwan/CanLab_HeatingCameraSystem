using System.Collections.Generic;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 카메라 메타데이터(하드웨어 ID, 별칭, 호스트 PC 등)를 저장·조회한다.
    /// 실제 영상 핸들은 여기서 다루지 않으며, 구성 정보만 담당한다.
    /// </summary>
    public interface ICameraDeviceRepository
    {
        /// <summary>등록된 모든 카메라를 반환한다.</summary>
        Task<IEnumerable<CameraDevice>> GetAllAsync();

        /// <summary>하드웨어 ID로 카메라를 조회한다. 없으면 null.</summary>
        Task<CameraDevice?>             GetByHardwareIdAsync(string hardwareId);

        /// <summary>별칭(alias)으로 카메라를 조회한다. 없으면 null.</summary>
        Task<CameraDevice?>             GetByAliasAsync(string alias);

        /// <summary>같은 PC에 등록된 카메라 목록을 반환한다.</summary>
        Task<IEnumerable<CameraDevice>> GetByPCIdAsync(string pcId);

        /// <summary>카메라 정보를 삽입 또는 갱신한다.</summary>
        Task                            UpsertAsync(CameraDevice device);

        /// <summary>하드웨어 ID로 카메라를 삭제한다.</summary>
        Task                            DeleteByHardwareIdAsync(string hardwareId);
    }
}
