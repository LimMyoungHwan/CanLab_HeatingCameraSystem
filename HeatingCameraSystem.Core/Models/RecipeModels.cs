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

        /// <summary>
        /// 기록 조건: 챔버 온도가 직전 기록 시점 대비 이만큼(℃) 변하면 측정을 1건 남긴다. 0이면 미사용.
        /// 아래 세 조건은 OR이며, 하나라도 충족하면 기록한다. 전부 0이면 기록 기능 자체가 꺼진다.
        /// 스텝과 무관하게 레시피 시작부터 종료까지 전 구간에서 동작한다.
        /// </summary>
        public float RecordOnTemperatureDelta { get; set; }

        /// <summary>기록 조건: 챔버 습도 변화량(%RH). 0이면 미사용.</summary>
        public float RecordOnHumidityDelta { get; set; }

        /// <summary>기록 조건: 경과 시간(초). 0이면 미사용.</summary>
        public int RecordIntervalSeconds { get; set; }

        /// <summary>
        /// 생산 저장 규칙의 Master 측 저장 루트. 레시피 시작 시 운영자가 지정하며,
        /// 비어 있으면 규칙 저장을 하지 않는다.
        /// </summary>
        public string SaveRootPath { get; set; } = string.Empty;

        /// <summary>레시피 시작 시 운영자가 입력하는 제품 번호. 배치 전체에 공통으로 쓰인다.</summary>
        public string ProductNumber { get; set; } = string.Empty;

        /// <summary>생산 저장 규칙의 파일 포맷. 레시피 시작 시 운영자가 고른다.</summary>
        public ProductionCaptureFormat SaveFormat { get; set; } = ProductionCaptureFormat.Raw;

        /// <summary>순차적으로 실행될 스텝 목록.</summary>
        public List<RecipeStep> Steps { get; set; } = new();
    }

    /// <summary>
    /// 카메라 Tecless 온도대역. 챔버 목표 온도와는 별개의 축이며, 같은 챔버 온도라도
    /// 대역이 다르면 다른 저장 폴더가 된다(+40℃ → 상온이면 RPP40, 고온이면 H1PP40).
    /// </summary>
    public enum ChamberRange
    {
        Low,
        Mid,
        High
    }

    /// <summary>촬영 시점에 카메라 앞에 있는 흑체의 역할. <see cref="Room"/>은 흑체 없음을 뜻한다.</summary>
    public enum BlackBodyRole
    {
        Hot,
        Cold,
        Room
    }

    /// <summary>레시피 스텝의 실행 대상이다. 기본값은 이전 버전의 일괄 촬영 스텝과 호환된다.</summary>
    public enum RecipeStepKind
    {
        LegacyCapture,
        MotorMove,

        /// <summary>챔버 온도 전용 스텝. 습도는 <see cref="HumidityControl"/>에서 따로 제어한다.</summary>
        ChamberControl,
        CameraCommand,
        BlackBodyControl,

        /// <summary>챔버 습도 전용 스텝.</summary>
        HumidityControl,

        /// <summary>지정한 시간(시·분·초)만큼 아무 동작 없이 대기하는 스텝.</summary>
        Wait,

        /// <summary>
        /// 결과를 기다리지 않고 넘어간(<see cref="RecipeStep.WaitForCaptureResult"/>=false) 캡처들이
        /// 모두 끝날 때까지 대기하는 스텝.
        /// </summary>
        CaptureJoin
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

        /// <summary>
        /// 이 카메라의 Tecless 온도대역. 저장 폴더의 앞 코드(LN/RP/H1P)가 되고, 캡처 전
        /// 대역별 BIAS를 적용하는 근거가 된다. null이면 규칙 저장을 하지 않는다.
        /// </summary>
        public ChamberRange? TargetChamber { get; set; }

        /// <summary>
        /// 촬영 시점에 이 카메라 앞에 놓인 흑체의 역할. 흑체 유닛이 아니라 카메라에 붙는 이유는
        /// 카메라가 고정이고 흑체가 이동하기 때문이다. null이면 규칙 저장을 하지 않는다.
        /// </summary>
        public BlackBodyRole? TargetBlackBody { get; set; }
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

        /// <summary>캡처 스텝에서 한 번에 찍을 장수(연속, 간격 없음). Agent가 장마다 결과를 보낸다.</summary>
        public int ShotCount { get; set; } = 1;

        /// <summary>
        /// 캡처 반복 간격(초). 0이면 반복하지 않고 1회만 촬영한다.
        /// <see cref="ShotCount"/>가 "한 번에 몇 장"이라면 이쪽은 "몇 초마다 다시 찍는가"다.
        /// </summary>
        public int CaptureIntervalSeconds { get; set; }

        /// <summary>
        /// 캡처 반복 전체 시간(초). 촬영 횟수는 <c>전체시간 / 간격</c>으로 정해진다
        /// (예: 간격 60초, 전체 1800초 → 30회). 간격이 0이면 무시된다.
        /// 각 회차는 스텝 시작 시각 기준 절대 시각에 맞춰 실행되므로 지연이 누적되지 않는다.
        /// </summary>
        public int CaptureDurationSeconds { get; set; }

        /// <summary>
        /// BIAS 자동 탐색이 맞출 목표 레벨. 0이면 Agent의 모드별 기본 목표를 쓴다.
        /// Cint 등 레지스터 값은 카메라 특성이라 이 값으로 바뀌지 않는다.
        /// </summary>
        public double BiasTargetLevel { get; set; }

        /// <summary>서보 유닛 직접 이동 X 좌표(direct-XY-move).</summary>
        public float PositionX { get; set; }

        /// <summary>서보 유닛 직접 이동 Y 좌표(direct-XY-move).</summary>
        public float PositionY { get; set; }

        /// <summary>스텝별 챔버 목표 온도(℃).</summary>
        public double TargetChamberTemperature { get; set; }

        /// <summary>스텝별 챔버 목표 습도(%RH).</summary>
        public double TargetChamberHumidity { get; set; }

        /// <summary>
        /// true면 습도 스텝이 목표를 쓰는 대신 챔버 습도 제어를 끈다.
        /// PLC는 마지막 목표를 계속 쫓으므로, 한 번 맞춘 뒤 방치하려면 이 스텝이 필요하다.
        /// </summary>
        public bool DisableHumidityControl { get; set; }

        /// <summary>true면 챔버가 목표값에 도달할 때까지 대기하고, false면 설정만 하고 다음 스텝으로 넘어간다.</summary>
        public bool WaitForChamberStabilization { get; set; } = true;

        /// <summary>온도 도달 판정 폭(±℃). 0 이하면 전역 설정 TemperatureTolerance를 쓴다.</summary>
        public double StabilizationToleranceC { get; set; }

        /// <summary>습도 도달 판정 폭(±%RH). 0 이하면 기본값 5%RH를 쓴다.</summary>
        public double StabilizationToleranceRh { get; set; }

        /// <summary>목표 도달 후 다음 스텝으로 넘어가기 전 유지할 시간(분). 0이면 도달 즉시 진행한다.</summary>
        public int SoakMinutes { get; set; }

        /// <summary>
        /// true면 이 스텝 이후 챔버 온도가 <see cref="SafetyTempMin"/>~<see cref="SafetyTempMax"/> 범위를
        /// 벗어날 때 알람 후 운전자 확인 대기. 목표값 기준 상대 오차가 아니라 절대 한계이므로
        /// 승온·냉각 중 과도구간을 오탐하지 않는다.
        /// </summary>
        public bool UseSafetyTemperature { get; set; }

        public float SafetyTempMin { get; set; }
        public float SafetyTempMax { get; set; }

        /// <summary>true면 습도를 <see cref="SafetyHumidityMin"/>~<see cref="SafetyHumidityMax"/>로 검사한다.</summary>
        public bool UseSafetyHumidity { get; set; }

        public float SafetyHumidityMin { get; set; }
        public float SafetyHumidityMax { get; set; }

        /// <summary>
        /// 대기 스텝의 총 대기 시간(초). 편집 UI의 시·분·초 입력을 합산한 값이며
        /// <see cref="RecipeStepKind.Wait"/>에서만 사용한다. 0이면 즉시 다음 스텝으로 넘어간다.
        /// </summary>
        public int WaitDurationSeconds { get; set; }

        /// <summary>
        /// false면 캡처 명령만 보내고 결과를 기다리지 않는다(fork). 이 경우 뒤에
        /// <see cref="RecipeStepKind.CaptureJoin"/> 스텝을 두지 않으면 촬영 중에 모터가 움직여
        /// 데이터가 오염될 수 있으며, 그 책임은 레시피 작성자에게 있다.
        /// </summary>
        public bool WaitForCaptureResult { get; set; } = true;

        /// <summary>
        /// <see cref="RecipeStepKind.CaptureJoin"/> 스텝의 대기 한도(초). 0이면 전역 설정을 쓴다.
        /// </summary>
        public int CaptureJoinTimeoutSeconds { get; set; }
    }
}
