using System;
using System.Threading.Tasks;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 카메라 셔터를 시리얼 명령으로 제어한다. cameraIndex는 카메라 식별자이며,
    /// 실제 바이트 버퍼에는 사용하지 않는다. 상태 조회는 하드웨어에서 불가능하므로
    /// 구현체가 소프트웨어 상태 캐시를 반환한다.
    /// </summary>
    public interface ISerialShutterController : IDisposable
    {
        /// <summary>셔터 제어 시리얼 연결이 살아있으면 true.</summary>
        bool IsConnected { get; }

        /// <summary>셔터 제어 시리얼 포트에 연결한다.</summary>
        Task ConnectAsync();

        /// <summary>연결을 끊고 리소스를 정리한다.</summary>
        void Disconnect();

        /// <summary>지정 카메라의 셔터를 연다.</summary>
        Task OpenShutterAsync(int cameraIndex);

        /// <summary>지정 카메라의 셔터를 닫는다.</summary>
        Task CloseShutterAsync(int cameraIndex);

        /// <summary>지정 카메라의 셔터 상태를 반환한다. 하드웨어 조회가 불가능하므로 소프트웨어 캐시 값이다.</summary>
        Task<bool> GetShutterStateAsync(int cameraIndex);
    }
}
