using System;
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
    public partial class App : Application
    {
        // Session-scoped single-instance guard: prevents autostart + Manager relaunch
        // (scheduled task) from double-launching AgentUI in the same operator session.
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

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                // Another AgentUI instance already owns this session.
                Shutdown();
                return;
            }

            base.OnStartup(e);

            AgentUiLog.Initialize();

            // [S8] Headless deploy mode: run cameras + NATS with no window. WPF would exit on
            // last-window-close, so switch to explicit shutdown before skipping the MainWindow.
            bool headless = e.Args.Any(a => a.Equals("--headless", StringComparison.OrdinalIgnoreCase));
            if (headless)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            AgentUiConfig config = AgentUiConfig.LoadOrCreate();

            if (!config.SimulationMode)
            {
                // Namespace each AgentId by host so N PCs' "Agent_1"s don't collide on shared NATS topics.
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
                ReconcileSerialPortsFromPairing(config, pairing);
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
                cameraControlHandler: async (descriptor, op) =>
                {
                    try
                    {
                        // [S7] Per-camera runtime load/unload from the Manager: release or
                        // re-acquire ONE camera's UVC handle without touching the others or the
                        // process. runtimeLoad is an idempotent reload (drop stale, re-add, start).
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
                            CameraControlOps.SaveConfig => panel.SaveConfigCommand,
                            CameraControlOps.RefreshInfo => panel.RefreshInfoCommand,
                            _ => null
                        };
                        if (command is null)
                        {
                            return (false, $"unknown camera control op: {op}");
                        }

                        await dispatcher.InvokeAsync(() => command.ExecuteAsync(null)).Task.Unwrap();
                        return (true, "ok");
                    }
                    catch (Exception ex)
                    {
                        return (false, ex.Message);
                    }
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
            _mainViewModel.Settings = new SettingsViewModel(config, pairing);

            var window = new MainWindow { DataContext = _mainViewModel };
            MainWindow = window;
            window.Show();
        }

        private static void ReconcileSerialPortsFromPairing(AgentUiConfig config, ICameraComPairingService pairing)
        {
            IReadOnlyList<CameraComPair> pairs;
            try
            {
                // ponytail: blocks the UI thread on serial S/N reads (~sub-second per camera).
                // Fine for a bench launch; if 8-camera startup drags, hoist to an async post-show reconcile.
                pairs = Task.Run(() => pairing.GetPairsAsync()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AgentUiLog.Logger.Warning(ex, "Startup serial pairing failed; keeping configured COM ports");
                return;
            }

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

        private static bool IsUsableSerial([NotNullWhen(true)] string? serial) =>
            !string.IsNullOrWhiteSpace(serial) && serial.Any(c => c is >= '1' and <= '9');

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
                if (serial is not null)
                {
                    try
                    {
                        _ = serial.InitializeAsync();
                    }
                    catch (Exception ex)
                    {
                        AgentUiLog.Logger.Warning(ex, "Camera {AgentId} serial {Port} open failed", agentId, cam.SerialPortName);
                        serial.Dispose();
                        serial = null;
                    }
                }

                ThermalNucCorrector nuc = _nucs.TryGetValue(cam.AgentId, out ThermalNucCorrector? existingNuc)
                    ? existingNuc
                    : new ThermalNucCorrector();
                _nucs[cam.AgentId] = nuc;

                var panel = new CameraPanelViewModel(cam.Alias, cam.AgentId, runtime, Dispatcher, nuc, _store, _config.CaptureBurstCount, serial,
                    publishResult: msg => _nats is { } n ? n.PublishCaptureResultAsync(msg) : Task.CompletedTask);
                _mainViewModel.Cameras.Add(panel);

                if (serial is not null)
                {
                    _ = panel.StartLiveAsync();
                }
            }

            _ = _manager.StartAllAsync();
        }

        // [S7] After a per-camera runtimeLoad/Unload, point the existing panel at the new video runtime
        // (or clear it on unload) so its live view follows the reloaded handle instead of freezing on the
        // stale one. Marshalled to the UI thread; the panel's serial client + NUC are untouched.
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
                    // ponytail: dirty-flag coalescing — a rebuild in flight re-runs once if another event
                    // landed. Residual loop-exit/release race is harmless: 1s WMI debounce spaces real
                    // hotplug events seconds apart, and the next plug re-triggers a rebuild anyway.
                    while (Interlocked.Exchange(ref _rebuildDirty, 0) == 1)
                    {
                        if (_config is not null && !_config.SimulationMode && _pairing is not null)
                        {
                            ReconcileSerialPortsFromPairing(_config, _pairing);
                            ReconcileVideoIndicesFromEnumeration(_config);
                        }

                        await Dispatcher.InvokeAsync(RebuildCameraPanels).Task.ConfigureAwait(false);
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
                // best effort during shutdown
            }

            _singleInstanceMutex?.Dispose();
            AgentUiLog.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
