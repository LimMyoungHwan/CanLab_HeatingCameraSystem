using System.IO;
using System.Windows;
using HeatingCameraSystem.Master.Localization;

namespace HeatingCameraSystem.Master.Views
{
    /// <summary>
    /// 레시피 시작 전 저장 루트와 제품 번호를 받는 모달. 센서 번호는 카메라에서 자동으로 읽으므로
    /// 운영자가 입력하지 않는다.
    /// </summary>
    public partial class ProductionRunDialog : Window
    {
        public ProductionRunDialog(string initialPath, string initialProductNumber)
        {
            InitializeComponent();

            Title = L("Dialog_ProductionRun_Title");
            HintText.Text = L("Dialog_ProductionRun_Hint");
            SaveRootLabel.Text = L("Dialog_ProductionRun_SaveRoot");
            ProductLabel.Text = L("Dialog_ProductionRun_ProductNumber");
            BrowseButton.Content = L("Dialog_ProductionRun_Browse");
            OkButton.Content = L("Dialog_ProductionRun_Ok");
            CancelButton.Content = L("Dialog_ProductionRun_Cancel");

            LocalPathWarning.Text = L("Dialog_ProductionRun_LocalPathWarning");

            SaveRootBox.Text = initialPath;
            ProductBox.Text = initialProductNumber;
            SaveRootBox.TextChanged += (_, _) => UpdatePreview();
            ProductBox.TextChanged += (_, _) => UpdatePreview();
            UpdatePreview();
        }

        public string SaveRootPath => SaveRootBox.Text.Trim();

        public string ProductNumber => ProductBox.Text.Trim();

        private static string L(string key) => LocalizationManager.Instance[key];

        private void UpdatePreview()
        {
            string suffix = Services.RecipeEngine.NextRunProductNumber(SaveRootPath, ProductNumber);
            string product = string.IsNullOrWhiteSpace(suffix) ? "" : "_" + suffix;
            PreviewText.Text = Path.Combine(SaveRootPath, $"544112136{product}", "RPP40", "cold", "BB20_000.raw");

            // 이 경로로 복사하는 주체는 Master가 아니라 카메라 PC다. 로컬 경로를 고르면 카메라 PC가
            // "자기" 드라이브의 같은 경로에 쓰고 조용히 성공하므로, Master 쪽 폴더는 끝까지 비어 있다.
            LocalPathWarning.Visibility = SaveRootPath.Length > 0 && !SaveRootPath.StartsWith(@"\\", StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Multiselect = false };
            if (Directory.Exists(SaveRootPath)) picker.InitialDirectory = SaveRootPath;
            if (picker.ShowDialog(this) == true) SaveRootBox.Text = picker.FolderName;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SaveRootPath))
            {
                ShowError(L("Dialog_ProductionRun_NeedPath"));
                return;
            }

            DialogResult = true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
