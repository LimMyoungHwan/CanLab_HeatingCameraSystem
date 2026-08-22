using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master;

public partial class App : Application
{
    private BackgroundDataCleanupService? _cleanupService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global safety net: a screen/navigation exception must NOT terminate the operator app —
        // it may be actively controlling a heater. Log + surface it and keep the app running.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            System.Diagnostics.Debug.WriteLine($"[App] Unhandled non-UI exception: {(args.ExceptionObject as Exception)?.Message}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"[App] Unobserved task exception: {args.Exception.Message}");
            args.SetObserved();
        };

        AppServices.Initialize();
        _ = AppServices.TryConnectServicesAsync();

        _cleanupService = new BackgroundDataCleanupService(
            AppServices.HistoryRepo, AppServices.ChamberHistoryRepo, AppServices.ImageCacheDir, retentionDays: AppServices.Settings.DataRetentionDays);
        _cleanupService.Start();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[App] Unhandled UI exception: {e.Exception}");
        try { AlarmSink.Raise(AlarmSeverity.Error, "UI", e.Exception.Message); } catch { /* best effort */ }
        try
        {
            MessageBox.Show(
                $"화면 처리 중 오류가 발생했습니다. 프로그램은 계속 실행됩니다.\n\n{e.Exception.Message}",
                "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { /* best effort */ }
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cleanupService?.Stop();
        AppServices.LiveThermalCamera?.Dispose();
        try
        {
            var disposeTask = Task.Run(async () => await AppServices.DisposeAsync());
            if (!disposeTask.Wait(TimeSpan.FromSeconds(5)))
            {
                System.Diagnostics.Debug.WriteLine("[App] AppServices.DisposeAsync timed out after 5s — forcing exit.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] AppServices.DisposeAsync threw: {ex.GetType().Name}: {ex.Message}");
        }
        base.OnExit(e);
    }
}

