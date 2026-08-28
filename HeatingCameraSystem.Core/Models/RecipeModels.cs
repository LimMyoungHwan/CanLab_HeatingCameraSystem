using System;
using System.Collections.Generic;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 촬영 레시피. 챔버 환경과 순차적으로 실행할 스텝 목록을 담는다.
    /// </summary>
    public class Recipe
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>레시피 표시 이름.</summary>
        public string Name { get; set; } = "New Recipe";

        /// <summary>챔버 공통 목표 온도(℃).</summary>
        public float GlobalTargetTemperature { get; set; } = 25.0f;

        /// <summary>챔버 공통 목표 습도(%RH).</summary>
        public float GlobalTargetHumidity { get; set; } = 50.0f;

        /// <summary>타겟 온도 도달 시간(분). 0이면 즉시 지정, 0보다 크면 현재 온도→타겟 선형 램프(히터 급출력 방지).</summary>
        public int TemperatureRampMinutes { get; set; } = 0;

        /// <summary>안전 밴드: 챔버 현재 온도가 전역 목표에서 이만큼(℃) 벗어나면 촬영을 멈추고 알람 후 사용자 확인 대기.</summary>
        public float SafetyTempTolerance { get; set; } = 1.0f;

        /// <summary>안전 밴드: 챔버 현재 습도가 전역 목표에서 이만큼(%RH) 벗어나면 촬영을 멈추고 알람 후 사용자 확인 대기.</summary>
        public float SafetyHumidityTolerance { get; set; } = 5.0f;

        /// <summary>순차적으로 실행될 스텝 목록.</summary>
        public List<RecipeStep> Steps { get; set; } = new();
    }

    /// <summary>레시피의 개별 스텝. 하나의 촬영 동작을 정의한다.</summary>
    public class RecipeStep
    {
        public string StepId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>대상 카메라 인덱스(1~64). <see cref="CameraAlias"/>가 설정되어 있으면 Alias 우선.</summary>
        public int CameraIndex { get; set; }

        /// <summary>운영자가 부여한 카메라 별칭. 비어 있으면 <see cref="CameraIndex"/> 기반 AgentId 폴백.</summary>
        public string? CameraAlias { get; set; }

        /// <summary>서보 유닛이 이동해야 할 위치. 카메라 위치에 대응.</summary>
        public int TargetPositionIndex { get; set; }

        /// <summary>스텝 수행 시 블랙바디가 도달해야 할 목표 온도(℃).</summary>
        public float TargetBlackBodyTemperature { get; set; }

        /// <summary>서보 유닛 직접 이동 X 좌표(direct-XY-move).</summary>
        public float PositionX { get; set; }

        /// <summary>서보 유닛 직접 이동 Y 좌표(direct-XY-move).</summary>
        public float PositionY { get; set; }

        /// <summary>스텝별 챔버 목표 온도(℃).</summary>
        public double TargetChamberTemperature { get; set; }

        /// <summary>스텝별 챔버 목표 습도(%RH).</summary>
        public double TargetChamberHumidity { get; set; }
    }
}
