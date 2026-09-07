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
using CommunityToolkit.Mvvm.Input;
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
                ? (d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource()))
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
                AutoRegisterDetectedCameras(config, pairs);
                AutoRegisterVideoOnlyCameras(config);
                ReconcileSerialPortsFromPairing(config, pairs);
                ReconcileVideoIndicesFromEnumeration(config);
            }

            RebuildCameraPanels();

            _nats = new NatsCommunicationService();
            _natsConnector = new CameraNatsConnector(_nats, _manager, _store, config.Cameras, config.HeartbeatSeconds, nucs, config.CaptureBurstCount,
                getConfigSnapshot: () => new AgentConfigSnapshot
                {
                    SimulationMode = config.SimulationMode,
                    NatsUrl = config.NatsUrl,
                    StoragePath = config.StoragePath,
                    HeartbeatSeconds = config.HeartbeatSeconds,
                    CaptureImageFormat = config.CaptureImageFormat,
                    CaptureBurstCount = config.CaptureBurstCount,
                    Cameras = config.Cameras
                },
                applyConfigSnapshot: snap =>
                {
                    config.SimulationMode = snap.SimulationMode;
                    config.NatsUrl = snap.NatsUrl;
                    config.StoragePath = snap.StoragePath;
                    config.HeartbeatSeconds = snap.HeartbeatSeconds;
                    config.CaptureImageFormat = snap.CaptureImageFormat;
                    config.CaptureBurstCount = snap.CaptureBurstCount;
                    config.Cameras = snap.Cameras ?? new List<CameraDescriptor>();
                    config.Save();
                },
                cameraControlHandler: async (descriptor, controlMessage) =>
                {
                    string op = controlMessage.Op;
                    try
                    {
                        // [S7] Manager발 카메라별 런타임 load/unload: 다른 카메라나 프로세스는
                        // 건드리지 않고 카메라 한 대의 UVC 핸들만 해제/재획득한다.
                        // runtimeLoad는 멱등 재적재다(낡은 것 제거, 재등록, 시작).
                        if (op == CameraControlOps.RuntimeUnload)
                        {
                            _manager!.Remove(descriptor.AgentId);
                            RebindPanelRuntime(descriptor.AgentId, null);
                            return (true, "runtime unloaded");
                        }
                        if (op == CameraControlOps.RuntimeLoad)
                        {
                            _manager!.Remove(descriptor.AgentId);
                            ICameraRuntime runtime = _manager.Add(descriptor);
                            await runtime.StartAsync();
                            RebindPanelRuntime(descriptor.AgentId, runtime);
                            return (true, "runtime loaded");
                        }

                        CameraPanelViewModel? panel = _mainViewModel?.Cameras
                            .FirstOrDefault(candidate => candidate.AgentId == descriptor.AgentId);
                        if (panel is null)
                        {
                            return (false, $"camera panel not found: {descriptor.AgentId}");
                        }

                        IAsyncRelayCommand? command = op switch
                        {
                            CameraControlOps.Run => panel.RunCameraCommand,
                            CameraControlOps.Stop => panel.StopCameraCommand,
                            CameraControlOps.ShutterOpen => panel.OpenShutterCommand,
                            CameraControlOps.ShutterClose => panel.CloseShutterCommand,
                            CameraControlOps.Capture => panel.CaptureSaveCommand,
                            CameraControlOps.Nuc => panel.RunNucCommand,
                            CameraControlOps.BiasLow => panel.RunBiasLowCommand,
                            CameraControlOps.BiasMid => panel.RunBiasMidCommand,
                            CameraControlOps.BiasHigh => panel.RunBiasHighCommand,
                            CameraControlOps.SaveConfig => panel.SaveConfigCommand,
                            CameraControlOps.RefreshInfo => panel.RefreshInfoCommand,
                            _ => null
                        };
                        if (command is null)
                        {
                            return (false, $"unknown camera control op: {op}");
                        }

                        // BIAS 명령만 목표 레벨을 인수로 받는다. 0이면 패널의 모드별 기본 목표를 쓴다.
                        object? parameter = controlMessage.BiasTargetLevel > 0
                            ? controlMessage.BiasTargetLevel
                            : null;

                        await dispatcher.InvokeAsync(() => command.ExecuteAsync(parameter)).Task.Unwrap();
                        return (true, "ok");
                    }
                    catch (Exception ex)
                    {
                        return (false, ex.Message);
                    }
                },
                // 패널이 핫플러그마다 재생성되므로 스냅샷이 아니라 매번 조회한다. 패널이 아직 없으면
                // 시리얼 제어가 없다는 뜻이므로 false — 영상 전용 구성은 지원 대상이 아니다.
                serialHealth: descriptor => _mainViewModel?.Cameras
                    .FirstOrDefault(panel => panel.AgentId == descriptor.AgentId)?.HasSerialControl ?? false,
                readCameraTemperature: ReadCameraTemperatureForAsync,
                readFpaRaw: ReadFpaRawForAsync,
                readBiasJson: descriptor => _mainViewModel?.Cameras
                    .FirstOrDefault(panel => panel.AgentId == descriptor.AgentId)?.LastBiasJson,
                productionSink: new ProductionCaptureSink(Path.Combine(storageDir, "production")));
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
            CameraPanelViewModel? panel = _mainViewModel?.Cameras
                .FirstOrDefault(candidate => candidate.AgentId == descriptor.AgentId);

            return panel is null ? Task.FromResult<short?>(null) : panel.ReadFpaTemperatureRawAsync();
        }

        private Task<double?> ReadCameraTemperatureForAsync(CameraDescriptor descriptor)
        {
            CameraPanelViewModel? panel = _mainViewModel?.Cameras
                .FirstOrDefault(candidate => candidate.AgentId == descriptor.AgentId);

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

            int added = CameraAutoRegistrar.Register(config.Cameras, pairs, Environment.MachineName);
            if (added > 0)
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

        private static void ReconcileSerialPortsFromPairing(AgentUiConfig config, IReadOnlyList<CameraComPair> pairs)
        {
            for (int i = 0; i < config.Cameras.Count; i++)
            {
                CameraDescriptor cam = config.Cameras[i];
                CameraComPair? pair = ResolveConfidentPair(pairs, cam);
                if (pair?.SerialPort is null)
                {
                    continue;
                }

                string newPort = pair.SerialPort.PortName;
                config.Cameras[i] = cam with
                {
                    SerialPortName = newPort,
                    CameraSerialNumber = IsUsableSerial(pair.CameraSerialNumber) ? pair.CameraSerialNumber : cam.CameraSerialNumber,
                    UsbContainerId = string.IsNullOrWhiteSpace(pair.Camera.UsbParentId) ? cam.UsbContainerId : pair.Camera.UsbParentId,
                };

                if (!string.Equals(cam.SerialPortName, newPort, StringComparison.OrdinalIgnoreCase))
                {
                    AgentUiLog.Logger.Information(
                        "Camera {AgentId}: serial {Old} -> {New} (matched by pairing)",
                        cam.AgentId, cam.SerialPortName ?? "(none)", newPort);
                }
            }
        }

        private void ReconcileVideoIndicesFromEnumeration(AgentUiConfig config)
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
                AgentUiLog.Logger.Warning(ex, "Video device enumeration failed; keeping configured OpenCvIndex");
                return;
            }

            int changed = VideoIndexReconciler.Reconcile(config.Cameras, devices);
            if (changed > 0)
            {
                AgentUiLog.Logger.Information("Rebound {Count} camera OpenCvIndex value(s) by ContainerId", changed);
            }
        }

        /// <summary>
        /// 설정된 카메라와 감지된 페어를 확신 가능한 키로만 매칭한다: 고유 카메라 S/N 일치 →
        /// UsbContainerId 일치. 애매하면 null(잘못된 포트를 배정하느니 건드리지 않는다).
        /// </summary>
        private static CameraComPair? ResolveConfidentPair(IReadOnlyList<CameraComPair> pairs, CameraDescriptor cam)
        {
            if (IsUsableSerial(cam.CameraSerialNumber))
            {
                var bySerial = pairs
                    .Where(p => string.Equals(p.CameraSerialNumber, cam.CameraSerialNumber, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (bySerial.Count == 1)
                {
                    return bySerial[0];
                }
            }

            if (!string.IsNullOrWhiteSpace(cam.UsbContainerId))
            {
                return pairs.FirstOrDefault(
                    p => string.Equals(p.Camera.UsbParentId, cam.UsbContainerId, StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        // 비었거나 0뿐인 S/N은 미기록 테스트 카메라 — 식별 키로 쓰지 않는다.
        private static bool IsUsableSerial([NotNullWhen(true)] string? serial) =>
            !string.IsNullOrWhiteSpace(serial) && serial.Any(c => c is >= '1' and <= '9');

        /// <summary>
        /// 설정된 카메라 전체의 패널을 처음부터 다시 만든다. 기존 패널을 전부 Dispose하고 런타임을
        /// 재등록하므로 핫플러그 재구성 때마다 카메라 패널이 재생성된다. 시리얼 포트 열기에 실패하면
        /// serial을 null로 두고 영상은 계속 살린다(영상-시리얼 분리).
        /// </summary>
        private void RebuildCameraPanels()
        {
            if (_manager is null || _mainViewModel is null || _config is null || _serialFactory is null || _nucs is null || _store is null)
            {
                return;
            }

            foreach (CameraPanelViewModel existing in _mainViewModel.Cameras.ToList())
            {
                existing.Dispose();
            }
            _mainViewModel.Cameras.Clear();

            foreach (CameraDescriptor cam in _config.Cameras)
            {
                ICameraRuntime runtime = _manager.Add(cam);
                string agentId = cam.AgentId;
                int cameraIndex = cam.OpenCvIndex;
                runtime.StatusChanged += (_, status) =>
                {
                    if (status == CameraRuntimeStatus.Faulted)
                    {
                        AgentUiLog.Logger.Error("Camera {AgentId} (index {Index}) faulted", agentId, cameraIndex);
                    }
                };

                ICameraSerialClient? serial = _serialFactory(cam);
                string? serialStatus = null;
                if (serial is not null)
                {
                    try
                    {
                        // (동기) 포트 열기를 지금 완료해 고장난 포트를 여기서 잡아 serial을 null로
                        // 만든다 — 영상 출력이 시리얼 성공에 좌우되는 일은 절대 없어야 한다.
                        serial.InitializeAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        AgentUiLog.Logger.Warning(ex, "Camera {AgentId} serial {Port} open failed", agentId, cam.SerialPortName);
                        serial.Dispose();
                        serial = null;
                        serialStatus = $"시리얼 제어 비활성화: {cam.SerialPortName} 열기 실패 ({ex.Message}). 영상은 계속 동작합니다.";
                    }
                }
                else if (string.IsNullOrWhiteSpace(cam.SerialPortName))
                {
                    serialStatus = "시리얼 포트 없음: 영상만 동작 (제어 비활성화)";
                }

                ThermalNucCorrector nuc = _nucs.TryGetValue(cam.AgentId, out ThermalNucCorrector? existingNuc)
                    ? existingNuc
                    : new ThermalNucCorrector();
                _nucs[cam.AgentId] = nuc;

                var panel = new CameraPanelViewModel(cam.Alias, cam.AgentId, runtime, Dispatcher, nuc, _store, _config.CaptureBurstCount, serial,
                    publishResult: msg => _nats is { } n ? n.PublishCaptureResultAsync(msg) : Task.CompletedTask);
                if (serialStatus is not null)
                {
                    panel.SerialStatus = serialStatus;
                }
                _mainViewModel.Cameras.Add(panel);

                // 라이브 시작은 항상 시도한다: 시리얼 RUN/셔터는 best-effort(serial이 null이면 no-op)라
                // 영상 출력은 시리얼과 분리된다. 영상 런타임 자체는 StartAllAsync에서 시작된다.
                _ = panel.StartLiveAsync();
            }

            _ = _manager.StartAllAsync();
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

        // [S7] 카메라별 runtimeLoad/Unload 후 기존 패널을 새 영상 런타임으로 향하게 하거나(unload면
        // 해제) 라이브 뷰가 낡은 핸들에 얼어붙지 않고 재적재된 핸들을 따르게 한다. UI 스레드로
        // 마샬링되며 패널의 시리얼 클라이언트 + NUC는 건드리지 않는다.
        private void RebindPanelRuntime(string agentId, ICameraRuntime? runtime)
        {
            CameraPanelViewModel? panel = _mainViewModel?.Cameras
                .FirstOrDefault(candidate => candidate.AgentId == agentId);
            if (panel is null)
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(() => panel.RebindRuntime(runtime));
        }

        /// <summary>
        /// USB 핫플러그 콜백. dirty 플래그 병합 루프로 재구성을 직렬화한다: 재구성 도중 도착한
        /// 이벤트는 플래그만 세우고, 진행 중인 루프가 끝나기 전에 한 번 더 돌아 반영된다.
        /// 재구성은 페어링 reconcile → 패널 재생성 → NATS 구독 동기화 순으로 진행된다.
        /// </summary>
        private void OnCameraHotplug(PnpChange change)
        {
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
                        if (_config is not null && !_config.SimulationMode && _pairing is not null)
                        {
                            IReadOnlyList<CameraComPair> pairs = GetPairsOrEmpty(_pairing);
                            AutoRegisterDetectedCameras(_config, pairs);
                            AutoRegisterVideoOnlyCameras(_config);
                            ReconcileSerialPortsFromPairing(_config, pairs);
                            ReconcileVideoIndicesFromEnumeration(_config);
                        }

                        await Dispatcher.InvokeAsync(RebuildCameraPanels).Task.ConfigureAwait(false);
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
                }
            });
        }

        /// <summary>
        /// 종료 정리. 네이티브 카메라/시리얼 콜은 CLR abort 한계 너머로 wedge될 수 있으므로,
        /// 정리와 별개로 워치독이 6초 뒤 프로세스를 강제 종료해 OS가 카메라+COM을 해제하게 한다.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
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
