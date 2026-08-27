using System.Windows.Controls;

namespace HeatingCameraSystem.Master.Views
{
    /// <summary>
    /// 상태 모니터 화면의 코드 비하인드. 컴포넌트 초기화만 담당하며
    /// 상태와 명령은 StatusMonitorViewModel에 있다.
    /// </summary>
    public partial class StatusMonitorView : UserControl
    {
        public StatusMonitorView() => InitializeComponent();
    }
}
