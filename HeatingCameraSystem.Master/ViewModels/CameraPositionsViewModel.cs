using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    public sealed class CameraPositionKindOption
    {
        public CameraPositionKind Value { get; init; }
        public string Label { get; init; } = string.Empty;
    }

    /// <summary>
        /// 위치표 1행 = 실제 카메라 × Hot/Cold/Room 역할. 저장된 Point는 좌표로 되돌릴 수 없다.
    /// (좌표 이동이 Point 1을 덮어쓰므로 좌표 값이 남아 있으면 엉뚱한 곳으로 갈 수 있다).
    /// </summary>
    public partial class CameraPositionRowModel : ObservableObject
    {
        private bool _loading;

        public CameraPositionRowModel(CameraIdentity camera, BlackBodyRole role)
        {
            Camera = camera.Copy();
            Role = role;
        }

        public CameraIdentity Camera { get; }
        public int CameraIndex => Camera.CameraIndex;
        public BlackBodyRole Role { get; }
        public string CameraLabel => Camera.Label;
        public string BlackBodyLabel => Role.ToString();

        [ObservableProperty] private CameraPositionKind _kind = CameraPositionKind.Coordinate;
        [ObservableProperty] private float _x;
        [ObservableProperty] private float _y;
        [ObservableProperty] private int _pointNumber = 2;

        /// <summary>"이 좌표를 Point n에 쓰기"의 대상 Point 번호.</summary>
        [ObservableProperty] private int _writeTargetPoint = 2;

        /// <summary>저장소에 있는 항목의 종류. null이면 미등록.</summary>
        [ObservableProperty] private CameraPositionKind? _persistedKind;

        [ObservableProperty] private bool _isEdited;

        public bool IsPersisted => PersistedKind != null;
        public bool IsKindLocked => PersistedKind == CameraPositionKind.Point;
        public bool CanChangeKind => !IsKindLocked;
        public bool IsCoordinate => Kind == CameraPositionKind.Coordinate;
        public bool IsPoint => Kind == CameraPositionKind.Point;
        public string KindLockTooltip => IsKindLocked
            ? "Point로 저장된 위치는 좌표로 바꿀 수 없습니다. 삭제 후 다시 등록하세요."
            : string.Empty;

        public string StatusText => IsEdited ? "변경됨" : IsPersisted ? "저장됨" : "미등록";

        partial void OnKindChanged(CameraPositionKind value)
        {
            if (IsKindLocked && value == CameraPositionKind.Coordinate)
            {
                Kind = CameraPositionKind.Point;
                return;
            }

            if (value == CameraPositionKind.Point && PointNumber < 2) PointNumber = 2;
            OnPropertyChanged(nameof(IsCoordinate));
            OnPropertyChanged(nameof(IsPoint));
            MarkEdited();
        }

        partial void OnXChanged(float value) => MarkEdited();
        partial void OnYChanged(float value) => MarkEdited();
        partial void OnPointNumberChanged(int value) => MarkEdited();

        partial void OnPersistedKindChanged(CameraPositionKind? value)
        {
            OnPropertyChanged(nameof(IsPersisted));
            OnPropertyChanged(nameof(IsKindLocked));
            OnPropertyChanged(nameof(CanChangeKind));
            OnPropertyChanged(nameof(KindLockTooltip));
            OnPropertyChanged(nameof(StatusText));
        }

        partial void OnIsEditedChanged(bool value) => OnPropertyChanged(nameof(StatusText));

        private void MarkEdited()
        {
            if (!_loading) IsEdited = true;
        }

        /// <summary>저장소에 있는 그대로의 위치 스냅샷(편집 중 값과 무관). 미등록이면 null.</summary>
        internal CameraPositionEntry? PersistedEntry { get; private set; }

        /// <summary>저장소 값(또는 미등록 기본값)으로 되돌린다. 편집 표시는 지운다.</summary>
        internal void ApplyPersisted(CameraPosition? position)
        {
            _loading = true;
            try
            {
                if (position == null)
                {
                    PersistedKind = null;
                    Kind = CameraPositionKind.Coordinate;
                    X = 0;
                    Y = 0;
                    PointNumber = 2;
                }
                else
                {
                    // Kind 보다 PersistedKind 를 먼저 갱신해야 Point→Coordinate 되돌림 가드가 새 값 기준으로 동작한다.
                    PersistedKind = position.Kind;
                    Kind = position.Kind;
                    X = position.X;
                    Y = position.Y;
                    PointNumber = position.PointNumber >= 2 ? position.PointNumber : 2;
                }
                IsEdited = false;
            }
            finally
            {
                _loading = false;
            }
            PersistedEntry = position == null ? null : new CameraPositionEntry
            {
                AgentId = Camera.AgentId, CameraIndex = CameraIndex,
                Role = Role,
                Position = new CameraPosition { Kind = position.Kind, X = position.X, Y = position.Y, PointNumber = position.PointNumber }
            };
        }

        internal CameraPositionEntry ToEntry() => new()
        {
            AgentId = Camera.AgentId, CameraIndex = CameraIndex,
            Role = Role,
            Position = new CameraPosition
            {
                Kind = Kind,
                X = X,
                Y = Y,
                PointNumber = Kind == CameraPositionKind.Point ? PointNumber : 0
            }
        };
    }

    /// <summary>
        /// 실제 온라인/저장 카메라의 Hot/Cold/Room 공유 위치표.
    /// 저장·삭제로 위치표가 바뀌면 <see cref="PositionsChanged"/>를 올려 호스트가 촬영 계획을 다시 컴파일할 수 있게 한다.
    /// </summary>
    public partial class CameraPositionsViewModel : ObservableObject
    {
        private readonly Func<ICameraPositionRepository?> _repo;
        private readonly Func<IPlcController?> _plc;
        private readonly Func<int> _pointCount;
        private readonly Func<bool> _isRecipeRunning;
        private readonly Func<string, string, bool> _confirm;

        private readonly Func<IReadOnlyList<CameraIdentity>> _online;

        public CameraPositionKindOption[] KindOptions { get; } =
        {
            new() { Value = CameraPositionKind.Coordinate, Label = "좌표" },
            new() { Value = CameraPositionKind.Point, Label = "Point" }
        };

        public ObservableCollection<CameraPositionRowModel> Rows { get; } = new();

        /// <summary>필터가 적용된 표시용 행. DataGrid는 이 컬렉션을 바인딩한다.</summary>
        public ObservableCollection<CameraPositionRowModel> VisibleRows { get; } = new();

        /// <summary>Point 번호 후보. Point 1은 좌표 이동이 덮어쓰므로 제외하고 2부터 시작한다.</summary>
        public int[] PointOptions { get; private set; }

        [ObservableProperty] private string _filterText = string.Empty;
        [ObservableProperty] private bool _showRegisteredOnly;
        [ObservableProperty] private string _statusMessage = string.Empty;
        [ObservableProperty] private bool _canUsePlc;
        [ObservableProperty] private bool _canMove;
        [ObservableProperty] private string _plcDisabledReason = string.Empty;

        /// <summary>위치표가 저장·삭제로 바뀌었을 때(호스트가 촬영 계획을 다시 컴파일하는 신호).</summary>
        public event EventHandler? PositionsChanged;

        public CameraPositionsViewModel()
            : this(() => AppServices.CameraPositions,
                   () => AppServices.PlcController,
                   () => AppServices.Settings.Plc.ServoPointCount,
                   () => AppServices.RecipeEngine?.IsRunning ?? false,
                   ConfirmWithMessageBox)
        {
        }

        internal CameraPositionsViewModel(
            Func<ICameraPositionRepository?> repo,
            Func<IPlcController?> plc,
            Func<int> pointCount,
            Func<bool> isRecipeRunning,
            Func<string, string, bool> confirm,
            Func<IReadOnlyList<CameraIdentity>>? online = null)
        {
            _repo = repo;
            _plc = plc;
            _pointCount = pointCount;
            _isRecipeRunning = isRecipeRunning;
            _confirm = confirm;
            _online = online ?? (() => AppServices.AgentDirectory?.OnlineCameras ?? Array.Empty<CameraIdentity>());

            PointOptions = BuildPointOptions(_pointCount());


            ApplyFilter();
            RefreshAvailability();
        }

        private static bool ConfirmWithMessageBox(string title, string message)
            => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

        private static int[] BuildPointOptions(int pointCount)
            => pointCount >= 2 ? Enumerable.Range(2, pointCount - 1).ToArray() : Array.Empty<int>();

        partial void OnFilterTextChanged(string value) => ApplyFilter();
        partial void OnShowRegisteredOnlyChanged(bool value) => ApplyFilter();

        /// <summary>저장소에 저장된 위치의 스냅샷. 편집 중인 미저장 값은 반영하지 않는다.</summary>
        public IReadOnlyList<CameraPositionEntry> RegisteredPositions
            => Rows.Select(r => r.PersistedEntry).OfType<CameraPositionEntry>().ToList();
        [RelayCommand]
        private Task ReloadAsync() => LoadAsync();

        /// <summary>실제 온라인 카메라와 저장된 카메라의 역할별 행을 불러온다.</summary>
        public async Task LoadAsync()
        {
            PointOptions = BuildPointOptions(_pointCount());
            OnPropertyChanged(nameof(PointOptions));

            var repo = _repo();
            if (repo == null)
            {
                StatusMessage = "위치 저장소가 초기화되지 않았습니다.";
                return;
            }

            IReadOnlyList<CameraPositionEntry> entries;
            try
            {
                entries = await repo.GetAllAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"불러오기 실패: {ex.Message}";
                return;
            }

            RunOnUi(() =>
            {
                var byKey = entries.ToDictionary(e => CapturePlanCompiler.PositionKey(e.Camera, e.Role));
                Rows.Clear();
                var cameras = _online().Concat(entries.Select(e => e.Camera)).GroupBy(c => c.Key).Select(g => g.First());
                foreach (var camera in cameras)
                foreach (var role in Enum.GetValues<BlackBodyRole>())
                {
                    var row = new CameraPositionRowModel(camera, role);
                    row.ApplyPersisted(byKey.TryGetValue(CapturePlanCompiler.PositionKey(camera, role), out var entry) ? entry.Position : null);
                    Rows.Add(row);
                }
                ApplyFilter();
                StatusMessage = $"등록된 위치 {entries.Count}건을 불러왔습니다. " + (repo is LiteDbCameraPositionRepository lite ? lite.MigrationWarning : string.Empty);
            });
        }

        /// <summary>PLC 연결·레시피 실행 상태를 다시 읽어 버튼 활성 여부와 사유를 갱신한다.</summary>
        public void RefreshAvailability()
        {
            var plc = _plc();
            if (plc == null || !plc.IsConnected)
            {
                CanUsePlc = false;
                CanMove = false;
                PlcDisabledReason = "PLC가 연결되어 있지 않아 현재 위치 가져오기·이동 테스트·Point 쓰기를 사용할 수 없습니다.";
                return;
            }

            CanUsePlc = true;
            if (_isRecipeRunning())
            {
                CanMove = false;
                PlcDisabledReason = "레시피 실행 중에는 이동 테스트·Point 쓰기를 사용할 수 없습니다.";
                return;
            }

            CanMove = true;
            PlcDisabledReason = string.Empty;
        }

        private void ApplyFilter()
        {
            string text = FilterText.Trim();
            bool hasNumber = int.TryParse(text, out int camera);

            var visible = Rows.Where(r =>
                (text.Length == 0 || (hasNumber && r.CameraIndex == camera) || r.CameraLabel.Contains(text, StringComparison.OrdinalIgnoreCase))
                && (!ShowRegisteredOnly || r.IsPersisted)).ToList();

            VisibleRows.Clear();
            foreach (var row in visible) VisibleRows.Add(row);
        }

        [RelayCommand]
        private async Task FetchCurrentAsync(CameraPositionRowModel? row)
        {
            if (row == null) return;
            RefreshAvailability();
            if (!CanUsePlc) { StatusMessage = PlcDisabledReason; return; }
            if (row.Kind != CameraPositionKind.Coordinate)
            {
                StatusMessage = "현재 위치는 좌표 방식 행에만 가져올 수 있습니다.";
                return;
            }

            var plc = _plc();
            if (plc == null) { StatusMessage = PlcDisabledReason; return; }

            try
            {
                var status = await plc.ReadStatusAsync();
                row.X = status.ServoXPosition;
                row.Y = status.ServoYPosition;
                StatusMessage = $"{row.CameraLabel} {row.BlackBodyLabel}: 현재 위치 ({row.X:F1}, {row.Y:F1}) mm를 가져왔습니다. 저장해야 반영됩니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"현재 위치 읽기 실패: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task TestMoveAsync(CameraPositionRowModel? row)
        {
            if (row == null) return;
            RefreshAvailability();
            if (!CanMove) { StatusMessage = PlcDisabledReason; return; }

            var plc = _plc();
            if (plc == null) { StatusMessage = PlcDisabledReason; return; }

            try
            {
                if (row.Kind == CameraPositionKind.Point)
                {
                    await plc.MoveServoToPositionAsync(row.PointNumber);
                    StatusMessage = $"{row.CameraLabel} {row.BlackBodyLabel}: Point {row.PointNumber}로 이동 명령을 보냈습니다.";
                }
                else
                {
                    await plc.MoveToCoordinateAsync(row.X, row.Y);
                    StatusMessage = $"{row.CameraLabel} {row.BlackBodyLabel}: ({row.X:F1}, {row.Y:F1}) mm로 이동 명령을 보냈습니다.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"이동 테스트 실패: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task SaveAsync(CameraPositionRowModel? row)
        {
            if (row == null) return;
            await SaveRowAsync(row);
        }

        private async Task<bool> SaveRowAsync(CameraPositionRowModel row)
        {
            var repo = _repo();
            if (repo == null)
            {
                StatusMessage = "위치 저장소가 초기화되지 않았습니다.";
                return false;
            }

            try
            {
                var entry = row.ToEntry();
                await repo.SaveAsync(entry);
                row.ApplyPersisted(entry.Position);
                ApplyFilter();
                StatusMessage = $"{row.CameraLabel} {row.BlackBodyLabel}: 저장했습니다.";
                PositionsChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"저장 실패 ({row.CameraLabel} {row.BlackBodyLabel}): {ex.Message}";
                return false;
            }
        }

        [RelayCommand]
        private async Task DeleteAsync(CameraPositionRowModel? row)
        {
            if (row == null) return;

            if (!row.IsPersisted)
            {
                row.ApplyPersisted(null);
                ApplyFilter();
                StatusMessage = $"{row.CameraLabel} {row.BlackBodyLabel}: 편집 내용을 초기화했습니다.";
                return;
            }

            if (!_confirm("위치 삭제", $"{row.CameraLabel} {row.BlackBodyLabel}의 저장된 위치를 삭제할까요?"))
                return;

            var repo = _repo();
            if (repo == null)
            {
                StatusMessage = "위치 저장소가 초기화되지 않았습니다.";
                return;
            }

            try
            {
                await repo.DeleteAsync(row.Camera, row.Role);
                row.ApplyPersisted(null);
                ApplyFilter();
                StatusMessage = $"{row.CameraLabel} {row.BlackBodyLabel}: 삭제했습니다.";
                PositionsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                StatusMessage = $"삭제 실패 ({row.CameraLabel} {row.BlackBodyLabel}): {ex.Message}";
            }
        }

        /// <summary>좌표 행의 X/Y를 PLC의 Point n에 쓰고, 이 행을 Point n으로 저장한다(되돌릴 수 없음).</summary>
        [RelayCommand]
        private async Task WriteToPointAsync(CameraPositionRowModel? row)
        {
            if (row == null) return;
            RefreshAvailability();
            if (!CanMove) { StatusMessage = PlcDisabledReason; return; }
            if (row.Kind != CameraPositionKind.Coordinate)
            {
                StatusMessage = "좌표 방식 행만 Point에 쓸 수 있습니다.";
                return;
            }

            int n = row.WriteTargetPoint;
            if (!PointOptions.Contains(n))
            {
                StatusMessage = $"Point 번호는 2~{_pointCount()} 범위여야 합니다.";
                return;
            }

            if (!float.IsFinite(row.X) || !float.IsFinite(row.Y))
            {
                StatusMessage = "좌표 값이 올바르지 않습니다.";
                return;
            }

            string message = $"{row.CameraLabel} {row.BlackBodyLabel}의 좌표 ({row.X:F1}, {row.Y:F1}) mm를 PLC Point {n}에 씁니다.\n\n"
                + $"• PLC의 기존 Point {n} 좌표를 덮어씁니다.\n"
                + "• 이 행은 Point로 저장되며 다시 좌표로 되돌릴 수 없습니다(삭제 후 재등록만 가능).\n\n계속할까요?";
            if (!_confirm("Point에 쓰기", message)) return;

            var plc = _plc();
            if (plc == null) { StatusMessage = PlcDisabledReason; return; }

            try
            {
                await plc.SetPointCoordinateAsync(n, row.X, row.Y);
            }
            catch (Exception ex)
            {
                StatusMessage = $"PLC Point {n} 쓰기 실패: {ex.Message}";
                return;
            }

            row.Kind = CameraPositionKind.Point;
            row.PointNumber = n;
            if (!await SaveRowAsync(row))
                StatusMessage = $"PLC Point {n}에는 썼지만 위치 저장에 실패했습니다. {StatusMessage}";
        }

        private static void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }
    }
}
