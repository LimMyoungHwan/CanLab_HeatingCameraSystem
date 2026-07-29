using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Master.Localization;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _currentViewTitle = string.Empty;

        [ObservableProperty]
        private object? _currentViewModel;

        private readonly DashboardViewModel _dashboardViewModel = new();
        private string _currentTitleKey = "Title_Dashboard";

        public PlcStatusService? PlcStatus => AppServices.PlcStatus;

        public MainViewModel()
        {
            LocalizationManager.Instance.PropertyChanged += (_, _) => UpdateTitle();
            NavigateToDashboard();
        }

        private void UpdateTitle() => CurrentViewTitle = LocalizationManager.Instance[_currentTitleKey];

        [RelayCommand]
        private void NavigateToDashboard()
        {
            _currentTitleKey = "Title_Dashboard";
            CurrentViewModel = _dashboardViewModel;
            UpdateTitle();
        }

        [RelayCommand]
        private void NavigateToRecipeEditor()
        {
            _currentTitleKey = "Title_RecipeEditor";
            CurrentViewModel = new RecipeEditorViewModel();
            UpdateTitle();
        }

        [RelayCommand]
        private void NavigateToHistory()
        {
            _currentTitleKey = "Title_History";
            CurrentViewModel = new HistoryViewModel();
            UpdateTitle();
        }

        [RelayCommand]
        private void NavigateToAgentSettings()
        {
            _currentTitleKey = "Title_AgentSettings";
            CurrentViewModel = new AgentSettingsViewModel();
            UpdateTitle();
        }

        [RelayCommand]
        private void NavigateToStatusMonitor()
        {
            _currentTitleKey = "Title_PlcStatus";
            CurrentViewModel = new StatusMonitorViewModel();
            UpdateTitle();
        }

        [RelayCommand]
        private void NavigateToPlcControlSettings()
        {
            _currentTitleKey = "Title_PlcSettings";
            CurrentViewModel = new PlcControlSettingsViewModel();
            UpdateTitle();
        }

        [RelayCommand]
        private void NavigateToManualControl()
        {
            _currentTitleKey = "Title_ManualControl";
            CurrentViewModel = new ManualControlViewModel();
            UpdateTitle();
        }
    }
}
