using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.AgentUI.ViewModels;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Protocols.Cameras.CL;
using HeatingCameraSystem.Protocols.Simulation;

namespace HeatingCameraSystem.AgentUI
{
    /// <summary>
    /// AgentUI 애플리케이션 진입점. 한 프로세스에서 로컬 카메라 여러 대를 호스팅하며,
    /// 시작 시 카메라 자동 등록·페어링 reconcile → 패널 구성 → <see cref="CameraNatsConnector"/>로
    /// NATS 브리지 기동 → USB 핫플러그 감시 순으로 초기화한다. 종료 시에는 네이티브 카메라/시리얼
    /// 콜이 wedge될 수 있어 워치독이 프로세스를 강제 종료한다(<see cref="OnExit"/>).
    /// </summary>
    public partial class App : Application
    {
        // 세션 범위 단일 인스턴스 가드: 자동 시작 + Manager 재기동(예약 작업)이 같은 운영자
        // 세션에서 AgentUI를 이중 실행하는 것을 막는다.
        private const string SingleInstanceMutexName = "HeatingCameraSystem.AgentUI.SingleInstance";

        private Mutex? _singleInstanceMutex;
        private CameraRuntimeManager? _manager;
        private MainViewModel? _mainViewModel;
        private CaptureStore? _store;
        private INatsCommunicationService? _nats;
        private CameraNatsConnector? _natsConnector;
        private AgentUiConfig? _config;
        private ICameraComPairingService? _pairing;
        private Func<CameraDescriptor, ICameraSerialClient?>? _serialFactory;
        private Dictionary<string, ThermalNucCorrector>? _nucs;
        private ICameraEnumerator? _cameraWatcher;
        private IVideoDeviceEnumerator? _videoEnumerator;
        private int _rebuildInFlight;
        private int _rebuildDirty;
        private readonly Dictionary<string, CameraDescriptor> _panelBindings = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource _appLifetime = new();
        private IReadOnlyList<CameraComPair> _lastPairs = Array.Empty<CameraComPair>();

        /// <summary>온도 조회에서 패널을 못 찾은 AgentId. 하트비트마다 같은 경고를 반복하지 않으려고 기억한다.</summary>
        private readonly ConcurrentDictionary<string, byte> _temperaturePanelMisses = new(StringComparer.OrdinalIgnoreCase);

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                // 다른 AgentUI 인스턴스가 이미 이 세션을 점유하고 있다.
                Shutdown();
                return;
            }

            base.OnStartup(e);

            AgentUiLog.Initialize();

            // [S8] 헤드리스 배포 모드: 창 없이 카메라 + NATS만 돌린다. WPF는 마지막 창이 닫히면
            // 종료되므로 MainWindow를 건너뛰기 전에 명시적 종료 모드로 전환한다.
            bool headless = e.Args.Any(a => a.Equals("--headless", StringComparison.OrdinalIgnoreCase));
            if (headless)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            AgentUiConfig config = AgentUiConfig.LoadOrCreate();
            // 기본 리플레이 원본은 exe와 함께 배포되는 Assets\BB70_000.raw다. 작업 디렉터리가
            // exe 폴더가 아닐 수 있으므로(바로가기·서비스·스크립트) 상대경로를 쓰지 않는다.
            string? envRawPath = Environment.GetEnvironmentVariable("HCS_SIM_RAW_PATH");
            string simulationRawPath = string.IsNullOrWhiteSpace(envRawPath)
                ? Path.Combine(AppContext.BaseDirectory, "Assets", "BB70_000.raw")
                : Path.GetFullPath(envRawPath);
            if (config.SimulationMode)
            {
                AgentUiLog.Logger.Information("Simulation RAW replay enabled: {RawPath}", simulationRawPath);
            }

            if (!config.SimulationMode)
            {
                // AgentId에 호스트명을 접두해 여러 PC의 "Agent_1"들이 공유 NATS 토픽에서 충돌하지 않게 한다.
                string host = Environment.MachineName;
                for (int i = 0; i < config.Cameras.Count; i++)
                {
                    CameraDescriptor cam = config.Cameras[i];
                    if (!cam.AgentId.StartsWith(host + "_", StringComparison.OrdinalIgnoreCase))
                    {
                        config.Cameras[i] = cam with { AgentId = $"{host}_{cam.AgentId}" };
                    }
                }
            }

