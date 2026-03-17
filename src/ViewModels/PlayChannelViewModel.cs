using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using WSTV.Messages;
using WSTV.Models;
using WSTV.Services;

namespace WSTV.ViewModels;

/// <summary>分组摘要，用于"分类"Tab</summary>
public class GroupSummary
{
    public string Title { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>带序号的线路条目，用于"线路"Tab</summary>
public class IndexedLink
{
    public int Index { get; set; }
    public string Url { get; set; } = string.Empty;
    public int DisplayIndex => Index + 1;
}

public partial class PlayChannelViewModel : ObservableObject, IRecipient<FavoriteChangedMessage>, IDisposable
{
    [ObservableProperty] private Channel _currentChannel;
    [ObservableProperty] private ObservableCollection<Channel> _channels;

    // ── Tab ──────────────────────────────────────────────
    [ObservableProperty] private int _selectedTabIndex = 1;

    // ── 视频信息面板 ──────────────────────────────────────
    [ObservableProperty] private bool _isInfoVisible = false;

    // ── 视频滤镜面板 ─────────────────────────────────────────
    [ObservableProperty] private bool _isVideoFilterVisible = false;
    public VideoFilterCardViewModel VideoFilter { get; private set; } = null!;

    // ── 画中画 ───────────────────────────────────────────
    /// <summary>FlyleafHost 实例（IsAttached=false 表示已浮动/PiP）</summary>
    private FlyleafHost? FlyleafHostControl => Player.Host as FlyleafHost;

    /// <summary>true = 当前处于画中画浮动模式</summary>
    public bool IsPiP => !(FlyleafHostControl?.IsAttached ?? true);

    [RelayCommand]
    private void TogglePip()
    {
        var host = FlyleafHostControl;
        if (host is null) return;
        bool attached = host.IsAttached;
        host.KeepRatioOnResize = attached;
        if (attached)
            host.PreferredLandscapeWidth = 600;
        host.IsAttached = !attached;
        OnPropertyChanged(nameof(IsPiP));
    }

    // ── EPG 加载状态 ─────────────────────────────────────
    [ObservableProperty] private bool _isEpgLoading = false;

    // ── 线路失败记录（自动切换用）──────────────────────────
    private readonly HashSet<int> _failedLinks = new();

    // ── IDisposable ──────────────────────────────────────
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _epgTimer.Stop();
        Player.PropertyChanged -= OnPlayerPropertyChanged;

        // 若处于画中画浮动状态，先收回窗口再释放，否则 Win32 浮动窗口会残留
        var host = FlyleafHostControl;
        if (host is not null && !host.IsAttached)
            host.IsAttached = true;

        Player?.Stop();
        Player?.Dispose();
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    [RelayCommand]
    private void ToggleInfo()
    {
        IsInfoVisible = !IsInfoVisible;
        if (IsInfoVisible)
        {
            IsVideoFilterVisible = false;
            RefreshStreamInfo();
        }
    }

    [RelayCommand]
    private void ToggleVideoFilter()
    {
        IsVideoFilterVisible = !IsVideoFilterVisible;
        if (IsVideoFilterVisible)
            IsInfoVisible = false;
    }

    // ── 视频流信息 ────────────────────────────────────────────
    public string VideoBitRateText
    {
        get
        {
            var br = Player?.VideoDecoder?.VideoStream?.BitRate ?? 0;
            return br > 0 ? $"{br / 1000.0:0.##} K" : "";
        }
    }

    public string VideoColorTypeText
        => Player?.VideoDecoder?.VideoStream?.ColorType.ToString() ?? "";

    // ── 音频流信息 ────────────────────────────────────────────
    public string AudioStreamName
    {
        get
        {
            var s = Player?.AudioDecoder?.AudioStream;
            if (s == null) return "";
            return !string.IsNullOrEmpty(s.Title) ? s.Title : s.Codec ?? "";
        }
    }

    public string AudioStreamLanguage
    {
        get
        {
            var lang = Player?.AudioDecoder?.AudioStream?.Language?.ToString();
            return string.IsNullOrEmpty(lang) ? "无" : lang;
        }
    }

    public string AudioStreamCodecText => Player?.AudioDecoder?.AudioStream?.Codec ?? "";

    public string AudioChannelLayoutText
    {
        get
        {
            return (Player?.Audio?.Channels ?? 0) switch
            {
                1 => "单声道",
                2 => "立体声",
                6 => "5.1声道",
                8 => "7.1声道",
                var n when n > 0 => $"{n}声道",
                _ => ""
            };
        }
    }

    public string AudioBitRateText
    {
        get
        {
            var br = Player?.AudioDecoder?.AudioStream?.BitRate ?? 0;
            return br > 0 ? $"{br / 1000.0:0.##} K" : "";
        }
    }

    public string AudioSampleRateText
    {
        get
        {
            var sr = Player?.Audio?.SampleRate ?? 0;
            return sr > 0 ? $"{sr / 1000.0:0.#} kHz" : "";
        }
    }

    private void RefreshStreamInfo()
    {
        OnPropertyChanged(nameof(VideoBitRateText));
        OnPropertyChanged(nameof(VideoColorTypeText));
        OnPropertyChanged(nameof(AudioStreamName));
        OnPropertyChanged(nameof(AudioStreamLanguage));
        OnPropertyChanged(nameof(AudioStreamCodecText));
        OnPropertyChanged(nameof(AudioChannelLayoutText));
        OnPropertyChanged(nameof(AudioBitRateText));
        OnPropertyChanged(nameof(AudioSampleRateText));
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        // 切到 EPG Tab 时重新通知，让 ScrollToItemBehavior 触发滚动
        if (value == 2)
            OnPropertyChanged(nameof(NowPlayingProgram));
    }

    // ── 分类 Tab ─────────────────────────────────────────
    public ObservableCollection<GroupSummary> GroupSummaries { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredChannels))]
    private GroupSummary? _selectedGroup;

