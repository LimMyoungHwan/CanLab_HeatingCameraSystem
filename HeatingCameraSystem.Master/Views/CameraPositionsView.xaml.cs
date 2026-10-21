using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HeatingCameraSystem.Master.ViewModels;

namespace HeatingCameraSystem.Master.Views
{
    /// <summary>
    /// 카메라 위치표 화면. DataContext가 지정되지 않으면 <see cref="CameraPositionsViewModel"/>을 만들어 불러온다
    /// (호스트가 자기 VM을 주입했다면 그대로 쓴다). PLC 연결·레시피 실행 상태는 보이는 동안 1초마다 다시 읽는다.
    /// </summary>
    public partial class CameraPositionsView : UserControl
    {
        private readonly DispatcherTimer _availabilityTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        public CameraPositionsView()
        {
            InitializeComponent();
            _availabilityTimer.Tick += (_, _) => (DataContext as CameraPositionsViewModel)?.RefreshAvailability();
            Loaded += OnLoaded;
            Unloaded += (_, _) => _availabilityTimer.Stop();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not CameraPositionsViewModel vm)
            {
                vm = new CameraPositionsViewModel();
                DataContext = vm;
            }

            vm.RefreshAvailability();
            _availabilityTimer.Start();
            await vm.LoadAsync();
        }
    }
}
