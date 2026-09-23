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
    /// 카메라 영상 출력 포맷. 값은 하드웨어 계약이다(<c>참고/util/DataPacket.h</c>의
    /// <c>OUT_UYVY=0x00</c>, <c>OUT_Y16=0x01</c>).
    /// <para>
    /// <see cref="Uyvy"/>는 카메라가 AGC·컬러맵을 적용한 8비트 영상이라 방사 측정 데이터가 없다 —
    /// 이 모드에서는 생산 저장의 <c>.raw</c>를 만들 수 없다. 캘리브레이션에는 <see cref="Y16"/>이어야 한다.
    /// </para>
    /// </summary>
    public enum CameraOutputFormat : byte
    {
        Uyvy = 0x00,
        Y16  = 0x01,
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

        /// <summary>
        /// FPA 온도를 레지스터 원시값(부호 있는 16비트)으로 읽는다. 생산 저장 규칙의 <c>.raw</c>가
        /// ℃가 아니라 이 값을 담기 때문에 별도로 필요하다.
        /// </summary>
        Task<short> ReadFpaTemperatureRawAsync(CancellationToken ct = default);

        /// <summary>true이면 셔터를 열고, false이면 닫는다.</summary>
        Task SetShutterAsync(bool open, CancellationToken ct = default);

        /// <summary>검출기 GSK LSB 바이어스 레지스터(0~255)를 설정한다.</summary>
        Task SetBiasAsync(byte value, CancellationToken ct = default);

        Task SetBiasRegisterAsync(CameraBiasRegister register, byte value, CancellationToken ct = default);

        /// <summary>true이면 카메라를 실행 상태로 만들고, false이면 중지한다.</summary>
        Task SetCameraRunningAsync(bool running, CancellationToken ct = default);

        /// <summary>현재 카메라 설정을 비휘발성 메모리에 저장한다.</summary>
        Task SaveConfigAsync(CancellationToken ct = default);

        /// <summary>카메라의 현재 영상 출력 포맷을 읽는다.</summary>
        Task<CameraOutputFormat> ReadOutputFormatAsync(CancellationToken ct = default);

        /// <summary>
        /// 영상 출력 포맷을 바꾼다. 같은 레지스터의 다른 필드는 보존된다.
        /// <para>
        /// 쓰는 즉시 카메라가 USB 링크를 끊는다(<c>참고/util/Viewer.cpp:414-425</c>) — 호출자는
        /// 시리얼 재연결과 비디오 재오픈을 해야 하며, 촬영 중에는 호출하면 안 된다.
        /// 재부팅 후에도 유지하려면 <see cref="SaveConfigAsync"/>를 이어서 호출한다.
        /// </para>
        /// </summary>
        Task SetOutputFormatAsync(CameraOutputFormat format, CancellationToken ct = default);
    }
}
