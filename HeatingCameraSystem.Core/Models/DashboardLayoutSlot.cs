namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 대시보드 레이아웃 슬롯. Master 화면의 그리드 칸 하나를 표현한다.
    /// </summary>
    public class DashboardLayoutSlot
    {
        public DashboardLayoutSlot()
        {
        }

        /// <summary>슬롯 표시 모드. UI 레이아웃에서 해당 칸의 역할을 구분한다.</summary>
        public int Mode { get; set; }

        /// <summary>슬롯 순서 인덱스.</summary>
        public int Index { get; set; }

        /// <summary>할당된 AgentId. null이면 아직 할당되지 않은 슬롯.</summary>
        public string? AgentId { get; set; }

        /// <summary>할당된 카메라 인덱스. null이면 Agent 단일 카메라 또는 미할당.</summary>
        public int? CameraIndex { get; set; }
    }
}
