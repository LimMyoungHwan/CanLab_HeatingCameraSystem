using System;
using System.IO;
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

        string imageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeatingCameraSystem", "ImageStorage");
        _cleanupService = new BackgroundDataCleanupService(
            AppServices.HistoryRepo, imageDir, retentionDays: 30);
        _cleanupService.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cleanupService?.Stop();
        AppServices.DisposeAsync().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}

