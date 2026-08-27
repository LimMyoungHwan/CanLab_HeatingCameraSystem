namespace HeatingCameraSystem.Core.Models
{
    /// <summary>카메라-COM 포트 페어링 상태.</summary>
    public enum PairingStatus
    {
        /// <summary>확정된 페어링.</summary>
        Paired,

        /// <summary>페어링되지 않음.</summary>
        Unpaired,

        /// <summary>한 카메라에 복수 후보가 있어 모호함.</summary>
        Ambiguous,

        /// <summary>카메라는 발견됐으나 시리얼 번호 확인 전.</summary>
        DetectedButUnverified
    }
}
