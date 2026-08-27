using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.ViewModels;

namespace HeatingCameraSystem.Master.Views
{
    /// <summary>
    /// 수동 제어 화면의 코드 비하인드. 마우스를 누르고 있는 동안만 움직여야 하는
    /// JOG 버튼의 press/release 이벤트 배선만 담당하고, 실제 이동은 ManualControlViewModel에 위임한다.
    /// </summary>
    public partial class ManualControlView : UserControl
    {
        public ManualControlView() => InitializeComponent();

        private ManualControlViewModel? Vm => DataContext as ManualControlViewModel;

        /// <summary>마우스를 누르면 Tag가 가리키는 축·방향으로 JOG 시작을 ViewModel에 알린다.</summary>
        private async void JogDown(object sender, MouseButtonEventArgs e)
        {
            if (Vm == null || sender is not FrameworkElement fe || fe.Tag is not string tag) return;
            var (axis, positive) = ParseJog(tag);
            await Vm.Jog(axis, positive, true);
        }

        /// <summary>마우스를 떼면 JOG 종료를 ViewModel에 알린다.</summary>
        private async void JogUp(object sender, MouseButtonEventArgs e)
        {
            if (Vm == null || sender is not FrameworkElement fe || fe.Tag is not string tag) return;
            var (axis, positive) = ParseJog(tag);
            await Vm.Jog(axis, positive, false);
        }

        /// <summary>"X+" 같은 Tag 문자열을 축·방향으로 푼다. 모르는 값은 (X, +)로 처리한다.</summary>
        private static (ServoAxis Axis, bool Positive) ParseJog(string tag) => tag switch
        {
            "X+" => (ServoAxis.X, true),
            "X-" => (ServoAxis.X, false),
            "Y+" => (ServoAxis.Y, true),
            "Y-" => (ServoAxis.Y, false),
            _ => (ServoAxis.X, true)
        };
    }
}
