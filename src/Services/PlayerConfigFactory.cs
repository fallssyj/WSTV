using FlyleafLib;

namespace WSTV.Services;

/// <summary>创建经过优化配置的 FlyleafLib Player Config</summary>
public static class PlayerConfigFactory
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    public static Config CreateConfig()
    {
        var config = new Config();

        // ── Player ────────────────────────────────────────────────────────────
        // 时间单位：ticks（100 纳秒），10_000_000 ticks = 1 秒
        config.Player.AutoPlay = true;                                                  // 打开/重置后自动开始播放
        config.Player.MinBufferDuration = 5_000_000;                                    // 开始播放所需最小缓冲量 500 ms（5_000_000 ticks = 500 ms）
        config.Player.MaxLatency = 0;                                                   // 自动追帧上限（0 = 关闭）；设为正值时若延迟超出会自动加速追赶直播末端
        config.Player.MinLatency = 0;                                                   // 追帧时允许的最低延迟下限（0 = 尽可能低）
        config.Player.LatencySpeedChangeInterval = 7_000_000;                           // 追帧速度调整的最小间隔 700 ms，防止频繁切速导致音画抖动
        config.Player.FolderRecordings = $"{ConfigService.AppDataPath}/Recordings";     // 录制文件默认存放目录（未指定文件名时使用）
        config.Player.FolderSnapshots = $"{ConfigService.AppDataPath}/Snapshots";      // 截图默认存放目录
        config.Player.SeekAccurate = false;                                             // 是否精确帧级跳转（直播无需精确 Seek，false 响应更快）
        config.Player.SnapshotFormat = "bmp";                                           // 截图格式（bmp / png / jpg）
        config.Player.Stats = false;                                                    // 是否刷新码率/FPS/丢帧等统计（开启有额外 CPU 开销）
        config.Player.ThreadPriority = ThreadPriority.AboveNormal;                      // 播放线程优先级（高于正常，减少系统调度导致的卡顿）

        // ── Demuxer ───────────────────────────────────────────────────────────
        // 时间单位：ticks（100 纳秒），10_000_000 ticks = 1 秒
        config.Demuxer.AllowFindStreamInfo = true;           // 调用 avformat_find_stream_info，能获取更准确的编码参数（略增首帧延迟，直播建议保留）
        config.Demuxer.AllowInterrupts = true;           // 启用自定义中断回调，超时/取消时可及时中断 FFmpeg 阻塞调用
        config.Demuxer.AllowReadInterrupts = true;           // 允许在 av_read_frame 期间触发中断
        config.Demuxer.AllowTimeouts = true;           // 在中断回调内检查超时（配合下方各 Timeout 字段生效）
        config.Demuxer.ExcludeInterruptFmts = ["rtsp"];       // RTSP 不使用中断（部分服务端不兼容中断机制）
        config.Demuxer.BufferDuration = 50_000_000;     // 最大允许缓冲时长 5 s（50_000_000 ticks = 5 s；直播无需大缓冲）
        config.Demuxer.BufferPackets = 0;              // 0 = 不以包数量限制缓冲，仅靠 BufferDuration 控制
        config.Demuxer.MaxAudioPackets = 0;              // 0 = 不限制音频包队列（设为 500 时队列满会直接丢音频包，导致有画面没声音）
        config.Demuxer.MaxErrors = 30;             // 连续读取错误超过 30 次时停止播放
        config.Demuxer.IOStreamBufferSize = 1_048_576;      // AVIO 自定义 I/O 缓冲区 1 MB（512KB 对 4K + 5.1音频高码率流偶发读取停顿，1MB 更稳定）
        config.Demuxer.CloseTimeout = 10_000_000;     // avformat_close_input 超时 1 s（快速关闭连接）
        config.Demuxer.OpenTimeout = 300_000_000;    // avformat_open + find_stream_info 超时 30 s（默认 5 min 太长，用户体验差）
        config.Demuxer.ReadTimeout = 100_000_000;    // av_read_frame 超时 10 s（点播流）
        config.Demuxer.ReadLiveTimeout = 200_000_000;    // av_read_frame 超时 20 s（直播流，网络波动时更宽容）
        config.Demuxer.SeekTimeout = 80_000_000;     // av_seek_frame 超时 8 s（直播基本不用 Seek，保留默认即可）
        config.Demuxer.ForceFormat = null;           // null = 自动检测容器格式
        config.Demuxer.ForceFPS = 0;              // 0 = 不强制帧率（仅对 h264/hevc 裸流等无时间戳格式有效）
        config.Demuxer.FormatOptToUnderlying = false;          // false = 不将 FormatOpt 传递给嵌套 demuxer（如 HLS 内层 TS）

        // FFmpeg 格式选项（avformat 层，对所有协议生效；单位见注释）
        config.Demuxer.FormatOpt["probesize"] = "5242880";    // 探针数据量 5 MB（单位：字节；默认 50 MB，降低可减少首帧等待）
        config.Demuxer.FormatOpt["analyzeduration"] = "5000000";    // 流分析时长 5 s（单位：微秒 μs，5_000_000 μs = 5 s）
        config.Demuxer.FormatOpt["reconnect"] = "1";          // EOF 前断线后自动重连（HTTP）
        config.Demuxer.FormatOpt["reconnect_streamed"] = "1";          // 不可 Seek 的流（直播）也自动重连（HTTP）
        config.Demuxer.FormatOpt["reconnect_delay_max"] = "7";          // 重连最大间隔 7 s
        config.Demuxer.FormatOpt["user_agent"] = UserAgent;    // HTTP User-Agent，防止被 CDN 拦截
        config.Demuxer.FormatOpt["rtsp_transport"] = "tcp";        // RTSP 传输层用 TCP（UDP 易丢包，直播建议 TCP）

        // ── Decoder ───────────────────────────────────────────────────────────
        config.Decoder.VideoThreads = Math.Max(8, Environment.ProcessorCount / 2); // 硬解时此值无效（GPU 解码）；软解回退时生效，4K HEVC 软解至少需要 8 线程，取 CPU 核数/2 动态适配
        config.Decoder.MaxVideoFrames = 2;        // 已解码待渲染视频帧队列深度（减少显存/内存占用；直播 2 帧够用，过小可能卡顿）
        config.Decoder.MaxAudioFrames = 16;       // 已解码待播放音频帧队列深度（16 帧缓冲，应对网络抖动时解码线程短暂停顿）
        config.Decoder.MaxSubsFrames = 1;        // 字幕帧队列深度
        config.Decoder.MaxErrors = 200;      // 解码器连续错误容忍次数（直播花屏/损坏帧较多，需要宽容值）
        config.Decoder.AllowProfileMismatch = false;    // false = 硬解档次不匹配时自动回退软解（更稳定；true 强行用硬解可能崩溃）
        config.Decoder.AllowDropFrames = true;     // true = 性能不足时允许跳过非关键帧（skip_frame=Default；减少卡顿，直播流推荐开启）
        config.Decoder.ShowCorrupted = false;    // false = 不渲染损坏帧（避免花屏）
        config.Decoder.LowDelay = false;     // false = 不强制 LOW_DELAY 标志；LowDelay 同时作用于音频解码器，AC3/AAC 强制 LOW_DELAY 后帧时序不均匀，XAudio2 缓冲耗尽导致音频卡顿/静音

        // ── Video ─────────────────────────────────────────────────────────────
        config.Video.Enabled = true;                                // 启用视频轨
        config.Video.VideoAcceleration = true;                                // 启用 D3D11/DXVA2 硬件解码（优先 GPU，不支持时自动回退软解）
        config.Video.VideoProcessor = VideoProcessors.Flyleaf;              // 强制使用 FlyleafVP（内嵌渲染器），同时支持硬解帧和软解帧，避免软解回退时黑屏；D3D11 仅支持硬解帧
        config.Video.HDRtoSDRMethod = HDRtoSDRMethod.Hable;                // HDR→SDR 色调映射算法（FLVP 模式生效）：Hable 高光保留最好，比 Aces/Reinhard 更亮
        config.Video.DeInterlace = DeInterlace.Auto;                    // 自动检测并反交错（电视信源常有交错信号）
        config.Video.SwsForce = false;                               // false = 软解帧优先用 FLVP 处理（true 强制 SwsScale，仅兼容性极差时开启）
        config.Video.ClearScreen = false;                               // false = 停止/关闭时保留最后一帧（不清黑屏）
        config.Video.GPUAdapter = "";                                  // 空字符串 = 使用系统默认 GPU（可填 "rx 580" 等描述字符串指定 GPU）
        config.Video.MaxVerticalResolutionCustom = 0;                                   // 0 = 不自定义分辨率上限（插件按系统能力自动决定）
        config.Video.BackColor = System.Windows.Media.Colors.Black;   // 视频区域背景色（无画面时填充）

        // ── Audio ─────────────────────────────────────────────────────────────
        config.Audio.Enabled = true;     // 启用音频轨
        config.Audio.FiltersEnabled = false;    // false = 使用 SWR（libswresample）重采样，兼容所有编码格式；true 用 avfilter 但对 E-AC3/DTS 等可能初始化失败导致静音
        config.Audio.VolumeMax = 150;      // 音量放大上限 150%（XAudio2 主音量上限，超过 100% 为数字放大）

        return config;
    }
}
