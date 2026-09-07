using System;
using System.Windows;
using HeatingCameraSystem.Master.Localization;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>레시피 시작 전 운영자가 지정하는 저장 위치와 제품 번호.</summary>
    public sealed record ProductionRunInput(string SaveRootPath, string ProductNumber);

    /// <summary>운영자용 알림 팝업 seam. 테스트에서는 페이크/모의로 대체한다(headless에서 실제 MessageBox 미표시).</summary>
    public interface IDialogService
    {
        void ShowError(string title, string message);

        /// <summary>저장 폴더·제품 번호를 묻는다. 운영자가 취소하면 null이며, 이때 레시피는 시작하지 않는다.</summary>
        ProductionRunInput? PromptProductionRun(string initialPath, string initialProductNumber);

        /// <summary>
        /// 촬영 중단 지시에 응답이 없을 때 운영자 판단을 받는다.
        /// 물어볼 수 없는 환경(headless 등)이면 <see cref="RecipeEngine.CaptureAbortDecision.Stop"/>을 돌려준다.
        /// </summary>
        RecipeEngine.CaptureAbortDecision AskCaptureAbortDecision(string agentId);
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

        public ProductionRunInput? PromptProductionRun(string initialPath, string initialProductNumber)
            => OnUi(() =>
            {
                var dialog = new Views.ProductionRunDialog(initialPath, initialProductNumber)
                {
                    Owner = Application.Current?.MainWindow
                };

                return dialog.ShowDialog() == true
                    ? new ProductionRunInput(dialog.SaveRootPath, dialog.ProductNumber)
                    : null;
            });

        public RecipeEngine.CaptureAbortDecision AskCaptureAbortDecision(string agentId)
            => OnUi(() =>
            {
                MessageBoxResult answer = MessageBox.Show(
                    LocalizationManager.Instance["Dialog_AbortNoAck_Message"].Replace("{0}", agentId),
                    LocalizationManager.Instance["Dialog_AbortNoAck_Title"],
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                return answer switch
                {
                    MessageBoxResult.Yes => RecipeEngine.CaptureAbortDecision.Continue,
                    MessageBoxResult.Cancel => RecipeEngine.CaptureAbortDecision.Retry,
                    _ => RecipeEngine.CaptureAbortDecision.Stop
                };
            });

        // 레시피 엔진은 UI가 아닌 스레드에서 돈다. 결과를 돌려줘야 하므로 BeginInvoke가 아니라 Invoke다.
        private static T OnUi<T>(Func<T> action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            return dispatcher is null || dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
        }
    }
}
