using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HeatingCameraSystem.Master.ViewModels;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Views
{
    public partial class RecipeEditorView : UserControl
    {
        private Point _dragStartPoint;

        public RecipeEditorView()
        {
            InitializeComponent();
        }

        private void Step_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

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

        private void Step_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("RecipeStepModel"))
            {
                e.Effects = DragDropEffects.None;
            }
        }

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
