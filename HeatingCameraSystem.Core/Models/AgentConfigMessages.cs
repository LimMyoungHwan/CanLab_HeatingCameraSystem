using System;
using System.Collections.Generic;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>Agent 실행 설정 스냅샷. <see cref="AgentConfigApplyMessage"/>로 원격 적용할 수 있다.</summary>
    public class AgentConfigSnapshot
    {
        /// <summary>
        /// true이면 실제 카메라 대신 합성 이미지를 생성한다.
        /// 카메라/웹캠이 없는 환경에서 테스트용.
        /// </summary>
        public bool SimulationMode { get; set; }

        /// <summary>NATS 서버 URL.</summary>
        public string NatsUrl { get; set; } = "nats://127.0.0.1:4222";

        /// <summary>캡처 이미지 저장 루트 경로.</summary>
        public string StoragePath { get; set; } = string.Empty;

        /// <summary>Master로 보내는 하트비트 간격(초).</summary>
        public int HeartbeatSeconds { get; set; } = 5;

        /// <summary>캡처 이미지 형식.</summary>
        public CaptureImageFormat CaptureImageFormat { get; set; } = CaptureImageFormat.Y16Raw;

        /// <summary>연속 촬영 장수. 1이면 단일 촬영.</summary>
        public int CaptureBurstCount { get; set; } = 1;

        /// <summary>이 Agent가 관리하는 카메라 설명 목록.</summary>
        public List<CameraDescriptor> Cameras { get; set; } = new();
    }

    /// <summary>Master → Agent 설정 조회 요청.</summary>
    public class AgentConfigRequestMessage
    {
        /// <summary>대상 AgentId.</summary>
        public string AgentId { get; set; } = string.Empty;

        /// <summary>요청 시각.</summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>Agent → Master 현재 설정 보고.</summary>
    public class AgentConfigSnapshotMessage
    {
        /// <summary>발신 AgentId.</summary>
        public string AgentId { get; set; } = string.Empty;

        /// <summary>현재 설정.</summary>
        public AgentConfigSnapshot Config { get; set; } = new();

        /// <summary>보고 시각.</summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>Master → Agent 설정 적용 명령.</summary>
    public class AgentConfigApplyMessage
    {
        /// <summary>대상 AgentId.</summary>
        public string AgentId { get; set; } = string.Empty;

        /// <summary>적용할 설정.</summary>
        public AgentConfigSnapshot Config { get; set; } = new();

        /// <summary>명령 시각.</summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>Agent → Master 설정 적용 결과.</summary>
    public class AgentConfigAckMessage
    {
        /// <summary>발신 AgentId.</summary>
        public string AgentId { get; set; } = string.Empty;

        /// <summary>적용 성공 여부.</summary>
        public bool IsSuccess { get; set; }

        /// <summary>실패 사유. 성공 시 빈 문자열.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>응답 시각.</summary>
        public DateTime Timestamp { get; set; }
    }
}
