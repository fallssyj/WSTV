using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using WSTV.Messages;
using WSTV.Models;
using WSTV.Services;
using WSTV.View;

namespace WSTV.ViewModels;

public partial class ConfigViewModel : ObservableObject, IRecipient<ConfigChangedMessage>
{
    [ObservableProperty] private ObservableCollection<ChannelConfiguration> _subscriptions = new();

    // ── 添加面板 ──────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isAddPanelVisible;
    [ObservableProperty] private string _newName = string.Empty;
    [ObservableProperty] private string _newUrl = string.Empty;
    [ObservableProperty] private bool _isUrlSource = true;
    [ObservableProperty] private bool _isLocalFileSource;

    // ── 编辑面板 ──────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isEditPanelVisible;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editUrl = string.Empty;
    [ObservableProperty] private bool _editIsLocalFile;
    [ObservableProperty] private bool _editIsUrlSource = true;
    [ObservableProperty] private bool _editIsLocalFileSource;
    private ChannelConfiguration? _editingItem;

    partial void OnEditIsUrlSourceChanged(bool value) { if (value) { EditIsLocalFileSource = false; EditIsLocalFile = false; } }
    partial void OnEditIsLocalFileSourceChanged(bool value) { if (value) { EditIsUrlSource = false; EditIsLocalFile = true; } }

    // ── 状态 ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    [ObservableProperty] private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    private CancellationTokenSource? _toastCts;

