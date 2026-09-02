using System.Windows;
using HeatingCameraSystem.Master.ViewModels;

namespace HeatingCameraSystem.Master.Views
{
    public partial class AlarmWindow : Window
    {
        private readonly AlarmWindowViewModel _viewModel = new();

        public AlarmWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
            Closed += (_, _) => _viewModel.Dispose();
        }
    }
}
