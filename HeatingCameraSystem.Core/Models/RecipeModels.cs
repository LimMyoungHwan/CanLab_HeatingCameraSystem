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

        /// <summary>타겟 온도 도달 시간(분). 0이면 즉시 지정, 0보다 크면 현재 온도→타겟 선형 램프(히터 급출력 방지).</summary>
        public int TemperatureRampMinutes { get; set; } = 0;

        /// <summary>순차적으로 실행될 스텝 목록.</summary>
        public List<RecipeStep> Steps { get; set; } = new();
    }

    /// <summary>레시피 스텝의 실행 대상이다. 기본값은 이전 버전의 일괄 촬영 스텝과 호환된다.</summary>
    public enum RecipeStepKind
    {
        LegacyCapture,
        MotorMove,
        ChamberControl,
        CameraCommand,
        BlackBodyControl
    }

    public enum MotorMoveType
    {
        Manual,
        Automatic
    }

    public class RecipeCameraTarget
    {
        public string AgentId { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
    }

    /// <summary>레시피의 개별 스텝. PLC 또는 카메라의 한 동작을 정의한다.</summary>
    public class RecipeStep
    {
        public string StepId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>실행할 장치·동작 종류. 저장된 기존 레시피는 <see cref="LegacyCapture"/>로 실행된다.</summary>
        public RecipeStepKind Kind { get; set; } = RecipeStepKind.LegacyCapture;

        /// <summary><see cref="CameraControlOps"/>의 카메라 명령. <see cref="RecipeStepKind.CameraCommand"/>에서만 사용한다.</summary>
        public string CameraOperation { get; set; } = CameraControlOps.Capture;

        public List<RecipeCameraTarget> CameraTargets { get; set; } = new();

        /// <summary>대상 카메라 인덱스(1~64). <see cref="CameraAlias"/>가 설정되어 있으면 Alias 우선.</summary>
        public int CameraIndex { get; set; }

        /// <summary>운영자가 부여한 카메라 별칭. 비어 있으면 <see cref="CameraIndex"/> 기반 AgentId 폴백.</summary>
        public string? CameraAlias { get; set; }

        /// <summary>서보 유닛이 이동해야 할 위치. 카메라 위치에 대응.</summary>
        public int TargetPositionIndex { get; set; }

        public MotorMoveType MotorMoveType { get; set; } = MotorMoveType.Manual;

        /// <summary>스텝 수행 시 블랙바디가 도달해야 할 목표 온도(℃).</summary>
        public float TargetBlackBodyTemperature { get; set; }

        public float TargetBlackBodyTemperature1 { get; set; }

        /// <summary>제어할 블랙바디 인덱스(0=흑체1, 1=흑체2).</summary>
        public int BlackBodyIndex { get; set; }

        /// <summary>true면 목표 온도에 도달할 때까지 대기한다.</summary>
        public bool WaitForStabilization { get; set; } = true;

        /// <summary>캡처 스텝에서 찍을 장수. Agent가 이 장수만큼 찍고 각각 결과를 보낼 때까지 스텝이 끝나지 않는다.</summary>
        public int ShotCount { get; set; } = 1;

        /// <summary>서보 유닛 직접 이동 X 좌표(direct-XY-move).</summary>
        public float PositionX { get; set; }

        /// <summary>서보 유닛 직접 이동 Y 좌표(direct-XY-move).</summary>
        public float PositionY { get; set; }

        /// <summary>스텝별 챔버 목표 온도(℃).</summary>
        public double TargetChamberTemperature { get; set; }

        /// <summary>스텝별 챔버 목표 습도(%RH).</summary>
        public double TargetChamberHumidity { get; set; }

        /// <summary>true면 챔버가 목표 온도에 도달할 때까지 대기하고, false면 설정만 하고 다음 스텝으로 넘어간다.</summary>
        public bool WaitForChamberStabilization { get; set; } = true;

        /// <summary>
        /// 안전 밴드 온도 허용오차(℃). 이 스텝 이후 챔버 현재 온도가 <see cref="TargetChamberTemperature"/>에서
        /// 이만큼 벗어나면 알람 후 운전자 확인 대기. 0이면 온도 안전 검사를 하지 않는다.
        /// </summary>
        public float SafetyTempTolerance { get; set; }

        /// <summary>
        /// 안전 밴드 습도 허용오차(%RH). 판정 기준은 <see cref="SafetyTempTolerance"/>와 같으며
        /// 0이면 습도 안전 검사를 하지 않는다.
        /// </summary>
        public float SafetyHumidityTolerance { get; set; }
    }
}
