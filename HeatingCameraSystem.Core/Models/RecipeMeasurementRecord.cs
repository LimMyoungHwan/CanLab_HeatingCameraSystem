using System;
using System.Collections.Generic;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 레시피 실행 중 기록 조건이 충족될 때마다 남기는 측정 1건.
    /// 캡처 이력(<see cref="CaptureHistoryRecord"/>)과 달리 촬영과 무관하게 남으며,
    /// 레시피 결과 화면이 <see cref="RunId"/>로 묶어 시계열 차트를 그린다.
    /// </summary>
    public class RecipeMeasurementRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>같은 레시피의 실행 회차를 구분하는 키. 실행 1회 = RunId 1개.</summary>
        public string RunId { get; set; } = string.Empty;

        public string RecipeId { get; set; } = string.Empty;
        public string RecipeName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }

        public float ChamberTemperature { get; set; }
        public float ChamberHumidity { get; set; }

        /// <summary>AgentId별 카메라 FPA 온도(℃). 하트비트로 온도를 보고한 Agent만 들어간다.</summary>
        public Dictionary<string, double> CameraTemperatures { get; set; } = new();
    }
}
