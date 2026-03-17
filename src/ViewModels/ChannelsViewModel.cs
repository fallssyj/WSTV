using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Windows;
using WSTV.Messages;
using WSTV.Models;
using WSTV.Services;

namespace WSTV.ViewModels;

public partial class ChannelsViewModel : BaseChannelViewModel,
    IRecipient<ConfigChangedMessage>,
    IRecipient<FavoriteChangedMessage>
{
    [ObservableProperty] private ObservableCollection<Channel> _channels = new();
    [ObservableProperty] private string _sourceTitle = "";

    public ChannelsViewModel()
    {
        WeakReferenceMessenger.Default.Register<ConfigChangedMessage>(this);
        WeakReferenceMessenger.Default.Register<FavoriteChangedMessage>(this);
        LoadChannels();
    }

    public void Receive(ConfigChangedMessage message)
        => Application.Current?.Dispatcher.Invoke(LoadChannels);

    public void Receive(FavoriteChangedMessage message)
    {
        Application.Current?.Dispatcher.Invoke(SyncFavoriteStates);
    }

    private void LoadChannels()
    {
        _allChannels = ConfigService.Instance.GetSelectedChannels();

        // 同步收藏状态（HashSet 匹配，O(n)）
        SyncFavoriteStates();

        // 更新标题
        var selected = ConfigService.Instance.Config.Subscriptions.FirstOrDefault(s => s.IsSelected);
        SourceTitle = selected?.Name ?? "M3U 播放器";

        BuildGroupTitles(_allChannels);
        ApplyFilter();
    }

    /// <summary>从 Config.Favorites 重新同步 _allChannels 的 Favorite 标志，不重载频道列表</summary>
    private void SyncFavoriteStates()
    {
        var favoriteKeys = ConfigService.Instance.Config.Favorites
            .Select(f => f.TvgId + "|" + f.TvgName)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        foreach (var ch in _allChannels)
            ch.Favorite = favoriteKeys.Contains(ch.TvgId + "|" + ch.DisplayName);
    }

    protected override void ApplyFilter()
    {
        Channels = new ObservableCollection<Channel>(FilterChannels(_allChannels));
    }

    [RelayCommand]
    private void ToggleFavorite(Channel channel)
    {
        ToggleFavoriteInternal(channel);
    }

    /// <summary>双击频道：发送消息通知 MainViewModel 切换到播放页</summary>
    [RelayCommand]
    private void PlayChannel(Channel channel)
    {
        WeakReferenceMessenger.Default.Send(
            new PlayChannelMessage(channel, _allChannels));
    }
}
