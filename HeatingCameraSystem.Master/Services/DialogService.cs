using System;
using System.Windows;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>운영자용 알림 팝업 seam. 테스트에서는 페이크/모의로 대체한다(headless에서 실제 MessageBox 미표시).</summary>
    public interface IDialogService
    {
        void ShowError(string title, string message);
    }

    /// <summary>WPF MessageBox 구현. UI 스레드에서 표시하되 호출자(PLC 폴링 스레드)를 막지 않도록 BeginInvoke로 큐잉.</summary>
    public sealed class MessageBoxDialogService : IDialogService
    {
        public void ShowError(string title, string message)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                Show(title, message);
            else
                dispatcher.BeginInvoke(new Action(() => Show(title, message)));
        }

        private static void Show(string title, string message)
            => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
