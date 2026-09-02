using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 현재 온도에서 목표 온도까지 제어 온도(SV)를 시간에 비례해 선형으로 올려
    /// 히터가 급출력으로 치닫지 않게 한다. <c>Recipe.TemperatureRampMinutes</c>(분)가 램프 길이다.
    /// </summary>
    public sealed class TemperatureRampController
    {
        private readonly IPlcController _plcController;
        private readonly TimeSpan _stepDelay;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Func<DateTime> _utcNow;

        /// <summary>스텝 간격은 최소 1초로 강제한다. delay·utcNow는 테스트에서 시간 흐름을 대체하기 위한 주입 지점이다.</summary>
        public TemperatureRampController(
            IPlcController plcController,
            int rampStepIntervalSeconds,
            Func<TimeSpan, CancellationToken, Task>? delay = null,
            Func<DateTime>? utcNow = null)
        {
            _plcController = plcController;
            _stepDelay = TimeSpan.FromSeconds(Math.Max(1, rampStepIntervalSeconds));
            _delay = delay ?? Task.Delay;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// 목표 온도를 PLC에 먼저 쓴 뒤, minutes가 0 이하면 제어 온도를 즉시 목표로 놓는다.
        /// 그 외에는 경과 시간 비율로 start→target 사이 값을 스텝 간격마다 다시 써서 선형 램프를
        /// 만들고, 램프가 끝나면 마지막에 목표값을 한 번 더 확정해 쓴다.
        /// </summary>
        public async Task RampAsync(
            float start,
            float target,
            int minutes,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            await _plcController.SetTargetTemperatureAsync(target);

            if (minutes <= 0)
            {
                await _plcController.SetControlTemperatureAsync(target);
                return;
            }

            double durationSeconds = minutes * 60.0;
            var startedAt = _utcNow();

            while (!ct.IsCancellationRequested)
            {
                double frac = Math.Min((_utcNow() - startedAt).TotalSeconds / durationSeconds, 1.0);
                float sv = start + (float)((target - start) * frac);
                await _plcController.SetControlTemperatureAsync(sv);
                progress?.Report(string.Format(
                    Localization.LocalizationManager.Instance["Recipe_Phase_TempRamp"], sv, target));
                if (frac >= 1.0) break;
                await _delay(_stepDelay, ct);
            }

            await _plcController.SetControlTemperatureAsync(target);
        }
    }
}
