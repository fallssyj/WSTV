using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace WSTV.Models;

/// <summary>
/// EPG 节目单订阅，支持 .xml / .xml.gz 格式
/// </summary>
public partial class EpgSubscription : ObservableObject
{
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private DateTime _updateTime;

    // ── UI 状态（不持久化）──────────────────────────────
    [JsonIgnore, ObservableProperty] private bool _isUpdating;
    [JsonIgnore, ObservableProperty] private string _lastError = string.Empty;

    private string _updateTimeDisplay = "从未更新";

    [JsonIgnore]
    public string UpdateTimeDisplay
    {
        get => _updateTimeDisplay;
        set => SetProperty(ref _updateTimeDisplay, value);
    }
}
