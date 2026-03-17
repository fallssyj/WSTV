using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Windows;
using WSTV.Messages;

namespace WSTV.ViewModels;

public enum AppTab { Channels, Favorites, Config, Settings }

public partial class MainViewModel : ObservableObject,
    IRecipient<PlayChannelMessage>,
    IRecipient<NavigateBackMessage>,
    IRecipient<NavigateToAboutMessage>
{
    [ObservableProperty] private string _title = $"WSTV";
    // 各页面 ViewModel 单例，切换时复用状态
    private readonly ChannelsViewModel _channelsVm = new();
    private readonly FavoritesViewModel _favoritesVm = new();
    private readonly ConfigViewModel _configVm = new();
    private readonly SettingsViewModel _settingsVm = new();
    private readonly AboutViewModel _aboutVm = new();
    private PlayChannelViewModel? _playVm;

    // 供 MainWindow.xaml 直接绑定 DataContext
    public ChannelsViewModel ChannelsPage => _channelsVm;
    public FavoritesViewModel FavoritesPage => _favoritesVm;
    public ConfigViewModel ConfigPage => _configVm;
    public SettingsViewModel SettingsPage => _settingsVm;
    public PlayChannelViewModel? PlayPage => _playVm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsChannelsActive),
        nameof(IsFavoritesActive),
        nameof(IsConfigActive),
        nameof(IsSettingsActive),
        nameof(IsPlayActive))]
    private AppTab _activeTab = AppTab.Channels;

    // 各 Tab 是否处于激活状态，供底部导航栏高亮绑定
    public bool IsChannelsActive => ActiveTab == AppTab.Channels && !IsPlayActive;
    public bool IsFavoritesActive => ActiveTab == AppTab.Favorites && !IsPlayActive;
    public bool IsConfigActive => ActiveTab == AppTab.Config && !IsPlayActive;
    public bool IsSettingsActive => ActiveTab == AppTab.Settings && !IsPlayActive && !IsAboutActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsChannelsActive),
        nameof(IsFavoritesActive),
        nameof(IsConfigActive),
        nameof(IsSettingsActive),
        nameof(IsNavVisible))]
    private bool _isPlayActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive), nameof(IsNavVisible))]
    private bool _isAboutActive;

    public AboutViewModel AboutPage => _aboutVm;

    /// <summary>播放或关于页时隐藏底部导航栏</summary>
    public bool IsNavVisible => !IsPlayActive && !IsAboutActive;

    public MainViewModel()
    {
        WeakReferenceMessenger.Default.Register<PlayChannelMessage>(this);
        WeakReferenceMessenger.Default.Register<NavigateBackMessage>(this);
        WeakReferenceMessenger.Default.Register<NavigateToAboutMessage>(this);
    }

    public void Receive(PlayChannelMessage message)
    {
        _playVm = new PlayChannelViewModel(message.Value.Current, message.Value.Channels);
        OnPropertyChanged(nameof(PlayPage));
        IsPlayActive = true;
    }

    public void Receive(NavigateToAboutMessage message)
    {
        IsAboutActive = true;
    }

    public void Receive(NavigateBackMessage message)
    {
        // 从关于页返回 → 切回设置页
        if (IsAboutActive)
        {
            IsAboutActive = false;
            return;
        }

        var vm = _playVm;           // 保留待释放的 VM 引用
        IsPlayActive = false;
        _playVm = null;
        OnPropertyChanged(nameof(PlayPage)); // ContentControl 移除 PlayChannelView + FlyleafHost

        // 等 WPF 完成当前渲染帧（FlyleafHost Unloaded / GPU 变换链脱离）
        // 再 Dispose Player，确保显卡资源在设备无引用后才被释放
        Application.Current?.Dispatcher.InvokeAsync(
            () => vm?.Dispose(),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>主窗口关闭时调用：同步强制释放播放 VM，确保 PiP 浮动窗口随主进程一起关闭</summary>
    public void DisposePlayVm()
    {
        var vm = _playVm;
        if (vm is null) return;
        _playVm = null;
        vm.Dispose();
    }

    [RelayCommand]
    private void Minimize() => Application.Current.MainWindow.WindowState = WindowState.Minimized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMaximized))]
    private WindowState _windowState = WindowState.Normal;

    public bool IsMaximized => WindowState == WindowState.Maximized;

    [RelayCommand]
    private void Maximize()
    {
        var win = Application.Current.MainWindow;
        win.WindowState = win.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        WindowState = win.WindowState;
    }

    [RelayCommand]
    private void Close() => Application.Current.MainWindow.Close();

    [RelayCommand]
    private void GoToChannels() => SwitchTo(AppTab.Channels);

    [RelayCommand]
    private void GoToFavorites() => SwitchTo(AppTab.Favorites);

    [RelayCommand]
    private void GoToConfig() => SwitchTo(AppTab.Config);

    [RelayCommand]
    private void GoToSettings() => SwitchTo(AppTab.Settings);

    private void SwitchTo(AppTab tab)
    {
        ActiveTab = tab;
    }
}
