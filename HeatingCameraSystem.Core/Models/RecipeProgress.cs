namespace HeatingCameraSystem.Core.Models
{
    /// <summary>현재 실행 중인 레시피의 진행 상황.</summary>
    public class RecipeProgress
    {
        /// <summary>현재 스텝 번호(1부터 시작).</summary>
        public int CurrentStep { get; set; }

        /// <summary>전체 스텝 수.</summary>
        public int TotalSteps { get; set; }

        /// <summary>현재 단계를 나타내는 표시 문자열.</summary>
        public string CurrentPhase { get; set; } = string.Empty;
    }
}
