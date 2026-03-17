namespace WSTV.Models;

/// <summary>
/// 全局配置根对象，持久化到 %LocalAppData%/WSTV/config.json
/// </summary>
public class AppConfig
{
    public List<ChannelConfiguration> Subscriptions { get; set; } = new();
    /// <summary>收藏频道标识符列表，只保存 TvgId+TvgName，运行时反查完整频道信息</summary>
    public List<FavoriteRef> Favorites { get; set; } = new();
    /// <summary>EPG 节目单订阅，支持 .xml / .xml.gz</summary>
    public List<EpgSubscription> EpgSubscriptions { get; set; } = new();
    // Logo cache and download tuning
    public int LogoLruCapacity { get; set; } = 200;
    public int LogoDownloadConcurrency { get; set; } = 6;
    public int LogoDecodePixelWidth { get; set; } = 200;
}
