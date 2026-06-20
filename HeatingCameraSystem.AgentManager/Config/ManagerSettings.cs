namespace HeatingCameraSystem.AgentManager.Config
{
    public class ManagerSettings
    {
        public string PCId                { get; set; } = Environment.MachineName;
        public string NatsUrl             { get; set; } = "nats://127.0.0.1:4222";
        public bool   SimulationMode      { get; set; } = false;
        public int    LogRetentionDays    { get; set; } = 7;
        public bool   WarnAlertEnabled    { get; set; } = false;
        public string InstallRoot         { get; set; } = @"C:\HeatingCameraSystem";
        public string AgentExePath        { get; set; } = @"C:\HeatingCameraSystem\Agent\HeatingCameraSystem.Agent.exe";
    }
}
