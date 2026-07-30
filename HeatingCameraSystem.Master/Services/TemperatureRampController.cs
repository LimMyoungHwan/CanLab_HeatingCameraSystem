using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Master.Services
{
    public sealed class TemperatureRampController
    {
        private readonly IPlcController _plcController;
        private readonly TimeSpan _stepDelay;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Func<DateTime> _utcNow;

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
                progress?.Report($"온도 램프 {sv:F1}℃ / {target:F1}℃");
                if (frac >= 1.0) break;
                await _delay(_stepDelay, ct);
            }

            await _plcController.SetControlTemperatureAsync(target);
        }
    }
}
