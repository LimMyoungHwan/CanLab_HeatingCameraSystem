using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HeatingCameraSystem.Master.ViewModels;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Views
{
    /// <summary>
    /// 레시피 편집 화면의 코드 비하인드. 스텝 드래그 앤 드롭 재정렬, JOG 버튼 press/release,
    /// 새 레시피 이름 입력란 포커스 이동처럼 WPF 이벤트·비주얼 트리에 묶인 배선만 담당하고,
    /// 실제 상태 변경은 RecipeEditorViewModel의 명령과 메서드에 위임한다.
    /// </summary>
    public partial class RecipeEditorView : UserControl
    {
        private Point _dragStartPoint;

        public RecipeEditorView()
        {
            InitializeComponent();
            this.DataContextChanged += RecipeEditorView_DataContextChanged;
        }

        /// <summary>ViewModel이 바뀔 때 RecipeAdded 이벤트 구독을 이전 VM에서 새 VM으로 옮긴다.</summary>
        private void RecipeEditorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is RecipeEditorViewModel oldVm)
            {
                oldVm.RecipeAdded -= Vm_RecipeAdded;
            }
            if (e.NewValue is RecipeEditorViewModel newVm)
            {
                newVm.RecipeAdded += Vm_RecipeAdded;
            }
        }

        /// <summary>새 레시피가 추가되면 렌더링이 끝난 뒤 이름 입력란에 포커스를 준다.</summary>
        private void Vm_RecipeAdded(object? sender, EventArgs e)
        {
            // 레시피 목록에서 활성 텍스트 박스를 찾는다
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var vm = DataContext as RecipeEditorViewModel;
                if (vm?.SelectedRecipe == null) return;
                
                // 아주 단순한 포커스 로직 - 표준 키보드 포커스에 의존한다
                FocusNameTextBox(this);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// 비주얼 트리를 재귀 탐색해 SelectedRecipe에 바인딩된 RecipeNameTextBox를 찾아
        /// 포커스를 주고 내용을 전체 선택한다.
        /// </summary>
        private void FocusNameTextBox(DependencyObject parent)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is TextBox tb && tb.Name == "RecipeNameTextBox")
                {
                    if (tb.DataContext == ((RecipeEditorViewModel)DataContext).SelectedRecipe)
                    {
                        tb.Focus();
                        tb.SelectAll();
                        return;
                    }
                }
                FocusNameTextBox(child);
            }
        }

        /// <summary>드래그 판정용 시작 좌표를 기록한다.</summary>
        private void Step_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        /// <summary>최소 드래그 거리를 넘으면 RecipeStepModel을 실은 드래그를 시작한다.</summary>
        private void Step_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var textBlock = sender as TextBlock;
                    if (textBlock == null) return;
                    
                    var step = textBlock.DataContext as RecipeStepModel;
                    if (step == null) return;

                    DataObject dragData = new DataObject("RecipeStepModel", step);
                    DragDrop.DoDragDrop(textBlock, dragData, DragDropEffects.Move);
                }
            }
        }

        /// <summary>RecipeStepModel 드래그가 아니면 드롭을 거부한다.</summary>
        private void Step_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("RecipeStepModel"))
            {
                e.Effects = DragDropEffects.None;
            }
        }

        /// <summary>드롭된 스텝을 대상 스텝 위치로 옮긴다. 실제 처리는 MoveStepCommand가 한다.</summary>
        private void Step_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("RecipeStepModel"))
            {
                var sourceStep = e.Data.GetData("RecipeStepModel") as RecipeStepModel;
                var border = sender as Border;
                if (border == null) return;

                var targetStep = border.DataContext as RecipeStepModel;
                if (sourceStep != null && targetStep != null && sourceStep != targetStep)
                {
                    var viewModel = DataContext as RecipeEditorViewModel;
                    if (viewModel != null)
                    {
                        var parameter = new Tuple<RecipeStepModel, RecipeStepModel>(sourceStep, targetStep);
                        if (viewModel.MoveStepCommand.CanExecute(parameter))
                        {
                            viewModel.MoveStepCommand.Execute(parameter);
                        }
                    }
                }
            }
        }

        /// <summary>스텝을 클릭하면 ViewModel의 SelectedStep으로 지정한다.</summary>
        private void Step_Select(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is RecipeStepModel step)
            {
                if (DataContext is RecipeEditorViewModel vm)
                {
                    vm.SelectedStep = step;
                }
            }
        }

        private void CameraTargetCheckBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                checkBox.IsChecked = checkBox.IsChecked != true;
                e.Handled = true;
            }
        }

        /// <summary>JOG 버튼을 누르면 Tag가 가리키는 축·방향으로 StartJog를 호출한다. 예외는 무시한다.</summary>
        private void Jog_Down(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.Tag is string tag)
                {
                    if (DataContext is RecipeEditorViewModel vm)
                    {
                        switch (tag)
                        {
                            case "X+": vm.StartJog(ServoAxis.X, true); break;
                            case "X-": vm.StartJog(ServoAxis.X, false); break;
                            case "Y+": vm.StartJog(ServoAxis.Y, true); break;
                            case "Y-": vm.StartJog(ServoAxis.Y, false); break;
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>JOG 버튼에서 손을 떼면 StopJog를 호출한다. 예외는 무시한다.</summary>
        private void Jog_Up(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.Tag is string tag)
                {
                    if (DataContext is RecipeEditorViewModel vm)
                    {
                        switch (tag)
                        {
                            case "X+":
                            case "X-": 
                                vm.StopJog(ServoAxis.X, tag.EndsWith("+")); 
                                break;
                            case "Y+":
                            case "Y-": 
                                vm.StopJog(ServoAxis.Y, tag.EndsWith("+")); 
                                break;
                        }
                    }
                }
            }
            catch { }
        }
    }
}
