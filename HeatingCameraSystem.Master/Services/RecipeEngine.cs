using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 레시피를 스텝 단위로 실행하는 오케스트레이터: 챔버 기동 → 온도 램프·안정화 →
    /// 스텝별(서보 이동 → 흑체 안정화 → NATS 캡처 명령 → 결과 대기) → 챔버 정지.
    /// 레시피 캡처 이력은 동기 PLC 온습도까지 채워 여기서 직접 기록한다
    /// (비레시피 캡처는 <see cref="CaptureResultHistoryRecorder"/> 담당).
    /// </summary>
    public class RecipeEngine
    {
        private readonly IPlcController _plcController;
        private readonly INatsCommunicationService _natsService;
        private readonly ICaptureHistoryRepository _historyRepo;
        private readonly float _tempTolerance;
        private readonly TimeSpan _captureTimeout;
        private readonly TemperatureRampController _temperatureRampController;
        private readonly string? _imageCacheDir;
        private readonly ICameraDeviceRepository? _deviceRepo;
        private readonly IBlackBodyController _blackBody;
        private readonly AgentDirectory? _agentDirectory;

        public RecipeEngine(
            IPlcController plcController,
            INatsCommunicationService natsService,
            ICaptureHistoryRepository historyRepo,
            RecipeEngineSettings? settings = null,
            string? imageCacheDir = null,
            ICameraDeviceRepository? deviceRepo = null,
            IBlackBodyController? blackBody = null,
            AgentDirectory? agentDirectory = null)
        {
            _plcController = plcController;
            _natsService = natsService;
            _historyRepo = historyRepo;
            var s = settings ?? new RecipeEngineSettings();
            _tempTolerance  = s.TemperatureTolerance;
            _captureTimeout = TimeSpan.FromSeconds(s.CaptureResultTimeoutSeconds);
            _temperatureRampController = new TemperatureRampController(plcController, s.RampStepIntervalSeconds);
            _imageCacheDir  = imageCacheDir;
            _deviceRepo     = deviceRepo;
            _blackBody      = blackBody ?? new HeatingCameraSystem.Protocols.PlcBlackBodyAdapter(plcController);
            _agentDirectory = agentDirectory;
        }

        /// <summary>
        /// 레시피 전체를 실행한다. 시작 시 캡처 결과 구독을 걸어 StepId별 TaskCompletionSource로
        /// 결과를 기다리며, 스텝의 캡처 실패·타임아웃은 <see cref="AlarmSink"/>에 알리고 다음 스텝을
        /// 계속한다. 정상 완료 시에만 StopChamberAsync를 호출한다 — 취소로 중단되면 챔버 정지는
        /// AppServices 종료 시퀀스가 유일한 안전망이다.
        /// </summary>
        public async Task ExecuteRecipeAsync(Recipe recipe, CancellationToken cancellationToken = default, IProgress<RecipeProgress>? progress = null)
        {
            int totalSteps = recipe.Steps.Count;
            var resultWaiters = new ConcurrentDictionary<string, TaskCompletionSource<CaptureResultMessage>>();

            await _natsService.SubscribeCaptureResultAsync(result =>
            {
                if (resultWaiters.TryGetValue(result.RecipeStepId, out var tcs))
                    tcs.TrySetResult(result);
            });

            Console.WriteLine($"[RecipeEngine] Starting recipe: {recipe.Name}");

            progress?.Report(new RecipeProgress { CurrentStep = 0, TotalSteps = totalSteps, CurrentPhase = "챔버 안정화" });

            await _plcController.StartChamberAsync();
            await _plcController.SetTargetHumidityAsync(recipe.GlobalTargetHumidity);
            await RampTemperatureAsync(recipe, totalSteps, progress, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                float currentTemp = await _plcController.GetCurrentTemperatureAsync();
                if (Math.Abs(currentTemp - recipe.GlobalTargetTemperature) <= _tempTolerance) break;
                await Task.Delay(2000, cancellationToken);
            }

            Console.WriteLine("[RecipeEngine] Chamber ready. Executing steps...");

            for (int i = 0; i < recipe.Steps.Count; i++)
            {
                var step = recipe.Steps[i];
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new RecipeProgress { CurrentStep = i, TotalSteps = totalSteps, CurrentPhase = $"서보 이동 ({i + 1}/{totalSteps})" });
                await _plcController.MoveToCoordinateAsync(step.PositionX, step.PositionY);
                while (!cancellationToken.IsCancellationRequested)
                {
                    // 좌표 이동은 포인트 인덱스가 아니므로 도착 판정은 서보 축 idle(비구동)로 확인.
                    // ponytail: 실HW는 이동 트리거 직후 busy가 늦게 서므로 정착 지연이 필요할 수 있음 — 하드웨어 QA에서 튜닝.
                    var servoStatus = await _plcController.ReadStatusAsync();
                    if (!servoStatus.ServoXBusy && !servoStatus.ServoYBusy) break;
                    await Task.Delay(500, cancellationToken);
                }

                progress?.Report(new RecipeProgress { CurrentStep = i, TotalSteps = totalSteps, CurrentPhase = $"BB 안정화 ({i + 1}/{totalSteps})" });
                const int activeBB = 0;
                await _blackBody.SetTemperatureAsync(activeBB, step.TargetBlackBodyTemperature);
                while (!cancellationToken.IsCancellationRequested)
                {
                    float bbTemp = await _blackBody.GetCurrentTemperatureAsync(activeBB);
                    if (Math.Abs(bbTemp - step.TargetBlackBodyTemperature) <= _tempTolerance) break;
                    await Task.Delay(1000, cancellationToken);
                }

                progress?.Report(new RecipeProgress { CurrentStep = i, TotalSteps = totalSteps, CurrentPhase = $"캡처 ({i + 1}/{totalSteps})" });
                var tcs = new TaskCompletionSource<CaptureResultMessage>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                resultWaiters[step.StepId] = tcs;

                string targetAgentId = await ResolveAgentIdAsync(step);

                await _natsService.PublishCaptureCommandAsync(new CaptureCommandMessage
                {
                    TargetAgentId = targetAgentId,
                    RecipeStepId  = step.StepId,
                    Source        = CaptureSource.Recipe,
                    Timestamp     = DateTime.UtcNow
                });

                var done = await Task.WhenAny(tcs.Task, Task.Delay(_captureTimeout, cancellationToken));
                if (done == tcs.Task)
                {
                    var captureResult = tcs.Task.Result;
                    if (captureResult.IsSuccess)
                    {
                        float temp = 0f, humidity = 0f;
                        try
                        {
                            temp     = await _plcController.GetCurrentTemperatureAsync();
                            humidity = await _plcController.GetCurrentHumidityAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[RecipeEngine] PLC read failed: {ex.Message}");
                        }

                        string storedImagePath = CaptureResultImageCache.Store(captureResult, _imageCacheDir) ?? captureResult.ImagePath;

                        string cameraId = !string.IsNullOrWhiteSpace(captureResult.Alias) ? captureResult.Alias
                            : !string.IsNullOrWhiteSpace(step.CameraAlias) ? step.CameraAlias
                            : captureResult.AgentId;

                        await _historyRepo.InsertAsync(new CaptureHistoryRecord
                        {
                            Id           = string.IsNullOrEmpty(captureResult.CaptureId) ? Guid.NewGuid().ToString() : captureResult.CaptureId,
                            CameraId     = cameraId,
                            AgentId      = captureResult.AgentId,
                            CameraAlias  = captureResult.Alias,
                            CameraIndex  = step.CameraIndex,
                            Source       = CaptureSource.Recipe,
                            ImagePath    = storedImagePath,
                            RecipeStepId = captureResult.RecipeStepId,
                            Timestamp    = captureResult.Timestamp,
                            Temperature  = temp,
                            Humidity     = humidity
                        });
                    }
                    else
                    {
                        AlarmSink.Raise(AlarmSeverity.Error, "레시피", $"스텝 {step.StepId} 캡처 실패");
                        Console.WriteLine($"[RecipeEngine] Step {step.StepId}: capture failed.");
                    }
                }
                else
                {
                    AlarmSink.Raise(AlarmSeverity.Warning, "레시피", $"스텝 {step.StepId} 캡처 타임아웃");
                    Console.WriteLine($"[RecipeEngine] Step {step.StepId}: capture timeout ({_captureTimeout.TotalSeconds:0}s).");
                }

                resultWaiters.TryRemove(step.StepId, out _);
            }

            await _plcController.StopChamberAsync();
            progress?.Report(new RecipeProgress { CurrentStep = totalSteps, TotalSteps = totalSteps, CurrentPhase = "완료" });
            Console.WriteLine($"[RecipeEngine] Recipe '{recipe.Name}' completed.");
        }

        /// <summary>
        /// 램프 시작점을 현재 온도로 읽어(램프 미사용이면 목표값 그대로)
        /// <see cref="TemperatureRampController"/>에 위임한다. 진행 문구는 RecipeProgress로 감싸 올린다.
        /// </summary>
        private async Task RampTemperatureAsync(Recipe recipe, int totalSteps, IProgress<RecipeProgress>? progress, CancellationToken ct)
        {
            float target = recipe.GlobalTargetTemperature;
            float start = recipe.TemperatureRampMinutes > 0
                ? await _plcController.GetCurrentTemperatureAsync()
                : target;
            var recipeProgress = progress;
            IProgress<string>? rampProgress = recipeProgress == null
                ? null
                : new Progress<string>(phase => recipeProgress.Report(new RecipeProgress
                {
                    CurrentStep = 0,
                    TotalSteps = totalSteps,
                    CurrentPhase = phase
                }));

            await _temperatureRampController.RampAsync(
                start,
                target,
                recipe.TemperatureRampMinutes,
                rampProgress,
                ct);
        }

        /// <summary>
        /// 캡처 대상 AgentId를 정한다: ① 라이브 하트비트 기반 <see cref="AgentDirectory"/>의 alias 매핑
        /// ② 장치 저장소에 등록된 alias의 AgentId ③ 최후 폴백 <c>Agent_{CameraIndex}</c>.
        /// </summary>
        private async Task<string> ResolveAgentIdAsync(RecipeStep step)
        {
            if (!string.IsNullOrEmpty(step.CameraAlias))
            {
                string? liveAgentId = _agentDirectory?.ResolveByAlias(step.CameraAlias);
                if (!string.IsNullOrEmpty(liveAgentId))
                    return liveAgentId;

                if (_deviceRepo != null)
                {
                    var device = await _deviceRepo.GetByAliasAsync(step.CameraAlias);
                    if (device != null && !string.IsNullOrEmpty(device.AgentId))
                        return device.AgentId;
                }

                Console.WriteLine($"[RecipeEngine] Alias '{step.CameraAlias}' not resolved (no live heartbeat / no device); falling back to CameraIndex");
            }

            return $"Agent_{step.CameraIndex}";
        }

    }
}
