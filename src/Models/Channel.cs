using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using WSTV.Services;

namespace WSTV.Models;

/// <summary>
/// 频道模型，支持多线路和收藏状态通知
/// </summary>
public partial class Channel : ObservableObject
{
    // ── 静态共享资源 ──────────────────────────────────────────────────────────
    /// <summary>内存缓存：URL → BitmapImage，避免同一 Logo 重复下载（LRU 限制）</summary>
    private static Utils.LruCache<string, BitmapImage>? _memoryCache;

    /// <summary>限制同时下载的并发数</summary>
    private static System.Threading.SemaphoreSlim? _logoDownloadSemaphore;

    private static int _decodePixelWidth = 200;

    private static readonly object _cacheInitLock = new();

    private static void EnsureCacheInitialized()
    {
        lock (_cacheInitLock)
        {
            if (_memoryCache != null && _logoDownloadSemaphore != null) return;
            try
            {
                var cfg = ConfigService.Instance?.Config;
                int cap = cfg?.LogoLruCapacity ?? 200;
                int conc = cfg?.LogoDownloadConcurrency ?? 6;
                int decode = cfg?.LogoDecodePixelWidth ?? 200;
                _memoryCache = new Utils.LruCache<string, BitmapImage>(cap);
                _logoDownloadSemaphore = new System.Threading.SemaphoreSlim(Math.Max(1, conc));
                _decodePixelWidth = Math.Max(32, decode);
            }
            catch
            {
                _memoryCache ??= new Utils.LruCache<string, BitmapImage>(200);
                _logoDownloadSemaphore ??= new System.Threading.SemaphoreSlim(6);
                _decodePixelWidth = 200;
            }
        }
    }

    /// <summary>Logo 磁盘缓存目录：%LocalAppData%\WSTV\logos</summary>
    private static readonly string _logoCacheDir =
        Path.Combine(ConfigService.AppDataPath, "logos");

    /// <summary>共享 HttpClient，使用 SocketsHttpHandler 复用连接池，Logo 专用（超时较短）</summary>
    private static readonly HttpClient _httpClient = new(
        new System.Net.Http.SocketsHttpHandler
        {
            MaxConnectionsPerServer = 8,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
    { Timeout = TimeSpan.FromSeconds(8) };

    // ── 属性 ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _tvgName = string.Empty;
    [ObservableProperty] private string _tvgId = string.Empty;
    [ObservableProperty] private string _groupTitle = string.Empty;
    /// <summary>EXTINF 逗号后的备用名称</summary>
    [ObservableProperty] private string _extinfName = string.Empty;
    /// <summary>是否已收藏，UI 绑定实时更新</summary>
    [ObservableProperty] private bool _favorite;

    /// <summary>Logo URL，赋值时自动触发三级缓存异步加载</summary>
    [ObservableProperty] private string _tvgLogo = string.Empty;

    /// <summary>已加载的 Logo 图片，供 UI 绑定（替代直接绑 TvgLogo 字符串）</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private BitmapImage? _logoImage;

    // ── 集合 ─────────────────────────────────────────────────────────────────
    /// <summary>多线路播放地址列表</summary>
    public List<string> Links { get; set; } = new();

    // ── 计算属性 ──────────────────────────────────────────────────────────────
    /// <summary>优先使用 tvg-name，其次 EXTINF 名称</summary>
    [JsonIgnore]
    public string DisplayName => !string.IsNullOrEmpty(TvgName) ? TvgName : ExtinfName;

    /// <summary>分组显示名，GroupTitle 为空时返回"未分类"</summary>
    [JsonIgnore]
    public string DisplayGroupTitle => string.IsNullOrEmpty(GroupTitle) ? "未分类" : GroupTitle;

    // ── Logo 缓存逻辑 ─────────────────────────────────────────────────────────
    partial void OnTvgLogoChanged(string value) => LoadLogoAsync(value);

    /// <summary>
    /// 异步加载 Logo：内存缓存 → 磁盘缓存 → 网络下载
    /// </summary>
    private async void LoadLogoAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        EnsureCacheInitialized();

        // 1. 命中内存缓存
        if (_memoryCache!.TryGet(url, out var cached))
        {
            LogoImage = cached;
            return;
        }

        if (!Directory.Exists(_logoCacheDir))
            Directory.CreateDirectory(_logoCacheDir);

        // 2. 查找磁盘缓存（支持多种格式）
        string key = GetUrlHash(url);
        string[] exts = new[] { ".png", ".jpg", ".gif", ".ico", ".webp" };
        foreach (var ext in exts)
        {
            var diskPath = Path.Combine(_logoCacheDir, key + ext);
            if (File.Exists(diskPath))
            {
                var bmp = LoadBitmapFromFile(diskPath);
                if (bmp != null)
                {
                    _memoryCache.Add(url, bmp);
                    LogoImage = bmp;
                    return;
                }
            }
        }

        // 3. 网络下载并写入磁盘缓存（受限并发）
        await (_logoDownloadSemaphore ?? new System.Threading.SemaphoreSlim(6)).WaitAsync();
        try
        {
            // 双重检查：其他任务可能已在等待期间完成了相同 URL 的下载
            if (_memoryCache!.TryGet(url, out var alreadyCached))
            {
                LogoImage = alreadyCached;
                return;
            }
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var ct = response.Content.Headers.ContentType?.MediaType ?? "";
            string saveExt = ct.Contains("jpeg") || ct.Contains("jpg") ? ".jpg"
                           : ct.Contains("gif") ? ".gif"
                           : ct.Contains("ico") ? ".ico"
                           : ct.Contains("webp") ? ".webp"
                           : ".png";

            string savePath = Path.Combine(_logoCacheDir, key + saveExt);
            await File.WriteAllBytesAsync(savePath, bytes);

            var bitmap = LoadBitmapFromBytes(bytes);
            if (bitmap != null)
            {
                _memoryCache.Add(url, bitmap);
                LogoImage = bitmap;
            }
        }
        catch { /* 下载失败静默处理 */ }
        finally { try { _logoDownloadSemaphore?.Release(); } catch { } }
    }

    private static BitmapImage? LoadBitmapFromFile(string path)
    {
        try
        {
            // 解码为缩略图以减少内存占用
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = _decodePixelWidth;
            using var fs = File.OpenRead(path);
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze(); // Freeze 后可跨线程访问
            return bmp;
        }
        catch { return null; }
    }

    private static BitmapImage? LoadBitmapFromBytes(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = _decodePixelWidth;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>对 URL 计算 SHA256 哈希，取前 16 位作为磁盘缓存文件名</summary>
    private static string GetUrlHash(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return BitConverter.ToString(bytes).Replace("-", "")[..16].ToLower();
    }

    // Called to refresh cache settings when user changes them in UI
    public static void UpdateCacheSettings()
    {
        lock (_cacheInitLock)
        {
            // Recreate cache and semaphore from current config
            var cfg = ConfigService.Instance?.Config;
            int cap = cfg?.LogoLruCapacity ?? 200;
            int conc = cfg?.LogoDownloadConcurrency ?? 6;
            int decode = cfg?.LogoDecodePixelWidth ?? 200;

            _memoryCache = new Utils.LruCache<string, BitmapImage>(cap);
            _logoDownloadSemaphore = new System.Threading.SemaphoreSlim(Math.Max(1, conc));
            _decodePixelWidth = Math.Max(32, decode);
        }
    }
}
