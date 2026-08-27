using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 카메라 USB 부모 ID와 COM 포트 이름을 매핑한다. 자동 검색이 실패하거나
    /// 한 PC에 동일 카메라가 여러 대 연결된 경우 수동 재정의로 안정적인 페어링을 유지한다.
    /// </summary>
    public interface ICameraComPairingService
    {
        /// <summary>현재 카메라↔COM 페어링 목록을 반환한다.</summary>
        Task<IReadOnlyList<CameraComPair>> GetPairsAsync(CancellationToken ct = default);

        /// <summary>
        /// 지정한 카메라가 항상 지정 COM 포트를 사용하도록 수동 재정의한다.
        /// 재정의한 항목은 자동 검색 결과보다 우선한다.
        /// </summary>
        void SetManualOverride(string cameraUsbParentId, string portName);
    }
}
