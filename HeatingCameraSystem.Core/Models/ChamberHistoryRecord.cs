using System;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 챔버 환경 이력 레코드. PLC 상태 폴링 결과를 LiteDB에 저장할 때 쓴다.
    /// </summary>
    public class ChamberHistoryRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>챔버 현재 온도(℃).</summary>
        public float Temperature { get; set; }

        /// <summary>챔버 현재 습도(%RH).</summary>
        public float Humidity { get; set; }

        /// <summary>흑체 1 현재 온도(℃).</summary>
        public float BlackBody1 { get; set; }

        /// <summary>흑체 2 현재 온도(℃).</summary>
        public float BlackBody2 { get; set; }
    }
}
