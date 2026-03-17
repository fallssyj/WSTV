using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib;
using System.IO;
using System.Text.Json;
using WSTV.Models;
using WSTV.Services;

namespace WSTV.ViewModels;

public partial class VideoFilterCardViewModel : ObservableObject
{
    private static readonly string ConfigFilePath =
        Path.Combine(ConfigService.AppDataPath, "video_filter.json");

    private readonly Config _config;
    private readonly Action? _onClose;
    private VideoFilterConfig _saved = new();

    // ── HDR ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _hdrToSdrMethod = "Hable";

    // ── SDR 亮度 ──────────────────────────────────────────────────────────────
    [ObservableProperty] private float _sdrDisplayNitsCustom = 0;

    // ── 视频滤镜 ──────────────────────────────────────────────────────────────
    [ObservableProperty] private int _brightness = 0;
    [ObservableProperty] private int _contrast = 0;
    [ObservableProperty] private int _hue = 0;
    [ObservableProperty] private int _saturation = 0;

    // ── 其他选项 ─────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _syncVPFilters = true;
    [ObservableProperty] private bool _swsForce = false;
    [ObservableProperty] private bool _superResolution = false;
    [ObservableProperty] private double _maxOutputFps = 60.0;
    [ObservableProperty] private bool _use2DGraphics = false;
    [ObservableProperty] private string _gpuAdapter = "";
    [ObservableProperty] private string _selectedGpuAdapter = "(系统默认)";

    public IReadOnlyList<string> GpuAdapterOptions { get; private set; } = new[] { "(系统默认)" };

    // ── 视频处理器 ────────────────────────────────────────────────────────────
    [ObservableProperty] private string _videoProcessor = "Flyleaf";

    public static IReadOnlyList<string> VideoProcessorOptions { get; } = ["Flyleaf", "D3D11", "Auto"];

    /// <summary>系统自动检测到的 SDR 亮度（只读，供 UI 显示）</summary>
    public float SDRDisplayNits => _config?.Video?.SDRDisplayNits ?? 0;

    /// <summary>SDR Nits 行右侧标签文字：最大值 + 系统自动值</summary>
    public string SdrNitsRightLabel => $"1000 ( {SDRDisplayNits:0} )";

    public static IReadOnlyList<string> HdrMethods { get; } = ["Hable", "Reinhard", "Aces"];

    public VideoFilterCardViewModel(Config config, Action? onClose = null)
    {
        _config = config;
        _onClose = onClose;
        Load();
    }

    // ── 初始化 ────────────────────────────────────────────────────────────────

    private void Load()
    {
        VideoFilterConfig cfg;
        try
        {
            cfg = File.Exists(ConfigFilePath)
                ? JsonSerializer.Deserialize<VideoFilterConfig>(File.ReadAllText(ConfigFilePath)) ?? new()
                : new();
        }
        catch { cfg = new(); }

        _saved = cfg;
        SetFromConfig(cfg);   // 触发 OnXxxChanged → ApplyToPlayer
    }

    private void SetFromConfig(VideoFilterConfig cfg)
    {
        HdrToSdrMethod = cfg.HdrToSdrMethod;
        SdrDisplayNitsCustom = cfg.SdrDisplayNitsCustom;
        Brightness = cfg.Brightness;
        Contrast = cfg.Contrast;
        Hue = cfg.Hue;
        Saturation = cfg.Saturation;
        VideoProcessor = cfg.VideoProcessor;
        SyncVPFilters = cfg.SyncVPFilters;
        SwsForce = cfg.SwsForce;
        SuperResolution = cfg.SuperResolution;
        MaxOutputFps = cfg.MaxOutputFps;
        Use2DGraphics = cfg.Use2DGraphics;
        GpuAdapter = cfg.GPUAdapter;

        // Populate GPU adapter list from Engine if available (reflection-safe)
        try
        {
            var videoProp = typeof(Engine).GetProperty("Video");
            if (videoProp != null)
            {
                var videoObj = videoProp.GetValue(null);
                if (videoObj != null)
                {
                    var gpuProp = videoObj.GetType().GetProperty("GPUAdapters");
                    if (gpuProp != null)
                    {
                        var adapters = gpuProp.GetValue(videoObj) as System.Collections.IEnumerable;
                        var list = new List<string> { "(系统默认)" };
                        if (adapters != null)
                        {
                            foreach (var a in adapters)
                            {
                                try { var s = a?.ToString(); if (!string.IsNullOrEmpty(s) && s != "(系统默认)") list.Add(s); } catch { }
                            }
                        }
                        GpuAdapterOptions = list;
                    }
                }
            }
        }
        catch { }

        SelectedGpuAdapter = string.IsNullOrEmpty(cfg.GPUAdapter) ? "(系统默认)" : cfg.GPUAdapter;
    }

