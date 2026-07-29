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

        private readonly DashboardViewModel _dashboardViewModel = new();

        public MainViewModel()
        {
            // Default View
            NavigateToDashboard();
        }

        [RelayCommand]
        private void NavigateToDashboard()
        {
            CurrentViewTitle = "대시보드 (Dashboard)";
            CurrentViewModel = _dashboardViewModel;
        }

        [RelayCommand]
        private void NavigateToRecipeEditor()
        {
            CurrentViewTitle = "레시피 편집기 (Recipe Editor)";
            CurrentViewModel = new RecipeEditorViewModel();
        }

        [RelayCommand]
        private void NavigateToHistory()
        {
            CurrentViewTitle = "이력 조회 (History Logs)";
            CurrentViewModel = new HistoryViewModel();
        }

        [RelayCommand]
        private void NavigateToAgentSettings()
        {
            CurrentViewTitle = "Agent 원격 설정 (Agent Settings)";
            CurrentViewModel = new AgentSettingsViewModel();
        }

        [RelayCommand]
        private void NavigateToStatusMonitor()
        {
            CurrentViewTitle = "PLC 상태 (Status)";
            CurrentViewModel = new StatusMonitorViewModel();
        }

        [RelayCommand]
        private void NavigateToPlcControlSettings()
        {
            CurrentViewTitle = "PLC 설정 (Control Settings)";
            CurrentViewModel = new PlcControlSettingsViewModel();
        }

        [RelayCommand]
        private void NavigateToManualControl()
        {
            CurrentViewTitle = "수동 조작 (Manual Control)";
            CurrentViewModel = new ManualControlViewModel();
        }
    }
}