    // ── 频道 Tab ─────────────────────────────────────────
    public IEnumerable<Channel> FilteredChannels =>
        SelectedGroup is null || SelectedGroup.Title == "全部"
            ? Channels
            : Channels.Where(c => c.GroupTitle == SelectedGroup.Title);

    // ── 线路 Tab ─────────────────────────────────────────
    [ObservableProperty] private int _selectedLinkIndex = 0;
    public ObservableCollection<IndexedLink> IndexedLinks { get; } = new();

    // ── 节目 Tab (EPG) ───────────────────────────────────
    // 缓存字段——避免每次 UI 访问属性都重新查询 EpgService
    private IReadOnlyList<EpgProgram> _epgPrograms = Array.Empty<EpgProgram>();
    private EpgProgram? _nowPlayingProgram;
    private bool _hasEpg;

    public IReadOnlyList<EpgProgram> EpgPrograms => _epgPrograms;
    public EpgProgram? NowPlayingProgram => _nowPlayingProgram;
    public bool HasEpg => _hasEpg;
    public bool HasNoEpg => !_hasEpg;

    /// <summary>重新查询 EpgService，更新所有缓存字段</summary>
    private void RefreshEpgCache()
    {
        _hasEpg = EpgService.Instance.HasEpgFor(
            CurrentChannel?.TvgId ?? string.Empty,
            CurrentChannel?.DisplayName ?? string.Empty);
        _epgPrograms = EpgService.Instance.GetPrograms(
            CurrentChannel?.TvgId ?? string.Empty,
            DateTime.Today,
            CurrentChannel?.DisplayName ?? string.Empty);
        _nowPlayingProgram = EpgService.Instance.GetNowPlaying(
            CurrentChannel?.TvgId ?? string.Empty,
            CurrentChannel?.DisplayName ?? string.Empty);
    }

    /// <summary>音量（0-150），双向同步到 Player.Audio.Volume</summary>
    public int Volume
    {
        get => Player?.Audio.Volume ?? 100;
        set
        {
            if (Player != null)
            {
                Player.Audio.Volume = value;
                OnPropertyChanged();
            }
        }
    }


    public Player Player { get; set; }
    public Config Config { get; set; }

    private readonly DispatcherTimer _epgTimer;

    public PlayChannelViewModel(Channel current, IReadOnlyList<Channel> channels)
    {
        _currentChannel = current;
        _channels = new ObservableCollection<Channel>(channels);

        Config = PlayerConfigFactory.CreateConfig();
        Player = new Player(Config);
        VideoFilter = new VideoFilterCardViewModel(Config, () => IsVideoFilterVisible = false);
        // 监听播放状态，当失败时自动尝试下一条线路
        Player.PropertyChanged += OnPlayerPropertyChanged;

        OpenCurrentChannel();
        BuildGroupSummaries();
        RebuildIndexedLinks();
        RefreshEpgCache();

        WeakReferenceMessenger.Default.Register<FavoriteChangedMessage>(this);

        // EPG：异步加载，不阻塞 UI
        if (!EpgService.Instance.HasAnyData)
            LoadEpgAsync();

        // 每 30 秒刷新一次节目状态（正在播放 / 已结束 / 未开始）
        _epgTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _epgTimer.Tick += (_, _) => RefreshEpgStatus();
        _epgTimer.Start();
    }

    private void BuildGroupSummaries()
    {
        GroupSummaries.Clear();
        GroupSummaries.Add(new GroupSummary { Title = "全部", Count = Channels.Count });
        foreach (var g in Channels.GroupBy(c => c.GroupTitle))
            GroupSummaries.Add(new GroupSummary { Title = g.Key, Count = g.Count() });
        SelectedGroup = GroupSummaries.FirstOrDefault(g => g.Title == "全部")
                        ?? GroupSummaries.FirstOrDefault();
    }

