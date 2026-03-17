using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using WSTV.Models;

namespace WSTV.Services;

/// <summary>
/// 全局配置与数据服务单例
/// 负责：配置读写、M3U 解析、HTTP 下载、频道缓存管理
/// </summary>
public class ConfigService
{
    public static readonly ConfigService Instance = new();

    private static readonly HttpClient _http;

    static ConfigService()
    {
        var handler = new System.Net.Http.SocketsHttpHandler()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 8,
            KeepAlivePingPolicy = System.Net.Http.HttpKeepAlivePingPolicy.Always,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10)
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static readonly string AppDataPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSTV");

    private static readonly string ConfigFilePath = Path.Combine(AppDataPath, "config.json");
    private static readonly string ConfigsDir = Path.Combine(AppDataPath, "configs");
    private static readonly string EpgDir = Path.Combine(AppDataPath, "epg");

    public AppConfig Config { get; private set; } = new();

    /// <summary>供 Channel 复用同一连接池（8s 超时由调用方按需 CancellationToken 控制）</summary>
    internal static HttpClient SharedHttp => _http;

    // 频道内存缓存：订阅名（sanitized）→ 频道列表，避免每次从磁盘反序列化
    private readonly Dictionary<string, List<Channel>> _channelCache = new();

    private ConfigService() { }

    // ── 配置读写 ──────────────────────────────────────────────────────────────