    partial void OnStatusMessageChanged(string value)
    {
        _toastCts?.Cancel();
        if (string.IsNullOrEmpty(value)) return;
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;
        Task.Delay(3000, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                Application.Current?.Dispatcher.Invoke(() => StatusMessage = string.Empty);
        }, token, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    // 互斥单选：URL 和本地文件
    partial void OnIsUrlSourceChanged(bool value) { if (value) IsLocalFileSource = false; }
    partial void OnIsLocalFileSourceChanged(bool value) { if (value) IsUrlSource = false; }

    public ConfigViewModel()
    {
        WeakReferenceMessenger.Default.Register(this);
        LoadSubscriptions();
    }

    public void Receive(ConfigChangedMessage message) => LoadSubscriptions();

    private void LoadSubscriptions()
    {
        Subscriptions.Clear();
        foreach (var sub in ConfigService.Instance.Config.Subscriptions)
        {
            sub.UpdateTimeDisplay = ConfigService.ComputeUpdateTimeDisplay(sub.UpdateTime);
            Subscriptions.Add(sub);
        }
    }

    // ── 添加订阅 ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ShowAddPanel()
    {
        IsEditPanelVisible = false;
        NewName = string.Empty;
        NewUrl = string.Empty;
        IsUrlSource = true;
        IsLocalFileSource = false;
        StatusMessage = string.Empty;
        IsAddPanelVisible = true;
    }

    [RelayCommand]
    private void HideAddPanel() => IsAddPanelVisible = false;

    [RelayCommand]
    private async Task AddSubscriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName) || string.IsNullOrWhiteSpace(NewUrl))
        {
            StatusMessage = "名称和地址不能为空";
            return;
        }
        if (ConfigService.Instance.Config.Subscriptions.Any(s => s.Name == NewName))
        {
            StatusMessage = "已存在同名订阅";
            return;
        }

        IsLoading = true;
        StatusMessage = "正在获取频道列表...";

        var config = new ChannelConfiguration
        {
            Name = NewName,
            Url = NewUrl,
            IsLocalFile = IsLocalFileSource,
        };

        var (success, error) = await ConfigService.Instance.FetchChannelsAsync(config);
        IsLoading = false;

        if (!success)
        {
            StatusMessage = $"获取失败: {error}";
            return;
        }

        // 若是第一个订阅则自动激活
        if (!ConfigService.Instance.Config.Subscriptions.Any(s => s.IsSelected))
            config.IsSelected = true;

        ConfigService.Instance.Config.Subscriptions.Add(config);
        ConfigService.Instance.SaveConfig();

        config.UpdateTimeDisplay = ConfigService.ComputeUpdateTimeDisplay(config.UpdateTime);
        Subscriptions.Add(config);
        IsAddPanelVisible = false;
        StatusMessage = $"已添加，共 {config.Count} 个频道";

        // 若这是第一个（已自动激活），通知其他页面刷新
        if (config.IsSelected)
            WeakReferenceMessenger.Default.Send(new ConfigChangedMessage(ConfigService.Instance.Config));
    }

    [RelayCommand]
    private void BrowseLocalFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择播放列表文件",
            Filter = "播放列表|*.m3u;*.m3u8;*.json|所有文件|*.*"
        };
        if (dialog.ShowDialog() == true)
            NewUrl = dialog.FileName;
    }

    // ── 编辑订阅 ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private void EditSubscription(ChannelConfiguration item)
    {
        _editingItem = item;
        EditName = item.Name;
        EditUrl = item.Url;
        EditIsLocalFile = item.IsLocalFile;
        EditIsLocalFileSource = item.IsLocalFile;
        EditIsUrlSource = !item.IsLocalFile;
        IsAddPanelVisible = false;
        IsEditPanelVisible = true;
    }

    [RelayCommand]
    private void BrowseEditFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择播放列表文件",
            Filter = "播放列表|*.m3u;*.m3u8;*.json|所有文件|*.*"
        };
        if (dialog.ShowDialog() == true)
            EditUrl = dialog.FileName;
    }

    [RelayCommand]
    private void SaveEdit()
    {
        if (_editingItem == null) return;
        try
        {
            _editingItem.Name = EditName;
            _editingItem.Url = EditUrl;
            _editingItem.IsLocalFile = EditIsLocalFile;
            ConfigService.Instance.SaveConfig();
            IsEditPanelVisible = false;
        }
        catch (Exception ex)
        {
            AppDialog.Show($"保存失败：{ex.Message}", "保存错误", DialogIcon.Error);
        }
    }

    [RelayCommand]
    private void CancelEdit() => IsEditPanelVisible = false;

    // ── 激活 / 刷新 / 删除 ────────────────────────────────────────────────────

    [RelayCommand]
    private void ActivateSubscription(ChannelConfiguration item)
    {
        foreach (var sub in ConfigService.Instance.Config.Subscriptions)
            sub.IsSelected = false;
        item.IsSelected = true;
        ConfigService.Instance.SaveConfig();
        WeakReferenceMessenger.Default.Send(new ConfigChangedMessage(ConfigService.Instance.Config));
    }

    [RelayCommand]
    private async Task RefreshSubscriptionAsync(ChannelConfiguration item)
    {
        item.UpdateTimeDisplay = "更新中...";
        var (success, error) = await ConfigService.Instance.FetchChannelsAsync(item);
        item.UpdateTimeDisplay = success
            ? ConfigService.ComputeUpdateTimeDisplay(item.UpdateTime)
            : $"更新失败: {error}";

        if (success)
        {
            ConfigService.Instance.SaveConfig();
            // 若刷新的是当前激活源，通知频道页刷新
            if (item.IsSelected)
                WeakReferenceMessenger.Default.Send(new ConfigChangedMessage(ConfigService.Instance.Config));
        }
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        IsLoading = true;
        StatusMessage = "正在批量刷新...";
        foreach (var sub in Subscriptions.ToList())
            await RefreshSubscriptionAsync(sub);
        // 批量刷新后清空收藏（与 TVPlayer 行为一致）
        ConfigService.Instance.Config.Favorites.Clear();
        ConfigService.Instance.SaveConfig();
        IsLoading = false;
        StatusMessage = "刷新完成";
    }

    [RelayCommand]
    private void DeleteSubscription(ChannelConfiguration item)
    {
        if (!AppDialog.Confirm(
            $"确认要删除订阅 \"{item.Name}\" 吗？\n此操作将同时删除本地缓存数据。",
            "删除订阅", DialogIcon.Warning))
            return;

        try
        {
            bool wasSelected = item.IsSelected;
            ConfigService.Instance.DeleteSubscriptionCache(item);
            ConfigService.Instance.Config.Subscriptions.Remove(item);
            Subscriptions.Remove(item);
            ConfigService.Instance.SaveConfig();

            // 若删除的是激活源则切换到第一个
            if (wasSelected && ConfigService.Instance.Config.Subscriptions.Count > 0)
                ActivateSubscription(ConfigService.Instance.Config.Subscriptions[0]);
        }
        catch (Exception ex)
        {
            AppDialog.Show($"删除失败：{ex.Message}", "删除错误", DialogIcon.Error);
        }
    }
}
