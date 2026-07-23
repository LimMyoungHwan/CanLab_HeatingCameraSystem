using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HeatingCameraSystem.AgentUI.ViewModels
{
    /// <summary>
    /// Root view model for the AgentUI window: a live-view panel per local camera.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _headerText;

        public ObservableCollection<CameraPanelViewModel> Cameras { get; } = new();

        public MainViewModel(string headerText)
        {
            _headerText = headerText;
        }
    }
}
