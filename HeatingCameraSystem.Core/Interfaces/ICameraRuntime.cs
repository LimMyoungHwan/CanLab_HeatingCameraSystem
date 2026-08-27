using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 하나의 물리 카메라를 독점한다: 하나의 핸들, 하나의 연속 Y16 읽기 루프.
    /// 라이브 뷰는 <see cref="FrameReady"/>를 구독하고, NATS/레시피 캡처는
    /// <see cref="CaptureSnapshotAsync"/>로 최신 프레임을 tee한다 — 두 번째 핸들을 열지도,
    /// 라이브 뷰를 멈추지도 않는다. 서로 다른 인덱스의 런타임은 한 프로세스에 공존할 수 있다.
    /// </summary>
    public interface ICameraRuntime : IDisposable
    {
        /// <summary>이 런타임이 담당하는 카메라 인덱스.</summary>
        int CameraIndex { get; }

        /// <summary>현재 런타임 상태(정지/실행/에러 등).</summary>
        CameraRuntimeStatus Status { get; }

        /// <summary>프레임 읽기 루프가 돌고 있으면 true.</summary>
        bool IsRunning { get; }

        /// <summary>가장 최근 프레임. 첫 프레임이 들어오기 전까지는 null. 원자적으로 갱신된다.</summary>
        ThermalFrame? LatestFrame { get; }

        /// <summary>새 프레임이 준비되면 발생한다.</summary>
        event EventHandler<ThermalFrame>? FrameReady;

        /// <summary>런타임 상태가 바뀌면 발생한다.</summary>
        event EventHandler<CameraRuntimeStatus>? StatusChanged;

        /// <summary>프레임 읽기 루프를 시작한다.</summary>
        Task StartAsync(CancellationToken ct = default);

        /// <summary>프레임 읽기 루프를 멈추고 핸들을 해제한다.</summary>
        Task StopAsync();

        /// <summary>
        /// 라이브 루프에서 tee한 캡처 프레임을 반환한다. <paramref name="maxAge"/>가 null이면
        /// 현재 최신 프레임을 즉시 반환하고, 그렇지 않으면 최신 프레임이 maxAge보다 오래되었거나
        /// 아직 없으면 <paramref name="nextFrameTimeout"/>까지 새 프레임을 기다린다.
        /// 런타임이 프레임을 만들지 않는 상태(시작하지 않았거나, 타임아웃 동안 멈춘 경우)면 null을 반환한다.
        /// </summary>
        Task<ThermalFrame?> CaptureSnapshotAsync(
            TimeSpan? maxAge = null,
            TimeSpan? nextFrameTimeout = null,
            CancellationToken ct = default);
    }
}