    private VideoFilterConfig ToConfig() => new()
    {
        HdrToSdrMethod = HdrToSdrMethod,
        SdrDisplayNitsCustom = SdrDisplayNitsCustom,
        Brightness = Brightness,
        Contrast = Contrast,
        Hue = Hue,
        Saturation = Saturation,
        VideoProcessor = VideoProcessor,
        SyncVPFilters = SyncVPFilters,
        SwsForce = SwsForce,
        SuperResolution = SuperResolution,
        MaxOutputFps = MaxOutputFps,
        Use2DGraphics = Use2DGraphics,
        GPUAdapter = SelectedGpuAdapter == "(系统默认)" ? "" : SelectedGpuAdapter,
    };

    // ── 实时应用 ──────────────────────────────────────────────────────────────

    private void ApplyToPlayer()
    {
        if (_config == null) return;

        _config.Video.HDRtoSDRMethod = HdrToSdrMethod switch
        {
            "Reinhard" => HDRtoSDRMethod.Reinhard,
            "Aces" => HDRtoSDRMethod.Aces,
            _ => HDRtoSDRMethod.Hable,
        };

        _config.Video.SDRDisplayNitsCustom = SdrDisplayNitsCustom;

        _config.Video.VideoProcessor = VideoProcessor switch
        {
            "D3D11" => VideoProcessors.D3D11,
            "Auto" => VideoProcessors.Auto,
            _ => VideoProcessors.Flyleaf,
        };

        _config.Video.SyncVPFilters = SyncVPFilters;
        _config.Video.SwsForce = SwsForce;
        _config.Video.SuperResolution = SuperResolution;
        _config.Video.MaxOutputFps = MaxOutputFps;
        _config.Video.Use2DGraphics = Use2DGraphics;
        _config.Video.GPUAdapter = SelectedGpuAdapter == "(系统默认)" ? "" : SelectedGpuAdapter;

        // Renderer notifications are handled by player/engine internals; avoid direct internal access here.

        TrySetFilter(FLFilters.Brightness, Brightness);
        TrySetFilter(FLFilters.Contrast, Contrast);
        TrySetFilter(FLFilters.Hue, Hue);
        TrySetFilter(FLFilters.Saturation, Saturation);

        OnPropertyChanged(nameof(SDRDisplayNits));
        OnPropertyChanged(nameof(SdrNitsRightLabel));
    }

    private void TrySetFilter(FLFilters key, int value)
    {
        try
        {
            if (_config?.Video?.FLFilters?.TryGetValue(key, out var f) == true && f != null)
                f.Value = value;
        }
        catch { /* 忽略平台不支持的滤镜 */ }
    }

    // 每个属性改变时立即应用到播放器（实时预览）
    partial void OnHdrToSdrMethodChanged(string value) => ApplyToPlayer();
    partial void OnSdrDisplayNitsCustomChanged(float value) => ApplyToPlayer();
    partial void OnBrightnessChanged(int value) => ApplyToPlayer();
    partial void OnContrastChanged(int value) => ApplyToPlayer();
    partial void OnHueChanged(int value) => ApplyToPlayer();
    partial void OnSaturationChanged(int value) => ApplyToPlayer();
    partial void OnVideoProcessorChanged(string value) => ApplyToPlayer();
    partial void OnSyncVPFiltersChanged(bool value) => ApplyToPlayer();
    partial void OnSwsForceChanged(bool value) => ApplyToPlayer();
    partial void OnSuperResolutionChanged(bool value) => ApplyToPlayer();
    partial void OnMaxOutputFpsChanged(double value) => ApplyToPlayer();
    partial void OnUse2DGraphicsChanged(bool value) => ApplyToPlayer();
    partial void OnGpuAdapterChanged(string value) => ApplyToPlayer();

    // ── 按钮命令 ──────────────────────────────────────────────────────────────

    /// <summary>保存：应用到播放器并持久化到 JSON</summary>
    [RelayCommand]
    private void Save()
    {
        ApplyToPlayer();
        var cfg = ToConfig();
        _saved = cfg;
        try
        {
            Directory.CreateDirectory(ConfigService.AppDataPath);
            File.WriteAllText(ConfigFilePath,
                JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
        _onClose?.Invoke();
    }

    /// <summary>应用：立即生效（不保存到文件）</summary>
    [RelayCommand]
    private void Apply() => ApplyToPlayer();

    /// <summary>取消：恢复到上次保存的状态并关闭面板</summary>
    [RelayCommand]
    private void Cancel()
    {
        SetFromConfig(_saved);
        _onClose?.Invoke();
    }

    /// <summary>重置：恢复所有参数到默认值</summary>
    [RelayCommand]
    private void Reset() => SetFromConfig(new VideoFilterConfig());
}
