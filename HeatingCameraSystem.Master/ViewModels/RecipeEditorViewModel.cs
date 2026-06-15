using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class RecipeStepModel : ObservableObject
    {
        [ObservableProperty]
        private int _stepNumber;

        [ObservableProperty]
        private string _nodeAssignment = string.Empty;

        [ObservableProperty]
        private float _blackbodyRef;
    }

    public partial class RecipeModel : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private string _lastModified = string.Empty;

        [ObservableProperty]
        private float _targetChamberTemp;

        [ObservableProperty]
        private float _targetChamberHumidity;

        [ObservableProperty]
        private bool _isSequentialMode = true;

        public ObservableCollection<RecipeStepModel> Steps { get; } = new ObservableCollection<RecipeStepModel>();
    }

    public partial class RecipeEditorViewModel : ObservableObject
    {
        public ObservableCollection<RecipeModel> Recipes { get; } = new ObservableCollection<RecipeModel>();

        [ObservableProperty]
        private RecipeModel? _selectedRecipe;

        public RecipeEditorViewModel()
        {
            // Dummy Data
            var recipeA = new RecipeModel { Name = "Recipe A - 45C", LastModified = "12m ago", TargetChamberTemp = 45.0f, TargetChamberHumidity = 40.0f };
            recipeA.Steps.Add(new RecipeStepModel { StepNumber = 1, NodeAssignment = "Position 12 -> CAM-12", BlackbodyRef = 45.0f });
            recipeA.Steps.Add(new RecipeStepModel { StepNumber = 2, NodeAssignment = "Position 08 -> CAM-08", BlackbodyRef = 45.2f });
            recipeA.Steps.Add(new RecipeStepModel { StepNumber = 3, NodeAssignment = "Position 24 -> CAM-24", BlackbodyRef = 44.9f });
            recipeA.Steps.Add(new RecipeStepModel { StepNumber = 4, NodeAssignment = "Position 15 -> CAM-15", BlackbodyRef = 45.0f });

            var recipeB = new RecipeModel { Name = "Recipe B - 60C", LastModified = "4h ago" };
            var recipeC = new RecipeModel { Name = "Recipe C - High Temp Sweep", LastModified = "2d ago" };

            Recipes.Add(recipeA);
            Recipes.Add(recipeB);
            Recipes.Add(recipeC);

            SelectRecipe(recipeA);
        }

        [RelayCommand]
        private void SelectRecipe(RecipeModel recipe)
        {
            if (SelectedRecipe != null)
                SelectedRecipe.IsSelected = false;
            
            SelectedRecipe = recipe;
            
            if (SelectedRecipe != null)
                SelectedRecipe.IsSelected = true;
        }

        [RelayCommand]
        private void AddStep()
        {
            if (SelectedRecipe != null)
            {
                int nextStepNum = SelectedRecipe.Steps.Count + 1;
                SelectedRecipe.Steps.Add(new RecipeStepModel { StepNumber = nextStepNum, NodeAssignment = "New Position -> CAM-XX", BlackbodyRef = 25.0f });
            }
        }

        [RelayCommand]
        private void DeleteStep(RecipeStepModel step)
        {
            if (SelectedRecipe != null && step != null)
            {
                SelectedRecipe.Steps.Remove(step);
                // Re-number steps
                for(int i = 0; i < SelectedRecipe.Steps.Count; i++)
                {
                    SelectedRecipe.Steps[i].StepNumber = i + 1;
                }
            }
        }

        [RelayCommand]
        private void MoveStep(Tuple<RecipeStepModel, RecipeStepModel> param)
        {
            if (param == null || SelectedRecipe == null) return;
            var source = param.Item1;
            var target = param.Item2;

            int oldIndex = SelectedRecipe.Steps.IndexOf(source);
            int newIndex = SelectedRecipe.Steps.IndexOf(target);

            if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
            {
                SelectedRecipe.Steps.Move(oldIndex, newIndex);
                
                // Re-number steps
                for (int i = 0; i < SelectedRecipe.Steps.Count; i++)
                {
                    SelectedRecipe.Steps[i].StepNumber = i + 1;
                }
            }
        }

        [RelayCommand]
        private void SetCaptureMode(string mode)
        {
            if (SelectedRecipe != null)
            {
                SelectedRecipe.IsSequentialMode = mode == "Sequential";
            }
        }
    }
}
