using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty]
        private float _currentTemperature;

        [ObservableProperty]
        private float _currentHumidity;

        [ObservableProperty]
        private string _recipeStatus = "대기 중";

        // 카메라 영상 뷰모델 목록 (간단히 이름만 표시하는 문자열 리스트로 임시 구현)
        public ObservableCollection<string> CameraFeeds { get; } = new ObservableCollection<string>();

        public DashboardViewModel()
        {
            // Initialize dummy camera feeds
            for (int i = 1; i <= 8; i++) // 분할 뷰 예시용 8개
            {
                CameraFeeds.Add($"Camera {i} Feed");
            }
        }

        [RelayCommand]
        private void StartRecipe()
        {
            RecipeStatus = "실행 중...";
            // 실제 RecipeEngine 호출 로직 연결 예정
        }

        [RelayCommand]
        private void StopRecipe()
        {
            RecipeStatus = "정지됨";
            // 정지 로직 연결 예정
        }
    }
}
