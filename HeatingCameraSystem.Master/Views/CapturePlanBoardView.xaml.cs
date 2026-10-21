using System.Windows;
using System.Windows.Controls;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Views;

public partial class CapturePlanBoardView : UserControl
{
    public CapturePlanBoardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not CapturePlanBoard board) return;
            int active = board.Entries.FindIndex(e => e.Cells.Any(c => c.IsCurrent));
            if (active >= 0) SetCurrentValue(SelectedEntryIndexProperty, active);
            else if (SelectedEntryIndex >= board.Entries.Count) SetCurrentValue(SelectedEntryIndexProperty, 0);
        };
    }
    public static readonly DependencyProperty SelectedEntryIndexProperty = DependencyProperty.Register(
        nameof(SelectedEntryIndex), typeof(int), typeof(CapturePlanBoardView),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public int SelectedEntryIndex { get => (int)GetValue(SelectedEntryIndexProperty); set => SetValue(SelectedEntryIndexProperty, value); }
}
