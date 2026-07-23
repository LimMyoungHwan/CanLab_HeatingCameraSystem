using System;
using System.Threading.Tasks;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 흑체(Black Body) 온도 직접 제어 추상화. PLC와 분리된다 — 흑체는 자체 온도 컨트롤러로
    /// 직접 제어하며(서보 <b>위치</b> 이동은 여전히 <see cref="IPlcController"/> 담당),
    /// index 0=흑체1, 1=흑체2. 온도는 엔지니어링 단위(℃).
    /// 실제 직접-제어 I/O(시리얼/TCP)는 장비 스펙 확보 후 구현하며, 그 전까지는
    /// FakeBlackBodyController가 시뮬/실장비 양쪽을 대체한다.
    /// </summary>
    public interface IBlackBodyController : IDisposable
    {
        /// <summary>흑체 개수(일반적으로 2 — 촬영용/웜업용).</summary>
        int Count { get; }

        bool IsConnected { get; }

        Task ConnectAsync();
        void Disconnect();

        /// <summary>흑체 타겟 온도(SV) 설정.</summary>
        Task SetTemperatureAsync(int blackBodyIndex, float celsius);

        /// <summary>흑체 현재 온도(PV) 읽기.</summary>
        Task<float> GetCurrentTemperatureAsync(int blackBodyIndex);

        /// <summary>흑체 설정 타겟 온도(SV) 읽기.</summary>
        Task<float> GetTargetTemperatureAsync(int blackBodyIndex);
    }
}
