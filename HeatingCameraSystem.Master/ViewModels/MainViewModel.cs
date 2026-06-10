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
            // CurrentViewModel = new RecipeEditorViewModel(); // To be implemented
        }
    }
}
