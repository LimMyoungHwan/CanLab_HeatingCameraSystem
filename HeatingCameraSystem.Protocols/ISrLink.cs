using System;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// SR-800N 흑체와 통신하는 저수준 링크 추상화. 시리얼(<see cref="SerialPortSrLink"/>),
    /// UDP(<see cref="UdpSrLink"/>), 시뮬레이터(<see cref="SimulatedSrDevice"/>)가 구현한다.
    /// 스레드 안전하지 않으므로 호출자(<see cref="SrBlackBodyController"/>)가 유닛별 게이트로 직렬화한다.
    /// </summary>
    public interface ISrLink : IDisposable
    {
        bool IsOpen { get; }
        void Open();
        void Close();
        void Write(byte[] data);

        /// <summary>완결된 SR-800N 응답 프레임 하나를 반환한다.</summary>
        byte[] Read();

        /// <summary>밀린 수신 데이터를 버려 다음 질의의 응답에 이전 응답이 섞이지 않게 한다.</summary>
        void DiscardInBuffer();
    }
}
