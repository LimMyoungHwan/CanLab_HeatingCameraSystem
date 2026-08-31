using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeatingCameraSystem.Core.Interfaces
{
    public enum CameraBiasRegister
    {
        Cint,
        TintMsb,
        TintLsb,
        GskMsb,
        GskLsb,
        Gfid
    }

    /// <summary>
    /// 카메라와 직접 통신하는 시리얼 클라이언트. 셔터 제어, FPA 온도 읽기,
    /// 시리얼 번호 조회 등 카메라 고유 명령을 담당한다.
    /// </summary>
    public interface ICameraSerialClient : IDisposable
    {
        /// <summary>현재 연결된 COM 포트 이름.</summary>
        string PortName { get; }

        /// <summary>포트가 열려 있으면 true.</summary>
        bool IsOpen { get; }

        /// <summary>시리얼 포트를 초기화하고 연결한다.</summary>
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>카메라의 시리얼 번호를 문자열로 읽는다.</summary>
        Task<string> ReadSerialNumberAsync(CancellationToken ct = default);

        /// <summary>카메라 FPA(초점 평면 어레이) 온도를 ℃로 읽는다.</summary>
        Task<double> ReadFpaTemperatureAsync(CancellationToken ct = default);

        /// <summary>true이면 셔터를 열고, false이면 닫는다.</summary>
        Task SetShutterAsync(bool open, CancellationToken ct = default);

        /// <summary>검출기 GSK LSB 바이어스 레지스터(0~255)를 설정한다.</summary>
        Task SetBiasAsync(byte value, CancellationToken ct = default);

        Task SetBiasRegisterAsync(CameraBiasRegister register, byte value, CancellationToken ct = default);

        /// <summary>true이면 카메라를 실행 상태로 만들고, false이면 중지한다.</summary>
        Task SetCameraRunningAsync(bool running, CancellationToken ct = default);

        /// <summary>현재 카메라 설정을 비휘발성 메모리에 저장한다.</summary>
        Task SaveConfigAsync(CancellationToken ct = default);
    }
}
