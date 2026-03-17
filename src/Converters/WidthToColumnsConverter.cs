using System.Globalization;
using System.Windows.Data;

namespace WSTV.Converters
{
    public class WidthToColumnsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                double minItemWidth = parameter is string s && double.TryParse(s, out double p) ? p : 180;
                return Math.Max(1, (int)(width / minItemWidth));
            }
            return 2;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
