using FlyleafLib;
using System.Windows;
using WSTV.Services;
using WSTV.View;

namespace WSTV
{
    public partial class App : Application
    {
        /// <summary>引擎加载完成后变为已完成状态</summary>
        public static readonly TaskCompletionSource<bool> EngineReadyTcs = new();
        public static Task EngineReady => EngineReadyTcs.Task;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 捕获未处理的 UI 线程异常，防止程序无提示崩溃
            DispatcherUnhandledException += (_, args) =>
            {
                args.Handled = true;
                AppDialog.Show(
                    $"发生未预期的错误：\n{args.Exception.Message}",
                    "程序错误", DialogIcon.Error);
            };

            ConfigService.Instance.LoadConfig();

            // 后台静默刷新所有 EPG 订阅（不阻塞启动）
            _ = Task.Run(() => ConfigService.Instance.RefreshAllEpgAsync());

            try
            {
                Engine.Start(new EngineConfig()
                {
                    DisableAudio = false,
                    FFmpegPath = ":FFmpeg",
                    FFmpegLoadProfile = 0,
                    FFmpegHLSLiveSeek = true,
                    FFmpegLogLevel = Flyleaf.FFmpeg.LogLevel.Warn,
                    LogOutput = null,
                    LogLevel = 0,
                    LogAppend = false,
                    LogCachedLines = 20,
                    LogRollMaxFileSize = 10485760,
                    LogRollMaxFiles = 0,
                    LogDateTimeFormat = "HH.mm.ss.fff",
                    UIRefresh = true,
                    UIRefreshInterval = 250,
                    KeepDisplayActive = true
                });

                // 后台轮询，直到引擎就绪（Config 初始化完成即表示就绪）
                Task.Run(async () =>
                {
                    for (int i = 0; i < 100; i++)
                    {
                        if (Engine.Config != null)
                        {
                            EngineReadyTcs.TrySetResult(true);
                            return;
                        }
                        await Task.Delay(50);
                    }
                    // 超时仍然放行，避免永久阻塞
                    EngineReadyTcs.TrySetResult(false);
                });
            }
            catch (System.Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? "No inner exception";
                AppDialog.Show(
                    $"FFmpeg 加载失败:\n{ex.Message}\n内部: {innerMsg}",
                    "启动错误", DialogIcon.Error);
                EngineReadyTcs.TrySetException(ex);
                Application.Current.Shutdown();
            }
        }
    }
}