            Func<CameraDescriptor, ICameraRuntime> sourceFactory = config.SimulationMode
                ? (d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource(simulationRawPath)))
                : (d => new CameraRuntime(d.OpenCvIndex, new CltcThermalFrameSource(d.OpenCvIndex)));

            Func<CameraDescriptor, ICameraSerialClient?> serialFactory = config.SimulationMode
                ? (d => string.IsNullOrWhiteSpace(d.SerialPortName) ? null : new FakeCameraSerialClient(d.SerialPortName!))
                : (d => string.IsNullOrWhiteSpace(d.SerialPortName) ? null : new ClSerialCameraClient(d.SerialPortName!));

            ICameraComPairingService pairing = config.SimulationMode
                ? new FakeCameraComPairingService()
                : new CameraComPairingService(
                    new WmiCameraEnumerator(),
                    new WmiUsbSerialEnumerator(),
                    portName => new ClSerialCameraClient(portName),
                    new HardwareSettings());

            _manager = new CameraRuntimeManager(sourceFactory);
            _mainViewModel = new MainViewModel(config.SimulationMode ? "AgentUI — SIMULATION" : "AgentUI");

            string storageDir = config.EffectiveStorageDir;
            Directory.CreateDirectory(storageDir);
            _store = new CaptureStore(storageDir, new LiteDbCaptureIndex(Path.Combine(storageDir, "index.db")), config.CaptureImageFormat);

            Dispatcher dispatcher = Dispatcher;
            var nucs = new Dictionary<string, ThermalNucCorrector>();

            _config = config;
            _pairing = pairing;
            _serialFactory = serialFactory;
            _nucs = nucs;
            _videoEnumerator = config.SimulationMode ? null : new DirectShowVideoDeviceEnumerator();

            if (!config.SimulationMode)
            {
                IReadOnlyList<CameraComPair> pairs = GetPairsOrEmpty(pairing);
                _lastPairs = pairs;
                int deduped = CameraAutoRegistrar.DedupeByAgentId(config.Cameras);
                AutoRegisterDetectedCameras(config, pairs);
                AutoRegisterVideoOnlyCameras(config);
                int serialChanged = ReconcileSerialPortsFromPairing(config, pairs);
                int videoChanged = ReconcileVideoIndicesFromEnumeration(config);
                if (deduped > 0 || serialChanged > 0 || videoChanged > 0)
                {
                    config.Save();
                    AgentUiLog.Logger.Information(
                        "Camera inventory reconciled: deduped={Deduped} serial={Serial} video={Video}",
                        deduped, serialChanged, videoChanged);
                }
            }

            RebuildCameraPanels();

            _nats = new NatsCommunicationService();
            _natsConnector = new CameraNatsConnector(_nats, _manager, _store, config.Cameras, config.HeartbeatSeconds, nucs, config.CaptureBurstCount,
                getConfigSnapshot: () => dispatcher.Invoke(() => CreateConfigSnapshot(config)),
                applyConfigSnapshot: snap => dispatcher.Invoke(() =>
                {
                    config.SaveDraft(new AgentUiConfig
                    {
                        SimulationMode = snap.SimulationMode,
                        NatsUrl = snap.NatsUrl,
                        StoragePath = snap.StoragePath,
                        HeartbeatSeconds = snap.HeartbeatSeconds,
                        CaptureImageFormat = snap.CaptureImageFormat,
                        CaptureBurstCount = snap.CaptureBurstCount,
                        Cameras = snap.Cameras?.ToList() ?? new List<CameraDescriptor>()
                    }, requireRestart: true);
                    if (_mainViewModel?.Settings is { } settings)
                        settings.StatusText = "원격 설정 저장됨. AgentUI 재시작 후 적용됩니다. 현재 실행 설정은 유지됩니다.";
                }),
                cameraControlHandler: async (descriptor, controlMessage) =>
                {
                    string op = controlMessage.Op;
                    try
                    {
                        return await dispatcher.InvokeAsync(async () =>
                        {
                            CameraDescriptor? current = config.Cameras.FirstOrDefault(candidate => candidate.AgentId == descriptor.AgentId);
                            CameraPanelViewModel? panel = _mainViewModel?.Cameras.FirstOrDefault(candidate => candidate.AgentId == descriptor.AgentId);
                            if (current is null || panel is null) return (false, $"camera panel not found: {descriptor.AgentId}");
                            if (op is CameraControlOps.RuntimeUnload or CameraControlOps.RuntimeLoad)
                            {
                                if (op == CameraControlOps.RuntimeLoad && (current.OpenCvIndex < 0
                                    || !_panelBindings.TryGetValue(current.AgentId, out CameraDescriptor? binding)
                                    || !CameraBindingPolicy.SameBinding(binding, current)))
                                    return (false, "camera physical identity/index is not resolved");
                                await panel.ChangeRuntimeAsync(async () =>
                                {
                                    CameraRuntimeManager manager = _manager ?? throw new InvalidOperationException("Camera manager unavailable");
                                    manager.Remove(current.AgentId);
                                    if (op == CameraControlOps.RuntimeUnload) return null;
                                    ICameraRuntime runtime = manager.Add(current);
                                    await runtime.StartAsync(_appLifetime.Token);
                                    return runtime;
                                }, _appLifetime.Token);
                                return (true, op == CameraControlOps.RuntimeLoad ? "runtime loaded" : "runtime unloaded");
                            }
                            if (BiasExecutionPolicy.IsBiasOperation(op))
                                await panel.RunBiasOperationAsync(op, controlMessage.BiasTargetLevel > 0 ? controlMessage.BiasTargetLevel : null, _appLifetime.Token);
                            else
                                await panel.RunCameraControlAsync(op, _appLifetime.Token);
                            return (true, "ok");
                        }).Task.Unwrap();
                    }
                    catch (Exception ex)
                    {
                        return (false, ex.Message);
                    }
                },
                // 패널이 핫플러그마다 재생성되므로 스냅샷이 아니라 매번 조회한다. 패널이 아직 없으면
                // 시리얼 제어가 없다는 뜻이므로 false — 영상 전용 구성은 지원 대상이 아니다.
                serialHealth: descriptor => dispatcher.Invoke(() => _mainViewModel?.Cameras
                    .FirstOrDefault(panel => panel.AgentId == descriptor.AgentId)?.HasSerialControl ?? false),
                readCameraTemperature: ReadCameraTemperatureForAsync,
                readFpaRaw: ReadFpaRawForAsync,
                readBiasJson: descriptor => dispatcher.Invoke(() => _mainViewModel?.Cameras
                    .FirstOrDefault(panel => panel.AgentId == descriptor.AgentId)?.LastBiasJson),
                productionSink: new ProductionCaptureSink(Path.Combine(storageDir, "production")),
                biasControlHandler: async (descriptor, controlMessage, cancellationToken) =>
                {
                    await dispatcher.InvokeAsync(async () =>
                    {
                        CameraPanelViewModel? panel = _mainViewModel?.Cameras
                            .FirstOrDefault(candidate => candidate.AgentId == descriptor.AgentId);
                        if (panel is null)
                            throw new InvalidOperationException($"카메라 패널이 없습니다: {descriptor.AgentId}");

                        if (controlMessage.Op == CameraControlOps.BiasUseCurrent)
                        {
                            await panel.VerifyAppliedBiasAsync(controlMessage.BiasSourceOperation, cancellationToken);
                            return;
                        }

                        double? target = controlMessage.BiasTargetLevel > 0 ? controlMessage.BiasTargetLevel : null;
                        await panel.RunBiasOperationAsync(controlMessage.Op, target, cancellationToken);
                    }).Task.Unwrap();
                },
                diagnosticLog: AgentUiLog.WriteBiasDiagnostic,
                readAppliedBias: descriptor => dispatcher.Invoke(() => _mainViewModel?.Cameras
                    .FirstOrDefault(panel => panel.AgentId == descriptor.AgentId)?.LastAppliedBias),
                readLogDirectory: () => AgentUiLog.LogDir,
                serialConfigHandler: ApplySerialConfigForAsync,
                acquireCaptureLease: async (descriptor, cancellationToken) => await dispatcher.InvokeAsync(() =>
                {
                    CameraPanelViewModel panel = _mainViewModel?.Cameras.FirstOrDefault(candidate => candidate.AgentId == descriptor.AgentId)
                        ?? throw new InvalidOperationException($"카메라 패널이 없습니다: {descriptor.AgentId}");
                    return panel.AcquireCaptureLeaseAsync(cancellationToken);
                }).Task.Unwrap(),
                reportCaptureWarning: (descriptor, warning) =>
                {
                    AgentUiLog.Logger.Warning("[{AgentId}] {CaptureWarning}", descriptor.AgentId, warning);
                    dispatcher.InvokeAsync(() =>
                    {
                        CameraPanelViewModel? panel = _mainViewModel?.Cameras
                            .FirstOrDefault(candidate => candidate.AgentId == descriptor.AgentId);
                        if (panel is not null) panel.CaptureStatus = warning;
                    });
                });
            _natsConnector.Start(config.NatsUrl);

            if (!config.SimulationMode)
            {
                var watcher = new WmiCameraEnumerator();
                watcher.Changed += OnCameraHotplug;
                watcher.StartWatching();
                _cameraWatcher = watcher;
            }

            AgentUiLog.Logger.Information(
                "AgentUI started: {CameraCount} cameras, simulation={Simulation}, nats={NatsUrl}",
                config.Cameras.Count, config.SimulationMode, config.NatsUrl);

            if (headless)
            {
                AgentUiLog.Logger.Information("AgentUI started headless — no window; NATS + camera runtimes active.");
                return;
            }

            _mainViewModel.DataBrowser = new DataBrowserViewModel(_store);
            _mainViewModel.Logs = new LogViewerViewModel(AgentUiLog.LogDir);
            var settingsVm = new SettingsViewModel(config, pairing);
            settingsVm.Saved += OnSettingsSaved;
            _mainViewModel.Settings = settingsVm;

            var window = new MainWindow { DataContext = _mainViewModel };
            MainWindow = window;
            window.Show();
        }

        // 자동 등록 + 시리얼/영상 reconcile이 공유하는 (비싼) 페어링 1회 수행. 실패 시 빈 목록.
        /// <summary>
        /// 하트비트와 캡처가 부르는 카메라 온도 조회. 패널을 못 찾으면 온도가 조용히 빠져
        /// Master 레시피 결과의 "카메라 온도"가 빈 값이 되므로 AgentId마다 한 번은 사유를 남긴다.
        /// </summary>
        private Task<short?> ReadFpaRawForAsync(CameraDescriptor descriptor)
        {
            CameraPanelViewModel? panel = Dispatcher.Invoke(() => _mainViewModel?.Cameras
                .FirstOrDefault(candidate => candidate.AgentId == descriptor.AgentId));

            return panel is null ? Task.FromResult<short?>(null) : panel.ReadFpaTemperatureRawAsync();
        }

        private Task<double?> ReadCameraTemperatureForAsync(CameraDescriptor descriptor)
        {
            CameraPanelViewModel? panel = Dispatcher.Invoke(() => _mainViewModel?.Cameras
                .FirstOrDefault(candidate => candidate.AgentId == descriptor.AgentId));

            if (panel is not null)
            {
                _temperaturePanelMisses.TryRemove(descriptor.AgentId, out _);
                return panel.ReadCameraTemperatureAsync();
            }

            if (_temperaturePanelMisses.TryAdd(descriptor.AgentId, 0))
            {
                AgentUiLog.Logger.Warning(
                    "[{AgentId}] 카메라(FPA) 온도를 읽지 못했습니다 — 카메라 패널을 찾지 못했습니다(패널 미생성 또는 AgentId 불일치). Master 레시피 결과의 카메라 온도가 빈 값이 됩니다.",
                    descriptor.AgentId);
            }

            return Task.FromResult<double?>(null);
        }

        private static IReadOnlyList<CameraComPair> GetPairsOrEmpty(ICameraComPairingService pairing)
        {
            try
            {
                // ponytail: 시리얼 S/N 읽는 동안 UI 스레드를 막는다(카메라당 1초 미만). 벤치 기동에는
                // 충분 — 8대 시작이 느려지면 창 표시 후 비동기 reconcile로 올릴 것.
                return Task.Run(() => pairing.GetPairsAsync()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AgentUiLog.Logger.Warning(ex, "Camera pairing failed; keeping configured cameras");
                return Array.Empty<CameraComPair>();
            }
        }

        // 새로 감지된 카메라를 저장(Save)해 패널/NATS가 지금은 물론 이후 실행에서도 인식하게 한다.
        private static void AutoRegisterDetectedCameras(AgentUiConfig config, IReadOnlyList<CameraComPair> pairs)
        {
            if (pairs.Count == 0)
            {
                return;
            }

            CameraDescriptor[] before = config.Cameras.ToArray();
            int added = CameraAutoRegistrar.Register(config.Cameras, pairs, Environment.MachineName);
            if (added > 0 || !before.SequenceEqual(config.Cameras))
            {
                config.Save();
                AgentUiLog.Logger.Information("Auto-registered {Count} new camera(s)", added);
            }
        }

        // 영상 전용 fallback: 영상 열거기에는 보이지만 페어링이 놓친 열화상 카메라(시리얼 고장/부재,
        // 혹은 페어 목록을 비워버린 WMI 오동작)를 등록해, 동작하는 카메라가 패널 + 라이브 영상은
        // 받게 한다. 페어 기반 등록 후, 영상 인덱스 reconcile 전에 실행된다.
        private void AutoRegisterVideoOnlyCameras(AgentUiConfig config)
        {
            if (_videoEnumerator is null)
            {
                return;
            }

            IReadOnlyList<VideoDevice> devices;
            try
            {
                devices = _videoEnumerator.Enumerate();
            }
            catch (Exception ex)
            {
                AgentUiLog.Logger.Warning(ex, "Video-only camera enumeration failed");
                return;
            }

            int added = CameraAutoRegistrar.RegisterVideoOnly(config.Cameras, devices, Environment.MachineName);
            if (added > 0)
            {
                config.Save();
                AgentUiLog.Logger.Information("Auto-registered {Count} video-only camera(s)", added);
            }
        }

        // 케이블을 뽑은 채로 기동하면 고스트 항목을 지운다. 런타임 중 핫플러그에서는
        // 호출하지 않는다 — 뽑았다 꽂으면 같은 AgentId로 살아나야 하므로.
        private void PruneDisconnectedCameras(AgentUiConfig config, IReadOnlyList<CameraComPair> pairs)
        {
            IReadOnlyList<VideoDevice> devices = Array.Empty<VideoDevice>();
            if (_videoEnumerator is not null)
            {
                try
                {
                    devices = _videoEnumerator.Enumerate();
                }
                catch (Exception ex)
                {
                    AgentUiLog.Logger.Warning(ex, "Video enumeration for prune failed; keeping configured cameras");
                }
            }

            int removed = CameraAutoRegistrar.PruneDisconnected(config.Cameras, pairs, devices);
            if (removed > 0)
            {
                config.Save();
                AgentUiLog.Logger.Information("Pruned {Count} disconnected camera(s)", removed);
            }
        }

        private static int ReconcileSerialPortsFromPairing(AgentUiConfig config, IReadOnlyList<CameraComPair> pairs)
        {
            // 이번 실행에서 이미 배정한 포트: 두 항목이 같은 페어에 걸리면(구 설정 중복)
            // 먼저 처리된 쪽이 가져가고 나머지는 기존 값을 유지한다.
            var claimedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int changed = 0;
            for (int i = 0; i < config.Cameras.Count; i++)
            {
                CameraDescriptor cam = config.Cameras[i];
                CameraComPair? pair = ResolveConfidentPair(pairs, cam);
                if (pair?.SerialPort is null)
                {
                    continue;
                }

                string newPort = pair.SerialPort.PortName;
                if (!string.Equals(cam.SerialPortName, newPort, StringComparison.OrdinalIgnoreCase)
                    && claimedPorts.Contains(newPort))
                {
                    AgentUiLog.Logger.Warning(
                        "Camera {AgentId}: serial {New} already claimed by another camera; keeping {Old}",
                        cam.AgentId, newPort, cam.SerialPortName ?? "(none)");
                    continue;
                }

                CameraDescriptor updated = cam with
                {
                    SerialPortName = newPort,
                    CameraSerialNumber = IsUsableSerial(pair.CameraSerialNumber) ? pair.CameraSerialNumber : cam.CameraSerialNumber,
                    UsbContainerId = string.IsNullOrWhiteSpace(pair.Camera.UsbParentId) ? cam.UsbContainerId : pair.Camera.UsbParentId,
                };
                if (!updated.Equals(cam))
                {
                    config.Cameras[i] = updated;
                    changed++;
                }
                claimedPorts.Add(newPort);

                if (!string.Equals(cam.SerialPortName, newPort, StringComparison.OrdinalIgnoreCase))
                {
                    AgentUiLog.Logger.Information(
                        "Camera {AgentId}: serial {Old} -> {New} (matched by pairing)",
                        cam.AgentId, cam.SerialPortName ?? "(none)", newPort);
                }
            }

            return changed;
        }

        private int ReconcileVideoIndicesFromEnumeration(AgentUiConfig config)
            => _videoEnumerator is null ? 0 : ReconcileVideoIndicesFromEnumeration(config, _videoEnumerator);

        internal static int ReconcileVideoIndicesFromEnumeration(AgentUiConfig config, IVideoDeviceEnumerator enumerator)
        {
            lock (config.Cameras)
            {
                IReadOnlyList<VideoDevice> devices;
                try
                {
                    devices = enumerator.Enumerate();
                }
                catch (Exception ex)
                {
                    int invalidated = VideoIndexReconciler.Reconcile(config.Cameras, Array.Empty<VideoDevice>());
                    AgentUiLog.Logger.Warning(ex,
                        "Video device enumeration failed; {Count} camera mapping(s) invalidated/offline. Native open is blocked until physical video identity is verified again.", invalidated);
                    return invalidated;
                }

                int changed = VideoIndexReconciler.Reconcile(config.Cameras, devices);
                if (changed > 0)
                    AgentUiLog.Logger.Information("Rebound {Count} camera OpenCvIndex value(s) by ContainerId", changed);
                return changed;
            }
        }

        /// <summary>
        /// 설정된 카메라와 감지된 페어를 확신 가능한 키로만 매칭한다: 고유 카메라 S/N 일치 →
        /// UsbContainerId 일치. 애매하면 null(잘못된 포트를 배정하느니 건드리지 않는다).
        /// </summary>
        private static CameraComPair? ResolveConfidentPair(IReadOnlyList<CameraComPair> pairs, CameraDescriptor cam)
        {
            CameraComPair? exact = SettingsViewModel.FindPairForRow(pairs, new CameraRow(cam));
            if (exact is not null) return exact;
            // 이미 열린 COM은 hotplug 탐색에서 S/N을 읽지 못할 수 있다. 같은 Container의
            // 유일한 미확인 후보만 재개방하며, 활성화 전 ReadSerialNumber로 기존 S/N을 확인한다.
            if (!IsUsableSerial(cam.CameraSerialNumber) || string.IsNullOrWhiteSpace(cam.UsbContainerId)) return null;
            CameraComPair[] candidates = pairs.Where(pair =>
                string.Equals(pair.Camera.UsbParentId, cam.UsbContainerId, StringComparison.OrdinalIgnoreCase)
                && !IsUsableSerial(pair.CameraSerialNumber)).ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }

        // 비었거나 0뿐인 S/N은 미기록 테스트 카메라 — 식별 키로 쓰지 않는다.
        private static bool IsUsableSerial([NotNullWhen(true)] string? serial) =>
            !string.IsNullOrWhiteSpace(serial) && serial.Any(c => c is >= '1' and <= '9');

        /// <summary>변경된 물리 바인딩만 교체한다. 다른 카메라의 핫플러그는 진행 중 BIAS를 취소하지 않는다.</summary>
        private void RebuildCameraPanels()
        {
            if (_manager is null || _mainViewModel is null || _config is null || _serialFactory is null || _nucs is null || _store is null) return;
            CameraDescriptor[] cameras;
            lock (_config.Cameras) cameras = _config.Cameras.ToArray();
            var ids = cameras.Select(camera => camera.AgentId).ToHashSet(StringComparer.Ordinal);
            foreach (CameraPanelViewModel panel in _mainViewModel.Cameras.ToList())
            {
                if (ids.Contains(panel.AgentId)) continue;
                panel.Dispose();
                _manager.Remove(panel.AgentId);
                _mainViewModel.Cameras.Remove(panel);
                _panelBindings.Remove(panel.AgentId);
                _nucs.Remove(panel.AgentId);
            }
            var duplicateIndices = cameras.Where(camera => camera.OpenCvIndex >= 0)
                .GroupBy(camera => camera.OpenCvIndex).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
            foreach (CameraDescriptor configured in cameras)
            {
                CameraDescriptor cam = duplicateIndices.Contains(configured.OpenCvIndex)
                    ? configured with { OpenCvIndex = -1 } : configured;
                CameraPanelViewModel? existing = _mainViewModel.Cameras.FirstOrDefault(candidate => candidate.AgentId == cam.AgentId);
                if (existing is not null && _panelBindings.TryGetValue(cam.AgentId, out CameraDescriptor? binding)
                    && CameraBindingPolicy.SameBinding(binding, cam))
                {
                    existing.Title = cam.Alias;
                    continue;
                }
                if (existing is not null)
                {
                    existing.Dispose();
                    _mainViewModel.Cameras.Remove(existing);
                    _manager.Remove(cam.AgentId);
                    _nucs.Remove(cam.AgentId);
                }
                ICameraRuntime? runtime = cam.OpenCvIndex >= 0 ? _manager.Add(cam) : null;
                ICameraSerialClient? serial = null;
                string? serialStatus = null;
                if (runtime is null)
                    serialStatus = "카메라 연결 대기: 물리 장치/영상 인덱스를 확정하지 못했습니다. 이전 BIAS는 무효입니다.";
                else if (!string.IsNullOrWhiteSpace(cam.SerialPortName))
                {
                    try
                    {
                        if (!_config.SimulationMode)
                        {
                            CameraComPair? pair = ResolveConfidentPair(_lastPairs, cam);
                            bool knownSerial = IsUsableSerial(cam.CameraSerialNumber);
                            if (knownSerial && _lastPairs.Count(candidate => string.Equals(candidate.CameraSerialNumber, cam.CameraSerialNumber, StringComparison.OrdinalIgnoreCase)) > 1)
                                throw new InvalidOperationException("카메라 S/N이 중복되어 COM 포트를 확정할 수 없습니다.");
                            if (!knownSerial && (pair?.SerialPort is null || !string.Equals(pair.SerialPort.PortName, cam.SerialPortName, StringComparison.OrdinalIgnoreCase)))
                                throw new InvalidOperationException("카메라 신원과 COM 포트의 일치를 확인하지 못했습니다.");
                            // 기존 포트를 점유한 패널 때문에 hotplug 탐색이 S/N을 못 읽은 경우,
                            // 포트 재개방 후 아래의 실제 S/N 읽기로 확인한다. 확인 전 쓰기는 없다.
                        }
                        serial = _serialFactory(cam);
                        if (serial is not null)
                        {
                            serial.InitializeAsync().GetAwaiter().GetResult();
                            if (!_config.SimulationMode && IsUsableSerial(cam.CameraSerialNumber))
                            {
                                string actual = serial.ReadSerialNumberAsync().GetAwaiter().GetResult();
                                if (!string.Equals(actual, cam.CameraSerialNumber, StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidOperationException($"시리얼 S/N 불일치: 설정 {cam.CameraSerialNumber}, 실제 {actual}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AgentUiLog.Logger.Warning(ex, "Camera {AgentId} serial {Port} open/identity check failed", cam.AgentId, cam.SerialPortName);
                        serial?.Dispose();
                        serial = null;
                        serialStatus = $"시리얼 제어 비활성화: {ex.Message}";
                    }
                }
                else serialStatus = "시리얼 포트 없음: 영상만 동작 (제어 비활성화)";
                var nuc = new ThermalNucCorrector();
                _nucs[cam.AgentId] = nuc;
                var panel = new CameraPanelViewModel(cam.Alias, cam.AgentId, runtime, Dispatcher, nuc, _store, _config.CaptureBurstCount, serial,
                    publishResult: message => _nats is { } nats ? nats.PublishCaptureResultAsync(message) : Task.CompletedTask);
                if (serialStatus is not null) panel.SerialStatus = serialStatus;
                _mainViewModel.Cameras.Add(panel);
                _panelBindings[cam.AgentId] = cam;
                if (runtime is not null)
                {
                    _ = StartCameraAsync(panel, runtime);
                }
            }
        }

        private static async Task StartCameraAsync(CameraPanelViewModel panel, ICameraRuntime runtime)
        {
            try
            {
                await panel.StartLiveAsync();
                if (panel.HasRuntime) await runtime.StartAsync();
            }
            catch (Exception ex)
            {
                AgentUiLog.Logger.Error(ex, "Camera {AgentId} (index {Index}) start failed", panel.AgentId, runtime.CameraIndex);
            }
        }

        internal static AgentConfigSnapshot CreateConfigSnapshot(AgentUiConfig active)
        {
            AgentUiConfig saved = active.EditableSnapshot();
            return new AgentConfigSnapshot
            {
                SimulationMode = saved.SimulationMode,
                NatsUrl = saved.NatsUrl,
                StoragePath = saved.StoragePath,
                HeartbeatSeconds = saved.HeartbeatSeconds,
                CaptureImageFormat = saved.CaptureImageFormat,
                CaptureBurstCount = saved.CaptureBurstCount,
                Cameras = saved.Cameras,
                RestartRequired = active.RestartRequired
            };
        }

        private async Task<(bool Success, string Message)> ApplySerialConfigForAsync(CameraDescriptor descriptor, SerialConfigMessage message)
        {
            try
            {
                return await Dispatcher.InvokeAsync(() =>
                {
                    AgentUiConfig config = _config ?? throw new InvalidOperationException("Agent 설정이 없습니다.");
                    if (config.RestartRequired) throw new InvalidOperationException("저장된 설정의 재시작 적용이 먼저 필요합니다.");
                    CameraDescriptor? current = config.Cameras.FirstOrDefault(camera => camera.AgentId == descriptor.AgentId);
                    if (current is null || !string.Equals(message.AgentId, current.AgentId, StringComparison.Ordinal))
                        throw new InvalidOperationException("설정 대상 카메라가 현재 인벤토리와 다릅니다.");
                    CameraBindingPolicy.ValidateSerialConfig(current, message.Settings);
                    CameraPanelViewModel? panel = _mainViewModel?.Cameras.FirstOrDefault(camera => camera.AgentId == current.AgentId);
                    if (panel is null || !panel.HasRuntime) throw new InvalidOperationException("카메라 런타임이 연결되지 않았습니다.");
                    if (panel.IsBiasBusy) throw new InvalidOperationException("카메라 명령/BIAS 진행 중에는 시리얼 설정을 바꿀 수 없습니다.");
                    if (!config.SimulationMode)
                    {
                        CameraComPair? pair = ResolveConfidentPair(_lastPairs, current);
                        if (pair?.SerialPort is null || !string.Equals(pair.SerialPort.PortName, message.Settings.PortName, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("카메라 신원과 요청 COM 포트의 일치를 확인하지 못했습니다.");
                    }
                    AgentUiConfig draft = config.EditableSnapshot();
                    int index = draft.Cameras.FindIndex(camera => camera.AgentId == current.AgentId);
                    draft.Cameras[index] = current with { SerialPortName = message.Settings.PortName.Trim() };
                    config.SaveDraft(draft);
                    // 같은 포트 재시도라도 이전 연결 실패를 복구하도록 이 패널만 교체한다.
                    _panelBindings.Remove(current.AgentId);
                    RebuildCameraPanels();
                    CameraPanelViewModel? applied = _mainViewModel?.Cameras.FirstOrDefault(camera => camera.AgentId == current.AgentId);
                    if (applied?.HasSerialControl != true)
                        throw new InvalidOperationException(applied?.SerialStatus ?? "시리얼 연결 실패");
                    return (true, "serial configuration applied");
                }).Task;
            }
            catch (Exception ex)
            {
                AgentUiLog.Logger.Warning(ex, "[{AgentId}] 시리얼 설정 실패", descriptor.AgentId);
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// 설정 화면 저장 → 카메라 구성을 재시작 없이 라이브 적용한다. 물리 감지(AutoRegister)는
        /// 일부러 돌리지 않는다 — 사용자가 방금 지운 카메라를 되살리지 않기 위함. config.Cameras는
        /// SettingsViewModel.Save가 in-place로 이미 갱신했으므로 그 기준으로 패널/런타임을 재구성하고,
        /// 구성에서 빠진 런타임을 제거한 뒤 NATS 인벤토리를 즉시 재발행한다(→ Master가 라이브 반영).
        /// </summary>
        private void OnSettingsSaved()
        {
            if (_config is null || _mainViewModel is null || _manager is null)
            {
                return;
            }

            var previous = _mainViewModel.Cameras
                .Select(panel => panel.AgentId)
                .ToHashSet(StringComparer.Ordinal);
            var current = new HashSet<string>(
                _config.Cameras.Select(cam => cam.AgentId), StringComparer.Ordinal);

            if (!_config.SimulationMode)
            {
                lock (_config.Cameras) ReconcileVideoIndicesFromEnumeration(_config);
            }
            RebuildCameraPanels();

            // RebuildCameraPanels는 현재 구성만 재등록(add 전용)하므로 삭제된 카메라의 런타임이 남는다.
            // UVC 핸들을 놓고 하트비트 인벤토리에서 빠지도록 명시적으로 제거한다.
            foreach (string agentId in previous)
            {
                if (!current.Contains(agentId))
                {
                    _manager.Remove(agentId);
                }
            }

            // 즉시 인벤토리 재발행: 추가분 구독 + 축소된 인벤토리 통지 → Master가 하트비트 주기를
            // 기다리지 않고 곧바로 노드를 지운다.
            _ = _natsConnector?.SyncSubscriptionsAsync();
        }

        /// <summary>
        /// USB 핫플러그 콜백. dirty 플래그 병합 루프로 재구성을 직렬화한다: 재구성 도중 도착한
        /// 이벤트는 플래그만 세우고, 진행 중인 루프가 끝나기 전에 한 번 더 돌아 반영된다.
        /// 재구성은 페어링 reconcile → 패널 재생성 → NATS 구독 동기화 순으로 진행된다.
        /// </summary>
        private void OnCameraHotplug(PnpChange change)
        {
            if (_appLifetime.IsCancellationRequested) return;
            Interlocked.Exchange(ref _rebuildDirty, 1);
            if (Interlocked.CompareExchange(ref _rebuildInFlight, 1, 0) != 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    // ponytail: dirty 플래그 병합 — 재구성 진행 중에 이벤트가 또 오면 한 번만 다시
                    // 돈다. 루프 종료/해제의 잔여 레이스는 무해하다: 1초 WMI 디바운스가 실제 핫플러그
                    // 이벤트를 수 초 간격으로 벌리고, 다음 플러그가 어차피 재구성을 다시 유발한다.
                    while (Interlocked.Exchange(ref _rebuildDirty, 0) == 1)
                    {
                        IReadOnlyList<CameraComPair>? pairs = _pairing is not null && _config?.SimulationMode == false
                            ? GetPairsOrEmpty(_pairing) : null;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (_config is not null && pairs is not null)
                            {
                                _lastPairs = pairs;
                                lock (_config.Cameras)
                                {
                                    CameraAutoRegistrar.DedupeByAgentId(_config.Cameras);
                                    AutoRegisterDetectedCameras(_config, pairs);
                                    AutoRegisterVideoOnlyCameras(_config);
                                    ReconcileSerialPortsFromPairing(_config, pairs);
                                    ReconcileVideoIndicesFromEnumeration(_config);
                                    _config.Save();
                                }
                            }
                            RebuildCameraPanels();
                        }).Task.ConfigureAwait(false);
                        if (_natsConnector is not null)
                        {
                            await _natsConnector.SyncSubscriptionsAsync().ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AgentUiLog.Logger.Warning(ex, "Camera hotplug reconcile failed");
                }
                finally
                {
                    Interlocked.Exchange(ref _rebuildInFlight, 0);
                    if (Volatile.Read(ref _rebuildDirty) != 0 && !_appLifetime.IsCancellationRequested)
                        OnCameraHotplug(change);
                }
            });
        }

        /// <summary>
        /// 종료 정리. 네이티브 카메라/시리얼 콜은 CLR abort 한계 너머로 wedge될 수 있으므로,
        /// 정리와 별개로 워치독이 6초 뒤 프로세스를 강제 종료해 OS가 카메라+COM을 해제하게 한다.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            _appLifetime.Cancel();
            _cameraWatcher?.StopWatching();
            _cameraWatcher?.Dispose();

            // 워치독: 아래 종료 정리가 네이티브 카메라(OpenCV DSHOW Read)/시리얼(SerialPort.Dispose)
            // 콜에 걸리면 CLR이 그 스레드를 abort할 수 없어 프로세스가 잔존한다. best-effort 정리가
            // wedge되면 OS가 카메라+COM을 해제하도록 강제 종료. 정상 종료 시엔 프로세스가 먼저 빠져나가
            // 이 백그라운드 타이머는 그냥 버려진다. (AgentUI 프로세스는 로그온 예약작업이 재기동 — S8)
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(6)).ConfigureAwait(false);
                Process.GetCurrentProcess().Kill();
            });

            try
            {
                // 종료 스텝을 UI 스레드 밖에서 총 5초 안에 실행 → 멈춘 시리얼 포트에서 hang해도
                // 프로세스가 빠져나가 Windows가 포트를 해제. 스텝 순서는 기존과 동일.
                var steps = new List<Func<Task>>();

                if (_natsConnector is CameraNatsConnector natsConnector)
                {
                    steps.Add(() => natsConnector.DisposeAsync().AsTask());
                }

                if (_mainViewModel is not null)
                {
                    foreach (CameraPanelViewModel panel in _mainViewModel.Cameras.ToList())
                    {
                        // 영상 종료: 셔터 닫기 + STOP (시리얼 포트 dispose 전에).
                        steps.Add(() => panel.StopLiveAsync());
                        // 시리얼 포트 닫기 — 멈춘 포트에서 hang 가능 → 반드시 timeout 안에서.
                        steps.Add(() => { panel.Dispose(); return Task.CompletedTask; });
                    }
                }

                if (_manager is CameraRuntimeManager manager)
                {
                    steps.Add(() => { manager.Dispose(); return Task.CompletedTask; });
                }
                if (_store is CaptureStore store)
                {
                    steps.Add(() => { store.Dispose(); return Task.CompletedTask; });
                }
                if (_nats is INatsCommunicationService nats)
                {
                    steps.Add(() => nats.DisposeAsync().AsTask());
                }

                if (!AppShutdown.Run(steps, TimeSpan.FromSeconds(5)))
                {
                    AgentUiLog.Logger.Warning("Shutdown exceeded {Timeout}s; forcing exit", 5);
                }
            }
            catch
            {
                // 종료 중 best-effort
            }

            _singleInstanceMutex?.Dispose();
            AgentUiLog.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
