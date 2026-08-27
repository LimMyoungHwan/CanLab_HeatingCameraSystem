using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace HeatingCameraSystem.Master;

/// <summary>
/// MainWindow.xaml의 상호작용 논리. 코드 비하인드는 컴포넌트 초기화만 담당하며
/// 화면 상태와 명령은 ViewModel과 XAML 바인딩이 처리한다.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}