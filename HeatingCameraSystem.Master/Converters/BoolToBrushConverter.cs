using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace HeatingCameraSystem.Master.Converters
{
    /// <summary>
    /// bool을 브러시로 바꾸는 컨버터. true면 OnBrush, false거나 bool이 아니면 OffBrush다.
    /// 두 브러시는 XAML에서 속성으로 바꿔 끼울 수 있으며 역변환은 지원하지 않는다.
    /// </summary>
    public class BoolToBrushConverter : IValueConverter
    {
        public Brush OnBrush { get; set; } = Brushes.LimeGreen;
        public Brush OffBrush { get; set; } = Brushes.Gray;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? OnBrush : OffBrush;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
