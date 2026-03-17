namespace WSTV.Models;

/// <summary>
/// 视频滤镜配置，持久化到 %LocalAppData%/WSTV/video_filter.json
/// </summary>
public class VideoFilterConfig
{
    /// <summary>HDR→SDR 色调映射算法名称（Hable / Reinhard / Aces）</summary>
    public string HdrToSdrMethod { get; set; } = "Hable";

    /// <summary>SDR 峰值亮度自定义值（0 = 跟随自动检测）</summary>
    public float SdrDisplayNitsCustom { get; set; } = 0;

    /// <summary>亮度偏移（-100 ~ 100）</summary>
    public int Brightness { get; set; } = 0;

    /// <summary>对比度偏移（-100 ~ 100）</summary>
    public int Contrast { get; set; } = 0;

    /// <summary>色调偏移（-180 ~ 180）</summary>
    public int Hue { get; set; } = 0;

    /// <summary>饱和度偏移（-100 ~ 100）</summary>
    public int Saturation { get; set; } = 0;

    /// <summary>视频处理器（Flyleaf / D3D11 / Auto）</summary>
    public string VideoProcessor { get; set; } = "Flyleaf";

    /// <summary>是否在两个 VideoProcessor 之间同步滤镜值</summary>
    public bool SyncVPFilters { get; set; } = true;

    /// <summary>是否强制使用 SwsScale 而非 VP</summary>
    public bool SwsForce { get; set; } = false;

    /// <summary>是否启用超分（仅 D3D11 有效）</summary>
    public bool SuperResolution { get; set; } = false;

    /// <summary>最大输出帧率（用于限制渲染）</summary>
    public double MaxOutputFps { get; set; } = 60.0;

    /// <summary>是否使用 2D 绘图（Direct2D）</summary>
    public bool Use2DGraphics { get; set; } = false;

    /// <summary>指定 GPU Adapter 名称（可选）</summary>
    public string GPUAdapter { get; set; } = "";
}
