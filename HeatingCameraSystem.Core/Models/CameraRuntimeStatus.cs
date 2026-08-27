namespace HeatingCameraSystem.Core.Models
{
    /// <summary>단일 카메라 런타임의 생명주기 상태.</summary>
    public enum CameraRuntimeStatus
    {
        /// <summary>정지 상태.</summary>
        Stopped,

        /// <summary>스트리밍/캡처 중.</summary>
        Running,

        /// <summary>오류로 인해 중단됨.</summary>
        Faulted
    }
}
