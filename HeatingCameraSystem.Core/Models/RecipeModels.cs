using System;
using System.Collections.Generic;

namespace HeatingCameraSystem.Core.Models
{
    public class Recipe
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Recipe";
        
        // 챔버 공통 환경 설정
        public float GlobalTargetTemperature { get; set; } = 25.0f;
        public float GlobalTargetHumidity { get; set; } = 50.0f;

        // 순차적으로 실행될 스텝 목록
        public List<RecipeStep> Steps { get; set; } = new();
    }

    public class RecipeStep
    {
        public string StepId { get; set; } = Guid.NewGuid().ToString();
        
        // 대상 카메라 인덱스 (1~64)
        public int CameraIndex { get; set; }
        
        // 서보 유닛이 이동해야 할 위치 (카메라 위치에 대응)
        public int TargetPositionIndex { get; set; }
        
        // 스텝 수행 시 블랙바디가 도달해야 할 타겟 온도
        public float TargetBlackBodyTemperature { get; set; }
    }
}
