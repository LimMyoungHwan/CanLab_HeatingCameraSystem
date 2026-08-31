using System;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// Master가 표시하는 카메라 상태. <b>영상 런타임에서만</b> 파생되며 시리얼 상태는 반영하지 않는다
    /// (시리얼 건강 상태는 <see cref="AgentStatusMessage.IsSerialConnected"/>가 따로 나른다).
    /// </summary>
    public enum CameraStatus
    {
        Offline,
        Connected,

        /// <summary>정의만 되어 있고 현재 어떤 Agent도 발행하지 않는다.</summary>
        Streaming
    }

    /// <summary>Master → Agent 카메라 시리얼 설정 전달.</summary>
    public class SerialConfigMessage
    {
        public string               AgentId   { get; set; } = string.Empty;
        public CameraSerialSettings Settings  { get; set; } = new();
        public DateTime             Timestamp { get; set; }
    }

    /// <summary>Agent → Master 시리얼 설정 적용 결과.</summary>
    public class SerialConfigAckMessage
    {
        public string   AgentId      { get; set; } = string.Empty;
        public bool     IsSuccess    { get; set; }
        public string   ErrorMessage { get; set; } = string.Empty;
        public DateTime Timestamp    { get; set; }
    }

    /// <summary>
    /// Agent → Master 카메라 상태 하트비트(<c>agent.status.{AgentId}</c>).
    /// 상태 보고이자 <see cref="HostAgentIds"/>를 통한 인벤토리 보고 역할을 겸한다.
    /// </summary>
    public class AgentStatusMessage
    {
        public string AgentId { get; set; } = string.Empty;

        /// <summary>
        /// 안정적인 라우팅 키. Master가 alias → 현재 AgentId로 매핑한다
        /// (레시피는 바뀌기 쉬운 슬롯이 아니라 alias를 대상으로 삼는다).
        /// </summary>
        public string Alias { get; set; } = string.Empty;

        public string HostName { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public CameraStatus CameraStatus { get; set; }
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 시리얼(셔터/COM) 건강 상태. <see cref="CameraStatus"/>가 영상 런타임에서만 파생되므로
        /// <b>별개의 축</b>으로 따로 보고한다 — 셔터가 죽어도 영상은 멀쩡할 수 있고 그 반대도 가능하다.
        /// 영상 전용 카메라는 지원 구성이 아니므로 false는 언제나 고장을 뜻한다.
        /// <br/>null = 보고할 수 없는 구버전 발신자 → Master는 시리얼 고장을 표시하지 않는다.
        /// </summary>
        public bool? IsSerialConnected { get; set; }

        /// <summary>
        /// 발신 호스트의 살아있는 카메라 인벤토리 전체(그 PC에서 현재 런타임이 돌고 있는 AgentId 목록).
        /// Master는 이 목록을 기준으로 해당 호스트를 정리하고 목록에 없는 카메라를 제거한다.
        /// AgentUI에서 카메라를 넣고 빼면 즉시 반영되는 근거가 이것이다.
        /// <br/>null = 아무것도 보고하지 않은 구버전 발신자 → Master는 <b>정리하지 않는다</b>(모르는 채로 지우지 않는다).
        /// <br/>빈 목록 = 보고했고, 그 호스트에 카메라가 정말 하나도 없다 → Master는 전부 제거한다.
        /// <br/>발신자 자신의 AgentId가 목록에 없으면 인벤토리 전용 보고이며(카메라가 0대인 호스트도
        /// 그 사실을 알려야 한다) 노드를 새로 만들어서는 안 된다.
        /// </summary>
        public List<string>? HostAgentIds { get; set; }
    }

    /// <summary>캡처를 유발한 주체. 이력 화면의 "촬영 구분" 필터가 이 값을 쓴다.</summary>
    public enum CaptureSource
    {
        Unknown = 0,
        Recipe = 1,
        Manual = 2,
        AgentUi = 3
    }

    /// <summary>Master → Agent 캡처 명령.</summary>
    public class CaptureCommandMessage
    {
        /// <summary>대상 Agent. <c>"all"</c>이면 전체 브로드캐스트다.</summary>
        public string TargetAgentId { get; set; } = string.Empty;

        public string RecipeStepId { get; set; } = string.Empty;
        public CaptureSource Source { get; set; } = CaptureSource.Unknown;
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Agent → Master 캡처 결과. 실패해도 <c>IsSuccess=false</c>로 발행하며,
    /// Master는 이 메시지를 받아 촬영 이력에 기록한다.
    /// </summary>
    public class CaptureResultMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public string RecipeStepId { get; set; } = string.Empty;
        public CaptureSource Source { get; set; } = CaptureSource.Unknown;

        /// <summary>이력 중복 기록을 막는 키.</summary>
        public string CaptureId { get; set; } = string.Empty;

        public bool IsSuccess { get; set; }

        /// <summary>Agent PC에 저장된 원본(방사 측정용) 파일 경로.</summary>
        public string ImagePath { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; }

        /// <summary>Master에서 바로 볼 수 있는 JPEG 미리보기.</summary>
        public byte[]? ImageBytes { get; set; }
        public double? CameraTemperature { get; set; }
    }

    /// <summary>Agent → Master 라이브 미리보기 프레임(<c>agent.live.{AgentId}</c>).</summary>
    public class LiveFrameMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public byte[]? ImageBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
