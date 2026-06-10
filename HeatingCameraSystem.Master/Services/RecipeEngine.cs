using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    public class RecipeEngine
    {
        private readonly IPlcController _plcController;
        private readonly INatsCommunicationService _natsService;
        private readonly float _tempTolerance = 0.5f; // Tolerance for reaching target temperature

        public RecipeEngine(IPlcController plcController, INatsCommunicationService natsService)
        {
            _plcController = plcController;
            _natsService = natsService;
        }

        public async Task ExecuteRecipeAsync(Recipe recipe, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[RecipeEngine] Starting recipe: {recipe.Name}");
            
            // 1. 챔버 시작
            await _plcController.StartChamberAsync();

            // 2. 챔버 전역 설정 (온도/습도)
            await _plcController.SetTargetTemperatureAsync(recipe.GlobalTargetTemperature);
            await _plcController.SetTargetHumidityAsync(recipe.GlobalTargetHumidity);

            Console.WriteLine($"[RecipeEngine] Waiting for chamber to reach Temp: {recipe.GlobalTargetTemperature}, Hum: {recipe.GlobalTargetHumidity}");
            
            // 3. 챔버 대기 (도달 시까지)
            while (!cancellationToken.IsCancellationRequested)
            {
                float currentTemp = await _plcController.GetCurrentTemperatureAsync();
                if (Math.Abs(currentTemp - recipe.GlobalTargetTemperature) <= _tempTolerance)
                {
                    break;
                }
                await Task.Delay(2000, cancellationToken);
            }

            Console.WriteLine("[RecipeEngine] Chamber environment ready. Starting steps...");

            // 4. 스텝 순차 실행
            foreach (var step in recipe.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine($"[RecipeEngine] Executing Step: {step.StepId} (Camera: {step.CameraIndex})");

                // a. 서보 이동
                await _plcController.MoveServoToPositionAsync(step.TargetPositionIndex);
                
                // b. 서보 이동 대기
                while (!cancellationToken.IsCancellationRequested)
                {
                    bool isArrived = await _plcController.IsServoAtPositionAsync();
                    if (isArrived) break;
                    await Task.Delay(500, cancellationToken);
                }

                // c. 블랙바디 온도 설정 (기본 모드: 0번 블랙바디 사용)
                int activeBlackBody = 0;
                await _plcController.SetBlackBodyTemperatureAsync(activeBlackBody, step.TargetBlackBodyTemperature);

                // d. 블랙바디 대기
                while (!cancellationToken.IsCancellationRequested)
                {
                    float bbTemp = await _plcController.GetCurrentBlackBodyTemperatureAsync(activeBlackBody);
                    if (Math.Abs(bbTemp - step.TargetBlackBodyTemperature) <= _tempTolerance)
                    {
                        break;
                    }
                    await Task.Delay(1000, cancellationToken);
                }

                // e. NATS로 에이전트에게 촬영 명령 송신
                var cmd = new CaptureCommandMessage
                {
                    TargetAgentId = $"Agent_{step.CameraIndex}", // Assuming simple mapping for now
                    RecipeStepId = step.StepId,
                    Timestamp = DateTime.UtcNow
                };
                
                await _natsService.PublishCaptureCommandAsync(cmd);
                
                // f. 에이전트 촬영 결과 응답 대기를 구현해야 함. (Phase 4의 범위)
                // 지금은 단방향 송신만 하고 다음으로 넘어감. 실전에서는 TaskCompletionSource 등을 이용해 응답 대기 필요.
                Console.WriteLine($"[RecipeEngine] Capture command sent to Agent_{step.CameraIndex}");
                
                // 임시 대기 (촬영 및 저장 시간)
                await Task.Delay(2000, cancellationToken);
            }

            // 5. 완료 후 챔버 정지
            await _plcController.StopChamberAsync();
            Console.WriteLine($"[RecipeEngine] Recipe {recipe.Name} completed successfully.");
        }
    }
}
