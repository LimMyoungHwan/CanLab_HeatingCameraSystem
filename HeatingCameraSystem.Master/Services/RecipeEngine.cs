using System;
using System.Collections.Concurrent;
using System.IO;
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

        /// <summary>
        /// 자동 이동에서 두 축이 멈춘 뒤 현재 포인트 워드가 갱신되기를 기다려 주는 유예.
        /// 정지 직후엔 포인트가 아직 안 올라와 있을 수 있어, 이 시간이 지나도 목표와 다를 때만
        /// 불일치로 판정한다.
        /// </summary>
        private static readonly TimeSpan PointSettleGrace = TimeSpan.FromSeconds(2);

        /// <summary>중단 지시 ACK 대기 한도. 살아 있는 Agent라면 즉시 응답하므로 짧게 잡는다.</summary>
        private static readonly TimeSpan AbortAckTimeout = TimeSpan.FromSeconds(3);

        private static string L(string key, params object?[] args)
            => string.Format(LocalizationManager.Instance[key], args);

        private static string RecipeSource => LocalizationManager.Instance["Alarm_Src_Recipe"];

        private readonly IPlcController _plcController;
        private readonly INatsCommunicationService _natsService;
        private readonly ICaptureHistoryRepository _historyRepo;
        private readonly float _tempTolerance;

        /// <summary>
        /// 기록 조건이 마지막으로 남긴 챔버 온도. 캡처 스텝은 챔버 스텝과 분리되어 있어 목표 온도를
        /// 모르므로, 저장 폴더의 온도 코드를 이 값으로 정한다. NaN이면 아직 한 건도 기록되지 않았다.
        /// </summary>
        private double _lastRecordedTemperature = double.NaN;

        /// <summary>
        /// 이번 실행의 제품 폴더명(<c>{제품번호}_{회차}</c>). 파일명이 <c>_000</c>부터 고정이라
        /// 회차를 폴더로 갈라놓지 않으면 재실행이 이전 실행을 덮어쓴다.
        /// </summary>
        private string _runProductNumber = string.Empty;
        private readonly int _rampStepIntervalSeconds;
        private readonly TimeSpan _captureTimeout;
        private readonly TimeSpan _motorMoveTimeout;
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
            _motorMoveTimeout = TimeSpan.FromSeconds(s.MotorMoveTimeoutSeconds);
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
        public async Task ExecuteRecipeAsync(Recipe recipe, CancellationToken cancellationToken = default, IProgress<RecipeProgress>? progress = null, Func<CancellationToken, Task<bool>>? waitForResumeAsync = null)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, GetEmergencyStopToken());
            cancellationToken = linkedCancellation.Token;
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine($"[RecipeEngine] Starting recipe: {recipe.Name}");

            // 기록 루프는 레시피 전 구간에서 스텝과 무관하게 돌아야 하므로 별도 태스크로 띄우고,
            // 어떤 경로로 끝나든 finally에서 세운 뒤 마지막 기록까지 비워낸다.
            using var recordingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            string runId = Guid.NewGuid().ToString();
            _runProductNumber = NextRunProductNumber(recipe.SaveRootPath, recipe.ProductNumber);
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
                        System.Threading.Volatile.Write(ref _lastRecordedTemperature, temp);

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

        /// <summary>
        /// 운영자가 오프라인 카메라를 보고 "동일 증상 스킵"을 고르면, 그 카메라는 남은 실행 동안
        /// 명령 대상에서 빠진다. 여러 대를 묶어 찍는 스텝에서 한 대가 죽었다고 나머지까지
        /// 타임아웃을 기다릴 이유가 없기 때문이다.
        /// </summary>
        private sealed class CameraSkipState
        {
            public bool SkipSimilar { get; set; }
            public HashSet<string> Skipped { get; } = new(StringComparer.OrdinalIgnoreCase);

            public void NoteFailure(string agentId)
            {
                if (SkipSimilar) Skipped.Add(agentId);
            }
        }

        private async Task ExecuteSegmentedRecipeAsync(Recipe recipe, string runId, CancellationToken cancellationToken, IProgress<RecipeProgress>? progress, Func<CancellationToken, Task<bool>>? waitForResumeAsync)
        {
            int totalSteps = recipe.Steps.Count;

            // 온도 한계와 습도 한계를 따로 기억한다. 스텝 하나를 통째로 기준 삼으면 온도 스텝 뒤에
            // 습도 스텝이 오는 순간 온도 감시가 사라진다.
            RecipeStep? temperatureSafety = null;
            RecipeStep? humiditySafety = null;
            var captureWaiters = new ConcurrentDictionary<string, CaptureBatch>();
            var pendingCaptures = new List<PendingCapture>();
            var controlWaiters = new ConcurrentDictionary<string, TaskCompletionSource<CameraControlAckMessage>>();
            var subscribedControlAgents = new HashSet<string>(StringComparer.Ordinal);
            bool chamberStarted = false;

            await _natsService.SubscribeCaptureResultAsync(result =>
            {
                if (captureWaiters.TryGetValue(result.RecipeStepId, out var batch))
                    batch.Add(result);
            });

            var skipState = new CameraSkipState();
            await PreflightCamerasAsync(recipe, skipState, totalSteps, progress, waitForResumeAsync, cancellationToken);

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
                            await WaitForMotorMoveAsync(step, i, totalSteps, cancellationToken);
                            break;

                        case RecipeStepKind.Wait:
                            progress?.Report(new RecipeProgress { CurrentStep = i, TotalSteps = totalSteps, CurrentPhase = L("Recipe_Phase_Wait", i + 1, totalSteps, FormatDuration(step.WaitDurationSeconds)) });
                            if (step.WaitDurationSeconds > 0)
                                await Task.Delay(TimeSpan.FromSeconds(step.WaitDurationSeconds), cancellationToken);
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
                            temperatureSafety = step;
                            break;

                        case RecipeStepKind.HumidityControl:
                            progress?.Report(new RecipeProgress { CurrentStep = i, TotalSteps = totalSteps, CurrentPhase = L("Recipe_Phase_HumiditySet", i + 1, totalSteps) });
                            if (!chamberStarted)
                            {
                                await StartChamberForRecipeAsync();
                                chamberStarted = true;
                            }
                            if (step.DisableHumidityControl)
                            {
                                await _plcController.SetHumidityControlAsync(false);
                            }
                            else
                            {
                                await _plcController.SetHumidityControlAsync(true);
                                await _plcController.SetTargetHumidityAsync((float)step.TargetChamberHumidity);
                                if (step.WaitForChamberStabilization)
                                {
                                    await WaitForHumidityAsync((float)step.TargetChamberHumidity, ToleranceRh(step), cancellationToken);
                                    await SoakAsync(step, i, totalSteps, progress, cancellationToken);
                                }
                            }
                            humiditySafety = step;
                            break;

                        case RecipeStepKind.BlackBodyControl:
                            await ExecuteBlackBodyStepAsync(step, i, totalSteps, cancellationToken, progress);
                            break;

                        case RecipeStepKind.CameraCommand:
                            await ExecuteCameraStepAsync(recipe, step, runId, i, totalSteps, captureWaiters, controlWaiters, subscribedControlAgents, pendingCaptures, skipState, cancellationToken, progress);
                            break;

                        case RecipeStepKind.CaptureJoin:
                            if (!await JoinCapturesAsync(step, pendingCaptures, captureWaiters, controlWaiters, subscribedControlAgents, skipState, cancellationToken))
                                throw new OperationCanceledException(cancellationToken);
                            break;

                        default:
                            AlarmSink.Raise(AlarmCodes.StepKindUnsupported, AlarmSeverity.Warning, RecipeSource, L("Alarm_Msg_StepKindUnsupported", step.Kind));
                            break;
                    }

                    await EnforceSafetyBandAsync(temperatureSafety, humiditySafety, i, totalSteps, progress, waitForResumeAsync, cancellationToken);
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

        /// <summary>
        /// 모터 이동 완료를 기다린다. 자동(포인트) 이동은 현재 포인트가 목표 인덱스가 되고 두 축이
        /// 모두 정지할 때까지, 수동(좌표) 이동은 두 축이 정지할 때까지 폴링한다. 이동 트리거가
        /// 모멘터리라 명령 직후엔 아직 Busy가 서지 않아 곧장 통과하던 문제(가끔 이동 없이 다음
        /// 스텝으로 넘어감)를 포인트 값 확인으로 막는다.
        ///
        /// 서보가 실제로 구동(Busy)한 뒤 정지했는데 포인트 워드만 목표와 다르면 이동 자체는
        /// 일어난 것이므로 경고(PLC-005)만 남기고 진행한다. ServoCurrentPoint(D2740)의 주소·인코딩은
        /// 설비마다 다를 수 있는데, 그 불일치로 레시피를 중단시키면 이후 캡처가 통째로 사라지기
        /// 때문이다. 구동을 한 번도 못 본 채 제한 시간을 넘기면(이동 명령 미실행 또는 서보 정지가
        /// 의심되는 경우) 종전대로 알람(PLC-004)을 올리고 예외로 중단한다.
        /// </summary>
        private async Task WaitForMotorMoveAsync(RecipeStep step, int index, int totalSteps, CancellationToken cancellationToken)
        {
            bool automatic = step.MotorMoveType == MotorMoveType.Automatic;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool sawBusy = false;
            TimeSpan? idleSince = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                PlcStatusSnapshot status = await _plcController.ReadStatusAsync();
                bool idle = !status.ServoXBusy && !status.ServoYBusy;

                if (!idle)
                {
                    sawBusy = true;
                    idleSince = null;
                }
                else
                {
                    idleSince ??= clock.Elapsed;
                }

                if (!automatic)
                {
                    if (idle) return;
                }
                else if (idle)
                {
                    if (status.CurrentPoint == step.TargetPositionIndex) return;

                    if (sawBusy && clock.Elapsed - idleSince!.Value >= PointSettleGrace)
                    {
                        AlarmSink.Raise(AlarmCodes.MotorPointMismatch, AlarmSeverity.Warning, RecipeSource,
                            L("Alarm_Msg_MotorPointMismatch", index + 1, totalSteps, step.TargetPositionIndex, status.CurrentPoint));
                        return;
                    }
                }

                if (clock.Elapsed >= _motorMoveTimeout)
                {
                    AlarmSink.Raise(AlarmCodes.MotorMoveTimeout, AlarmSeverity.Error, RecipeSource,
                        L("Alarm_Msg_MotorTimeout", index + 1, totalSteps, (int)_motorMoveTimeout.TotalMinutes));
                    throw new TimeoutException($"Servo move timed out after {_motorMoveTimeout.TotalMinutes:F0} min at step {index + 1}/{totalSteps}.");
                }

                await Task.Delay(500, cancellationToken);
            }
        }

        private static string FormatDuration(int totalSeconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(totalSeconds < 0 ? 0 : totalSeconds);
            return $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
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
            Recipe recipe,
            RecipeStep step,
            string runId,
            int index,
            int totalSteps,
            ConcurrentDictionary<string, CaptureBatch> captureWaiters,
            ConcurrentDictionary<string, TaskCompletionSource<CameraControlAckMessage>> controlWaiters,
            HashSet<string> subscribedControlAgents,
            List<PendingCapture> pending,
            CameraSkipState skipState,
            CancellationToken cancellationToken,
            IProgress<RecipeProgress>? progress)
        {
            progress?.Report(new RecipeProgress { CurrentStep = index, TotalSteps = totalSteps, CurrentPhase = L("Recipe_Phase_CameraOp", step.CameraOperation, index + 1, totalSteps) });
            List<RecipeCameraTarget> targets = await ActiveTargetsAsync(step, skipState);
            if (targets.Count == 0) return;

            if (step.CameraOperation == CameraControlOps.Capture)
            {
                await RepeatCaptureAsync(recipe, step, runId, index, totalSteps, targets, captureWaiters, pending, skipState, cancellationToken, progress);
                return;
            }

            foreach (var target in targets)
            {
                string agentId = await ResolveTargetAgentIdAsync(step, target);
                string requestId = $"{step.StepId}:{agentId}:{target.CameraIndex}";
                string operation = ResolveCameraOperation(step, target);

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
                        Op = operation,
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
                {
                    AlarmSink.Raise(AlarmCodes.AgentTimeout, AlarmSeverity.Warning, RecipeSource, L("Alarm_Msg_ControlTimeout", step.StepId, target.CameraIndex));
                    skipState.NoteFailure(agentId);
                }
                controlWaiters.TryRemove(requestId, out _);
            }
        }

        /// <summary>
        /// 캡처 반복 횟수. 간격이 0이면 1회, 아니면 <c>전체시간 / 간격</c>이다(최소 1회).
        /// </summary>
        internal static int CaptureRepeatCount(RecipeStep step)
            => CaptureRepeatCount(step.CaptureIntervalSeconds, step.CaptureDurationSeconds);

        internal static int CaptureRepeatCount(int intervalSeconds, int durationSeconds)
        {
            if (intervalSeconds <= 0) return 1;
            int count = durationSeconds / intervalSeconds;
            return count < 1 ? 1 : count;
        }

        /// <summary>
        /// 회차를 스텝 시작 시각 기준 절대 시각(0초, interval, 2×interval …)에 맞춰 실행한다.
        /// 매번 고정 시간을 자는 방식이 아니므로 촬영·PLC 지연이 다음 회차로 누적되지 않는다.
        /// 한 회차가 간격을 통째로 넘겨 늦으면 따라잡기를 시도하지 않고 경고만 남긴다
        /// (몰아 찍으면 샘플 간격이 오히려 더 망가진다).
        /// </summary>
        private async Task RepeatCaptureAsync(
            Recipe recipe,
            RecipeStep step,
            string runId,
            int index,
            int totalSteps,
            List<RecipeCameraTarget> targets,
            ConcurrentDictionary<string, CaptureBatch> captureWaiters,
            List<PendingCapture> pending,
            CameraSkipState skipState,
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
                {
                    if (await IsSkippedAsync(step, target, skipState)) continue;
                    await CaptureOnceAsync(recipe, step, runId, round, target, captureWaiters, pending, skipState, cancellationToken);
                }
            }
        }

        /// <summary>
        /// BIAS 대역은 카메라 타겟의 <see cref="RecipeCameraTarget.TargetChamber"/>에서 가져온다.
        /// 대역이 스텝 op과 타겟에 따로 있으면 서로 어긋날 수 있는데, 그 경우 카메라는 상온으로
        /// 보정되고 폴더는 고온으로 만들어져 파일은 멀쩡한 채 내용만 틀린다.
        /// 타겟에 대역이 없는 기존 레시피는 저장된 op을 그대로 쓴다.
        /// </summary>
        internal static string ResolveCameraOperation(RecipeStep step, RecipeCameraTarget target)
        {
            bool isBias = step.CameraOperation is CameraControlOps.Bias
                or CameraControlOps.BiasLow
                or CameraControlOps.BiasMid
                or CameraControlOps.BiasHigh;

            if (!isBias || target.TargetChamber is not ChamberRange range) return step.CameraOperation;

            return range switch
            {
                ChamberRange.Low => CameraControlOps.BiasLow,
                ChamberRange.Mid => CameraControlOps.BiasMid,
                ChamberRange.High => CameraControlOps.BiasHigh,
                _ => step.CameraOperation
            };
        }

        /// <summary>
        /// 결과를 아직 거두지 않은 캡처 1건. fork된 캡처만 여기에 쌓인다.
        /// <paramref name="ExpectedResults"/>는 찍을 장수가 아니라 받기로 한 결과 건수다 —
        /// 규칙 저장은 배치 전체를 1건으로 보고한다.
        /// </summary>
        private sealed record PendingCapture(
            string RequestId,
            CaptureBatch Batch,
            RecipeStep Step,
            string AgentId,
            int CameraIndex,
            int ExpectedResults,
            string RunId);

        /// <summary>운영자가 중단 실패 상황에서 고를 수 있는 선택지.</summary>
        public enum CaptureAbortDecision
        {
            Continue,
            Stop,
            Retry
        }

        /// <summary>
        /// 중단 지시에 카메라가 응답하지 않을 때 운영자에게 물어보는 훅. 연결되어 있지 않으면
        /// 중단(Stop)으로 간주한다 — 아직 촬영 중인 카메라 앞에서 모터를 움직이면 이후 데이터가
        /// 조용히 오염되므로, 모르는 채로 진행하는 것이 가장 나쁜 선택이다.
        /// </summary>
        public Func<string, Task<CaptureAbortDecision>>? AbortDecisionRequested { get; set; }

        /// <summary>
        /// 캡처 명령에 실을 생산 저장 규칙 값들. 규칙 저장을 하지 않는 캡처에서는 null이다.
        /// </summary>
        private sealed record ProductionNaming(
            string ConditionFolder,
            string BlackBodyFolder,
            string FilePrefix,
            bool WriteBiasJson);

        /// <summary>
        /// 챔버 온도는 기록 조건이 마지막으로 남긴 실측값을 쓴다. 캡처 스텝과 챔버 스텝이 분리되어
        /// 있어 목표 온도를 알 수 없기 때문이다.
        /// </summary>
        private ProductionNaming? ResolveProductionNaming(Recipe recipe, RecipeStep step, RecipeCameraTarget target)
        {
            if (string.IsNullOrWhiteSpace(recipe.SaveRootPath)) return null;
            if (target.TargetChamber is not ChamberRange range || target.TargetBlackBody is not BlackBodyRole role) return null;

            double celsius = System.Threading.Volatile.Read(ref _lastRecordedTemperature);

            try
            {
                if (double.IsNaN(celsius))
                    throw new InvalidOperationException("기록된 챔버 온도가 없습니다(기록 조건이 모두 0이면 규칙 저장을 할 수 없습니다).");

                return new ProductionNaming(
                    CaptureNamingRule.ConditionFolder(range, celsius, _tempTolerance),
                    CaptureNamingRule.BlackBodyFolder(role),
                    CaptureNamingRule.FilePrefix(range, role),
                    CaptureNamingRule.WritesBiasJson(role));
            }
            catch (Exception ex)
            {
                AlarmSink.Raise(AlarmCodes.ProductionNamingFailed, AlarmSeverity.Error, RecipeSource,
                    L("Alarm_Msg_ProductionNamingFailed", step.StepId, target.CameraIndex, ex.Message));
                return null;
            }
        }

        /// <summary>
        /// 첫 실행은 접미사 없이 <c>{센서}_{제품번호}</c>, 이후부터 <c>_1</c>, <c>_2</c>로 회차를 붙인다.
        /// 센서번호는 Agent만 알기 때문에 앞부분은 비교에서 뺀다.
        /// 제품번호가 비면 회차만 돌려줘 Agent가 <c>{센서}</c> → <c>{센서}_1</c>로 결합하게 한다.
        /// ponytail: 이때는 제품번호로 걸러낼 수 없어 루트의 모든 폴더를 같은 계열로 본다.
        /// 제품번호를 넣은 실행과 안 넣은 실행을 한 루트에 섞으면 회차가 함께 올라간다.
        /// 접미사 없는 폴더를 지웠어도 남은 회차 다음 번호를 쓴다 — 번호를 재사용하면 남은 폴더를 덮어쓴다.
        /// 루트를 읽지 못하면 접미사 없이 이전 동작을 유지한다.
        /// </summary>
        internal static string NextRunProductNumber(string saveRootPath, string productNumber)
        {
            bool hasProduct = !string.IsNullOrWhiteSpace(productNumber);
            string bare = hasProduct ? "_" + productNumber : string.Empty;
            bool bareExists = false;
            int max = 0;

            bool Matches(ReadOnlySpan<char> head) => !hasProduct || head.EndsWith(bare, StringComparison.Ordinal);

            try
            {
                foreach (string dir in Directory.EnumerateDirectories(saveRootPath))
                {
                    string name = Path.GetFileName(dir);
                    int cut = name.LastIndexOf('_');

                    if (cut > 0 && int.TryParse(name.AsSpan(cut + 1), out int run) && Matches(name.AsSpan(0, cut)))
                    {
                        if (run > max) max = run;
                        continue;
                    }

                    if (Matches(name)) bareExists = true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return productNumber;
            }

            if (!bareExists && max == 0) return productNumber;
            return hasProduct ? $"{productNumber}_{max + 1}" : (max + 1).ToString();
        }

        private async Task CaptureOnceAsync(
            Recipe recipe,
            RecipeStep step,
            string runId,
            int round,
            RecipeCameraTarget target,
            ConcurrentDictionary<string, CaptureBatch> captureWaiters,
            List<PendingCapture> pending,
            CameraSkipState skipState,
            CancellationToken cancellationToken)
        {
            string agentId = await ResolveTargetAgentIdAsync(step, target);

            // 회차마다 다른 키를 써야 이전 회차 결과가 다음 회차 배치로 새지 않는다.
            string requestId = $"{step.StepId}:{agentId}:{target.CameraIndex}:{round}";

            ProductionNaming? production = ResolveProductionNaming(recipe, step, target);

            // 장수는 규칙 저장 여부와 무관하게 운영자가 스텝에 입력한 값만 쓴다.
            int shots = step.ShotCount > 0 ? step.ShotCount : 1;

            // 규칙 저장은 배치 전체를 결과 1건으로 보고한다. 장마다 JPEG을 실어 보내면 100장 x
            // 카메라수 만큼의 미리보기가 Master 메모리에 쌓이기 때문이다.
            int expectedResults = production is null ? shots : 1;
            var batch = new CaptureBatch(expectedResults);
            captureWaiters[requestId] = batch;

            try
            {
                await _natsService.PublishCaptureCommandAsync(new CaptureCommandMessage
                {
                    TargetAgentId = agentId,
                    RecipeStepId = requestId,
                    Source = CaptureSource.Recipe,
                    ShotCount = shots,
                    Timestamp = DateTime.UtcNow,
                    StorageRootUnc = production is null ? string.Empty : recipe.SaveRootPath,
                    ProductNumber = production is null ? string.Empty : _runProductNumber,
                    ConditionFolder = production?.ConditionFolder ?? string.Empty,
                    BlackBodyFolder = production?.BlackBodyFolder ?? string.Empty,
                    FilePrefix = production?.FilePrefix ?? string.Empty,
                    WriteBiasJson = production?.WriteBiasJson ?? false
                });
            }
            catch (Exception ex)
            {
                // 카메라 통신 실패로 챔버 시퀀스까지 죽이지 않는다. 이 회차만 건너뛴다.
                AlarmSink.Raise(AlarmCodes.NatsPublishFailed, AlarmSeverity.Error, RecipeSource, L("Alarm_Msg_CapturePublishFailed", step.StepId, target.CameraIndex, ex.Message));
                captureWaiters.TryRemove(requestId, out _);
                return;
            }

            var item = new PendingCapture(requestId, batch, step, agentId, target.CameraIndex, expectedResults, runId);

            // 결과를 기다리지 않는 스텝(fork)은 CaptureJoin 스텝이 대신 거둔다. 여기서 배치를
            // 지우면 그때까지 오는 결과가 갈 곳을 잃으므로 captureWaiters에서 빼지 않는다.
            if (!step.WaitForCaptureResult)
            {
                lock (pending) pending.Add(item);
                return;
            }

            try
            {
                // 장수만큼 결과가 오므로 대기 한도도 장수에 비례해 늘린다.
                TimeSpan batchTimeout = TimeSpan.FromTicks(_captureTimeout.Ticks * shots);
                await Task.WhenAny(batch.Completed, Task.Delay(batchTimeout, cancellationToken));
                await CollectCaptureAsync(item, skipState);
            }
            finally
            {
                captureWaiters.TryRemove(requestId, out _);
            }
        }

        /// <summary>
        /// fork된 캡처가 모두 끝날 때까지 기다린다. 타임아웃이면 그때까지 받은 결과만 기록하고
        /// 미완료 카메라에 중단을 지시한다. 중단 ACK가 오지 않으면 운영자 판단을 받는다.
        /// </summary>
        /// <returns>레시피를 계속 진행해도 되면 true.</returns>
        private async Task<bool> JoinCapturesAsync(
            RecipeStep step,
            List<PendingCapture> pending,
            ConcurrentDictionary<string, CaptureBatch> captureWaiters,
            ConcurrentDictionary<string, TaskCompletionSource<CameraControlAckMessage>> controlWaiters,
            HashSet<string> subscribedControlAgents,
            CameraSkipState skipState,
            CancellationToken cancellationToken)
        {
            PendingCapture[] items;
            lock (pending)
            {
                items = pending.ToArray();
                pending.Clear();
            }

            if (items.Length == 0) return true;

            int maxShots = items.Max(i => i.Step.ShotCount > 0 ? i.Step.ShotCount : 1);
            TimeSpan timeout = step.CaptureJoinTimeoutSeconds > 0
                ? TimeSpan.FromSeconds(step.CaptureJoinTimeoutSeconds)
                : TimeSpan.FromTicks(_captureTimeout.Ticks * maxShots);

            Task all = Task.WhenAll(items.Select(i => i.Batch.Completed));
            Task finished = await Task.WhenAny(all, Task.Delay(timeout, cancellationToken));

            foreach (PendingCapture item in items)
            {
                await CollectCaptureAsync(item, skipState);
                captureWaiters.TryRemove(item.RequestId, out _);
            }

            if (finished == all) return true;

            bool proceed = true;
            foreach (PendingCapture item in items)
            {
                if (item.Batch.Snapshot().Count >= item.ExpectedResults) continue;
                if (!await AbortCaptureAsync(item, controlWaiters, subscribedControlAgents, cancellationToken))
                    proceed = false;
            }

            return proceed;
        }

        /// <summary>
        /// 미완료 카메라에 촬영 중단을 지시하고 ACK를 기다린다. ACK가 없으면 카메라가 아직 찍고
        /// 있을 수 있으므로 운영자에게 진행/중단/재시도를 묻는다.
        /// </summary>
        /// <returns>레시피를 계속 진행해도 되면 true.</returns>
        private async Task<bool> AbortCaptureAsync(
            PendingCapture item,
            ConcurrentDictionary<string, TaskCompletionSource<CameraControlAckMessage>> controlWaiters,
            HashSet<string> subscribedControlAgents,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                string requestId = $"abort:{item.RequestId}:{Guid.NewGuid():N}";

                if (subscribedControlAgents.Add(item.AgentId))
                {
                    await _natsService.SubscribeCameraControlAckAsync(item.AgentId, ack =>
                    {
                        if (controlWaiters.TryGetValue(ack.RequestId, out var waiter)) waiter.TrySetResult(ack);
                    });
                }

                var waiter = new TaskCompletionSource<CameraControlAckMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                controlWaiters[requestId] = waiter;

                bool acked = false;
                try
                {
                    await _natsService.PublishCameraControlAsync(new CameraControlMessage
                    {
                        AgentId = item.AgentId,
                        CameraIndex = item.CameraIndex,
                        Op = CameraControlOps.CaptureAbort,
                        RequestId = requestId,
                        Timestamp = DateTime.UtcNow
                    });

                    Task completed = await Task.WhenAny(waiter.Task, Task.Delay(AbortAckTimeout, cancellationToken));
                    acked = completed == waiter.Task && waiter.Task.Result.IsSuccess;
                }
                catch (Exception ex)
                {
                    AlarmSink.Raise(AlarmCodes.NatsPublishFailed, AlarmSeverity.Error, RecipeSource,
                        L("Alarm_Msg_ControlPublishFailed", item.Step.StepId, item.CameraIndex, ex.Message));
                }
                finally
                {
                    controlWaiters.TryRemove(requestId, out _);
                }

                if (acked)
                {
                    AlarmSink.Raise(AlarmCodes.PartialCapture, AlarmSeverity.Warning, RecipeSource,
                        L("Alarm_Msg_CaptureAborted", item.Step.StepId, item.CameraIndex));
                    return true;
                }

                AlarmSink.Raise(AlarmCodes.CaptureAbortNoAck, AlarmSeverity.Error, RecipeSource,
                    L("Alarm_Msg_CaptureAbortNoAck", item.Step.StepId, item.CameraIndex));

                CaptureAbortDecision decision = AbortDecisionRequested is null
                    ? CaptureAbortDecision.Stop
                    : await AbortDecisionRequested(item.AgentId);

                if (decision == CaptureAbortDecision.Retry) continue;
                return decision == CaptureAbortDecision.Continue;
            }
        }

        private async Task CollectCaptureAsync(PendingCapture item, CameraSkipState skipState)
        {
            var results = item.Batch.Snapshot();
            foreach (var result in results)
            {
                if (result.IsSuccess)
                    await StoreCaptureResultAsync(item.Step, item.RunId, item.CameraIndex, result);
            }

            int stored = results.Count(r => r.IsSuccess);
            if (stored < item.ExpectedResults)
            {
                AlarmSink.Raise(AlarmCodes.PartialCapture, AlarmSeverity.Warning, RecipeSource, L("Alarm_Msg_PartialCapture", item.Step.StepId, item.CameraIndex, stored, item.ExpectedResults));

                // 한 장도 못 받은 건 카메라가 죽었다는 뜻이다. 일부라도 왔으면 살아 있으니 스킵 대상이 아니다.
                if (stored == 0) skipState.NoteFailure(item.AgentId);
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
            RecipeStep? temperatureReference,
            RecipeStep? humidityReference,
            int index,
            int totalSteps,
            IProgress<RecipeProgress>? progress,
            Func<CancellationToken, Task<bool>>? waitForResumeAsync,
            CancellationToken cancellationToken)
        {
            bool checkTemperature = temperatureReference?.UseSafetyTemperature == true;
            bool checkHumidity = humidityReference?.UseSafetyHumidity == true;
            if (!checkTemperature && !checkHumidity) return;

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

                string? violation = DescribeSafetyViolation(temperatureReference, humidityReference, temp, humidity);
                if (violation is null) return;

                AlarmSink.Raise(AlarmCodes.SafetyBandViolation, AlarmSeverity.Error, RecipeSource, L("Alarm_Msg_SafetyHold", violation));
                progress?.Report(new RecipeProgress { CurrentStep = index, TotalSteps = totalSteps, CurrentPhase = LocalizationManager.Instance["Recipe_Phase_SafetyHold"] });

                if (waitForResumeAsync is null) return;
                await waitForResumeAsync(cancellationToken);
            }
        }

        /// <summary>범위를 벗어난 항목의 설명을 돌려준다. 모두 정상이면 null.</summary>
        internal static string? DescribeSafetyViolation(RecipeStep reference, float temperature, float humidity)
            => DescribeSafetyViolation(reference, reference, temperature, humidity);

        /// <summary>
        /// 온도 한계는 마지막 온도 스텝이, 습도 한계는 마지막 습도 스텝이 정한다.
        /// 두 스텝이 서로를 덮어쓰지 않도록 기준을 따로 받는다.
        /// </summary>
        internal static string? DescribeSafetyViolation(
            RecipeStep? temperatureReference,
            RecipeStep? humidityReference,
            float temperature,
            float humidity)
        {
            if (temperatureReference?.UseSafetyTemperature == true)
            {
                if (temperature < temperatureReference.SafetyTempMin)
                    return L("Safety_BelowMinTemp", temperature, temperatureReference.SafetyTempMin);
                if (temperature > temperatureReference.SafetyTempMax)
                    return L("Safety_AboveMaxTemp", temperature, temperatureReference.SafetyTempMax);
            }

            if (humidityReference?.UseSafetyHumidity == true)
            {
                if (humidity < humidityReference.SafetyHumidityMin)
                    return L("Safety_BelowMinHumidity", humidity, humidityReference.SafetyHumidityMin);
                if (humidity > humidityReference.SafetyHumidityMax)
                    return L("Safety_AboveMaxHumidity", humidity, humidityReference.SafetyHumidityMax);
            }

            return null;
        }

        private async Task<string> ResolveTargetAgentIdAsync(RecipeStep step, RecipeCameraTarget target)
            => string.IsNullOrWhiteSpace(target.AgentId)
                ? target.CameraIndex == step.CameraIndex ? await ResolveAgentIdAsync(step) : $"Agent_{target.CameraIndex}"
                : target.AgentId;

        private async Task<bool> IsSkippedAsync(RecipeStep step, RecipeCameraTarget target, CameraSkipState skipState)
            => skipState.Skipped.Count > 0 && skipState.Skipped.Contains(await ResolveTargetAgentIdAsync(step, target));

        private async Task<List<RecipeCameraTarget>> ActiveTargetsAsync(RecipeStep step, CameraSkipState skipState)
        {
            var declared = step.CameraTargets.Count > 0
                ? step.CameraTargets
                : new List<RecipeCameraTarget> { new() { CameraIndex = step.CameraIndex } };

            var active = new List<RecipeCameraTarget>(declared.Count);
            foreach (var target in declared)
            {
                if (!await IsSkippedAsync(step, target, skipState))
                    active.Add(target);
            }
            return active;
        }

        /// <summary>
        /// 레시피가 명령할 카메라 중 하트비트가 끊긴 것을 시작 전에 찾아 운영자에게 알린다.
        /// 승온을 마친 뒤 촬영 단계에서 발견하면 시험 전체를 다시 해야 하므로, 몇 초면 고칠 수 있는
        /// 시작 시점에 잡는다. 운영자가 "동일 증상 스킵"을 고르면 그 카메라들은 남은 실행에서 제외된다.
        /// </summary>
        private async Task PreflightCamerasAsync(
            Recipe recipe,
            CameraSkipState skipState,
            int totalSteps,
            IProgress<RecipeProgress>? progress,
            Func<CancellationToken, Task<bool>>? waitForResumeAsync,
            CancellationToken cancellationToken)
        {
            if (_agentDirectory is null) return;

            var offline = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (RecipeStep step in recipe.Steps)
            {
                if (step.Kind != RecipeStepKind.CameraCommand) continue;

                foreach (var target in await ActiveTargetsAsync(step, skipState))
                {
                    string agentId = await ResolveTargetAgentIdAsync(step, target);
                    if (!seen.Add(agentId)) continue;
                    if (_agentDirectory.IsAgentOnline(agentId)) continue;

                    offline.Add(agentId);
                }
            }

            if (offline.Count == 0) return;

            AlarmSink.Raise(AlarmCodes.CameraOffline, AlarmSeverity.Error, RecipeSource,
                L("Alarm_Msg_CameraOffline", string.Join(", ", offline)));

            if (waitForResumeAsync is null) return;

            progress?.Report(new RecipeProgress { CurrentStep = 0, TotalSteps = totalSteps, CurrentPhase = LocalizationManager.Instance["Recipe_Phase_CameraOfflineHold"] });

            if (await waitForResumeAsync(cancellationToken))
            {
                skipState.SkipSimilar = true;
                foreach (string agentId in offline)
                    skipState.Skipped.Add(agentId);
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
