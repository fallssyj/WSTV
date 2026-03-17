using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace WSTV.Converters;

/// <summary>
/// 附加行为：将指定条目滚动到可视区域。
/// 触发时机：① ScrollToItem 属性变化（新引用）② 宿主控件由不可见变可见（Tab 切换）③ 容器生成完毕（ItemsSource 异步加载）
/// </summary>
public static class ScrollToItemBehavior
{
    private static readonly DependencyProperty IsHookedProperty =
        DependencyProperty.RegisterAttached("IsHooked", typeof(bool), typeof(ScrollToItemBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ScrollToItemProperty =
        DependencyProperty.RegisterAttached(
            "ScrollToItem",
            typeof(object),
            typeof(ScrollToItemBehavior),
            new PropertyMetadata(null, OnScrollToItemChanged));

    public static object GetScrollToItem(DependencyObject obj) =>
        obj.GetValue(ScrollToItemProperty);

    public static void SetScrollToItem(DependencyObject obj, object value) =>
        obj.SetValue(ScrollToItemProperty, value);

    private static void OnScrollToItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl ic) return;
        EnsureHooked(ic);
        if (e.NewValue is not null)
            ScheduleScroll(ic, e.NewValue);
    }

    /// <summary>确保只挂一次事件</summary>
    private static void EnsureHooked(ItemsControl ic)
    {
        if ((bool)ic.GetValue(IsHookedProperty)) return;
        ic.SetValue(IsHookedProperty, true);

        // ② Tab 切换：控件由不可见变可见时重新滚动
        ic.IsVisibleChanged += (s, e) =>
        {
            if (s is ItemsControl ctrl && (bool)e.NewValue)
                ScheduleScroll(ctrl, ctrl.GetValue(ScrollToItemProperty));
        };

        // ③ ItemsSource 加载完毕后容器生成时重新滚动
        ic.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (ic.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
                ScheduleScroll(ic, ic.GetValue(ScrollToItemProperty));
        };
    }

    private static void ScheduleScroll(ItemsControl ic, object? target)
    {
        if (target is null) return;
        ic.Dispatcher.InvokeAsync(() =>
        {
            ic.UpdateLayout();
            (ic.ItemContainerGenerator.ContainerFromItem(target) as FrameworkElement)?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }
}
