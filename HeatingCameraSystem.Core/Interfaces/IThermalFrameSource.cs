using System;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 하위 수준 카메라별 열화상 프레임 획득. 한 소스는 하나의 물리 카메라 핸들을 소유한다.
    /// <see cref="ICameraRuntime"/>이 이 소스를 감싸 연속 읽기 루프, 최신 프레임 보관,
    /// 캡처 스냅샷(tee)을 제공하므로 런타임 로직은 하드웨어에 독립적이고 단위 테스트 가능하다.
    /// </summary>
    public interface IThermalFrameSource : IDisposable
    {
        /// <summary>하위 카메라 핸들을 연다. 실패하면 예외를 던진다. 멱등하다.</summary>
        void Open();

        /// <summary>
        /// 다음 프레임을 읽는다. 이번 tick에 사용 가능한 프레임이 없으면 null을 반환하고,
        /// 호출자는 짧은 지연 후 다시 시도한다. 실제 하드웨어에서는 잠시 블록될 수 있다.
        /// </summary>
        ThermalFrame? Read();

        /// <summary>카메라 핸들을 해제한다. 열려 있지 않을 때 호출해도 안전하며, 다시 열 수 있다.</summary>
        void Close();
    }
}
