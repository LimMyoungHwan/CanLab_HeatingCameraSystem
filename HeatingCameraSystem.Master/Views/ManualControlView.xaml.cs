using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.ViewModels;

namespace HeatingCameraSystem.Master.Views
{
    public partial class ManualControlView : UserControl
    {
        public ManualControlView() => InitializeComponent();

        private ManualControlViewModel? Vm => DataContext as ManualControlViewModel;

        private async void JogDown(object sender, MouseButtonEventArgs e)
        {
            if (Vm == null || sender is not FrameworkElement fe || fe.Tag is not string tag) return;
            var (axis, positive) = ParseJog(tag);
            await Vm.Jog(axis, positive, true);
        }

        private async void JogUp(object sender, MouseButtonEventArgs e)
        {
            if (Vm == null || sender is not FrameworkElement fe || fe.Tag is not string tag) return;
            var (axis, positive) = ParseJog(tag);
            await Vm.Jog(axis, positive, false);
        }

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
