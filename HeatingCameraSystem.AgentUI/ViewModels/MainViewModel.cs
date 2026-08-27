using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HeatingCameraSystem.AgentUI.ViewModels
{
    /// <summary>
    /// AgentUI 창의 루트 뷰모델: 로컬 카메라마다 라이브 뷰 패널 하나를 가진다.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _headerText;

        public ObservableCollection<CameraPanelViewModel> Cameras { get; } = new();

        public DataBrowserViewModel? DataBrowser { get; set; }

        public LogViewerViewModel? Logs { get; set; }

        public SettingsViewModel? Settings { get; set; }

        public MainViewModel(string headerText)
        {
            _headerText = headerText;
        }
    }
}
