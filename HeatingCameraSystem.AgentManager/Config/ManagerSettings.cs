namespace HeatingCameraSystem.AgentManager.Config
{
    /// <summary>
    /// manager-settings.json에서 읽는 AgentManager 설정. 파일이 없으면 기본값을 쓰고,
    /// InstallRoot는 실행 시 첫 번째 CLI 인수로 항상 덮어써진다.
    /// </summary>
    public class ManagerSettings
    {
        /// <summary>이 카메라 PC의 라우팅 키. <c>agent-mgr.*</c>/<c>server.*</c> 토픽의 {PCId} 자리에 쓰인다.</summary>
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
        /// <summary>true이면 Error/Fatal 외에 Warning 로그도 LogAlert로 승격한다.</summary>
        public bool   WarnAlertEnabled    { get; set; } = false;
        public string InstallRoot         { get; set; } = @"C:\HeatingCameraSystem";
        /// <summary>콘솔 Agent 실행 파일 경로. [S7] 이후 Manager는 프로세스를 spawn하지 않으므로 현재 코드에서는 사용되지 않는다.</summary>
        public string AgentExePath        { get; set; } = @"C:\HeatingCameraSystem\Agent\HeatingCameraSystem.Agent.exe";

        // [S7] 이제 로컬 카메라 전부를 소유하는 단일 WPF AgentUI의 경로. 마이그레이션 동안
        // AgentExePath(콘솔 Agent)와 나란히 유지한다. 주의: Manager 서비스가 직접 실행하지 않는다
        // (세션 0 격리 + UVC 단일 핸들 문제) — 로그온 예약 작업(S8)이 실행한다.
        // S8 배포/진단과 향후 대화형 모드 실행을 위해 설정에 남겨 둔다.
        public string AgentUiExePath      { get; set; } = @"C:\HeatingCameraSystem\AgentUI\HeatingCameraSystem.AgentUI.exe";
    }
}
