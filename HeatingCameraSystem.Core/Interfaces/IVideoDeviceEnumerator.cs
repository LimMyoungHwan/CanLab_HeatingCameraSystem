using System.Collections.Generic;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 영상 입력 장치를 OpenCV DirectShow 백엔드가 정수 인덱스를 매기는 것과 <b>같은 순서로</b> 열거한다.
    /// 순서가 같아야 포트가 바뀌어도 유지되는 ContainerId를 현재 OpenCV 인덱스로 환산할 수 있다.
    /// </summary>
    public interface IVideoDeviceEnumerator
    {
        /// <summary>현재 연결된 영상 장치를 OpenCV 인덱스 순서로 반환한다.</summary>
        IReadOnlyList<VideoDevice> Enumerate();
    }
}
