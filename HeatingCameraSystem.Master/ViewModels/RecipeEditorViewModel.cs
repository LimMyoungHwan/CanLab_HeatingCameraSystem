using HeatingCameraSystem.Core.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Microsoft.Win32;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class RecipeStepModel : ObservableObject
    {
        [ObservableProperty] private int _stepNumber;
        [ObservableProperty] private string _nodeAssignment = string.Empty;
        [ObservableProperty] private float _blackbodyRef;
        [ObservableProperty] private float _positionX;
        [ObservableProperty] private float _positionY;
        [ObservableProperty] private double _targetChamberTemperature;
        [ObservableProperty] private double _targetChamberHumidity;

        public int CameraIndex { get; set; }
        public int TargetPositionIndex { get; set; }
    }

    public sealed class AgentCameraOption
    {
        public string AgentId { get; init; } = string.Empty;
        public int CameraIndex { get; init; }
        public string Label => $"{AgentId} (CAM-{CameraIndex:D2})";
    }

    public partial class RecipeModel : ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private string _lastModified = string.Empty;
        [ObservableProperty] private float _targetChamberTemp;
        [ObservableProperty] private int _rampMinutes;
        [ObservableProperty] private float _targetChamberHumidity;
        [ObservableProperty] private bool _isSequentialMode = true;

        public ObservableCollection<RecipeStepModel> Steps { get; } = new();
    }

    public partial class RecipeEditorViewModel : ObservableObject, IDisposable
    {
        public ObservableCollection<RecipeModel> Recipes { get; } = new ObservableCollection<RecipeModel>();

        [ObservableProperty]
        private RecipeModel? _selectedRecipe;

        public RecipeEditorViewModel()
        {
            SubscribeCameraServices();

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

        public event EventHandler? RecipeAdded;

        [RelayCommand]
        private void AddRecipe()
        {
            var vm = new RecipeModel { Name = "새 레시피", LastModified = DateTime.Now.ToString("g"), TargetChamberTemp = 25.0f, RampMinutes = 0, TargetChamberHumidity = 50.0f };
            Recipes.Add(vm);
            AppServices.RecipeRepo.SaveAsync(ToDomain(vm)).GetAwaiter().GetResult();
            SelectRecipe(vm);
            RecipeAdded?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void CopyRecipe()
        {
            if (SelectedRecipe == null) return;

            var clone = CloneRecipe(ToDomain(SelectedRecipe));
            AppServices.RecipeRepo.SaveAsync(clone).GetAwaiter().GetResult();

            var vm = FromDomain(clone);
            Recipes.Add(vm);
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
        private void ExportRecipe()
        {
            if (SelectedRecipe == null) return;

            var dlg = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"{SelectedRecipe.Name}.json"
            };
            if (dlg.ShowDialog() != true) return;

            var recipe = ToDomain(SelectedRecipe);
            var json = JsonSerializer.Serialize(recipe, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
        }

        [RelayCommand]
        private void ImportRecipe()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var recipe = JsonSerializer.Deserialize<Recipe>(json);
                if (recipe == null) return;

                recipe.Id = Guid.NewGuid().ToString();
                AppServices.RecipeRepo.SaveAsync(recipe).GetAwaiter().GetResult();

                var vm = FromDomain(recipe);
                Recipes.Add(vm);
                SelectRecipe(vm);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecipeEditor] Import failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void SetCaptureMode(string mode)
        {
            if (SelectedRecipe != null)
                SelectedRecipe.IsSequentialMode = mode == "Sequential";
        }

        public static Recipe CloneRecipe(Recipe source)
        {
            return new Recipe
            {
                Id = Guid.NewGuid().ToString(),
                Name = source.Name + " (복사)",
                GlobalTargetTemperature = source.GlobalTargetTemperature,
                GlobalTargetHumidity = source.GlobalTargetHumidity,
                TemperatureRampMinutes = source.TemperatureRampMinutes,
                Steps = source.Steps.Select(s => new RecipeStep
                {
                    StepId = s.StepId,
                    CameraIndex = s.CameraIndex,
                    CameraAlias = s.CameraAlias,
                    TargetPositionIndex = s.TargetPositionIndex,
                    TargetBlackBodyTemperature = s.TargetBlackBodyTemperature,
                    PositionX = s.PositionX,
                    PositionY = s.PositionY,
                    TargetChamberTemperature = s.TargetChamberTemperature,
                    TargetChamberHumidity = s.TargetChamberHumidity
                }).ToList()
            };
        }

        private static Recipe ToDomain(RecipeModel vm)
        {
            var r = new Recipe
            {
                Id = vm.Id,
                Name = vm.Name,
                GlobalTargetTemperature = vm.TargetChamberTemp,
                TemperatureRampMinutes = vm.RampMinutes,
                GlobalTargetHumidity = vm.TargetChamberHumidity
            };
            foreach (var s in vm.Steps)
                r.Steps.Add(new RecipeStep
                {
                    CameraIndex = s.CameraIndex > 0 ? s.CameraIndex : ParseCameraIndex(s.NodeAssignment),
                    TargetPositionIndex = s.TargetPositionIndex > 0 ? s.TargetPositionIndex : ParsePositionIndex(s.NodeAssignment),
                    TargetBlackBodyTemperature = s.BlackbodyRef,
                    PositionX = s.PositionX,
                    PositionY = s.PositionY,
                    TargetChamberTemperature = s.TargetChamberTemperature,
                    TargetChamberHumidity = s.TargetChamberHumidity
                });
            return r;
        }

        private static RecipeModel FromDomain(Recipe r)
        {
            var vm = new RecipeModel { Id = r.Id, Name = r.Name, TargetChamberTemp = r.GlobalTargetTemperature, RampMinutes = r.TemperatureRampMinutes, TargetChamberHumidity = r.GlobalTargetHumidity, LastModified = DateTime.Now.ToString("g") };
            int n = 1;
            foreach (var s in r.Steps)
                vm.Steps.Add(new RecipeStepModel
                {
                    StepNumber = n++,
                    NodeAssignment = $"Position {s.TargetPositionIndex:D2} -> CAM-{s.CameraIndex:D2}",
                    CameraIndex = s.CameraIndex,
                    TargetPositionIndex = s.TargetPositionIndex,
                    BlackbodyRef = s.TargetBlackBodyTemperature,
                    PositionX = s.PositionX,
                    PositionY = s.PositionY,
                    TargetChamberTemperature = s.TargetChamberTemperature,
                    TargetChamberHumidity = s.TargetChamberHumidity
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
        [ObservableProperty] private RecipeStepModel? _selectedStep;
        [ObservableProperty] private System.Windows.Media.Imaging.BitmapSource? _currentPreview;
        [ObservableProperty] private int _currentServoX;
        [ObservableProperty] private int _currentServoY;

        [ObservableProperty] private AgentCameraOption? _selectedPreviewCamera;

        public ObservableCollection<AgentCameraOption> OnlineAgentCameras { get; } = new();

        private void SubscribeCameraServices()
        {
            var nats = AppServices.NatsService;
            if (nats == null) return;
            try
            {
                nats.SubscribeAgentStatusAsync(OnAgentStatus);
                nats.SubscribeLiveFrameAsync(OnLiveFrame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecipeEditor] NATS subscribe failed: {ex.Message}");
            }
        }

        private void OnAgentStatus(AgentStatusMessage msg)
        {
            if (string.IsNullOrEmpty(msg.AgentId)) return;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (!OnlineAgentCameras.Any(a => a.AgentId == msg.AgentId && a.CameraIndex == msg.CameraIndex))
                    OnlineAgentCameras.Add(new AgentCameraOption { AgentId = msg.AgentId, CameraIndex = msg.CameraIndex });
            });
        }

        private void OnLiveFrame(LiveFrameMessage msg)
        {
            if (msg.ImageBytes is null || msg.ImageBytes.Length == 0) return;
            var sel = SelectedPreviewCamera;
            if (sel == null || msg.AgentId != sel.AgentId || msg.CameraIndex != sel.CameraIndex) return;

            var bmp = Decode(msg.ImageBytes);
            if (bmp is null) return;
            bmp = HeatingCameraSystem.Master.Services.LivePreviewColorMode.Apply(bmp);
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => CurrentPreview = bmp));
        }

        private static System.Windows.Media.Imaging.BitmapSource? Decode(byte[] jpeg)
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                using var ms = new MemoryStream(jpeg);
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        partial void OnSelectedPreviewCameraChanged(AgentCameraOption? value) => CurrentPreview = null;

        [RelayCommand]
        private async System.Threading.Tasks.Task GoToXyAsync()
        {
            if (SelectedStep != null && AppServices.PlcController != null)
            {
                await AppServices.PlcController.MoveToCoordinateAsync(SelectedStep.PositionX, SelectedStep.PositionY);
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task UseCurrentXAsync()
        {
            if (AppServices.PlcController != null)
            {
                var st = await AppServices.PlcController.ReadStatusAsync();
                if (SelectedStep != null)
                {
                    SelectedStep.PositionX = st.ServoXPosition;
                }
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task UseCurrentYAsync()
        {
            if (AppServices.PlcController != null)
            {
                var st = await AppServices.PlcController.ReadStatusAsync();
                if (SelectedStep != null)
                {
                    SelectedStep.PositionY = st.ServoYPosition;
                }
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task HomeServoAsync()
        {
            if (AppServices.PlcController != null)
            {
                await AppServices.PlcController.HomeAsync(ServoAxis.X);
                await AppServices.PlcController.HomeAsync(ServoAxis.Y);
            }
        }

        private async System.Threading.Tasks.Task SendCameraOpAsync(string op)
        {
            var sel = SelectedPreviewCamera;
            if (sel == null || AppServices.NatsService == null) return;
            try
            {
                await AppServices.NatsService.PublishCameraControlAsync(new CameraControlMessage
                {
                    AgentId = sel.AgentId,
                    CameraIndex = sel.CameraIndex,
                    Op = op,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecipeEditor] camera op publish failed: {ex.Message}");
            }
        }

        [RelayCommand] private System.Threading.Tasks.Task OpenShutterAsync() => SendCameraOpAsync(CameraControlOps.ShutterOpen);
        [RelayCommand] private System.Threading.Tasks.Task CloseShutterAsync() => SendCameraOpAsync(CameraControlOps.ShutterClose);
        [RelayCommand] private System.Threading.Tasks.Task StartCameraAsync() => SendCameraOpAsync(CameraControlOps.Run);
        [RelayCommand] private System.Threading.Tasks.Task StopCameraAsync() => SendCameraOpAsync(CameraControlOps.Stop);

        public System.Threading.Tasks.Task StartJog(ServoAxis axis, bool positive) => AppServices.PlcController?.JogAsync(axis, positive, true) ?? System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task StopJog(ServoAxis axis, bool positive) => AppServices.PlcController?.JogAsync(axis, positive, false) ?? System.Threading.Tasks.Task.CompletedTask;

        public void Dispose()
        {
            // ponytail: NatsCommunicationService has no unsubscribe API (fire-and-forget loops),
            // same as ManualControlViewModel — nothing to release here.
        }
    }
}

