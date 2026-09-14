using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// AgentUI 프로세스의 선택적 NATS 브리지. 로컬 카메라마다 논리적
    /// <see cref="CameraDescriptor.AgentId"/>를 유지하므로 기존 Master 계약은 그대로다:
    /// 카메라별로 <c>master.cmd.capture.{AgentId}</c>(그리고 공유 <c>master.cmd.capture.all</c> —
    /// 프로세스 내 팬아웃)를 구독하고, 라이브 루프를 스냅샷하고(tee — 카메라를 다시 열지 않는다),
    /// 방사 측정 <c>.y16</c>을 로컬에 저장하며, 볼 수 있는 JPG를 실은
    /// <c>agent.result.capture.{AgentId}</c>와 주기적 <c>agent.status.{AgentId}</c> 하트비트를
    /// 발행한다. NATS는 절대 시작 의존성이 아니다: 접속은 백그라운드에서 재시도로 돌고,
    /// NATS가 없어도 로컬 런타임은 동작한다.
    /// </summary>
    public sealed class CameraNatsConnector : IAsyncDisposable
    {
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

        private readonly INatsCommunicationService _nats;
        private readonly CameraRuntimeManager _manager;
        private readonly CaptureStore _store;
        private readonly IReadOnlyList<CameraDescriptor> _cameras;
        private readonly int _heartbeatSeconds;
        private readonly int _captureBurstCount;
        private readonly IReadOnlyDictionary<string, ThermalNucCorrector>? _nucs;
        private readonly Func<AgentConfigSnapshot>? _getConfigSnapshot;
        private readonly Action<AgentConfigSnapshot>? _applyConfigSnapshot;
        private readonly Func<CameraDescriptor, CameraControlMessage, Task<(bool Success, string Message)>>? _cameraControlHandler;
        private readonly Func<CameraDescriptor, bool>? _serialHealth;
        private readonly Func<CameraDescriptor, Task<double?>>? _readCameraTemperature;
        private readonly Func<CameraDescriptor, Task<short?>>? _readFpaRaw;
        private readonly Func<CameraDescriptor, string?>? _readBiasJson;
        private readonly ProductionCaptureSink? _productionSink;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _captureAborts = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _subscriptionGate = new(1, 1);
        private readonly HashSet<string> _subscribedAgentIds = new(StringComparer.Ordinal);
        private Timer? _heartbeat;
        private volatile bool _connected;

        public CameraNatsConnector(
            INatsCommunicationService nats,
            CameraRuntimeManager manager,
            CaptureStore store,
            IReadOnlyList<CameraDescriptor> cameras,
            int heartbeatSeconds = 5,
            IReadOnlyDictionary<string, ThermalNucCorrector>? nucs = null,
            int captureBurstCount = 1,
            Func<AgentConfigSnapshot>? getConfigSnapshot = null,
            Action<AgentConfigSnapshot>? applyConfigSnapshot = null,
            Func<CameraDescriptor, CameraControlMessage, Task<(bool Success, string Message)>>? cameraControlHandler = null,
            Func<CameraDescriptor, bool>? serialHealth = null,
            Func<CameraDescriptor, Task<double?>>? readCameraTemperature = null,
            Func<CameraDescriptor, Task<short?>>? readFpaRaw = null,
            Func<CameraDescriptor, string?>? readBiasJson = null,
            ProductionCaptureSink? productionSink = null)
        {
            _nats = nats ?? throw new ArgumentNullException(nameof(nats));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
            _heartbeatSeconds = heartbeatSeconds > 0 ? heartbeatSeconds : 5;
            _captureBurstCount = captureBurstCount > 0 ? captureBurstCount : 1;
            _nucs = nucs;
            _getConfigSnapshot = getConfigSnapshot;
            _applyConfigSnapshot = applyConfigSnapshot;
            _cameraControlHandler = cameraControlHandler;
            _serialHealth = serialHealth;
            _readCameraTemperature = readCameraTemperature;
            _readFpaRaw = readFpaRaw;
            _readBiasJson = readBiasJson;
            _productionSink = productionSink;
        }

        public bool IsConnected => _connected;

        /// <summary>백그라운드에서 접속 재시도를 시작한다. 접속에 실패해도 호출자를 막지 않는다.</summary>
        public void Start(string natsUrl)
        {
            _ = Task.Run(() => ConnectWithRetryAsync(natsUrl, _cts.Token));
        }

        private async Task ConnectWithRetryAsync(string natsUrl, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_connected)
            {
                try
                {
                    await _nats.ConnectAsync(natsUrl).ConfigureAwait(false);
                    _connected = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CameraNats] connect failed, retrying: {ex.Message}");
                    try { await Task.Delay(ReconnectDelay, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }

            if (!_connected || ct.IsCancellationRequested)
            {
                return;
            }

            await SyncSubscriptionsAsync().ConfigureAwait(false);

            _heartbeat = new Timer(_ => PublishHeartbeats(), null, TimeSpan.Zero, TimeSpan.FromSeconds(_heartbeatSeconds));

            _ = Task.Run(() => LiveStreamLoopAsync(_cts.Token));
        }

        /// <summary>
        /// 아직 구독하지 않은 카메라의 NATS 구독을 채운다. 핫플러그로 카메라가 늘어난 뒤 호출하지
        /// 않으면 새 카메라는 캡처 명령을 받지 못한다.
        /// </summary>
        public async Task SyncSubscriptionsAsync()
        {
            if (!_connected || _cts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await _subscriptionGate.WaitAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                foreach (CameraDescriptor descriptor in new List<CameraDescriptor>(_cameras))
                {
                    if (!_subscribedAgentIds.Add(descriptor.AgentId))
                    {
                        continue;
                    }

                    if (!await SubscribeCameraAsync(descriptor).ConfigureAwait(false))
                    {
                        _subscribedAgentIds.Remove(descriptor.AgentId);
                    }
                }
            }
            finally
            {
                _subscriptionGate.Release();
            }

            // 카메라 구성이 바뀐 직후(핫플러그/재구성) 즉시 인벤토리를 알린다. 하트비트 주기를
            // 기다리면 Master가 최대 HeartbeatSeconds 동안 사라진 카메라를 계속 보여준다.
            PublishHeartbeats();
        }

        private async Task<bool> SubscribeCameraAsync(CameraDescriptor descriptor)
        {
            bool success = true;
            try
            {
                await _nats.SubscribeCaptureCommandAsync(
                    descriptor.AgentId,
                    cmd => _ = HandleCaptureAsync(descriptor, cmd)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                success = false;
                Debug.WriteLine($"[CameraNats] subscribe failed for {descriptor.AgentId}: {ex.Message}");
            }

            try
            {
                await _nats.SubscribeCameraControlAsync(
                    descriptor.AgentId,
                    msg => _ = HandleCameraControlAsync(descriptor, msg)).ConfigureAwait(false);
                await _nats.SubscribeRawImageRequestAsync(
                    descriptor.AgentId,
                    req => _ = HandleRawImageRequestAsync(req)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                success = false;
                Debug.WriteLine($"[CameraNats] camera control subscribe failed for {descriptor.AgentId}: {ex.Message}");
            }

            if (_getConfigSnapshot is not null || _applyConfigSnapshot is not null)
            {
                try
                {
                    if (_getConfigSnapshot is not null)
                        await _nats.SubscribeAgentConfigRequestAsync(descriptor.AgentId, req => _ = PublishConfigSnapshotAsync(descriptor.AgentId)).ConfigureAwait(false);
                    if (_applyConfigSnapshot is not null)
                        await _nats.SubscribeAgentConfigApplyAsync(descriptor.AgentId, msg => _ = ApplyConfigAsync(msg)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    success = false;
                    Debug.WriteLine($"[CameraNats] config subscribe failed for {descriptor.AgentId}: {ex.Message}");
                }
            }

            return success;
        }

        /// <summary>
        /// 캡처 명령 처리: 라이브 루프를 스냅샷해 NUC 보정 후 저장하고 결과를 발행한다.
        /// 장수는 명령의 <see cref="CaptureCommandMessage.ShotCount"/>가 우선이고 없으면 로컬 설정을 쓴다.
        /// Master가 장수를 세어 스텝 완료를 판정하므로 실패한 장도 <c>IsSuccess=false</c>로 반드시 1건 발행한다.
        /// </summary>
        public async Task HandleCaptureAsync(CameraDescriptor descriptor, CaptureCommandMessage cmd)
        {
            int shots = cmd.ShotCount > 0 ? cmd.ShotCount : _captureBurstCount;
            if (shots < 1) shots = 1;

            double? cameraTemperature = null;
            if (_manager.TryGet(descriptor.AgentId, out ICameraRuntime runtime) && _readCameraTemperature is not null)
            {
                try { cameraTemperature = await _readCameraTemperature(descriptor).ConfigureAwait(false); }
                catch (Exception ex) { Debug.WriteLine($"[CameraNats] camera info read failed for {descriptor.AgentId}: {ex.Message}"); }
            }

            bool production = _productionSink is not null && ProductionCaptureSink.IsEnabled(cmd);

            // FPA는 시리얼 왕복이라 장마다 읽으면 촬영이 멈춘다. AISEN 원본처럼 배치당 한 번만 읽어
            // 모든 장의 픽셀(0,0)에 같은 값을 심는다.
            short? fpaRaw = null;
            if (production && _readFpaRaw is not null)
            {
                try { fpaRaw = await _readFpaRaw(descriptor).ConfigureAwait(false); }
                catch (Exception ex) { Debug.WriteLine($"[CameraNats] FPA raw read failed for {descriptor.AgentId}: {ex.Message}"); }
            }

            using var abort = new CancellationTokenSource();
            _captureAborts[descriptor.AgentId] = abort;

            // 규칙 저장에서는 .raw가 방사 측정 원본이므로 .y16을 장마다 또 쓰지 않는다. 결과 화면용으로
            // 마지막 성공 프레임 한 장만 남기고, 결과도 배치당 1건만 발행한다(장마다 JPEG을 붙이면
            // 100장 x 카메라수 만큼의 미리보기가 Master 메모리에 쌓인다).
            ThermalFrame? representative = null;
            int captured = 0;

            try
            {
            for (int i = 0; i < shots; i++)
            {
                if (abort.IsCancellationRequested)
                {
                    Debug.WriteLine($"[CameraNats] capture aborted for {descriptor.AgentId} at shot {i + 1}/{shots}");
                    break;
                }

                bool success = false;
                string imagePath = string.Empty;
                byte[]? bytes = null;

                try
                {
                    if (_manager.TryGet(descriptor.AgentId, out ICameraRuntime shotRuntime))
                    {
                        bool forceFreshFrame = i > 0;
                        ThermalFrame? snap = await shotRuntime.CaptureSnapshotAsync(
                            maxAge: forceFreshFrame ? TimeSpan.Zero : TimeSpan.FromSeconds(1),
                            nextFrameTimeout: TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                        if (snap is not null)
                        {
                            ThermalFrame frame = snap;
                            if (_nucs is not null && _nucs.TryGetValue(descriptor.AgentId, out ThermalNucCorrector? nuc) && nuc is not null)
                            {
                                frame = nuc.Apply(frame);
                            }

                            if (production)
                            {
                                // 후처리 툴이 캘리브레이션을 직접 하므로 .raw는 NUC 미보정 원본(snap)이어야 한다.
                                // .jpg는 육안 검사용이라 반대로 NUC 보정 프레임을 쓴다.
                                _productionSink!.WriteShot(
                                    descriptor,
                                    cmd,
                                    cmd.SaveFormat == ProductionCaptureFormat.Jpeg ? frame : snap,
                                    fpaRaw,
                                    i);
                                representative = frame;
                            }
                            else
                            {
                                CaptureRecord record = _store.Save(frame, descriptor.AgentId, descriptor.OpenCvIndex, cmd.RecipeStepId);
                                imagePath = record.Y16Path;
                                bytes = ThermalPreviewEncoder.EncodeJpeg(frame);
                            }

                            captured++;
                            success = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CameraNats] capture failed for {descriptor.AgentId}: {ex.Message}");
                    success = false;
                }

                if (!production) await PublishCaptureResultAsync(descriptor, cmd, success, imagePath, bytes, cameraTemperature).ConfigureAwait(false);
            }
            }
            finally
            {
                _captureAborts.TryRemove(descriptor.AgentId, out _);
            }

            if (production)
            {
                string imagePath = string.Empty;
                byte[]? bytes = null;
                if (representative is not null)
                {
                    CaptureRecord record = _store.Save(representative, descriptor.AgentId, descriptor.OpenCvIndex, cmd.RecipeStepId);
                    imagePath = record.Y16Path;
                    bytes = ThermalPreviewEncoder.EncodeJpeg(representative);
                }

                await PublishCaptureResultAsync(descriptor, cmd, captured == shots, imagePath, bytes, cameraTemperature).ConfigureAwait(false);
            }

            if (production && cmd.WriteBiasJson)
            {
                string? biasJson = _readBiasJson?.Invoke(descriptor);
                if (string.IsNullOrWhiteSpace(biasJson))
                    Debug.WriteLine($"[CameraNats] bias.json skipped for {descriptor.AgentId}: no bias result yet");
                else
                    _productionSink!.WriteBiasJson(descriptor, cmd, biasJson!);
            }

            // 폴더가 완성된 뒤에만 전송한다 — 파일 단위로 옮기면 후처리 툴이 복사 도중인 파일을 읽는다.
            if (production) await _productionSink!.SyncFolderAsync(descriptor, cmd, _cts.Token).ConfigureAwait(false);
        }

        private async Task PublishCaptureResultAsync(
            CameraDescriptor descriptor,
            CaptureCommandMessage cmd,
            bool success,
            string imagePath,
            byte[]? bytes,
            double? cameraTemperature)
        {
            try
            {
                await _nats.PublishCaptureResultAsync(new CaptureResultMessage
                {
                    AgentId = descriptor.AgentId,
                    Alias = descriptor.Alias,
                    CameraIndex = descriptor.OpenCvIndex,
                    RecipeStepId = cmd.RecipeStepId,
                    Source = cmd.Source != CaptureSource.Unknown
                        ? cmd.Source
                        : (string.IsNullOrEmpty(cmd.RecipeStepId) ? CaptureSource.Manual : CaptureSource.Recipe),
                    CaptureId = Guid.NewGuid().ToString(),
                    IsSuccess = success,
                    ImagePath = imagePath,
                    ImageBytes = bytes,
                    Timestamp = DateTime.UtcNow,
                    CameraTemperature = cameraTemperature
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] publish result failed for {descriptor.AgentId}: {ex.Message}");
            }
        }

        /// <summary>진행 중인 버스트를 취소한다. 취소할 것이 없어도 성공으로 응답한다(멱등).</summary>
        private bool AbortCapture(string agentId)
        {
            if (!_captureAborts.TryGetValue(agentId, out CancellationTokenSource? abort)) return false;
            try { abort.Cancel(); } catch (ObjectDisposedException) { }
            return true;
        }

        /// <summary>카메라 제어 명령을 주입된 핸들러에 위임하고 성패를 ACK로 발행한다.</summary>
        public async Task HandleCameraControlAsync(CameraDescriptor cam, CameraControlMessage msg)
        {
            bool success = false;
            string message = "control handler not wired";

            // 중단은 패널 명령이 아니라 커넥터가 직접 처리한다 — 진행 중인 버스트 루프를 아는 곳이 여기다.
            if (msg.Op == CameraControlOps.CaptureAbort)
            {
                bool aborted = AbortCapture(cam.AgentId);
                await PublishControlAckAsync(cam, msg, true, aborted ? "capture aborted" : "no capture in progress").ConfigureAwait(false);
                return;
            }

            try
            {
                if (_cameraControlHandler is not null)
                {
                    (success, message) = await _cameraControlHandler(cam, msg).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Debug.WriteLine($"[CameraNats] camera control failed for {cam.AgentId}: {ex.Message}");
            }

            await PublishControlAckAsync(cam, msg, success, message).ConfigureAwait(false);
        }

        private async Task PublishControlAckAsync(CameraDescriptor cam, CameraControlMessage msg, bool success, string message)
        {
            try
            {
                await _nats.PublishCameraControlAckAsync(new CameraControlAckMessage
                {
                    AgentId = cam.AgentId,
                    CameraIndex = msg.CameraIndex,
                    Op = msg.Op,
                    RequestId = msg.RequestId,
                    IsSuccess = success,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] camera control ack publish failed for {cam.AgentId}: {ex.Message}");
            }
        }

        private async Task PublishConfigSnapshotAsync(string agentId)
        {
            if (_getConfigSnapshot is null) return;
            try
            {
                await _nats.PublishAgentConfigSnapshotAsync(new AgentConfigSnapshotMessage
                {
                    AgentId = agentId,
                    Config = _getConfigSnapshot(),
                    Timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] config snapshot publish failed for {agentId}: {ex.Message}");
            }
        }

        private async Task ApplyConfigAsync(AgentConfigApplyMessage msg)
        {
            bool success = true;
            string message = "저장됨. AgentUI 재시작 후 적용됩니다.";
            try
            {
                _applyConfigSnapshot?.Invoke(msg.Config);
            }
            catch (Exception ex)
            {
                success = false;
                message = ex.Message;
                Debug.WriteLine($"[CameraNats] config apply failed for {msg.AgentId}: {ex.Message}");
            }

            try
            {
                await _nats.PublishAgentConfigAckAsync(new AgentConfigAckMessage
                {
                    AgentId = msg.AgentId,
                    IsSuccess = success,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] config ack publish failed for {msg.AgentId}: {ex.Message}");
            }
        }

        private void PublishHeartbeats()
        {
            var live = new List<CameraDescriptor>();
            foreach (CameraDescriptor cam in new List<CameraDescriptor>(_cameras))
            {
                if (_manager.TryGet(cam.AgentId, out _))
                {
                    live.Add(cam);
                }
            }

            var inventory = new List<string>(live.Count);
            foreach (CameraDescriptor cam in live) inventory.Add(cam.AgentId);

            // 마지막 카메라까지 빠지면 인벤토리를 실어 보낼 카메라가 없다. 호스트 이름으로 빈
            // 인벤토리를 한 번 보고해야 Master가 그 PC의 카메라를 지운다(침묵은 신호가 아니다).
            if (live.Count == 0)
            {
                _ = PublishHostInventoryAsync(inventory);
                return;
            }

            foreach (CameraDescriptor cam in live)
            {
                if (_manager.TryGet(cam.AgentId, out ICameraRuntime runtime))
                {
                    _ = PublishStatusAsync(cam, MapStatus(runtime.Status), inventory);
                }
            }
        }

        private async Task PublishHostInventoryAsync(List<string> inventory)
        {
            try
            {
                await _nats.PublishAgentStatusAsync(new AgentStatusMessage
                {
                    AgentId = Environment.MachineName,
                    HostName = Environment.MachineName,
                    CameraStatus = CameraStatus.Offline,
                    Timestamp = DateTime.UtcNow,
                    HostAgentIds = inventory
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] host inventory publish failed: {ex.Message}");
            }
        }

        private async Task PublishStatusAsync(CameraDescriptor cam, CameraStatus status, List<string> inventory)
        {
            try
            {
                await _nats.PublishAgentStatusAsync(new AgentStatusMessage
                {
                    AgentId = cam.AgentId,
                    Alias = cam.Alias,
                    HostName = Environment.MachineName,
                    CameraIndex = cam.OpenCvIndex,
                    CameraStatus = status,
                    Timestamp = DateTime.UtcNow,
                    HostAgentIds = inventory,
                    IsSerialConnected = ReadSerialHealth(cam),
                    CameraTemperature = await ReadCameraTemperatureSafeAsync(cam).ConfigureAwait(false),
                    PendingSyncFiles = _productionSink?.PendingFiles
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] heartbeat failed for {cam.AgentId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Master의 온디맨드 원본 요청에 응답한다. 요청 경로의 .y16을 그대로 실어 보내고,
        /// 가로/세로는 옆에 있는 .json 사이드카에서 최선으로 읽는다(없어도 히스토그램은 그린다).
        /// Master가 타임아웃까지 붙잡히지 않도록 실패도 반드시 응답한다.
        /// </summary>
        private async Task HandleRawImageRequestAsync(RawImageRequestMessage request)
        {
            var response = new RawImageResponseMessage
            {
                AgentId = request.AgentId,
                RequestId = request.RequestId
            };

            try
            {
                if (string.IsNullOrWhiteSpace(request.RawPath) || !File.Exists(request.RawPath))
                {
                    response.Message = $"원본 파일을 찾을 수 없습니다: {request.RawPath}";
                }
                else
                {
                    response.Pixels = await File.ReadAllBytesAsync(request.RawPath).ConfigureAwait(false);
                    response.IsSuccess = true;

                    string sidecar = Path.ChangeExtension(request.RawPath, ".json");
                    if (File.Exists(sidecar))
                    {
                        try
                        {
                            var meta = JsonSerializer.Deserialize<CaptureMetadata>(
                                await File.ReadAllTextAsync(sidecar).ConfigureAwait(false));
                            if (meta is not null)
                            {
                                response.Width = meta.Width;
                                response.Height = meta.Height;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[CameraNats] raw sidecar parse failed for {sidecar}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.Message = ex.Message;
                Debug.WriteLine($"[CameraNats] raw read failed for {request.RawPath}: {ex.Message}");
            }

            try { await _nats.PublishRawImageResponseAsync(response).ConfigureAwait(false); }
            catch (Exception ex) { Debug.WriteLine($"[CameraNats] raw response publish failed: {ex.Message}"); }
        }

        /// <summary>하트비트가 카메라 시리얼 고장으로 끊기면 안 되므로 실패는 null로 흡수한다.</summary>
        private async Task<double?> ReadCameraTemperatureSafeAsync(CameraDescriptor cam)
        {
            if (_readCameraTemperature is null) return null;
            try { return await _readCameraTemperature(cam).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] heartbeat temperature read failed for {cam.AgentId}: {ex.Message}");
                return null;
            }
        }

        private bool? ReadSerialHealth(CameraDescriptor cam)
        {
            if (_serialHealth is null) return null;
            try { return _serialHealth(cam); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CameraNats] serial health probe failed for {cam.AgentId}: {ex.Message}");
                return false;
            }
        }

        private static CameraStatus MapStatus(CameraRuntimeStatus status) => status switch
        {
            CameraRuntimeStatus.Running => CameraStatus.Connected,
            _ => CameraStatus.Offline
        };

        // ponytail: 카메라당 NATS로 ~10fps 컬러 JPEG 미리보기. 대역폭 상한 — 여러 Agent가 링크를
        // 포화시키면 지연을 늘리거나 해상도를 낮출 것.
        private async Task LiveStreamLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (CameraDescriptor cam in _cameras)
                {
                    if (!_manager.TryGet(cam.AgentId, out ICameraRuntime runtime)) continue;

                    // 라이브는 무조건 원본(LatestFrame)을 본다 — NUC·AGC 같은 표시 처리는
                    // Master 측에서 한다. 여기서 보정 프레임을 본면 Master는 원본을 볼 수 없다.
                    ThermalFrame? frame = runtime.LatestFrame;
                    if (frame is null) continue;

                    try
                    {
                        byte[] jpeg = ThermalPreviewEncoder.EncodeJpeg(frame);
                        await _nats.PublishLiveFrameAsync(new LiveFrameMessage
                        {
                            AgentId = cam.AgentId,
                            CameraIndex = cam.OpenCvIndex,
                            ImageBytes = jpeg,
                            Width = frame.Width,
                            Height = frame.Height,
                            Timestamp = DateTime.UtcNow
                        }).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CameraNats] live publish failed for {cam.AgentId}: {ex.Message}");
                    }
                }

                try { await Task.Delay(100, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();

            if (_heartbeat is not null)
            {
                await _heartbeat.DisposeAsync().ConfigureAwait(false);
            }

            _subscriptionGate.Dispose();
            _cts.Dispose();
        }
    }
}
