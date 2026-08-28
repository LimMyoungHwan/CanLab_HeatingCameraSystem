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
    /// <summary>
    /// 레시피 편집 그리드의 스텝 1행. 좌표는 mm, 온도는 ℃ 단위다.
    /// <see cref="CameraIndex"/>·<see cref="TargetPositionIndex"/>가 실제 값이고,
    /// NodeAssignment 문자열은 표시용이자 값이 0일 때의 파싱 폴백이다.
    /// </summary>
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

    /// <summary>미리보기 카메라 선택 콤보의 항목(온라인 Agent 카메라 1대).</summary>
    public sealed class AgentCameraOption
    {
        public string AgentId { get; init; } = string.Empty;
        public int CameraIndex { get; init; }
        public string Label => $"{AgentId} (CAM-{CameraIndex:D2})";
    }

    /// <summary>레시피 1건의 편집용 모델. 도메인 <see cref="Recipe"/>와 ToDomain/FromDomain으로 상호 변환한다.</summary>
    public partial class RecipeModel : ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private string _lastModified = string.Empty;
        [ObservableProperty] private float _targetChamberTemp;
        [ObservableProperty] private int _rampMinutes;
        [ObservableProperty] private float _targetChamberHumidity;
        [ObservableProperty] private float _safetyTempTolerance = 1.0f;
        [ObservableProperty] private float _safetyHumidityTolerance = 5.0f;
        [ObservableProperty] private bool _isSequentialMode = true;

        public ObservableCollection<RecipeStepModel> Steps { get; } = new();
    }

    /// <summary>
    /// 레시피 편집 화면. 레시피 CRUD·복사·JSON 가져오기/내보내기, 스텝 편집(추가·삭제·순서 이동),
    /// 그리고 좌표 잡기를 위한 서보 이동·JOG와 카메라 라이브 미리보기를 담당한다.
    /// NATS 콜백은 백그라운드 스레드로 오므로 UI 갱신은 Dispatcher로 마샬링한다.
    /// </summary>
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

        /// <summary>레시피를 선택하고 목록의 선택 하이라이트를 옮긴다.</summary>
        [RelayCommand]
        private void SelectRecipe(RecipeModel recipe)
        {
            if (SelectedRecipe != null) SelectedRecipe.IsSelected = false;
            SelectedRecipe = recipe;
            if (SelectedRecipe != null) SelectedRecipe.IsSelected = true;
        }

        public event EventHandler? RecipeAdded;

        /// <summary>기본값으로 새 레시피를 만들고 즉시 저장·선택한다.</summary>
        [RelayCommand]
        private void AddRecipe()
        {
            var vm = new RecipeModel { Name = "새 레시피", LastModified = DateTime.Now.ToString("g"), TargetChamberTemp = 25.0f, RampMinutes = 0, TargetChamberHumidity = 50.0f, SafetyTempTolerance = 1.0f, SafetyHumidityTolerance = 5.0f };
            Recipes.Add(vm);
            AppServices.RecipeRepo.SaveAsync(ToDomain(vm)).GetAwaiter().GetResult();
            SelectRecipe(vm);
            RecipeAdded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>선택 레시피를 새 Id로 복제해 저장·선택한다. 이름에는 "(복사)"가 붙는다.</summary>
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

        /// <summary>선택 레시피를 저장하고 수정 시각을 갱신한다.</summary>
        [RelayCommand]
        private void SaveRecipe()
        {
            if (SelectedRecipe == null) return;
            SelectedRecipe.LastModified = DateTime.Now.ToString("g");
            AppServices.RecipeRepo.SaveAsync(ToDomain(SelectedRecipe)).GetAwaiter().GetResult();
        }

        /// <summary>레시피를 삭제한다. 선택 중이던 레시피였다면 남은 첫 레시피를 선택한다.</summary>
        [RelayCommand]
        private void DeleteRecipe(RecipeModel recipe)
        {
            if (recipe == null) return;
            AppServices.RecipeRepo.DeleteAsync(recipe.Id).GetAwaiter().GetResult();
            Recipes.Remove(recipe);
            if (SelectedRecipe == recipe)
                SelectedRecipe = Recipes.FirstOrDefault();
        }

        /// <summary>다음 번호의 스텝을 추가한다. 기본값은 "Position N -> CAM-N" 짝이다.</summary>
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

        /// <summary>스텝을 삭제하고 남은 스텝 번호를 1부터 다시 매긴다.</summary>
        [RelayCommand]
        private void DeleteStep(RecipeStepModel step)
        {
            if (SelectedRecipe == null || step == null) return;
            SelectedRecipe.Steps.Remove(step);
            for (int i = 0; i < SelectedRecipe.Steps.Count; i++)
                SelectedRecipe.Steps[i].StepNumber = i + 1;
        }

        /// <summary>드래그&amp;드롭으로 스텝(Item1)을 대상 스텝(Item2) 위치로 옮기고 번호를 다시 매긴다.</summary>
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

        /// <summary>선택 레시피를 JSON 파일로 내보낸다.</summary>
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

        /// <summary>JSON 파일에서 레시피를 가져온다. Id는 새로 발급해 기존 레시피를 덮어쓰지 않는다.</summary>
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

        /// <summary>촬영 방식 토글. "Sequential"이면 순차 모드, 그 외는 동시 모드다.</summary>
        [RelayCommand]
        private void SetCaptureMode(string mode)
        {
            if (SelectedRecipe != null)
                SelectedRecipe.IsSequentialMode = mode == "Sequential";
        }

        /// <summary>레시피 깊은 복사본을 새 Id와 "(복사)" 이름으로 만든다. 스텝 목록까지 복제한다.</summary>
        public static Recipe CloneRecipe(Recipe source)
        {
            return new Recipe
            {
                Id = Guid.NewGuid().ToString(),
                Name = source.Name + " (복사)",
                GlobalTargetTemperature = source.GlobalTargetTemperature,
                GlobalTargetHumidity = source.GlobalTargetHumidity,
                TemperatureRampMinutes = source.TemperatureRampMinutes,
                SafetyTempTolerance = source.SafetyTempTolerance,
                SafetyHumidityTolerance = source.SafetyHumidityTolerance,
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

        /// <summary>
        /// 편집 모델을 도메인 <see cref="Recipe"/>로 변환한다. CameraIndex/TargetPositionIndex가
        /// 0이면 NodeAssignment 문자열 파싱으로 폴백한다(구버전 데이터 호환).
        /// </summary>
        private static Recipe ToDomain(RecipeModel vm)
        {
            var r = new Recipe
            {
                Id = vm.Id,
                Name = vm.Name,
                GlobalTargetTemperature = vm.TargetChamberTemp,
                TemperatureRampMinutes = vm.RampMinutes,
                GlobalTargetHumidity = vm.TargetChamberHumidity,
                SafetyTempTolerance = vm.SafetyTempTolerance,
                SafetyHumidityTolerance = vm.SafetyHumidityTolerance
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

        /// <summary>도메인 <see cref="Recipe"/>를 편집 모델로 변환하고 스텝 번호·표시 문자열을 만든다.</summary>
        private static RecipeModel FromDomain(Recipe r)
        {
            var vm = new RecipeModel { Id = r.Id, Name = r.Name, TargetChamberTemp = r.GlobalTargetTemperature, RampMinutes = r.TemperatureRampMinutes, TargetChamberHumidity = r.GlobalTargetHumidity, SafetyTempTolerance = r.SafetyTempTolerance, SafetyHumidityTolerance = r.SafetyHumidityTolerance, LastModified = DateTime.Now.ToString("g") };
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

        /// <summary>"Position NN -> CAM-NN" 표시 문자열에서 카메라 인덱스를 파싱한다. 실패 시 1.</summary>
        private static int ParseCameraIndex(string s)
        {
            try { var p = s.Split(new[] { "-> CAM-" }, StringSplitOptions.None); if (p.Length > 1 && int.TryParse(p[1].Trim(), out int v)) return v; } catch { }
            return 1;
        }

        /// <summary>"Position NN -> CAM-NN" 표시 문자열에서 포지션 인덱스를 파싱한다. 실패 시 1.</summary>
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

        /// <summary>선택된 미리보기 카메라의 프레임만 디코드해 표시한다. 나머지 카메라 프레임은 버린다.</summary>
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

        /// <summary>JPEG 바이트를 디코드한다. Freeze로 스레드 간 전달을 허용하며, 손상 데이터는 null을 반환한다.</summary>
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

        /// <summary>선택 스텝의 좌표(mm)로 서보를 이동시킨다.</summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task GoToXyAsync()
        {
            if (SelectedStep != null && AppServices.PlcController != null)
            {
                await AppServices.PlcController.MoveToCoordinateAsync(SelectedStep.PositionX, SelectedStep.PositionY);
            }
        }

        /// <summary>현재 서보 X 위치(mm)를 선택 스텝의 X 좌표로 채운다.</summary>
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

        /// <summary>현재 서보 Y 위치(mm)를 선택 스텝의 Y 좌표로 채운다.</summary>
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

        /// <summary>X·Y축을 차례로 원점 복귀시킨다.</summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task HomeServoAsync()
        {
            if (AppServices.PlcController != null)
            {
                await AppServices.PlcController.HomeAsync(ServoAxis.X);
                await AppServices.PlcController.HomeAsync(ServoAxis.Y);
            }
        }

        /// <summary>선택된 미리보기 카메라에 카메라 제어 명령(Run/Stop/셔터)을 발행한다.</summary>
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

        /// <summary>JOG 이동 시작(버튼 누름). View 코드비하인드가 직접 호출한다.</summary>
        public System.Threading.Tasks.Task StartJog(ServoAxis axis, bool positive) => AppServices.PlcController?.JogAsync(axis, positive, true) ?? System.Threading.Tasks.Task.CompletedTask;
        /// <summary>JOG 이동 정지(버튼 뗌). View 코드비하인드가 직접 호출한다.</summary>
        public System.Threading.Tasks.Task StopJog(ServoAxis axis, bool positive) => AppServices.PlcController?.JogAsync(axis, positive, false) ?? System.Threading.Tasks.Task.CompletedTask;

        public void Dispose()
        {
            // ponytail: NatsCommunicationService에는 구독 해제 API가 없다(fire-and-forget 루프).
            // ManualControlViewModel과 같은 상황이므로 여기서 해제할 것이 없다.
        }
    }
}

