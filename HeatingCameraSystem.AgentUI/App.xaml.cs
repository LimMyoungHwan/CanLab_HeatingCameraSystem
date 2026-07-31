using System;
using System.Collections.Generic;
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
            foreach (CameraDescriptor cam in config.Cameras)
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

                ICameraSerialClient? serial = serialFactory(cam);
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

                var nuc = new ThermalNucCorrector();
                nucs[cam.AgentId] = nuc;

                var panel = new CameraPanelViewModel(cam.Alias, cam.AgentId, runtime, dispatcher, nuc, _store, config.CaptureBurstCount, serial);
                _mainViewModel.Cameras.Add(panel);

                // 영상 ON: 카메라 RUN + 셔터 열기 (기본 셔터 닫힘 → 흰 화면 방지). 카메라별 격리.
                if (serial is not null)
                {
                    _ = panel.StartLiveAsync();
                }
            }

            // Fire-and-forget: per-camera start failures are isolated inside the manager.
            _ = _manager.StartAllAsync();

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
                            return (true, "runtime unloaded");
                        }
                        if (op == CameraControlOps.RuntimeLoad)
                        {
                            _manager!.Remove(descriptor.AgentId);
                            ICameraRuntime runtime = _manager.Add(descriptor);
                            await runtime.StartAsync();
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

        protected override void OnExit(ExitEventArgs e)
        {
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