    [RelayCommand]
    private void SelectGroup(GroupSummary group)
    {
        SelectedGroup = group;
        SelectedTabIndex = 1;   // 点分类后自动切到"频道"Tab
    }

    [RelayCommand]
    private void SelectChannel(Channel channel)
    {
        CurrentChannel = channel;
    }

    partial void OnCurrentChannelChanged(Channel value)
    {
        _failedLinks.Clear();
        SelectedLinkIndex = 0;
        // FilteredChannels 依赖 SelectedGroup 和 Channels，不需要在此通知
        RebuildIndexedLinks();
        RefreshEpgCache();
        OnPropertyChanged(nameof(EpgPrograms));
        OnPropertyChanged(nameof(NowPlayingProgram));
        OnPropertyChanged(nameof(HasEpg));
        OnPropertyChanged(nameof(HasNoEpg));
        OpenCurrentChannel();
    }

    private void RebuildIndexedLinks()
    {
        IndexedLinks.Clear();
        if (CurrentChannel is null) return;
        for (int i = 0; i < CurrentChannel.Links.Count; i++)
            IndexedLinks.Add(new IndexedLink { Index = i, Url = CurrentChannel.Links[i] });
    }

    private void OpenCurrentChannel()
    {
        if (CurrentChannel?.Links.Count > 0)
            Player.OpenAsync(CurrentChannel.Links[SelectedLinkIndex]);
    }

    [RelayCommand]
    private void SwitchLink(IndexedLink link)
    {
        if (link is null) return;
        SelectedLinkIndex = link.Index;
        Player.OpenAsync(link.Url);
    }

    private void RefreshEpgStatus()
    {
        // 更新当前播放节目
        _nowPlayingProgram = EpgService.Instance.GetNowPlaying(
            CurrentChannel?.TvgId ?? string.Empty,
            CurrentChannel?.DisplayName ?? string.Empty);
        // 每个 EpgProgram 自运行 PropertyChanged，避免重建容器
        foreach (var p in _epgPrograms)
            p.NotifyStatusChanged();
        OnPropertyChanged(nameof(NowPlayingProgram));
    }

    private int _epgLoadingFlag = 0;

    private void LoadEpgAsync()
    {
        // 防重入：若已有加载任务进行中，直接返回
        if (Interlocked.CompareExchange(ref _epgLoadingFlag, 1, 0) != 0) return;
        IsEpgLoading = true;
        _ = Task.Run(EpgService.Instance.Reload)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    IsEpgLoading = false;
                    Interlocked.Exchange(ref _epgLoadingFlag, 0);
                    return;
                }
                RefreshEpgCache();
                OnPropertyChanged(nameof(HasEpg));
                OnPropertyChanged(nameof(HasNoEpg));
                OnPropertyChanged(nameof(EpgPrograms));
                OnPropertyChanged(nameof(NowPlayingProgram));
                IsEpgLoading = false;
                Interlocked.Exchange(ref _epgLoadingFlag, 0);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        var channel = CurrentChannel;
        if (channel is null) return;
        channel.Favorite = !channel.Favorite;
        if (channel.Favorite)
            ConfigService.Instance.AddFavorite(channel);
        else
            ConfigService.Instance.RemoveFavorite(channel);
        WeakReferenceMessenger.Default.Send(new FavoriteChangedMessage(channel.Favorite));
    }

    public void Receive(FavoriteChangedMessage message)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (CurrentChannel is null) return;
            CurrentChannel.Favorite = ConfigService.Instance.IsFavorite(CurrentChannel);
        });
    }

    [RelayCommand]
    private void GoBack()
    {
        // 返回前先退出画中画，防止浮动窗口孤立
        var host = FlyleafHostControl;
        if (host is not null && !host.IsAttached)
            host.IsAttached = true;
        // 不在此处调用 Dispose，需等 FlyleafHost 先从可视树卸载后再释放 GPU。
        // Dispose 由 MainViewModel 在下一渲染帧后调用。
        WeakReferenceMessenger.Default.Send(new NavigateBackMessage());
    }

    /// <summary>监听 Player 状态：失败时自动尝试未尝试的下一条线路</summary>
    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Player.Status)) return;
        if (Player.Status != Status.Failed) return;

        _failedLinks.Add(SelectedLinkIndex);
        var next = Enumerable.Range(0, CurrentChannel?.Links.Count ?? 0)
                             .FirstOrDefault(i => !_failedLinks.Contains(i), -1);
        if (next < 0) return; // 所有线路均失败

        Application.Current?.Dispatcher.Invoke(() =>
        {
            SelectedLinkIndex = next;
            Player.OpenAsync(CurrentChannel!.Links[next]);
        });
    }
}
