namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// OpenCV가 보는 DirectShow 영상 입력 장치 하나.
    /// </summary>
    /// <param name="Index">
    /// <c>VideoCapture(index, DSHOW)</c>에 그대로 넘기는 정수. OpenCV의 DirectShow 열거 순서를 따른다.
    /// </param>
    /// <param name="DevicePath">DirectShow 장치 경로.</param>
    /// <param name="ContainerId">
    /// 물리 장치의 Windows ContainerID. 재연결로 열거 순서가 뒤바뀐 뒤
    /// <c>CameraDescriptor.UsbContainerId</c>와 대조해 <paramref name="Index"/>를 다시 묶는 데 쓴다.
    /// </param>
    /// <param name="FriendlyName">장치 표시 이름.</param>
    public sealed record VideoDevice(int Index, string DevicePath, string ContainerId, string FriendlyName);
}
