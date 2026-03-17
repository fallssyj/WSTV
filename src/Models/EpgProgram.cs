using CommunityToolkit.Mvvm.ComponentModel;

namespace WSTV.Models;

/// <summary>XMLTV 节目条目</summary>
public class EpgProgram : ObservableObject
{
    public string ChannelId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string TimeText => StartTime.ToString("HH:mm");
    public int DurationMinutes => Math.Max(0, (int)(EndTime - StartTime).TotalMinutes);
    public string DurationText => DurationMinutes > 0 ? $"时长：{DurationMinutes}分钟" : string.Empty;

    public bool IsLive => DateTime.Now >= StartTime && DateTime.Now < EndTime;
    public bool IsPast => DateTime.Now >= EndTime;
    public bool IsUpcoming => DateTime.Now < StartTime;

    /// <summary>"正在直播" / "未播放" / "已播完"</summary>
    public string StatusText => IsLive ? "正在直播" : (IsUpcoming ? "未播放" : "已播完");

    /// <summary>通知 UI 刷新时间相关状态（每30秒由 DispatcherTimer 调用）</summary>
    public void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsPast));
        OnPropertyChanged(nameof(IsUpcoming));
        OnPropertyChanged(nameof(StatusText));
    }
}
