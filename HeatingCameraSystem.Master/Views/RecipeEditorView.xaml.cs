using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HeatingCameraSystem.Master.ViewModels;

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
    }
}
