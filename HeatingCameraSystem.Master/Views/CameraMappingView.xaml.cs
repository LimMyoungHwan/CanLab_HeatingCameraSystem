using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HeatingCameraSystem.Master.ViewModels;

namespace HeatingCameraSystem.Master.Views
{
    public partial class CameraMappingView : UserControl
    {
        private Point _dragStartPoint;

        public CameraMappingView()
        {
            InitializeComponent();
        }

        private void CameraItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void CameraItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var border = sender as Border;
                    if (border == null) return;
                    
                    var camera = border.DataContext as MappingCamera;
                    if (camera == null || camera.IsAssigned) return;

                    DataObject dragData = new DataObject("MappingCamera", camera);
                    DragDrop.DoDragDrop(border, dragData, DragDropEffects.Move);
                }
            }
        }

        private void Slot_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("MappingCamera"))
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Slot_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("MappingCamera"))
            {
                var camera = e.Data.GetData("MappingCamera") as MappingCamera;
                var border = sender as Border;
                if (border == null) return;

                var slot = border.DataContext as MappingSlot;
                if (camera != null && slot != null)
                {
                    var viewModel = DataContext as CameraMappingViewModel;
                    if (viewModel != null)
                    {
                        var parameter = new Tuple<MappingCamera, MappingSlot>(camera, slot);
                        if (viewModel.AssignCameraToSlotCommand.CanExecute(parameter))
                        {
                            viewModel.AssignCameraToSlotCommand.Execute(parameter);
                        }
                    }
                }
            }
        }

        private void Slot_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Right click to unassign
            var border = sender as Border;
            if (border == null) return;

            var slot = border.DataContext as MappingSlot;
            if (slot != null && slot.HasCamera)
            {
                var viewModel = DataContext as CameraMappingViewModel;
                if (viewModel != null)
                {
                    if (viewModel.UnassignSlotCommand.CanExecute(slot))
                    {
                        viewModel.UnassignSlotCommand.Execute(slot);
                    }
                }
            }
        }
    }
}
