using System;
using System.ComponentModel;
using System.Windows;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master;

/// <summary>닫기 전에 진행 작업을 취소하고 원격 abort가 전송될 동안 UI와 NATS를 유지한다.</summary>
public partial class MainWindow : Window
{
    private bool _closing;
    private bool _closeReady;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeReady) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            (DataContext as IDisposable)?.Dispose();
            await AppServices.CancelActiveOperationsAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            _closeReady = true;
            Close();
        }
    }
}