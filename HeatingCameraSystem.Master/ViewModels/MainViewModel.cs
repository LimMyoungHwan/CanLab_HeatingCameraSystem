using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _currentViewTitle = "대시보드 (Dashboard)";

        [ObservableProperty]
        private object? _currentViewModel;

        public MainViewModel()
        {
            // Default View
            NavigateToDashboard();
        }

        [RelayCommand]
        private void NavigateToDashboard()
        {
            CurrentViewTitle = "대시보드 (Dashboard)";
            CurrentViewModel = new DashboardViewModel();
        }

        [RelayCommand]
        private void NavigateToRecipeEditor()
        {
            CurrentViewTitle = "레시피 편집기 (Recipe Editor)";
            CurrentViewModel = new RecipeEditorViewModel();
        }

        [RelayCommand]
        private void NavigateToCameraMapping()
        {
            CurrentViewTitle = "카메라 맵핑 (Camera Mapping)";
            CurrentViewModel = new CameraMappingViewModel();
        }

        [RelayCommand]
        private void NavigateToHistory()
        {
            CurrentViewTitle = "이력 조회 (History Logs)";
            CurrentViewModel = new HistoryViewModel();
        }

        [RelayCommand]
        private void NavigateToSettings()
        {
            CurrentViewTitle = "시리얼 설정 (Serial Settings)";
            CurrentViewModel = new SettingsViewModel();
        }

        [RelayCommand]
        private void NavigateToDevices()
        {
            CurrentViewTitle = "디바이스 관리 (Devices)";
            CurrentViewModel = new DevicesViewModel();
        }
    }
}
