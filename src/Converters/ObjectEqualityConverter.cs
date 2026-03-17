using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace WSTV.Converters;

/// <summary>
/// [0] Equals [1] → MatchBrush / DefaultBrush（前景/背景高亮用）。
/// 未设置 Brush 时返回 bool，可直接用于 BoolToVis 等场景。
/// </summary>
public class ObjectEqualityMultiConverter : IMultiValueConverter
{
    public Brush? MatchBrush { get; set; }
    public Brush? DefaultBrush { get; set; }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2) return DependencyProperty.UnsetValue;
        bool equal = Equals(values[0], values[1]);

        if (MatchBrush is not null)
            return equal ? MatchBrush : (DefaultBrush ?? Brushes.Transparent);

        if (targetType == typeof(Visibility))
            return equal ? Visibility.Visible : Visibility.Collapsed;

        return equal;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