    public void LoadConfig()
    {
        try
        {
            Directory.CreateDirectory(AppDataPath);
            Directory.CreateDirectory(ConfigsDir);
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                Config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOpts) ?? new AppConfig();
            }
        }
        catch
        {
            Config = new AppConfig();
        }

        // 初始化各订阅的更新时间显示
        foreach (var sub in Config.Subscriptions)
            sub.UpdateTimeDisplay = ComputeUpdateTimeDisplay(sub.UpdateTime);
    }

    /// <summary>原子写入：先写 .tmp 再 Move，防止断电损坏</summary>
    public void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(AppDataPath);
            var json = JsonSerializer.Serialize(Config, _jsonOpts);
            var tmp = ConfigFilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, ConfigFilePath, overwrite: true);
        }
        catch { /* 静默失败，避免影响 UI */ }
    }

    // ── 收藏助手（统一匹配逻辑，消除重复代码）───────────────────────────────────

    /// <summary>判断频道是否已收藏（TvgId 精确匹配优先，否则显示名称不区分大小写匹配）</summary>
    public bool IsFavorite(Channel channel) => Config.Favorites.Any(f =>
        (!string.IsNullOrEmpty(f.TvgId) && f.TvgId == channel.TvgId)
        || f.TvgName.Equals(channel.DisplayName, StringComparison.OrdinalIgnoreCase));

    /// <summary>添加收藏（幂等：若已存在则不重复写入）</summary>
    public void AddFavorite(Channel channel)
    {
        if (IsFavorite(channel)) return;
        Config.Favorites.Add(new FavoriteRef { TvgId = channel.TvgId, TvgName = channel.DisplayName });
        SaveConfig();
    }

    /// <summary>移除收藏</summary>
    public void RemoveFavorite(Channel channel)
    {
        Config.Favorites.RemoveAll(f =>
            (!string.IsNullOrEmpty(f.TvgId) && f.TvgId == channel.TvgId)
            || f.TvgName.Equals(channel.DisplayName, StringComparison.OrdinalIgnoreCase));
        SaveConfig();
    }

    // ── 频道缓存 ───────────────────────────────────────────────────────────────

    /// <summary>读取当前激活订阅的频道列表（内存缓存 → 磁盘）</summary>
    public List<Channel> GetSelectedChannels()
    {
        var selected = Config.Subscriptions.FirstOrDefault(s => s.IsSelected);
        if (selected == null) return new();

        var key = SanitizeFileName(selected.Name);
        if (_channelCache.TryGetValue(key, out var cached))
            return cached;

        var cachePath = GetChannelCachePath(selected);
        if (!File.Exists(cachePath)) return new();

        try
        {
            var json = File.ReadAllText(cachePath);
            var list = JsonSerializer.Deserialize<List<Channel>>(json, _jsonOpts) ?? new();
            _channelCache[key] = list;
            return list;
        }
        catch { return new(); }
    }

    /// <summary>
    /// 从所有订阅缓存中反查收藏标识符对应的完整 Channel。
    /// 匹配规则：TvgId 非空时优先精确匹配 TvgId，否则匹配 DisplayName。
    /// 若某条标识符在所有缓存中都找不到，返回占位频道（Links 为空）以保留收藏记录。
    /// </summary>
    public List<Channel> ResolveFavoriteChannels()
    {
        var refs = Config.Favorites;
        if (refs.Count == 0) return new();

        // 收集所有订阅缓存（优先内存缓存）
        var allChannels = new List<Channel>();
        foreach (var sub in Config.Subscriptions)
        {
            var key = SanitizeFileName(sub.Name);
            if (_channelCache.TryGetValue(key, out var memList))
            {
                allChannels.AddRange(memList);
                continue;
            }
            var path = GetChannelCachePath(sub);
            if (!File.Exists(path)) continue;
            try
            {
                var json = File.ReadAllText(path);
                var channels = JsonSerializer.Deserialize<List<Channel>>(json, _jsonOpts);
                if (channels != null)
                {
                    _channelCache[key] = channels;
                    allChannels.AddRange(channels);
                }
            }
            catch { }
        }

        var result = new List<Channel>();
        foreach (var fav in refs)
        {
            Channel? match = null;

            if (!string.IsNullOrEmpty(fav.TvgId))
                match = allChannels.FirstOrDefault(c => c.TvgId == fav.TvgId);

            if (match == null)
                match = allChannels.FirstOrDefault(c =>
                    c.DisplayName.Equals(fav.TvgName, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                // 订阅缓存中找不到：创建占位频道，保留标识符供下次重新匹配
                match = new Channel { TvgName = fav.TvgName, TvgId = fav.TvgId };
            }

            match.Favorite = true;
            result.Add(match);
        }
        return result;
    }

    /// <summary>从 URL 或本地文件拉取并解析频道，写入缓存</summary>
    public async Task<(bool Success, string Error)> FetchChannelsAsync(ChannelConfiguration config)
    {
        try
        {
            string content;
            if (config.IsLocalFile)
            {
                content = await File.ReadAllTextAsync(config.Url);
            }
            else
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, config.Url);
                req.Headers.TryAddWithoutValidation("User-Agent", "WSTV/1.0");
                var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                content = await resp.Content.ReadAsStringAsync();
            }

            var channels = ParseContent(content);
            config.Count = channels.Count;
            config.UpdateTime = DateTime.Now;
            await SaveChannelCacheAsync(config, channels);
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task SaveChannelCacheAsync(ChannelConfiguration config, List<Channel> channels)
    {
        var path = GetChannelCachePath(config);
        var json = JsonSerializer.Serialize(channels, _jsonOpts);
        await File.WriteAllTextAsync(path, json);
        // 同步更新内存缓存
        _channelCache[SanitizeFileName(config.Name)] = channels;
    }

    private string GetChannelCachePath(ChannelConfiguration config)
    {
        var dir = Path.Combine(ConfigsDir, SanitizeFileName(config.Name));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "channels.json");
    }

    public void DeleteSubscriptionCache(ChannelConfiguration config)
    {
        var key = SanitizeFileName(config.Name);
        var dir = Path.Combine(ConfigsDir, key);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        _channelCache.Remove(key);
    }

    // ── M3U 解析 ───────────────────────────────────────────────────────────────

    private static List<Channel> ParseContent(string content)
    {
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("[") || trimmed.StartsWith("{"))
        {
            return JsonSerializer.Deserialize<List<Channel>>(content, _jsonOpts) ?? new();
        }
        return ParseM3U(content);
    }

    private static List<Channel> ParseM3U(string content)
    {
        // key = TvgName 或 ExtinfName，用于多线路合并
        var keyMap = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Channel>();

        Channel? current = null;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                current = new Channel
                {
                    TvgName = ExtractAttr(line, "tvg-name"),
                    TvgId = ExtractAttr(line, "tvg-id"),
                    TvgLogo = ExtractAttr(line, "tvg-logo"),
                    GroupTitle = ExtractAttr(line, "group-title") is { Length: > 0 } g ? g : "未分类",
                };
                var commaIdx = line.LastIndexOf(',');
                if (commaIdx >= 0 && commaIdx < line.Length - 1)
                    current.ExtinfName = line[(commaIdx + 1)..].Trim();
            }
            else if (current != null && IsStreamUrl(line))
            {
                var key = !string.IsNullOrEmpty(current.TvgName) ? current.TvgName : current.ExtinfName;

                if (!string.IsNullOrEmpty(key) && keyMap.TryGetValue(key, out var existing))
                {
                    // 同名频道：追加线路（去重）
                    if (!existing.Links.Contains(line))
                        existing.Links.Add(line);
                }
                else
                {
                    current.Links.Add(line);
                    if (!string.IsNullOrEmpty(key))
                        keyMap[key] = current;
                    result.Add(current);
                }
                current = null;
            }
        }
        return result;
    }

    private static bool IsStreamUrl(string line)
        => line.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("udp://", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("rtp://", StringComparison.OrdinalIgnoreCase);

    private static string ExtractAttr(string line, string attr)
    {
        var m = Regex.Match(line, attr + @"=""([^""]*)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    // ── 工具方法 ───────────────────────────────────────────────────────────────

    public static string ComputeUpdateTimeDisplay(DateTime updateTime)
    {
        if (updateTime == default) return "从未更新";
        var diff = DateTime.Now - updateTime;
        if (diff.TotalSeconds < 60) return "刚刚更新";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} 分钟前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} 小时前";
        return $"{(int)diff.TotalDays} 天前";
    }

    private static string SanitizeFileName(string name)
        => string.Join("_", name.Split(Path.GetInvalidFileNameChars()));

    // ── EPG 订阅 ─────────────────────────────────────────────────────────

    /// <summary>EPG 订阅缓存路径，文件名由 URL 的 SHA-256 决定，跨运行始终一致</summary>
    public string GetEpgCachePath(EpgSubscription sub)
    {
        Directory.CreateDirectory(EpgDir);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sub.Url)));
        return Path.Combine(EpgDir, hash + ".xml");
    }

    /// <summary>下载并缓存单条 EPG，支持 .xml / .xml.gz</summary>
    public async Task<(bool Success, string Error)> FetchEpgAsync(EpgSubscription sub)
    {
        await SetOnUiAsync(() => { sub.IsUpdating = true; sub.LastError = string.Empty; });
        try
        {
            byte[] rawBytes;
            if (Uri.TryCreate(sub.Url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, sub.Url);
                req.Headers.TryAddWithoutValidation("User-Agent", "WSTV/1.0");
                var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead);
                resp.EnsureSuccessStatusCode();
                rawBytes = await resp.Content.ReadAsByteArrayAsync();
            }
            else
            {
                rawBytes = await File.ReadAllBytesAsync(sub.Url);
            }

            // 检测是否 gzip：URL 以 .gz 结尾，或内容为 gzip magic bytes (0x1f 0x8b)
            bool isGzip = sub.Url.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                          || (rawBytes.Length >= 2 && rawBytes[0] == 0x1f && rawBytes[1] == 0x8b);

            string xmlContent;
            if (isGzip)
            {
                using var ms = new MemoryStream(rawBytes);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var reader = new StreamReader(gz, Encoding.UTF8);
                xmlContent = await reader.ReadToEndAsync();
            }
            else
            {
                xmlContent = Encoding.UTF8.GetString(rawBytes);
            }

            var cachePath = GetEpgCachePath(sub);
            await File.WriteAllTextAsync(cachePath, xmlContent);

            await SetOnUiAsync(() =>
            {
                sub.UpdateTime = DateTime.Now;
                sub.UpdateTimeDisplay = "刚刚更新";
                sub.IsUpdating = false;
            });
            SaveConfig();
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            await SetOnUiAsync(() =>
            {
                sub.IsUpdating = false;
                sub.LastError = ex.Message;
            });
            return (false, ex.Message);
        }
    }

    /// <summary>并行刷新所有 EPG 订阅</summary>
    public Task RefreshAllEpgAsync()
    {
        if (Config.EpgSubscriptions.Count == 0) return Task.CompletedTask;
        return Task.WhenAll(Config.EpgSubscriptions.Select(FetchEpgAsync));
    }

    private static Task SetOnUiAsync(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) { action(); return Task.CompletedTask; }
        return d.InvokeAsync(action).Task;
    }
}
