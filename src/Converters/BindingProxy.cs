using System.Windows;

namespace WSTV.Converters;

/// <summary>
/// Freezable 子类，用于在 visual tree 断裂场景（如 FlyleafHost 内部）中
/// 将外层 DataContext 作为 StaticResource 传递给内部绑定。
/// </summary>
public class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy));

    public object Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}
