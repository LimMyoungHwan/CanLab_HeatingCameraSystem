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

        AppServices.Initialize();
        _ = AppServices.TryConnectServicesAsync();

        _cleanupService = new BackgroundDataCleanupService(
            AppServices.HistoryRepo, AppServices.ImageCacheDir, retentionDays: AppServices.Settings.DataRetentionDays);
        _cleanupService.Start();
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

