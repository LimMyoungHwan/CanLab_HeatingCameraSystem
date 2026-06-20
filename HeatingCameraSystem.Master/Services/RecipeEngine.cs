using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    public class RecipeEngine
    {
        private readonly IPlcController _plcController;
        private readonly INatsCommunicationService _natsService;
        private readonly ICaptureHistoryRepository _historyRepo;
        private readonly float _tempTolerance;
        private readonly TimeSpan _captureTimeout;

        public RecipeEngine(
            IPlcController plcController,
            INatsCommunicationService natsService,
            ICaptureHistoryRepository historyRepo,
            RecipeEngineSettings? settings = null)
        {
            _plcController = plcController;
            _natsService = natsService;
            _historyRepo = historyRepo;
            var s = settings ?? new RecipeEngineSettings();
            _tempTolerance  = s.TemperatureTolerance;
            _captureTimeout = TimeSpan.FromSeconds(s.CaptureResultTimeoutSeconds);
        }

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
            await _plcController.SetTargetTemperatureAsync(recipe.GlobalTargetTemperature);
            await _plcController.SetTargetHumidityAsync(recipe.GlobalTargetHumidity);

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
                await _plcController.MoveServoToPositionAsync(step.TargetPositionIndex);
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (await _plcController.IsServoAtPositionAsync()) break;
                    await Task.Delay(500, cancellationToken);
                }

                progress?.Report(new RecipeProgress { CurrentStep = i, TotalSteps = totalSteps, CurrentPhase = $"BB 안정화 ({i + 1}/{totalSteps})" });
                const int activeBB = 0;
                await _plcController.SetBlackBodyTemperatureAsync(activeBB, step.TargetBlackBodyTemperature);
                while (!cancellationToken.IsCancellationRequested)
                {
                    float bbTemp = await _plcController.GetCurrentBlackBodyTemperatureAsync(activeBB);
                    if (Math.Abs(bbTemp - step.TargetBlackBodyTemperature) <= _tempTolerance) break;
                    await Task.Delay(1000, cancellationToken);
                }

                progress?.Report(new RecipeProgress { CurrentStep = i, TotalSteps = totalSteps, CurrentPhase = $"캡처 ({i + 1}/{totalSteps})" });
                var tcs = new TaskCompletionSource<CaptureResultMessage>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                resultWaiters[step.StepId] = tcs;

                await _natsService.PublishCaptureCommandAsync(new CaptureCommandMessage
                {
                    TargetAgentId = $"Agent_{step.CameraIndex}",
                    RecipeStepId  = step.StepId,
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

                        await _historyRepo.InsertAsync(new CaptureHistoryRecord
                        {
                            CameraId     = $"CAM-{step.CameraIndex:D2}",
                            ImagePath    = captureResult.ImagePath,
                            RecipeStepId = captureResult.RecipeStepId,
                            Timestamp    = captureResult.Timestamp,
                            Temperature  = temp,
                            Humidity     = humidity
                        });
                    }
                    else
                    {
                        Console.WriteLine($"[RecipeEngine] Step {step.StepId}: capture failed.");
                    }
                }
                else
                {
                    Console.WriteLine($"[RecipeEngine] Step {step.StepId}: capture timeout ({_captureTimeout.TotalSeconds:0}s).");
                }

                resultWaiters.TryRemove(step.StepId, out _);
            }

            await _plcController.StopChamberAsync();
            progress?.Report(new RecipeProgress { CurrentStep = totalSteps, TotalSteps = totalSteps, CurrentPhase = "완료" });
            Console.WriteLine($"[RecipeEngine] Recipe '{recipe.Name}' completed.");
        }
    }
}
