using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using WSTV.Messages;
using WSTV.Models;
using WSTV.Services;

namespace WSTV.ViewModels;

/// <summary>
/// 频道列表和收藏列表的公共抽象基类
/// 封装：分组过滤、关键词搜索、收藏切换
/// </summary>
public abstract partial class BaseChannelViewModel : ObservableObject
{
    protected List<Channel> _allChannels = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _groupTitles = new();
    [ObservableProperty] private string _selectedGroup = "全部频道";

    private DispatcherTimer? _searchDebounce;

    partial void OnSearchTextChanged(string value)
    {
        // 防抖：延迟 200ms 后再执行过滤，避免每次击键都重建集合
        if (_searchDebounce == null)
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); ApplyFilter(); };
        }
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    partial void OnSelectedGroupChanged(string value) => ApplyFilter();

    /// <summary>由子类实现：将 _allChannels 按当前筛选条件写入展示集合</summary>
    protected abstract void ApplyFilter();

    [RelayCommand]
    private void SelectGroup(string group) => SelectedGroup = group;

    protected IEnumerable<Channel> FilterChannels(IEnumerable<Channel> source)
    {
        var result = source;

        if (SelectedGroup != "全部频道")
        {
            result = result.Where(c =>
                (string.IsNullOrEmpty(c.GroupTitle) ? "未分类" : c.GroupTitle) == SelectedGroup);
        }

        if (!string.IsNullOrEmpty(SearchText))
        {
            result = result.Where(c =>
                c.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.GroupTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        return result;
    }

    protected void BuildGroupTitles(IEnumerable<Channel> channels)
    {
        var groups = channels
            .Select(c => string.IsNullOrEmpty(c.GroupTitle) ? "未分类" : c.GroupTitle)
            .Distinct()
            .ToList();

        GroupTitles.Clear();
        GroupTitles.Add("全部频道");
        foreach (var g in groups)
            GroupTitles.Add(g);

        SelectedGroup = "全部频道";
    }

    /// <summary>切换收藏状态并持久化</summary>
    protected void ToggleFavoriteInternal(Channel channel)
    {
        channel.Favorite = !channel.Favorite;
        if (channel.Favorite)
            ConfigService.Instance.AddFavorite(channel);
        else
            ConfigService.Instance.RemoveFavorite(channel);
        WeakReferenceMessenger.Default.Send(new FavoriteChangedMessage(channel.Favorite));
    }
}
