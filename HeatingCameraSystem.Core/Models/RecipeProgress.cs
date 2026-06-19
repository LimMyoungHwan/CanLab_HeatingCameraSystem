namespace HeatingCameraSystem.Core.Models
{
    public class RecipeProgress
    {
        public int    CurrentStep  { get; set; }
        public int    TotalSteps   { get; set; }
        public string CurrentPhase { get; set; } = string.Empty;
    }
}
