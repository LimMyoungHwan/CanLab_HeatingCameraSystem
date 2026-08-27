using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HeatingCameraSystem.Master.ViewModels;

namespace HeatingCameraSystem.Master.Views
{
    /// <summary>
    /// 대시보드 화면의 코드 비하인드. WPF 이벤트라 코드 비하인드가 맡을 수밖에 없는
    /// 카메라 드래그 앤 드롭 배선만 담당하고, 실제 슬롯 배치 변경은
    /// DashboardViewModel의 명령에 위임한다. 뷰 모드가 1일 때는 배치 편집을 막는다.
    /// </summary>
    public partial class DashboardView : UserControl
    {
        private Point _dragStartPoint;

        public DashboardView()
        {
            InitializeComponent();
        }

        /// <summary>드래그 판정용 시작 좌표를 기록한다.</summary>
        private void CameraItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        /// <summary>최소 드래그 거리를 넘으면 CameraNode를 실은 드래그를 시작한다.</summary>
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
                    
                    var camera = border.DataContext as CameraNode;
                    if (camera == null) return;

                    DataObject dragData = new DataObject("CameraNode", camera);
                    DragDrop.DoDragDrop(border, dragData, DragDropEffects.Move);
                }
            }
        }

        /// <summary>CameraNode 드래그가 아니거나 뷰 모드가 1이면 드롭을 거부한다.</summary>
        private void Slot_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("CameraNode") || 
                (DataContext is DashboardViewModel vm && vm.CurrentViewMode == 1))
            {
                e.Effects = DragDropEffects.None;
            }
        }

        /// <summary>드롭된 카메라를 슬롯에 배치한다. 실제 처리는 AssignCameraToDashboardSlotCommand가 한다.</summary>
        private void Slot_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("CameraNode"))
            {
                var camera = e.Data.GetData("CameraNode") as CameraNode;
                var border = sender as Border;
                if (border == null) return;

                var slot = border.DataContext as DashboardSlot;
                if (camera != null && slot != null)
                {
                    var viewModel = DataContext as DashboardViewModel;
                    if (viewModel != null && viewModel.CurrentViewMode != 1)
                    {
                        var parameter = new Tuple<CameraNode, DashboardSlot>(camera, slot);
                        if (viewModel.AssignCameraToDashboardSlotCommand.CanExecute(parameter))
                        {
                            viewModel.AssignCameraToDashboardSlotCommand.Execute(parameter);
                        }
                    }
                }
            }
        }

        /// <summary>카메라가 배치된 슬롯을 우클릭하면 UnassignDashboardSlotCommand로 배치를 해제한다.</summary>
        private void Slot_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;

            var slot = border.DataContext as DashboardSlot;
            if (slot != null && slot.HasCamera)
            {
                var viewModel = DataContext as DashboardViewModel;
                if (viewModel != null && viewModel.CurrentViewMode != 1)
                {
                    if (viewModel.UnassignDashboardSlotCommand.CanExecute(slot))
                    {
                        viewModel.UnassignDashboardSlotCommand.Execute(slot);
                    }
                }
            }
        }
    }
}
