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
        private readonly int _rampStepIntervalSeconds;
        private readonly TimeSpan _captureTimeout;
        private readonly string? _imageCacheDir;
        private readonly ICameraDeviceRepository? _deviceRepo;
        private readonly IBlackBodyController _blackBody;
        private readonly AgentDirectory? _agentDirectory;
        private readonly object _emergencyStopSync = new();
        private CancellationTokenSource _emergencyStopCts = new();

        public event EventHandler? EmergencyStopChanged;

        public bool IsEmergencyStopRequested
        {
            get
            {
                lock (_emergencyStopSync)
                    return _emergencyStopCts.IsCancellationRequested;
            }
        }

        /// <summary>
        /// 한 캡처 요청에 대해 Agent가 보내오는 장수만큼의 결과를 모은다. 기대 장수가 다 차면
        /// <see cref="Completed"/>가 끝나고, 타임아웃으로 중단돼도 <see cref="Snapshot"/>으로
        /// 그때까지 받은 것만 꺼내 쓸 수 있다.
        /// </summary>
        private sealed class CaptureBatch
        {
            private readonly int _expected;
            private readonly List<CaptureResultMessage> _received = new();
            private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public CaptureBatch(int expected) => _expected = expected;

            public Task Completed => _done.Task;

            public void Add(CaptureResultMessage result)
            {
                lock (_received)
                {
                    _received.Add(result);
                    if (_received.Count >= _expected) _done.TrySetResult();
                }
            }

            public List<CaptureResultMessage> Snapshot()
            {
                lock (_received) return new List<CaptureResultMessage>(_received);
            }
        }

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
            _rampStepIntervalSeconds = s.RampStepIntervalSeconds;
            _captureTimeout = TimeSpan.FromSeconds(s.CaptureResultTimeoutSeconds);
            _imageCacheDir  = imageCacheDir;
            _deviceRepo     = deviceRepo;
            _blackBody      = blackBody ?? new HeatingCameraSystem.Protocols.PlcBlackBodyAdapter(plcController);
            _agentDirectory = agentDirectory;
        }

        public void RequestEmergencyStop()
        {
            lock (_emergencyStopSync)
            {
                if (_emergencyStopCts.IsCancellationRequested)
                    return;

                _emergencyStopCts.Cancel();
            }

            EmergencyStopChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ResetEmergencyStop()
        {
            CancellationTokenSource? previous = null;
            lock (_emergencyStopSync)
            {
                if (!_emergencyStopCts.IsCancellationRequested)
                    return;

                previous = _emergencyStopCts;
                _emergencyStopCts = new CancellationTokenSource();
            }

            previous.Dispose();
            EmergencyStopChanged?.Invoke(this, EventArgs.Empty);
        }

        private CancellationToken GetEmergencyStopToken()
        {
            lock (_emergencyStopSync)
                return _emergencyStopCts.Token;
        }

        /// <summary>
        /// 레시피 전체를 실행한다. 시작 시 캡처 결과 구독을 걸어 StepId별 TaskCompletionSource로
        /// 결과를 기다리며, 스텝의 캡처 실패·타임아웃은 <see cref="AlarmSink"/>에 알리고 다음 스텝을
        /// 계속한다. 챔버를 켠 뒤에는 정상 완료·취소·PLC 알람 어느 경로로 끝나든 StopChamberAsync를
        /// 호출한다.
        /// </summary>
        public async Task ExecuteRecipeAsync(Recipe recipe, CancellationToken cancellationToken = default, IProgress<RecipeProgress>? progress = null, Func<CancellationToken, Task>? waitForResumeAsync = null)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, GetEmergencyStopToken());
            cancellationToken = linkedCancellation.Token;
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine($"[RecipeEngine] Starting recipe: {recipe.Name}");
            await ExecuteSegmentedRecipeAsync(recipe, cancellationToken, progress, waitForResumeAsync);
            Console.WriteLine($"[RecipeEngine] Recipe '{recipe.Name}' completed.");
        }

        private async Task ExecuteSegmentedRecipeAsync(Recipe recipe, CancellationToken cancellationToken, IProgress<RecipeProgress>? progress, Func<CancellationToken, Task>? waitForResumeAsync)
        {
            int totalSteps = recipe.Steps.Count;
            RecipeStep? safetyReference = null;
            var captureWaiters = new ConcurrentDictionary<string, CaptureBatch>();
            var controlWaiters = new ConcurrentDictionary<string, TaskCompletionSource<CameraControlAckMessage>>();
            var subscribedControlAgents = new HashSet<string>(StringComparer.Ordinal);
            bool chamberStarted = false;

            await _natsService.SubscribeCaptureResultAsync(result =>
            {
                if (captureWaiters.TryGetValue(result.RecipeStepId, out var batch))
                    batch.Add(result);
            });

            try
            {
                for (int i = 0; i < totalSteps; i++)
                {
                    RecipeStep step = recipe.Steps[i];
                    cancellationToken.ThrowIfCancellationRequested();

                    switch (step.Kind)
                    {
                        case RecipeStepKind.MotorMove:
                            progress?.Report(new RecipeProgress { CurrentStep = i, TotalSteps = totalSteps, CurrentPhase = $"서보 이동 ({i + 1}/{totalSteps})" });
                            if (step.MotorMoveType == MotorMoveType.Automatic)
                                await _plcController.MoveServoToPositionAsync(step.TargetPositionIndex);
                            else
                                await _plcController.MoveToCoordinateAsync(step.PositionX, step.PositionY);
                            while (!cancellationToken.IsCancellationRequested)
                            {
                                PlcStatusSnapshot status = await _plcController.ReadStatusAsync();
                                if (!status.ServoXBusy && !status.ServoYBusy) break;
                                await Task.Delay(500, cancellationToken);
                            }
                            break;

                        case RecipeStepKind.ChamberControl:
                            progress?.Report(new RecipeProgress { CurrentStep = i, TotalSteps = totalSteps, CurrentPhase = $"온습도 설정 ({i + 1}/{totalSteps})" });
                            if (!chamberStarted)
                            {
                                await StartChamberForRecipeAsync();
                                chamberStarted = true;
                            }
                            await _plcController.SetTargetHumidityAsync((float)step.TargetChamberHumidity);
                            await ApplyChamberTemperatureAsync((float)step.TargetChamberTemperature, recipe.TemperatureRampMinutes, i, totalSteps, progress, cancellationToken);
                            if (step.WaitForChamberStabilization)
                                await WaitForTemperatureAsync((float)step.TargetChamberTemperature, cancellationToken);
                            safetyReference = step;
                            break;

                        case RecipeStepKind.BlackBodyControl:
                            await ExecuteBlackBodyStepAsync(step, i, totalSteps, cancellationToken, progress);
                            break;

                        case RecipeStepKind.CameraCommand:
                            await ExecuteCameraStepAsync(step, i, totalSteps, captureWaiters, controlWaiters, subscribedControlAgents, cancellationToken, progress);
                            break;

                        default:
                            AlarmSink.Raise(AlarmSeverity.Warning, "레시피", $"지원하지 않는 스텝 종류: {step.Kind}");
                            break;
                    }

                    await EnforceSafetyBandAsync(safetyReference, i, totalSteps, progress, waitForResumeAsync, cancellationToken);
                }
            }
            finally
            {
                // 정상 완료든 취소·비상정지든 챔버는 반드시 세운다. PLC 알람으로 중단될 때
                // 챔버만 계속 도는 상황을 막는 유일한 지점이다.
                if (chamberStarted)
                {
                    try { await _plcController.StopChamberAsync(); }
                    catch (Exception ex) { AlarmSink.Raise(AlarmSeverity.Error, "레시피", $"챔버 정지 실패: {ex.Message}"); }
                }
            }

            progress?.Report(new RecipeProgress { CurrentStep = totalSteps, TotalSteps = totalSteps, CurrentPhase = "완료" });
        }

        private async Task ExecuteBlackBodyStepAsync(
            RecipeStep step,
            int index,
            int totalSteps,
            CancellationToken cancellationToken,
            IProgress<RecipeProgress>? progress)
        {
            progress?.Report(new RecipeProgress { CurrentStep = index, TotalSteps = totalSteps, CurrentPhase = $"BB 0, 1 온도 설정 ({index + 1}/{totalSteps})" });
            try
            {
                float target1 = step.TargetBlackBodyTemperature1 == 0
                    ? step.TargetBlackBodyTemperature
                    : step.TargetBlackBodyTemperature1;
                await Task.WhenAll(
                    _blackBody.SetTemperatureAsync(0, step.TargetBlackBodyTemperature),
                    _blackBody.SetTemperatureAsync(1, target1));
            }
            catch (Exception ex)
            {
                AlarmSink.Raise(AlarmSeverity.Warning, "레시피", $"스텝 {step.StepId} 블랙바디 {step.BlackBodyIndex} 온도 설정 실패: {ex.Message}");
                return;
            }

            if (!step.WaitForStabilization)
                return;

            progress?.Report(new RecipeProgress { CurrentStep = index, TotalSteps = totalSteps, CurrentPhase = $"BB 0, 1 안정화 대기 ({index + 1}/{totalSteps})" });
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var current = await Task.WhenAll(
                        _blackBody.GetCurrentTemperatureAsync(0),
                        _blackBody.GetCurrentTemperatureAsync(1));
                    float target1 = step.TargetBlackBodyTemperature1 == 0
                        ? step.TargetBlackBodyTemperature
                        : step.TargetBlackBodyTemperature1;
                    if (Math.Abs(current[0] - step.TargetBlackBodyTemperature) <= _tempTolerance &&
                        Math.Abs(current[1] - target1) <= _tempTolerance)
                        break;
                }
                catch (Exception ex)
                {
                    AlarmSink.Raise(AlarmSeverity.Warning, "레시피", $"스텝 {step.StepId} 블랙바디 {step.BlackBodyIndex} 온도 읽기 실패: {ex.Message}");
                    break;
                }
                await Task.Delay(1000, cancellationToken);
            }
        }

        /// <summary>
        /// 챔버 온도를 <see cref="TemperatureRampController"/>로 적용한다. 최종 목표 워드(TempTarget)와
        /// 챔버가 실제로 추종하는 제어 워드(TempSv)를 함께 써야 하며, 목표 워드만 쓰면 값은 PLC에
        /// 보이지만 챔버는 움직이지 않는다.
        /// </summary>
        private async Task ApplyChamberTemperatureAsync(
            float target,
            int rampMinutes,
            int currentStep,
            int totalSteps,
            IProgress<RecipeProgress>? progress,
            CancellationToken cancellationToken)
        {
            float start = rampMinutes > 0
                ? await _plcController.GetCurrentTemperatureAsync()
                : target;

            IProgress<string>? rampProgress = progress == null
                ? null
                : new Progress<string>(phase => progress.Report(new RecipeProgress
                {
                    CurrentStep = currentStep,
                    TotalSteps = totalSteps,
                    CurrentPhase = phase
                }));

            var ramp = new TemperatureRampController(_plcController, _rampStepIntervalSeconds);
            await ramp.RampAsync(start, target, rampMinutes, rampProgress, cancellationToken);
        }

        private async Task WaitForTemperatureAsync(float target, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Math.Abs(await _plcController.GetCurrentTemperatureAsync() - target) <= _tempTolerance)
                    return;
                await Task.Delay(1000, cancellationToken);
            }
        }

        private async Task ExecuteCameraStepAsync(
            RecipeStep step,
            int index,
            int totalSteps,
            ConcurrentDictionary<string, CaptureBatch> captureWaiters,
            ConcurrentDictionary<string, TaskCompletionSource<CameraControlAckMessage>> controlWaiters,
            HashSet<string> subscribedControlAgents,
            CancellationToken cancellationToken,
            IProgress<RecipeProgress>? progress)
        {
            progress?.Report(new RecipeProgress { CurrentStep = index, TotalSteps = totalSteps, CurrentPhase = $"카메라 {step.CameraOperation} ({index + 1}/{totalSteps})" });
            var targets = step.CameraTargets.Count > 0
                ? step.CameraTargets
                : new List<RecipeCameraTarget> { new() { CameraIndex = step.CameraIndex } };

            foreach (var target in targets)
            {
                string agentId = string.IsNullOrWhiteSpace(target.AgentId)
                    ? target.CameraIndex == step.CameraIndex ? await ResolveAgentIdAsync(step) : $"Agent_{target.CameraIndex}"
                    : target.AgentId;
                string requestId = $"{step.StepId}:{agentId}:{target.CameraIndex}";

                if (step.CameraOperation == CameraControlOps.Capture)
                {
                    int shots = step.ShotCount > 0 ? step.ShotCount : 1;
                    var batch = new CaptureBatch(shots);
                    captureWaiters[requestId] = batch;
                    await _natsService.PublishCaptureCommandAsync(new CaptureCommandMessage
                    {
                        TargetAgentId = agentId,
                        RecipeStepId = requestId,
                        Source = CaptureSource.Recipe,
                        ShotCount = shots,
                        Timestamp = DateTime.UtcNow
                    });

                    // 장수만큼 결과가 오므로 대기 한도도 장수에 비례해 늘린다.
                    TimeSpan batchTimeout = TimeSpan.FromTicks(_captureTimeout.Ticks * shots);
                    await Task.WhenAny(batch.Completed, Task.Delay(batchTimeout, cancellationToken));

                    var results = batch.Snapshot();
                    foreach (var result in results)
                    {
                        if (result.IsSuccess)
                            await StoreCaptureResultAsync(step, target.CameraIndex, result);
                    }

                    int stored = results.Count(r => r.IsSuccess);
                    if (stored < shots)
                        AlarmSink.Raise(AlarmSeverity.Warning, "레시피", $"스텝 {step.StepId} 카메라 {target.CameraIndex} 캡처 {stored}/{shots}장만 성공(실패 또는 타임아웃)");
                    captureWaiters.TryRemove(requestId, out _);
                    continue;
                }

                if (subscribedControlAgents.Add(agentId))
                {
                    await _natsService.SubscribeCameraControlAckAsync(agentId, ack =>
                    {
                        if (controlWaiters.TryGetValue(ack.RequestId, out var waiter))
                            waiter.TrySetResult(ack);
                    });
                }

                var controlWaiter = new TaskCompletionSource<CameraControlAckMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                controlWaiters[requestId] = controlWaiter;
                await _natsService.PublishCameraControlAsync(new CameraControlMessage
                {
                    AgentId = agentId,
                    CameraIndex = target.CameraIndex,
                    Op = step.CameraOperation,
                    RequestId = requestId,
                    Timestamp = DateTime.UtcNow
                });

                Task controlCompleted = await Task.WhenAny(controlWaiter.Task, Task.Delay(_captureTimeout, cancellationToken));
                if (controlCompleted != controlWaiter.Task || !controlWaiter.Task.Result.IsSuccess)
                    AlarmSink.Raise(AlarmSeverity.Warning, "레시피", $"스텝 {step.StepId} 카메라 {target.CameraIndex} 명령 실패 또는 타임아웃");
                controlWaiters.TryRemove(requestId, out _);
            }
        }

        private async Task StoreCaptureResultAsync(RecipeStep step, int cameraIndex, CaptureResultMessage captureResult)
        {
            float temperature = 0f;
            float humidity = 0f;
            try
            {
                temperature = await _plcController.GetCurrentTemperatureAsync();
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
                Id = string.IsNullOrEmpty(captureResult.CaptureId) ? Guid.NewGuid().ToString() : captureResult.CaptureId,
                CameraId = cameraId,
                AgentId = captureResult.AgentId,
                CameraAlias = captureResult.Alias,
                CameraIndex = cameraIndex,
                Source = CaptureSource.Recipe,
                ImagePath = storedImagePath,
                RecipeStepId = captureResult.RecipeStepId,
                Timestamp = captureResult.Timestamp,
                Temperature = temperature,
                Humidity = humidity,
                CameraTemperature = captureResult.CameraTemperature
            });
        }

        private async Task StartChamberForRecipeAsync()
        {
            PlcStatusSnapshot status = await _plcController.ReadStatusAsync();
            if (!status.Chiller)
                await _plcController.SetEquipmentAsync(PlcEquipment.Chiller, true);
            if (!status.DoorLock)
                await _plcController.SetEquipmentAsync(PlcEquipment.DoorLock, true);
            await _plcController.StartChamberAsync();
        }

        /// <summary>
        /// 가장 최근 ChamberControl 스텝의 목표값을 기준으로 안전 밴드를 검사한다. 허용오차가 0인
        /// 항목은 검사 대상이 아니며, 기준 스텝이 아직 없으면 검사 자체를 건너뛴다. 이탈 시 알람을
        /// 올리고 운전자가 재개할 때까지 대기한다(재개 콜백이 없으면 1회 알람 후 통과).
        /// </summary>
        private async Task EnforceSafetyBandAsync(
            RecipeStep? reference,
            int index,
            int totalSteps,
            IProgress<RecipeProgress>? progress,
            Func<CancellationToken, Task>? waitForResumeAsync,
            CancellationToken cancellationToken)
        {
            if (reference == null) return;
            bool checkTemp = reference.SafetyTempTolerance > 0;
            bool checkHumidity = reference.SafetyHumidityTolerance > 0;
            if (!checkTemp && !checkHumidity) return;

            while (!cancellationToken.IsCancellationRequested)
            {
                float temp = await _plcController.GetCurrentTemperatureAsync();
                float humidity = await _plcController.GetCurrentHumidityAsync();

                bool tempOk = !checkTemp
                    || Math.Abs(temp - (float)reference.TargetChamberTemperature) <= reference.SafetyTempTolerance;
                bool humidityOk = !checkHumidity
                    || Math.Abs(humidity - (float)reference.TargetChamberHumidity) <= reference.SafetyHumidityTolerance;
                if (tempOk && humidityOk) return;

                AlarmSink.Raise(AlarmSeverity.Error, "레시피",
                    $"안전 밴드 이탈 (T={temp:F1}℃ 목표 {reference.TargetChamberTemperature:F1}±{reference.SafetyTempTolerance:F1}, " +
                    $"H={humidity:F1}%RH 목표 {reference.TargetChamberHumidity:F1}±{reference.SafetyHumidityTolerance:F1}). 사용자 확인 대기.");
                progress?.Report(new RecipeProgress { CurrentStep = index, TotalSteps = totalSteps, CurrentPhase = "안전조건 이탈 — 사용자 확인 대기" });

                if (waitForResumeAsync is null) return;
                await waitForResumeAsync(cancellationToken);
            }
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
