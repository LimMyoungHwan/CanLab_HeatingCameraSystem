using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class RecipeStepModel : ObservableObject
    {
        [ObservableProperty] private int _stepNumber;
        [ObservableProperty] private string _nodeAssignment = string.Empty;
        [ObservableProperty] private float _blackbodyRef;

        public int CameraIndex { get; set; }
        public int TargetPositionIndex { get; set; }
    }

    public partial class RecipeModel : ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private string _lastModified = string.Empty;
        [ObservableProperty] private float _targetChamberTemp;
        [ObservableProperty] private float _targetChamberHumidity;
        [ObservableProperty] private bool _isSequentialMode = true;

        public ObservableCollection<RecipeStepModel> Steps { get; } = new();
    }

    public partial class RecipeEditorViewModel : ObservableObject
    {
        public ObservableCollection<RecipeModel> Recipes { get; } = new ObservableCollection<RecipeModel>();

        [ObservableProperty]
        private RecipeModel? _selectedRecipe;

        public RecipeEditorViewModel()
        {
            foreach (var r in AppServices.RecipeRepo.GetAllAsync().GetAwaiter().GetResult())
                Recipes.Add(FromDomain(r));

            if (Recipes.Count > 0)
                SelectRecipe(Recipes[0]);
        }

        [RelayCommand]
        private void SelectRecipe(RecipeModel recipe)
        {
            if (SelectedRecipe != null) SelectedRecipe.IsSelected = false;
            SelectedRecipe = recipe;
            if (SelectedRecipe != null) SelectedRecipe.IsSelected = true;
        }

        [RelayCommand]
        private void AddRecipe()
        {
            var vm = new RecipeModel { Name = "New Recipe", LastModified = DateTime.Now.ToString("g"), TargetChamberTemp = 25.0f, TargetChamberHumidity = 50.0f };
            Recipes.Add(vm);
            AppServices.RecipeRepo.SaveAsync(ToDomain(vm)).GetAwaiter().GetResult();
            SelectRecipe(vm);
        }

        [RelayCommand]
        private void SaveRecipe()
        {
            if (SelectedRecipe == null) return;
            SelectedRecipe.LastModified = DateTime.Now.ToString("g");
            AppServices.RecipeRepo.SaveAsync(ToDomain(SelectedRecipe)).GetAwaiter().GetResult();
        }

        [RelayCommand]
        private void DeleteRecipe(RecipeModel recipe)
        {
            if (recipe == null) return;
            AppServices.RecipeRepo.DeleteAsync(recipe.Id).GetAwaiter().GetResult();
            Recipes.Remove(recipe);
            if (SelectedRecipe == recipe)
                SelectedRecipe = Recipes.FirstOrDefault();
        }

        [RelayCommand]
        private void AddStep()
        {
            if (SelectedRecipe == null) return;
            int n = SelectedRecipe.Steps.Count + 1;
            SelectedRecipe.Steps.Add(new RecipeStepModel
            {
                StepNumber = n,
                NodeAssignment = $"Position {n:D2} -> CAM-{n:D2}",
                CameraIndex = n,
                TargetPositionIndex = n,
                BlackbodyRef = 25.0f
            });
        }

        [RelayCommand]
        private void DeleteStep(RecipeStepModel step)
        {
            if (SelectedRecipe == null || step == null) return;
            SelectedRecipe.Steps.Remove(step);
            for (int i = 0; i < SelectedRecipe.Steps.Count; i++)
                SelectedRecipe.Steps[i].StepNumber = i + 1;
        }

        [RelayCommand]
        private void MoveStep(Tuple<RecipeStepModel, RecipeStepModel> param)
        {
            if (param == null || SelectedRecipe == null) return;
            int oldIdx = SelectedRecipe.Steps.IndexOf(param.Item1);
            int newIdx = SelectedRecipe.Steps.IndexOf(param.Item2);
            if (oldIdx >= 0 && newIdx >= 0 && oldIdx != newIdx)
            {
                SelectedRecipe.Steps.Move(oldIdx, newIdx);
                for (int i = 0; i < SelectedRecipe.Steps.Count; i++)
                    SelectedRecipe.Steps[i].StepNumber = i + 1;
            }
        }

        [RelayCommand]
        private void SetCaptureMode(string mode)
        {
            if (SelectedRecipe != null)
                SelectedRecipe.IsSequentialMode = mode == "Sequential";
        }

        private static Recipe ToDomain(RecipeModel vm)
        {
            var r = new Recipe { Id = vm.Id, Name = vm.Name, GlobalTargetTemperature = vm.TargetChamberTemp, GlobalTargetHumidity = vm.TargetChamberHumidity };
            foreach (var s in vm.Steps)
                r.Steps.Add(new RecipeStep
                {
                    CameraIndex = s.CameraIndex > 0 ? s.CameraIndex : ParseCameraIndex(s.NodeAssignment),
                    TargetPositionIndex = s.TargetPositionIndex > 0 ? s.TargetPositionIndex : ParsePositionIndex(s.NodeAssignment),
                    TargetBlackBodyTemperature = s.BlackbodyRef
                });
            return r;
        }

        private static RecipeModel FromDomain(Recipe r)
        {
            var vm = new RecipeModel { Id = r.Id, Name = r.Name, TargetChamberTemp = r.GlobalTargetTemperature, TargetChamberHumidity = r.GlobalTargetHumidity, LastModified = DateTime.Now.ToString("g") };
            int n = 1;
            foreach (var s in r.Steps)
                vm.Steps.Add(new RecipeStepModel
                {
                    StepNumber = n++,
                    NodeAssignment = $"Position {s.TargetPositionIndex:D2} -> CAM-{s.CameraIndex:D2}",
                    CameraIndex = s.CameraIndex,
                    TargetPositionIndex = s.TargetPositionIndex,
                    BlackbodyRef = s.TargetBlackBodyTemperature
                });
            return vm;
        }

        private static int ParseCameraIndex(string s)
        {
            try { var p = s.Split(new[] { "-> CAM-" }, StringSplitOptions.None); if (p.Length > 1 && int.TryParse(p[1].Trim(), out int v)) return v; } catch { }
            return 1;
        }

        private static int ParsePositionIndex(string s)
        {
            try { var p = s.Replace("Position ", "").Split(new[] { " ->" }, StringSplitOptions.None); if (p.Length > 0 && int.TryParse(p[0].Trim(), out int v)) return v; } catch { }
            return 1;
        }
    }
}
