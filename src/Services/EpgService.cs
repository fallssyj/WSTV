using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using WSTV.Models;

namespace WSTV.Services;

/// <summary>
/// 解析缓存的 XMLTV 文件，提供按频道 / 日期查询节目的能力
/// </summary>
public class EpgService : IEpgService
{
    public static readonly EpgService Instance = new();

    /// <summary>channelId → programs（按开始时间升序）</summary>
    private readonly Dictionary<string, List<EpgProgram>> _index =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>归一化后的 display-name / channelId → channelId（用于 tvg-id 不匹配时 fallback）
    /// 同时存入原始归一化 key 与去质量后缀 key，后者仅在 key 不已存在时写入（精确优先）</summary>
    private readonly Dictionary<string, string> _displayIdMap =
        new(StringComparer.OrdinalIgnoreCase);

    private EpgService() { }

    // ── 加载 ──────────────────────────────────────────────────────────────────

    /// <summary>扫描并解析 EpgDir 中所有 .xml 文件（全量重建索引）</summary>
    public void Reload()
    {
        _index.Clear();
        _displayIdMap.Clear();
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WSTV", "epg");

        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.EnumerateFiles(dir, "*.xml"))
        {
            try { ParseFile(file); }
            catch { /* 静默跳过损坏文件 */ }
        }
    }

    /// <summary>
    /// 用 XmlReader 流式解析 XMLTV 文件，不需将整个 DOM 加载进内存。
    /// 单次前向遍历：先读 &lt;channel&gt; 建 display-name 映射，再读 &lt;programme&gt; 建索引。
    /// </summary>
    private void ParseFile(string path)
    {
        var localIndex = new Dictionary<string, List<EpgProgram>>(StringComparer.OrdinalIgnoreCase);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };

        using var xmlReader = XmlReader.Create(path, settings);
        while (xmlReader.Read())
        {
            if (xmlReader.NodeType != XmlNodeType.Element) continue;

            if (xmlReader.LocalName == "channel")
            {
                var id = xmlReader.GetAttribute("id") ?? string.Empty;
                if (string.IsNullOrEmpty(id)) { xmlReader.Skip(); continue; }

                AddToDisplayMap(Normalize(id), id);

                // 读子树收集 display-name
                using var sub = xmlReader.ReadSubtree();
                sub.Read(); // 定位到 <channel> 元素本身
                while (sub.Read())
                {
                    if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "display-name")
                    {
                        var norm = Normalize(sub.ReadElementContentAsString());
                        if (!string.IsNullOrEmpty(norm))
                            AddToDisplayMap(norm, id);
                    }
                }
            }
            else if (xmlReader.LocalName == "programme")
            {
                var channelId = xmlReader.GetAttribute("channel") ?? string.Empty;
                if (string.IsNullOrEmpty(channelId)) { xmlReader.Skip(); continue; }
                // 已有其他文件提供了该频道的数据 → 跳过整个节点
                if (_index.ContainsKey(channelId)) { xmlReader.Skip(); continue; }

                var startStr = xmlReader.GetAttribute("start") ?? string.Empty;
                var stopStr = xmlReader.GetAttribute("stop") ?? string.Empty;
                if (!TryParseXmltvTime(startStr, out var start)) { xmlReader.Skip(); continue; }
                if (!TryParseXmltvTime(stopStr, out var end)) { xmlReader.Skip(); continue; }

                string title = string.Empty, desc = string.Empty;
                using var sub = xmlReader.ReadSubtree();
                sub.Read(); // 定位到 <programme>
                while (sub.Read())
                {
                    if (sub.NodeType != XmlNodeType.Element) continue;
                    if (sub.LocalName == "title" && string.IsNullOrEmpty(title))
                        title = sub.ReadElementContentAsString();
                    else if (sub.LocalName == "desc" && string.IsNullOrEmpty(desc))
                        desc = sub.ReadElementContentAsString();
                }

                var prog = new EpgProgram
                {
                    ChannelId = channelId,
                    StartTime = start,
                    EndTime = end,
                    Title = title,
                    Description = desc,
                };

                if (!localIndex.TryGetValue(channelId, out var list))
                {
                    list = new List<EpgProgram>();
                    localIndex[channelId] = list;
                }
                list.Add(prog);
            }
        }

        // 将本文件结果合并到全局索引（已排序，first-wins）
        foreach (var kv in localIndex)
        {
            kv.Value.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            _index[kv.Key] = kv.Value;
        }
    }

    // ── 查询 ──────────────────────────────────────────────────────────────────

    /// <summary>判断是否有任何已加载的EPG数据</summary>
    public bool HasAnyData => _index.Count > 0;

    /// <summary>返回指定频道、指定日期的节目列表</summary>
    public IReadOnlyList<EpgProgram> GetPrograms(string tvgId, DateTime date, string channelName = "")
    {
        var list = FindList(tvgId, channelName);
        if (list == null) return Array.Empty<EpgProgram>();

        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);
        return list.Where(p => p.StartTime >= dayStart && p.StartTime < dayEnd).ToList();
    }

    /// <summary>返回指定频道当前正在播放的节目（null = 无）</summary>
    public EpgProgram? GetNowPlaying(string tvgId, string channelName = "")
    {
        var list = FindList(tvgId, channelName);
        if (list == null) return null;
        var now = DateTime.Now;
        return list.FirstOrDefault(p => p.StartTime <= now && now < p.EndTime);
    }

    /// <summary>是否存在该频道的EPG数据</summary>
    public bool HasEpgFor(string tvgId, string channelName = "")
        => FindList(tvgId, channelName) != null;

    /// <summary>
    /// 五级严格查找（全部为精确键匹配，不做 Contains/substring 模糊匹配）：
    /// ① TvgId 精确匹配 _index
    /// ② TvgId 归一化（去空格/连字符）→ _displayIdMap 精确
    /// ③ TvgId 归一化后再去质量后缀（高清/4K/HD…）→ _displayIdMap 精确
    /// ④ DisplayName 归一化 → _displayIdMap 精确
    /// ⑤ DisplayName 归一化后再去质量后缀 → _displayIdMap 精确
    /// </summary>
    private List<EpgProgram>? FindList(string tvgId, string channelName)
    {
        // ① 精确匹配 tvgId（_index 字典为 OrdinalIgnoreCase）
        if (!string.IsNullOrEmpty(tvgId))
        {
            if (_index.TryGetValue(tvgId, out var list)) return list;

            // ② 归一化后匹配 tvgId（CCTV-1 → cctv1 / CCTV 1 → cctv1）
            var normId = Normalize(tvgId);
            if (TryResolve(normId, out var r)) return r;

            // ③ 归一化后再去质量后缀（CCTV1高清 / CCTV1HD → cctv1）
            var strippedId = StripQuality(normId);
            if (strippedId != normId && TryResolve(strippedId, out r)) return r;
        }

        // ④ DisplayName 归一化精确匹配（tvgId 为空或前三步均未命中）
        if (!string.IsNullOrEmpty(channelName))
        {
            var normName = Normalize(channelName);
            if (TryResolve(normName, out var r)) return r;

            // ⑤ DisplayName 去质量后缀后精确匹配
            var strippedName = StripQuality(normName);
            if (strippedName != normName && TryResolve(strippedName, out r)) return r;
        }

        return null;
    }

    /// <summary>通过归一化 key 查 _displayIdMap，再查 _index，两步均需命中</summary>
    private bool TryResolve(string normalizedKey, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out List<EpgProgram>? list)
    {
        list = null;
        return _displayIdMap.TryGetValue(normalizedKey, out var id)
            && _index.TryGetValue(id, out list);
    }

    /// <summary>
    /// 向 _displayIdMap 写入精确 key 和去质量后缀 key（精确优先，已存在时不覆盖后者）
    /// </summary>
    private void AddToDisplayMap(string normalizedKey, string channelId)
    {
        if (string.IsNullOrEmpty(normalizedKey)) return;
        // 精确 key 始终写入（后来的文件若有同名 channel，first-wins 由调用方保证）
        _displayIdMap[normalizedKey] = channelId;
        // 去质量后缀 key：仅在尚未被精确 key 占用时写入，避免 CCTV1高清 的去后缀覆盖 CCTV1 自身
        var stripped = StripQuality(normalizedKey);
        if (stripped.Length > 0 && stripped != normalizedKey && !_displayIdMap.ContainsKey(stripped))
            _displayIdMap[stripped] = channelId;
    }

    /// <summary>归一化：转小写，去掉空格/连字符/下划线/常见分隔符及全角字符</summary>
    private static string Normalize(string s)
        => string.IsNullOrEmpty(s)
            ? string.Empty
            : Regex.Replace(s.ToLowerInvariant(), @"[\s\-_·・•　\(\)（）【】\[\]]+", "");

    /// <summary>
    /// 在归一化结果基础上，剥离常见质量/分辨率后缀，使同一频道不同版本名能映射到同一 key。
    /// 例：cctv1高清 → cctv1 / bbc1hd → bbc1 / cctv14k → cctv1
    /// 注意：仅用于辅助匹配，不改写 _displayIdMap 的精确 key。
    /// </summary>
    private static readonly Regex _qualitySuffix =
        new(@"(高清|标清|超清|蓝光|hd|sd|4k|uhd|fhd|2k|频道|channel|\d{3,4}p)+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string StripQuality(string normalized)
    {
        var result = _qualitySuffix.Replace(normalized, string.Empty);
        return result.Length > 0 ? result : normalized; // 避免全部剥除后变空
    }

    // ── XMLTV 时间解析 ────────────────────────────────────────────────────────

    /// <summary>解析 XMLTV 时间字符串，如 "20260306083600 +0800"，转换到本地时间</summary>
    private static bool TryParseXmltvTime(string s, out DateTime result)
    {
        result = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(s)) return false;

        s = s.Trim();
        // 分割日期部分和时区部分
        var spaceIdx = s.IndexOf(' ');
        var datePart = spaceIdx > 0 ? s[..spaceIdx] : s;
        var tzPart = spaceIdx > 0 ? s[(spaceIdx + 1)..].Trim() : "+0000";

        // 日期可能是 14位或12位（无秒）
        var fmt = datePart.Length >= 14 ? "yyyyMMddHHmmss" : "yyyyMMddHHmm";
        if (!DateTime.TryParseExact(datePart[..fmt.Length], fmt,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return false;

        // 解析时区偏移，格式 +HHMM 或 -HHMM
        if (tzPart.Length >= 5
            && int.TryParse(tzPart[1..3], out var tzH)
            && int.TryParse(tzPart[3..5], out var tzM))
        {
            var sign = tzPart[0] == '-' ? -1 : 1;
            var offset = new TimeSpan(sign * tzH, sign * tzM, 0);
            result = new DateTimeOffset(dt, offset).LocalDateTime;
        }
        else
        {
            // 无时区信息，当本地时间处理
            result = dt;
        }
        return true;
    }
}
