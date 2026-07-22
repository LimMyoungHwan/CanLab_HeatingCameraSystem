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
        [ObservableProperty] private int _positionX;
        [ObservableProperty] private int _positionY;
        [ObservableProperty] private double _targetChamberTemperature;
        [ObservableProperty] private double _targetChamberHumidity;

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

    public partial class RecipeEditorViewModel : ObservableObject, IDisposable
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

        private static Recipe ToDomain(RecipeModel vm)
        {
            var r = new Recipe { Id = vm.Id, Name = vm.Name, GlobalTargetTemperature = vm.TargetChamberTemp, GlobalTargetHumidity = vm.TargetChamberHumidity };
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
            var vm = new RecipeModel { Id = r.Id, Name = r.Name, TargetChamberTemp = r.GlobalTargetTemperature, TargetChamberHumidity = r.GlobalTargetHumidity, LastModified = DateTime.Now.ToString("g") };
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
        [ObservableProperty] private CameraComPair? _selectedCameraPair;
        [ObservableProperty] private System.Windows.Media.Imaging.BitmapSource? _currentPreview;
        [ObservableProperty] private double _fpaTemperature;
        [ObservableProperty] private int _currentServoX;
        [ObservableProperty] private int _currentServoY;

        public ObservableCollection<CameraComPair> CameraPairs { get; } = new();

        private ICameraSerialClient? _serialClient;
        private EventHandler<ThermalFrame>? _frameHandler;
        private System.Windows.Threading.DispatcherTimer? _pollTimer;

        partial void OnSelectedCameraPairChanged(CameraComPair? value)
        {
            if (_frameHandler != null && AppServices.LiveThermalCamera != null)
            {
                AppServices.LiveThermalCamera.FrameReady -= _frameHandler;
                _ = AppServices.LiveThermalCamera.StopAsync();
                _frameHandler = null;
            }

            _serialClient?.Dispose();
            _serialClient = null;

            CurrentPreview = null;

            if (value?.Camera != null && AppServices.LiveThermalCamera != null)
            {
                _frameHandler = (s, frame) =>
                {
                    var bmp = ThermalFrameBitmapSourceConverter.ToBitmapSource(frame);
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => CurrentPreview = bmp));
                };
                AppServices.LiveThermalCamera.FrameReady += _frameHandler;
                _ = AppServices.LiveThermalCamera.StartAsync(value.Camera.OpenCvIndex);

                if (value.SerialPort != null && AppServices.CameraSerialClientFactory != null)
                {
                    _serialClient = AppServices.CameraSerialClientFactory(value.SerialPort.PortName);
                    _ = _serialClient.InitializeAsync();
                }
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task RefreshPairingsAsync()
        {
            if (_frameHandler != null && AppServices.LiveThermalCamera != null)
            {
                AppServices.LiveThermalCamera.FrameReady -= _frameHandler;
                _ = AppServices.LiveThermalCamera.StopAsync();
                _frameHandler = null;
            }

            _serialClient?.Dispose();
            _serialClient = null;
            SelectedCameraPair = null;

            CameraPairs.Clear();
            if (AppServices.CameraPairingService != null)
            {
                foreach (var p in await AppServices.CameraPairingService.GetPairsAsync())
                {
                    CameraPairs.Add(p);
                }
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task GoToXyAsync()
        {
            if (SelectedStep != null && AppServices.PlcController != null)
            {
                await AppServices.PlcController.MoveToCoordinateAsync(SelectedStep.PositionX, SelectedStep.PositionY);
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task UseCurrentXyAsync()
        {
            if (AppServices.PlcController != null)
            {
                var st = await AppServices.PlcController.ReadStatusAsync();
                if (SelectedStep != null)
                {
                    SelectedStep.PositionX = st.ServoXPosition;
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

        [RelayCommand]
        private async System.Threading.Tasks.Task OpenShutterAsync()
        {
            if (_serialClient != null) await _serialClient.SetShutterAsync(true);
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task CloseShutterAsync()
        {
            if (_serialClient != null) await _serialClient.SetShutterAsync(false);
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task StartCameraAsync()
        {
            if (_serialClient != null) await _serialClient.SetCameraRunningAsync(true);
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task StopCameraAsync()
        {
            if (_serialClient != null) await _serialClient.SetCameraRunningAsync(false);
        }

        public System.Threading.Tasks.Task StartJog(ServoAxis axis, bool positive) => AppServices.PlcController?.JogAsync(axis, positive, true) ?? System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task StopJog(ServoAxis axis, bool positive) => AppServices.PlcController?.JogAsync(axis, positive, false) ?? System.Threading.Tasks.Task.CompletedTask;

        private async void PollTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (AppServices.PlcController != null)
                {
                    var st = await AppServices.PlcController.ReadStatusAsync();
                    CurrentServoX = st.ServoXPosition;
                    CurrentServoY = st.ServoYPosition;
                }
                if (_serialClient != null)
                {
                    try { FpaTemperature = await _serialClient.ReadFpaTemperatureAsync(); } catch { }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer.Tick -= PollTimer_Tick;
                _pollTimer = null;
            }
            if (_frameHandler != null && AppServices.LiveThermalCamera != null)
            {
                AppServices.LiveThermalCamera.FrameReady -= _frameHandler;
                _ = AppServices.LiveThermalCamera.StopAsync();
                _frameHandler = null;
            }
            if (_serialClient != null)
            {
                _serialClient.Dispose();
                _serialClient = null;
            }
        }
    }
}

