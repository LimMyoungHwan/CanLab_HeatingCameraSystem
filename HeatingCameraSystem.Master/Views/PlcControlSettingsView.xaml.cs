using System.Windows.Controls;

namespace HeatingCameraSystem.Master.Views
{
    /// <summary>
    /// PLC 제어 설정 화면의 코드 비하인드. 컴포넌트 초기화만 담당하며
    /// 상태와 명령은 PlcControlSettingsViewModel에 있다.
    /// </summary>
    public partial class PlcControlSettingsView : UserControl
    {
        public PlcControlSettingsView() => InitializeComponent();
    }
}
