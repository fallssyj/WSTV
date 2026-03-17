using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;
using WSTV.Messages;
using WSTV.Models;
using WSTV.Services;
using WSTV.View;

namespace WSTV.ViewModels;

public partial class SettingsViewModel : ObservableObject, IRecipient<ConfigChangedMessage>
{
    [ObservableProperty] private string _newEpgUrl = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isRefreshingAll;

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
                System.Windows.Application.Current?.Dispatcher.Invoke(() => StatusMessage = string.Empty);
        }, token, System.Threading.Tasks.TaskContinuationOptions.None,
           System.Threading.Tasks.TaskScheduler.Default);
    }

    [RelayCommand]
    private void GoToAbout() =>
        WeakReferenceMessenger.Default.Send(new NavigateToAboutMessage());

    /// <summary>直接暴露 Config 中的 EPG 订阅列表，增删同步持久化</summary>
    public ObservableCollection<EpgSubscription> EpgSubscriptions { get; } = new();

    public SettingsViewModel()
    {
        WeakReferenceMessenger.Default.Register(this);
        LoadFromConfig();
    }

    [ObservableProperty] private bool _deleteConfig;
    [ObservableProperty] private bool _deleteEpg;
    [ObservableProperty] private bool _deleteLogos;
    [ObservableProperty] private int _logoLruCapacity;
    [ObservableProperty] private int _logoDownloadConcurrency;
    [ObservableProperty] private int _logoDecodePixelWidth;

    public void Receive(ConfigChangedMessage message) => LoadFromConfig();

    private void LoadFromConfig()
    {
        EpgSubscriptions.Clear();
        foreach (var sub in ConfigService.Instance.Config.EpgSubscriptions)
        {
            sub.UpdateTimeDisplay = ConfigService.ComputeUpdateTimeDisplay(sub.UpdateTime);
            EpgSubscriptions.Add(sub);
        }
        // load performance settings
        var cfg = ConfigService.Instance.Config;
        LogoLruCapacity = cfg.LogoLruCapacity;
        LogoDownloadConcurrency = cfg.LogoDownloadConcurrency;
        LogoDecodePixelWidth = cfg.LogoDecodePixelWidth;
    }

    [RelayCommand]
    private void SavePerformanceSettings()
    {
        var cfg = ConfigService.Instance.Config;
        cfg.LogoLruCapacity = Math.Max(10, LogoLruCapacity);
        cfg.LogoDownloadConcurrency = Math.Max(1, LogoDownloadConcurrency);
        cfg.LogoDecodePixelWidth = Math.Max(32, LogoDecodePixelWidth);
        ConfigService.Instance.SaveConfig();

        // Apply immediately
        Models.Channel.UpdateCacheSettings();
        StatusMessage = "已保存性能设置";
    }

    [RelayCommand]
    private void AddEpg()
    {
        var url = NewEpgUrl.Trim();
        if (string.IsNullOrEmpty(url)) return;
        if (EpgSubscriptions.Any(s => s.Url == url))
        {
            StatusMessage = "该地址已存在";
            return;
        }

        var sub = new EpgSubscription { Url = url, UpdateTimeDisplay = "从未更新" };
        EpgSubscriptions.Add(sub);
        ConfigService.Instance.Config.EpgSubscriptions.Add(sub);
        ConfigService.Instance.SaveConfig();
        NewEpgUrl = string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void RemoveEpg(EpgSubscription sub)
    {
        EpgSubscriptions.Remove(sub);
        ConfigService.Instance.Config.EpgSubscriptions.Remove(sub);
        ConfigService.Instance.SaveConfig();
    }

    [RelayCommand]
    private async Task RefreshEpg(EpgSubscription sub)
    {
        var (success, error) = await ConfigService.Instance.FetchEpgAsync(sub);
        StatusMessage = success ? $"已更新：{sub.Url}" : $"更新失败：{error}";
    }

    [RelayCommand]
    private async Task RefreshAllEpg()
    {
        if (EpgSubscriptions.Count == 0) { StatusMessage = "没有订阅"; return; }
        IsRefreshingAll = true;
        StatusMessage = "正在更新全部 EPG...";
        await ConfigService.Instance.RefreshAllEpgAsync();
        IsRefreshingAll = false;
        StatusMessage = $"全部更新完成（{EpgSubscriptions.Count} 个）";
    }

    [RelayCommand]
    private void OpenCacheDir()
    {
        var path = ConfigService.AppDataPath;
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    private void ClearCaches()
    {
        if (!DeleteConfig && !DeleteEpg && !DeleteLogos)
        {
            StatusMessage = "请选择要清除的项目";
            return;
        }

        if (!AppDialog.Confirm("确认要清除所选项吗？此操作不可撤销。", "清空缓存", DialogIcon.Warning)) return;

        try
        {
            if (DeleteConfig)
            {
                var cfg = Path.Combine(ConfigService.AppDataPath, "config.json");
                var configsDir = Path.Combine(ConfigService.AppDataPath, "configs");
                var videoFilter = Path.Combine(ConfigService.AppDataPath, "video_filter.json");
                if (File.Exists(cfg)) File.Delete(cfg);
                if (File.Exists(videoFilter)) File.Delete(videoFilter);
                if (Directory.Exists(configsDir)) Directory.Delete(configsDir, true);
                DeleteConfig = false;
            }

            if (DeleteEpg)
            {
                var epgDir = Path.Combine(ConfigService.AppDataPath, "epg");
                if (Directory.Exists(epgDir)) Directory.Delete(epgDir, true);
                DeleteEpg = false;
            }

            if (DeleteLogos)
            {
                var logos = Path.Combine(ConfigService.AppDataPath, "logos");
                if (Directory.Exists(logos)) Directory.Delete(logos, true);
                DeleteLogos = false;
            }

            StatusMessage = "已清除所选缓存。请重启应用以确保所有更改生效。";
        }
        catch (Exception ex)
        {
            StatusMessage = "清除失败：" + ex.Message;
        }
    }
}

