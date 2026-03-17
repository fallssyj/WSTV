using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WSTV.View;

public enum DialogIcon { Info, Warning, Error, Question }

public partial class AppDialog : Window
{
    public bool? DialogAnswer { get; private set; }

    private AppDialog(string title, string message, DialogIcon icon, bool showCancel)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;

        // 图标颜色
        IconPath.Data = icon switch
        {
            DialogIcon.Warning => (Geometry)FindResource("CircleInfo"),
            DialogIcon.Error => (Geometry)FindResource("CircleInfo"),
            DialogIcon.Question => (Geometry)FindResource("CircleInfo"),
            _ => (Geometry)FindResource("CircleInfo"),
        };
        IconPath.Fill = icon switch
        {
            DialogIcon.Warning => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
            DialogIcon.Error => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
            DialogIcon.Question => (Brush)FindResource("AccentPrimaryBrush"),
            _ => (Brush)FindResource("AccentPrimaryBrush"),
        };

        if (showCancel)
        {
            var cancel = MakeButton("取消", isAccent: false);
            cancel.Click += (_, _) => { DialogAnswer = false; Close(); };
            ButtonPanel.Children.Add(cancel);
        }

        var confirm = MakeButton(showCancel ? "确认" : "好的", isAccent: true);
        confirm.Click += (_, _) => { DialogAnswer = true; Close(); };
        ButtonPanel.Children.Add(confirm);
    }

    private static Button MakeButton(string text, bool isAccent)
    {
        var btn = new Button
        {
            Content = new TextBlock { Text = text },
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 72,
        };
        btn.SetResourceReference(StyleProperty,
            isAccent ? "AccentButtonStyle" : "ActionButtonStyle");
        return btn;
    }

    private static AppDialog Create(string title, string message, DialogIcon icon, bool showCancel)
    {
        var dlg = new AppDialog(title, message, icon, showCancel);
        var owner = Application.Current?.MainWindow;
        if (owner != null && owner != dlg && owner.IsLoaded)
            dlg.Owner = owner;
        return dlg;
    }

    /// <summary>显示纯提示框（只有"好的"按钮）</summary>
    public static void Show(string message, string title = "提示", DialogIcon icon = DialogIcon.Info)
    {
        Create(title, message, icon, showCancel: false).ShowDialog();
    }

    /// <summary>显示确认框，返回 true 表示用户点击"确认"</summary>
    public static bool Confirm(string message, string title = "确认", DialogIcon icon = DialogIcon.Question)
    {
        var dlg = Create(title, message, icon, showCancel: true);
        dlg.ShowDialog();
        return dlg.DialogAnswer == true;
    }
}
