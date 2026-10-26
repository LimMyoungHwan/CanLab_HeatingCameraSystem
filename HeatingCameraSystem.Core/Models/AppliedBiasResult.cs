using System;

namespace HeatingCameraSystem.Core.Models
{
    public enum BiasApplicationSource
    {
        Automatic,
        Manual
    }

    /// <summary>
    /// 현재 카메라에 적용한 보정 설정. 온도별 저장 폴더와 별개이며, 새 적용마다 ID가 바뀐다.
    /// 수동 적용은 레지스터 전송과 GSK_LSB 읽기 일치로 확인하며 픽셀 목표를 다시 탐색하지 않는다.
    /// </summary>
    public sealed record AppliedBiasResult(
        string ApplicationId,
        string Operation,
        byte GskLsb,
        string Json,
        BiasApplicationSource Source,
        DateTimeOffset AppliedAtUtc);
}
