using System;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>Master와 Agent가 공유하는 BIAS 재시도·대기 계약.</summary>
    public static class BiasExecutionPolicy
    {
        public const int MaxAttempts = 4; // 최초 1회 + 재시도 3회
        public const int RetryDelayMilliseconds = 500;
        public const int DefaultAttemptTimeoutSeconds = 120;
        public const string FileFailureCode = "biasFileFailed";

        public static bool IsBiasOperation(string op)
            => op is CameraControlOps.BiasLow or CameraControlOps.BiasMid or CameraControlOps.BiasHigh;

        public static int NormalizeAttemptTimeoutSeconds(int value)
            => Math.Clamp(value > 0 ? value : DefaultAttemptTimeoutSeconds, 1, 3600);

        /// <summary>Agent의 전체 시도와 재시도 간격 뒤에 통신 여유 10초를 둔다.</summary>
        public static TimeSpan ResponseTimeout(int attemptTimeoutSeconds)
            => TimeSpan.FromSeconds(MaxAttempts * NormalizeAttemptTimeoutSeconds(attemptTimeoutSeconds) + 10)
               + TimeSpan.FromMilliseconds((MaxAttempts - 1) * RetryDelayMilliseconds);
    }
}
