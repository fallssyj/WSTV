using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Windows;
using WSTV.Messages;
using WSTV.Models;
using WSTV.Services;

namespace WSTV.ViewModels;

public partial class FavoritesViewModel : BaseChannelViewModel,
    IRecipient<ConfigChangedMessage>,
    IRecipient<FavoriteChangedMessage>
{
    [ObservableProperty] private ObservableCollection<Channel> _favorites = new();

    public FavoritesViewModel()
    {
        WeakReferenceMessenger.Default.Register<ConfigChangedMessage>(this);
        WeakReferenceMessenger.Default.Register<FavoriteChangedMessage>(this);
        LoadFavorites();
    }

    public void Receive(ConfigChangedMessage message)
        => Application.Current?.Dispatcher.Invoke(LoadFavorites);

    public void Receive(FavoriteChangedMessage message)
    {
        Application.Current?.Dispatcher.Invoke(LoadFavorites);
    }

    private void LoadFavorites()
    {
        _allChannels = ConfigService.Instance.ResolveFavoriteChannels();
        foreach (var ch in _allChannels)
            ch.Favorite = true;
        BuildGroupTitles(_allChannels);
        ApplyFilter();
    }

    protected override void ApplyFilter()
    {
        Favorites = new ObservableCollection<Channel>(FilterChannels(_allChannels));
    }

    [RelayCommand]
    private void RemoveFavorite(Channel channel)
    {
        ToggleFavoriteInternal(channel); // Favorite = false, 从 Config 移除
        _allChannels.Remove(channel);

        // 若该分组已无频道则从 GroupTitles 移除
        var group = string.IsNullOrEmpty(channel.GroupTitle) ? "未分类" : channel.GroupTitle;
        if (!_allChannels.Any(c => (string.IsNullOrEmpty(c.GroupTitle) ? "未分类" : c.GroupTitle) == group))
            GroupTitles.Remove(group);

        ApplyFilter();
    }

    /// <summary>双击收藏频道：发送消息通知 MainViewModel 切换到播放页</summary>
    [RelayCommand]
    private void PlayChannel(Channel channel)
    {
        WeakReferenceMessenger.Default.Send(
            new PlayChannelMessage(channel, _allChannels));
    }
}
