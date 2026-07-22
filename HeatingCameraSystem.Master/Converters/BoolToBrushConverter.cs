using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace HeatingCameraSystem.Master.Converters
{
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
