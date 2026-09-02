using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Localization;

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
        private const float DefaultHumidityTolerance = 5f;

        private static string L(string key, params object?[] args)
            => string.Format(LocalizationManager.Instance[key], args);

        private static string RecipeSource => LocalizationManager.Instance["Alarm_Src_Recipe"];

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
        private readonly IRecipeMeasurementRepository? _measurementRepo;
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
            AgentDirectory? agentDirectory = null,
            IRecipeMeasurementRepository? measurementRepo = null)
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
            _measurementRepo = measurementRepo;
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

            // 기록 루프는 레시피 전 구간에서 스텝과 무관하게 돌아야 하므로 별도 태스크로 띄우고,
            // 어떤 경로로 끝나든 finally에서 세운 뒤 마지막 기록까지 비워낸다.
            using var recordingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            string runId = Guid.NewGuid().ToString();
            Task recording = RecordMeasurementsAsync(recipe, runId, recordingCts.Token);

            try
            {
                await ExecuteSegmentedRecipeAsync(recipe, runId, cancellationToken, progress, waitForResumeAsync);
            }
            finally
            {
                recordingCts.Cancel();
                try { await recording; } catch (OperationCanceledException) { }
            }

            Console.WriteLine($"[RecipeEngine] Recipe '{recipe.Name}' completed.");
        }

        /// <summary>
        /// 기록 조건(온도 변화량 OR 습도 변화량 OR 경과 시간)을 1초 주기로 평가해 충족될 때마다
        /// 챔버 온습도와 카메라 온도를 남긴다. 세 조건이 모두 0이면 아무것도 하지 않는다.
        /// 첫 샘플은 이후 변화량의 기준점이 필요하므로 무조건 1건 기록한다.
        /// </summary>
        private async Task RecordMeasurementsAsync(Recipe recipe, string runId, CancellationToken cancellationToken)
        {
            if (_measurementRepo == null) return;

            bool byTemperature = recipe.RecordOnTemperatureDelta > 0;
            bool byHumidity = recipe.RecordOnHumidityDelta > 0;
            bool byInterval = recipe.RecordIntervalSeconds > 0;
            if (!byTemperature && !byHumidity && !byInterval) return;

            float? lastTemp = null;
            float? lastHumidity = null;
            DateTime? lastAt = null;

            // 샘플 시각을 레시피 시작 기준 절대 시각에 맞춘다. PLC 왕복 시간이 매 주기 더해지는
            // 고정 지연 방식이면 1시간짜리 측정에서 수십 초가 밀린다.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var tick = TimeSpan.FromSeconds(1);

            for (long round = 0; !cancellationToken.IsCancellationRequested; round++)
            {
                if (round > 0)
                {
                    TimeSpan wait = tick * round - clock.Elapsed;
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
                }

                try
                {
                    float temp = await _plcController.GetCurrentTemperatureAsync();
                    float humidity = await _plcController.GetCurrentHumidityAsync();
                    DateTime now = DateTime.UtcNow;

                    bool first = lastAt is null;
                    bool hit = first
                        || (byTemperature && lastTemp.HasValue && Math.Abs(temp - lastTemp.Value) >= recipe.RecordOnTemperatureDelta)
                        || (byHumidity && lastHumidity.HasValue && Math.Abs(humidity - lastHumidity.Value) >= recipe.RecordOnHumidityDelta)
                        || (byInterval && lastAt.HasValue && (now - lastAt.Value).TotalSeconds >= recipe.RecordIntervalSeconds);

                    if (hit)
                    {
                        lastTemp = temp;
                        lastHumidity = humidity;
                        lastAt = now;

                        await _measurementRepo.InsertAsync(new RecipeMeasurementRecord
                        {
                            RunId = runId,
                            RecipeId = recipe.Id,
                            RecipeName = recipe.Name,
                            Timestamp = now,
                            ChamberTemperature = temp,
                            ChamberHumidity = humidity,
                            CameraTemperatures = _agentDirectory == null
                                ? new Dictionary<string, double>()
                                : new Dictionary<string, double>(_agentDirectory.CameraTemperatures)
                        });
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RecipeEngine] measurement record failed: {ex.Message}");
                }
            }
        }

        private async Task ExecuteSegmentedRecipeAsync(Recipe recipe, string runId, CancellationToken cancellationToken, IProgress<RecipeProgress>? progress, Func<CancellationToken, Task>? waitForResumeAsync)
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

            int currentStep = 0;

            try
            {
                for (int i = 0; i < totalSteps; i++)
                {
                    RecipeStep step = recipe.Steps[i];
                    currentStep = i;
                    cancellationToken.ThrowIfCancellationRequested();

                    switch (step.Kind)
                    {
                        case RecipeStepKind.MotorMove:
                            progress?.Report(new RecipeProgress { CurrentStep = i, TotalSteps = totalSteps, CurrentPhase = L("Recipe_Phase_ServoMove", i + 1, totalSteps) });
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
                            progress?.Report(new RecipeProgress { CurrentStep = i, TotalSteps = totalSteps, CurrentPhase = L("Recipe_Phase_TempSet", i + 1, totalSteps) });
                            if (!chamberStarted)
                            {
                                await StartChamberForRecipeAsync();
                                chamberStarted = true;
                            }
                            await ApplyChamberTemperatureAsync((float)step.TargetChamberTemperature, recipe.TemperatureRampMinutes, i, totalSteps, progress, cancellationToken);
                            if (step.WaitForChamberStabilization)
                            {
                                await WaitForTemperatureAsync((float)step.TargetChamberTemperature, ToleranceC(step), cancellationToken);
                                await SoakAsync(step, i, totalSteps, progress, cancellationToken);
                            }
                            safetyReference = step;
                            break;

                        case RecipeStepKind.HumidityControl:
                            progress?.Report(new RecipeProgress { CurrentStep = i, TotalSteps = totalSteps, CurrentPhase = L("Recipe_Phase_HumiditySet", i + 1, totalSteps) });
                            if (!chamberStarted)
                            {
                                await StartChamberForRecipeAsync();
                                chamberStarted = true;
                            }
                            await _plcController.SetTargetHumidityAsync((float)step.TargetChamberHumidity);
                            if (step.WaitForChamberStabilization)
                            {
                                await WaitForHumidityAsync((float)step.TargetChamberHumidity, ToleranceRh(step), cancellationToken);
                                await SoakAsync(step, i, totalSteps, progress, cancellationToken);
                            }
                            safetyReference = step;
                            break;

                        case RecipeStepKind.BlackBodyControl:
                            await ExecuteBlackBodyStepAsync(step, i, totalSteps, cancellationToken, progress);
                            break;

                        case RecipeStepKind.CameraCommand:
                            await ExecuteCameraStepAsync(step, runId, i, totalSteps, captureWaiters, controlWaiters, subscribedControlAgents, cancellationToken, progress);
                            break;

                        default:
                            AlarmSink.Raise(AlarmCodes.StepKindUnsupported, AlarmSeverity.Warning, RecipeSource, L("Alarm_Msg_StepKindUnsupported", step.Kind));
                            break;
                    }

                    await EnforceSafetyBandAsync(safetyReference, i, totalSteps, progress, waitForResumeAsync, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                bool emergency = IsEmergencyStopRequested;
                AlarmSink.Raise(
                    emergency ? AlarmCodes.EmergencyStopped : AlarmCodes.UserStopped,
                    emergency ? AlarmSeverity.Error : AlarmSeverity.Info,
                    RecipeSource,
                    L("Alarm_Msg_Stopped", recipe.Name, currentStep + 1, totalSteps));
                throw;
            }
            catch (Exception ex)
            {
                AlarmSink.Raise(AlarmCodes.PlcCommFailed, AlarmSeverity.Error, RecipeSource,
                    L("Alarm_Msg_StepFailed", recipe.Name, currentStep + 1, totalSteps, ex.Message));
                throw;
            }
            finally
            {
                // 정상 완료든 취소·비상정지든 챔버는 반드시 세운다. PLC 알람으로 중단될 때
                // 챔버만 계속 도는 상황을 막는 유일한 지점이다.
                if (chamberStarted)
                    await StopChamberWithRetryAsync();
            }

            progress?.Report(new RecipeProgress { CurrentStep = totalSteps, TotalSteps = totalSteps, CurrentPhase = LocalizationManager.Instance["Recipe_Phase_Done"] });
        }

        private async Task ExecuteBlackBodyStepAsync(
            RecipeStep step,
            int index,
            int totalSteps,
            CancellationToken cancellationToken,
            IProgress<RecipeProgress>? progress)
        {
            progress?.Report(new RecipeProgress { CurrentStep = index, TotalSteps = totalSteps, CurrentPhase = L("Recipe_Phase_BlackBodySet", index + 1, totalSteps) });
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
                AlarmSink.Raise(AlarmCodes.BlackBodyFailed, AlarmSeverity.Warning, RecipeSource, L("Alarm_Msg_BlackBodySetFailed", step.StepId, step.BlackBodyIndex, ex.Message));
                return;
            }

            if (!step.WaitForStabilization)
                return;

            progress?.Report(new RecipeProgress { CurrentStep = index, TotalSteps = totalSteps, CurrentPhase = L("Recipe_Phase_BlackBodyWait", index + 1, totalSteps) });
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
                    if (Math.Abs(current[0] - step.TargetBlackBodyTemperature) <= ToleranceC(step) &&
                        Math.Abs(current[1] - target1) <= ToleranceC(step))
                        break;
                }
                catch (Exception ex)
                {
                    AlarmSink.Raise(AlarmCodes.BlackBodyFailed, AlarmSeverity.Warning, RecipeSource, L("Alarm_Msg_BlackBodyReadFailed", step.StepId, step.BlackBodyIndex, ex.Message));
                    break;
                }
                await Task.Delay(1000, cancellationToken);
            }

            await SoakAsync(step, index, totalSteps, progress, cancellationToken);
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

        private async Task WaitForTemperatureAsync(float target, float tolerance, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Math.Abs(await _plcController.GetCurrentTemperatureAsync() - target) <= tolerance)
                    return;
                await Task.Delay(1000, cancellationToken);
            }
        }

        private async Task WaitForHumidityAsync(float target, float tolerance, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Math.Abs(await _plcController.GetCurrentHumidityAsync() - target) <= tolerance)
                    return;
                await Task.Delay(1000, cancellationToken);
            }
        }

        /// <summary>
        /// 목표 도달 후 유지 시간. 도달 판정은 오차 범위에 처음 들어온 순간이라 시료 내부는 아직
        /// 따라오지 못했을 수 있어, 이 시간만큼 더 버틴 뒤 다음 스텝으로 넘어간다.
        /// 유지 중 범위를 다시 벗어나도 타이머를 되돌리지 않는다(이탈은 안전밴드가 잡는다).
        /// </summary>
        internal static TimeSpan SoakDuration(RecipeStep step)
            => step.SoakMinutes > 0 ? TimeSpan.FromMinutes(step.SoakMinutes) : TimeSpan.Zero;

        private float ToleranceC(RecipeStep step)
            => step.StabilizationToleranceC > 0 ? (float)step.StabilizationToleranceC : _tempTolerance;

        private static float ToleranceRh(RecipeStep step)
            => step.StabilizationToleranceRh > 0 ? (float)step.StabilizationToleranceRh : DefaultHumidityTolerance;

        private async Task SoakAsync(RecipeStep step, int index, int totalSteps, IProgress<RecipeProgress>? progress, CancellationToken cancellationToken)
        {
            TimeSpan soak = SoakDuration(step);
            if (soak <= TimeSpan.Zero) return;

            progress?.Report(new RecipeProgress { CurrentStep = index, TotalSteps = totalSteps, CurrentPhase = L("Recipe_Phase_Soak", step.SoakMinutes, index + 1, totalSteps) });
            await Task.Delay(soak, cancellationToken);
        }

        /// <summary>
        /// 챔버 정지를 최대 두 번 시도한다. 첫 실패가 PLC 순간 단절이면 짧은 재시도로 살아나는 경우가 있고,
        /// 여기서 끝내 실패하면 히터가 켜진 채 남으므로 <see cref="AlarmCodes.ChamberStopFailed"/>로
        /// 운영자에게 수동 정지를 요구한다.
        /// </summary>
        private async Task StopChamberWithRetryAsync()
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await _plcController.StopChamberAsync();
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 1)
                    {
                        AlarmSink.Raise(AlarmCodes.ChamberStopFailed, AlarmSeverity.Error, RecipeSource, L("Alarm_Msg_ChamberStopFailed", ex.Message));
                        return;
                    }
                    await Task.Delay(2000);
                }
            }
        }

        private async Task ExecuteCameraStepAsync(
            RecipeStep step,
            string runId,
            int index,
            int totalSteps,
            ConcurrentDictionary<string, CaptureBatch> captureWaiters,
            ConcurrentDictionary<string, TaskCompletionSource<CameraControlAckMessage>> controlWaiters,
            HashSet<string> subscribedControlAgents,
            CancellationToken cancellationToken,
            IProgress<RecipeProgress>? progress)
        {
            progress?.Report(new RecipeProgress { CurrentStep = index, TotalSteps = totalSteps, CurrentPhase = L("Recipe_Phase_CameraOp", step.CameraOperation, index + 1, totalSteps) });
            var targets = step.CameraTargets.Count > 0
                ? step.CameraTargets
                : new List<RecipeCameraTarget> { new() { CameraIndex = step.CameraIndex } };

            if (step.CameraOperation == CameraControlOps.Capture)
            {
                await RepeatCaptureAsync(step, runId, index, totalSteps, targets, captureWaiters, cancellationToken, progress);
                return;
            }

            foreach (var target in targets)
            {
                string agentId = string.IsNullOrWhiteSpace(target.AgentId)
                    ? target.CameraIndex == step.CameraIndex ? await ResolveAgentIdAsync(step) : $"Agent_{target.CameraIndex}"
                    : target.AgentId;
                string requestId = $"{step.StepId}:{agentId}:{target.CameraIndex}";

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
                try
                {
                    await _natsService.PublishCameraControlAsync(new CameraControlMessage
                    {
                        AgentId = agentId,
                        CameraIndex = target.CameraIndex,
                        Op = step.CameraOperation,
                        RequestId = requestId,
                        BiasTargetLevel = step.BiasTargetLevel,
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    // 카메라 통신 실패로 챔버 시퀀스까지 죽이지 않는다. 이 카메라만 건너뛴다.
                    AlarmSink.Raise(AlarmCodes.NatsPublishFailed, AlarmSeverity.Error, RecipeSource, L("Alarm_Msg_ControlPublishFailed", step.StepId, target.CameraIndex, ex.Message));
                    controlWaiters.TryRemove(requestId, out _);
                    continue;
                }

                Task controlCompleted = await Task.WhenAny(controlWaiter.Task, Task.Delay(_captureTimeout, cancellationToken));
                if (controlCompleted != controlWaiter.Task || !controlWaiter.Task.Result.IsSuccess)
                    AlarmSink.Raise(AlarmCodes.AgentTimeout, AlarmSeverity.Warning, RecipeSource, L("Alarm_Msg_ControlTimeout", step.StepId, target.CameraIndex));
                controlWaiters.TryRemove(requestId, out _);
            }
        }

        /// <summary>
        /// 캡처 반복 횟수. 간격이 0이면 1회, 아니면 <c>전체시간 / 간격</c>이다(최소 1회).
        /// </summary>
        internal static int CaptureRepeatCount(RecipeStep step)
        {
            if (step.CaptureIntervalSeconds <= 0) return 1;
            int count = step.CaptureDurationSeconds / step.CaptureIntervalSeconds;
            return count < 1 ? 1 : count;
        }

        /// <summary>
        /// 회차를 스텝 시작 시각 기준 절대 시각(0초, interval, 2×interval …)에 맞춰 실행한다.
        /// 매번 고정 시간을 자는 방식이 아니므로 촬영·PLC 지연이 다음 회차로 누적되지 않는다.
        /// 한 회차가 간격을 통째로 넘겨 늦으면 따라잡기를 시도하지 않고 경고만 남긴다
        /// (몰아 찍으면 샘플 간격이 오히려 더 망가진다).
        /// </summary>
        private async Task RepeatCaptureAsync(
            RecipeStep step,
            string runId,
            int index,
            int totalSteps,
            List<RecipeCameraTarget> targets,
            ConcurrentDictionary<string, CaptureBatch> captureWaiters,
            CancellationToken cancellationToken,
            IProgress<RecipeProgress>? progress)
        {
            int repeats = CaptureRepeatCount(step);
            var clock = System.Diagnostics.Stopwatch.StartNew();

            for (int round = 0; round < repeats; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (round > 0)
                {
                    TimeSpan due = TimeSpan.FromSeconds((double)step.CaptureIntervalSeconds * round);
                    TimeSpan wait = due - clock.Elapsed;
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, cancellationToken);
                    else if (-wait >= TimeSpan.FromSeconds(step.CaptureIntervalSeconds))
                        AlarmSink.Raise(AlarmCodes.CaptureScheduleSlip, AlarmSeverity.Warning, RecipeSource,
                            L("Alarm_Msg_CaptureSlip", step.StepId, round + 1, repeats, -wait.TotalSeconds, step.CaptureIntervalSeconds));
                }

                if (repeats > 1)
                    progress?.Report(new RecipeProgress { CurrentStep = index, TotalSteps = totalSteps, CurrentPhase = L("Recipe_Phase_CaptureRound", round + 1, repeats, index + 1, totalSteps) });

                foreach (var target in targets)
                    await CaptureOnceAsync(step, runId, round, target, captureWaiters, cancellationToken);
            }
        }

        private async Task CaptureOnceAsync(
            RecipeStep step,
            string runId,
            int round,
            RecipeCameraTarget target,
            ConcurrentDictionary<string, CaptureBatch> captureWaiters,
            CancellationToken cancellationToken)
        {
            string agentId = string.IsNullOrWhiteSpace(target.AgentId)
                ? target.CameraIndex == step.CameraIndex ? await ResolveAgentIdAsync(step) : $"Agent_{target.CameraIndex}"
                : target.AgentId;

            // 회차마다 다른 키를 써야 이전 회차 결과가 다음 회차 배치로 새지 않는다.
            string requestId = $"{step.StepId}:{agentId}:{target.CameraIndex}:{round}";

            int shots = step.ShotCount > 0 ? step.ShotCount : 1;
            var batch = new CaptureBatch(shots);
            captureWaiters[requestId] = batch;

            try
            {
                try
                {
                    await _natsService.PublishCaptureCommandAsync(new CaptureCommandMessage
                    {
                        TargetAgentId = agentId,
                        RecipeStepId = requestId,
                        Source = CaptureSource.Recipe,
                        ShotCount = shots,
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    // 카메라 통신 실패로 챔버 시퀀스까지 죽이지 않는다. 이 회차만 건너뛴다.
                    AlarmSink.Raise(AlarmCodes.NatsPublishFailed, AlarmSeverity.Error, RecipeSource, L("Alarm_Msg_CapturePublishFailed", step.StepId, target.CameraIndex, ex.Message));
                    return;
                }

                // 장수만큼 결과가 오므로 대기 한도도 장수에 비례해 늘린다.
                TimeSpan batchTimeout = TimeSpan.FromTicks(_captureTimeout.Ticks * shots);
                await Task.WhenAny(batch.Completed, Task.Delay(batchTimeout, cancellationToken));

                var results = batch.Snapshot();
                foreach (var result in results)
                {
                    if (result.IsSuccess)
                        await StoreCaptureResultAsync(step, runId, target.CameraIndex, result);
                }

                int stored = results.Count(r => r.IsSuccess);
                if (stored < shots)
                    AlarmSink.Raise(AlarmCodes.PartialCapture, AlarmSeverity.Warning, RecipeSource, L("Alarm_Msg_PartialCapture", step.StepId, target.CameraIndex, stored, shots));
            }
            finally
            {
                captureWaiters.TryRemove(requestId, out _);
            }
        }

        private async Task StoreCaptureResultAsync(RecipeStep step, string runId, int cameraIndex, CaptureResultMessage captureResult)
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
                RunId = runId,
                AgentRawPath = captureResult.ImagePath,
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
        /// 가장 최근 ChamberControl 스텝이 정한 절대 상·하한으로 챔버 온습도를 검사한다.
        /// 한계가 null인 항목은 검사하지 않고, 기준 스텝이 아직 없으면 검사 자체를 건너뛴다.
        /// 이탈 시 알람을 올리고 운전자가 재개할 때까지 대기한다(재개 콜백이 없으면 1회 알람 후 통과).
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
            if (!reference.UseSafetyTemperature && !reference.UseSafetyHumidity) return;

            while (!cancellationToken.IsCancellationRequested)
            {
                float temp;
                float humidity;
                try
                {
                    temp = await _plcController.GetCurrentTemperatureAsync();
                    humidity = await _plcController.GetCurrentHumidityAsync();
                }
                catch (Exception ex)
                {
                    // 안전 검사 자체가 불가능해진 상태다. 건너뛰고 진행하면 이탈을 못 잡으므로
                    // 알람만 남기고 예외를 그대로 올려 레시피를 세운다(finally가 챔버를 정지시킨다).
                    AlarmSink.Raise(AlarmCodes.SafetyCheckFailed, AlarmSeverity.Error, RecipeSource, L("Alarm_Msg_SafetyCheckFailed", ex.Message));
                    throw;
                }

                string? violation = DescribeSafetyViolation(reference, temp, humidity);
                if (violation is null) return;

                AlarmSink.Raise(AlarmCodes.SafetyBandViolation, AlarmSeverity.Error, RecipeSource, L("Alarm_Msg_SafetyHold", violation));
                progress?.Report(new RecipeProgress { CurrentStep = index, TotalSteps = totalSteps, CurrentPhase = LocalizationManager.Instance["Recipe_Phase_SafetyHold"] });

                if (waitForResumeAsync is null) return;
                await waitForResumeAsync(cancellationToken);
            }
        }

        /// <summary>범위를 벗어난 항목의 설명을 돌려준다. 모두 정상이면 null.</summary>
        internal static string? DescribeSafetyViolation(RecipeStep reference, float temperature, float humidity)
        {
            if (reference.UseSafetyTemperature)
            {
                if (temperature < reference.SafetyTempMin)
                    return L("Safety_BelowMinTemp", temperature, reference.SafetyTempMin);
                if (temperature > reference.SafetyTempMax)
                    return L("Safety_AboveMaxTemp", temperature, reference.SafetyTempMax);
            }

            if (reference.UseSafetyHumidity)
            {
                if (humidity < reference.SafetyHumidityMin)
                    return L("Safety_BelowMinHumidity", humidity, reference.SafetyHumidityMin);
                if (humidity > reference.SafetyHumidityMax)
                    return L("Safety_AboveMaxHumidity", humidity, reference.SafetyHumidityMax);
            }

            return null;
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
