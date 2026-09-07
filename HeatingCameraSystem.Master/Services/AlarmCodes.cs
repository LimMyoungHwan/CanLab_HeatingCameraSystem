namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 알람 식별 코드. 알람 창이 이 코드로 원인·해결책 번역 문구를 찾으므로,
    /// 코드를 추가하면 <c>Resources/Lang/ko.txt</c>·<c>en.txt</c>에
    /// <c>Alarm_{코드}_Cause</c>·<c>Alarm_{코드}_Action</c> 두 키를 함께 추가해야 한다.
    /// </summary>
    public static class AlarmCodes
    {
        public const string PlcCommFailed = "PLC-001";
        public const string SafetyCheckFailed = "PLC-002";
        public const string ChamberStopFailed = "PLC-003";
        public const string MotorMoveTimeout = "PLC-004";
        public const string MotorPointMismatch = "PLC-005";
        public const string NatsPublishFailed = "NATS-001";
        public const string AgentTimeout = "CAM-001";
        public const string PartialCapture = "CAM-002";
        public const string CaptureScheduleSlip = "CAM-003";
        public const string CameraOffline = "CAM-004";
        public const string CaptureAbortNoAck = "CAM-005";
        public const string BlackBodyFailed = "BB-001";
        public const string UserStopped = "RCP-001";
        public const string EmergencyStopped = "RCP-002";
        public const string SafetyBandViolation = "RCP-003";
        public const string StepKindUnsupported = "RCP-004";
        public const string ProductionNamingFailed = "RCP-005";

        public static string CauseKey(string code) => "Alarm_" + code.Replace("-", "_") + "_Cause";

        public static string ActionKey(string code) => "Alarm_" + code.Replace("-", "_") + "_Action";
    }
}
