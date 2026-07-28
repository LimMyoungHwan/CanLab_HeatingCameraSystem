using System;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// SR-800R 한 유닛과의 통신 링크 추상화. 실제 장비는 <see cref="SerialPortSrLink"/>,
    /// 장비 없이 동일하게 동작시키려면 <see cref="SimulatedSrDevice"/>가 구현한다.
    /// <see cref="SrBlackBodyController"/>는 이 인터페이스만 사용하므로 명령/응답/파싱 경로가
    /// 실장비/시뮬 양쪽에서 동일하다.
    /// </summary>
    public interface ISrLink : IDisposable
    {
        bool IsOpen { get; }
        void Open();
        void Close();
        void Write(string data);
        string ReadLine();
        void DiscardInBuffer();
    }
}
