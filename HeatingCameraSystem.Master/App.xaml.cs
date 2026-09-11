using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master;

/// <summary>
/// WPF 수명주기를 소유하는 진입점. 시작 시 앱 전역 예외 안전망을 서비스 초기화보다
/// 먼저 설치해, 히터를 제어 중일 수 있는 운영자 앱이 화면 오류로 종료되지 않게 한다.
/// </summary>
public partial class App : Application
{
    private BackgroundDataCleanupService? _cleanupService;

    /// <summary>
    /// true이면 종료가 끝난 뒤 자기 자신을 다시 띄운다. 하드웨어 구성(시뮬레이션 선택)이
    /// <see cref="AppServices.Initialize"/> 시점에만 결정되므로 재시작 외에 적용 방법이 없다.
    /// </summary>
    public static bool RestartRequested { get; set; }

    /// <summary>예외 안전망 설치 → AppServices 초기화·연결 시도 → 이력 정리 서비스 시작 순으로 부팅한다.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 전역 안전망: 화면·내비게이션 예외로 운영자 앱이 종료되어서는 안 된다 —
        // 히터를 제어하는 중일 수 있다. 기록하고 알린 뒤 앱은 계속 실행한다.
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

    /// <summary>UI 스레드 미처리 예외를 처리한다. 알람으로 기록하고 경고 창을 띄운 뒤 앱을 계속 실행한다.</summary>
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[App] Unhandled UI exception: {e.Exception}");
        try { AlarmSink.Raise(AlarmSeverity.Error, "UI", e.Exception.Message); } catch { /* 실패해도 무시 */ }
        try
        {
            MessageBox.Show(
                $"화면 처리 중 오류가 발생했습니다. 프로그램은 계속 실행됩니다.\n\n{e.Exception.Message}",
                "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { /* 실패해도 무시 */ }
        e.Handled = true;
    }

    /// <summary>
    /// 정리 서비스와 라이브 카메라를 내리고 AppServices를 해제한다. 비동기 해제를 최대 5초
    /// 동기 대기하는 부분은 알려진 기술 부채로, 명시적 작업 없이는 리팩터링하지 않는다.
    /// </summary>
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

        // 재실행은 반드시 여기 — AppServices.DisposeAsync가 끝난 뒤다. 그 전에 새 프로세스를 띄우면
        // 두 프로세스가 동시에 data.db(LiteDB)를 열어 파일 잠금 예외로 새 인스턴스가 죽는다.
        if (RestartRequested && Environment.ProcessPath is string exePath)
        {
            try { System.Diagnostics.Process.Start(exePath); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] Restart failed: {ex.Message}"); }
        }
    }
}

