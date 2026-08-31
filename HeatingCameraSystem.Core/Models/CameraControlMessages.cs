using System;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// Master → Agent 카메라 제어 명령(<c>master.cmd.camera.{AgentId}</c>).
    /// 수행할 동작은 <see cref="Op"/>에 <see cref="CameraControlOps"/> 상수로 담는다.
    /// </summary>
    public class CameraControlMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public int CameraIndex { get; set; }

        /// <summary><see cref="CameraControlOps"/>의 상수 중 하나.</summary>
        public string Op { get; set; } = string.Empty;

        /// <summary>명령 완료 ACK를 해당 레시피 스텝과 안전하게 연결하는 요청 식별자.</summary>
        public string RequestId { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// <see cref="CameraControlMessage.Op"/>에 쓰는 명령 문자열 모음. 프로세스 간 계약이므로
    /// 값을 바꾸면 Master와 Agent를 함께 배포해야 한다.
    /// </summary>
    public static class CameraControlOps
    {
        public const string Run = "run";
        public const string Stop = "stop";
        public const string ShutterOpen = "shutterOpen";
        public const string ShutterClose = "shutterClose";
        public const string Capture = "capture";
        public const string Nuc = "nuc";
        public const string BiasLow = "biasLow";
        public const string BiasMid = "biasMid";
        public const string BiasHigh = "biasHigh";
        public const string SaveConfig = "saveConfig";
        public const string RefreshInfo = "refreshInfo";

        // [S7] 카메라 단위 런타임 로드/언로드 — 위의 시리얼 Run/Stop과는 다른 개념이다.
        // Manager(재정의된 AgentSupervisor)가 AgentUI 프로세스를 죽이지 않고 그 안의 카메라
        // 런타임 하나만 로드/언로드할 때 쓴다. runtimeLoad는 멱등 재로드라서 Restart를 Load
        // 메시지 한 건으로 처리할 수 있다(언로드→로드 사이의 경쟁 상태가 생기지 않는다).
        public const string RuntimeLoad = "runtimeLoad";
        public const string RuntimeUnload = "runtimeUnload";
    }

    /// <summary>
    /// Agent → Master 카메라 제어 응답(<c>agent.ack.camera.{AgentId}</c>).
    /// 실패 사유는 <see cref="Message"/>에 담기며, 이 값이 운영자 화면에 그대로 노출된다.
    /// </summary>
    public class CameraControlAckMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public string Op { get; set; } = string.Empty;

        /// <summary>원본 <see cref="CameraControlMessage.RequestId"/>. 수동 명령은 비어 있을 수 있다.</summary>
        public string RequestId { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
