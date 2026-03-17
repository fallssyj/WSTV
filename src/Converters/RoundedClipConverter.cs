using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace WSTV.Converters;

/// <summary>
/// 将 Border 的 [ActualWidth, ActualHeight, CornerRadius, BorderThickness] 转换为
/// RectangleGeometry Clip，使子元素渲染被圆角区域精确裁剪，同时保留边框笔画。
/// 用法（MultiBinding 绑定到 Border.Clip，RelativeSource=Self）：
///   Values[0] = ActualWidth
///   Values[1] = ActualHeight
///   Values[2] = CornerRadius
///   Values[3] = BorderThickness
/// </summary>
public class RoundedClipConverter : IMultiValueConverter
{
    public static readonly RoundedClipConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4
            || values[0] is not double width
            || values[1] is not double height
            || values[2] is not CornerRadius cr
            || values[3] is not Thickness bt)
            return DependencyProperty.UnsetValue;

        // 收缩半个边框厚度，避免笔画被裁掉（WPF 边框笔画居中对齐）
        var half = bt.Left / 2.0;
        var r = cr.TopLeft;

        var rect = new Rect(
            half, half,
            Math.Max(0, width - half * 2),
            Math.Max(0, height - half * 2));

        var geo = new RectangleGeometry(rect, r, r);
        geo.Freeze();
        return geo;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
