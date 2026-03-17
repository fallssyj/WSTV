using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace WSTV.Converters;

/// <summary>int == ConverterParameter  →  bool（用于 RadioButton.IsChecked）</summary>
[ValueConversion(typeof(int), typeof(bool))]
public class IntEqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i && parameter is string s && int.TryParse(s, out int p))
            return i == p;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string s && int.TryParse(s, out int p))
            return p;
        return Binding.DoNothing;
    }
}

/// <summary>int == ConverterParameter  →  Visible / Collapsed</summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public class IntEqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i && parameter is string s && int.TryParse(s, out int p))
            return i == p ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>[0]==selectedIndex, [1]==thisIndex → Visible/Collapsed（线路 Tab 正在播放标签）</summary>
public class IntEqualityToVisibilityMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is int a && values[1] is int b)
            return a == b ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>[0]==selectedIndex, [1]==thisIndex → 高亮 Brush / Transparent（线路 Tab 行背景）</summary>
public class IntEqualityToBrushMultiConverter : IMultiValueConverter
{
    public Brush MatchBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
    public Brush DefaultBrush { get; set; } = Brushes.Transparent;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is int a && values[1] is int b)
            return a == b ? MatchBrush : DefaultBrush;
        return DefaultBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
