namespace HeatingCameraSystem.AgentManager.Config
{
    public class ManagerSettings
    {
        public string PCId                { get; set; } = Environment.MachineName;
        public string NatsUrl             { get; set; } = "nats://127.0.0.1:4222";
        // [SC-12 범위 2] Design Ref: §4.1 — SimulationMode 단일 플래그를 두 독립 플래그로 분리.
        // SimulationMode 하나가 열거기 선택·spawn 스킵·Agent 동작 3가지를 동시 제어했던 문제를 해결.
        // 이제 각 역할이 독립 플래그로 분리되어 조합이 자유로워짐.

        /// <summary>
        /// true이면 실제 USB 카메라를 탐지하는 WmiCameraEnumerator 대신
        /// 가상 카메라 2대를 반환하는 FakeCameraEnumerator를 사용한다.
        /// NATS·NATS는 실제 연결이 필요하지만 카메라 하드웨어는 없어도 됨.
        /// </summary>
        public bool SimulateEnumeration { get; set; } = false;

        /// <summary>
        /// true이면 AgentSupervisor가 Agent.exe를 spawn할 때
        /// CLI 인수 5번째 자리에 "True"를 넣어 Agent가
        /// FakeCameraCaptureService(가짜 캡처)를 사용하게 만든다.
        /// 실제 USB 카메라 없이 캡처 roundtrip을 검증할 때 사용.
        /// </summary>
        public bool SimulateAgentMode   { get; set; } = false;
        public int    LogRetentionDays    { get; set; } = 7;
        public bool   WarnAlertEnabled    { get; set; } = false;
        public string InstallRoot         { get; set; } = @"C:\HeatingCameraSystem";
        public string AgentExePath        { get; set; } = @"C:\HeatingCameraSystem\Agent\HeatingCameraSystem.Agent.exe";
    }
}
