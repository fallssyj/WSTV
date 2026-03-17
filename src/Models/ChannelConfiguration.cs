using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace WSTV.Models;

/// <summary>
/// 订阅源配置，每条对应一个 M3U/JSON 文件或本地文件
/// </summary>
public partial class ChannelConfiguration : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private bool _isLocalFile;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private int _count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateTimeDisplay))]
    private DateTime _updateTime;

    // 手动实现以便直接标注 [JsonIgnore]，支持"更新中..."等临时文本
    private string _updateTimeDisplay = string.Empty;

    [JsonIgnore]
    public string UpdateTimeDisplay
    {
        get => _updateTimeDisplay;
        set => SetProperty(ref _updateTimeDisplay, value);
    }
}
