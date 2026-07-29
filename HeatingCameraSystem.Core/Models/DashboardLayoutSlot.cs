namespace HeatingCameraSystem.Core.Models
{
    public class DashboardLayoutSlot
    {
        public DashboardLayoutSlot()
        {
        }

        public int Mode { get; set; }
        public int Index { get; set; }
        public string? AgentId { get; set; }
        public int? CameraIndex { get; set; }
    }
}
