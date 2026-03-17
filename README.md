<div align="center">

<img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet">
<img src="https://img.shields.io/badge/WPF-Desktop-512BD4?logo=windows">
<img src="https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D4?logo=windows">
<img src="https://img.shields.io/badge/Platform-x64-0078D4?logo=windows">
<img src="https://img.shields.io/badge/License-MIT-22c55e">

<br><br>

<img src="https://raw.githubusercontent.com/fallssyj/WSTV/refs/heads/main/src/Assets/icons/app.svg" width="120" />

<br><br>

基于 WPF（.NET 8）为 Windows 打包的 M3U 播放客户端。
</div>

---


## 功能特性

| 功能 | 说明 |
|------|------|
| 📺 订阅管理 | 支持 M3U / M3U8 / JSON 格式，可添加多个订阅源 |
| 🔍 频道浏览 | 分组显示、关键词搜索、收藏管理 |
| 📅 EPG 节目单 | 支持 XMLTV 格式，自动缓存，可管理多个 EPG 订阅 |
| 🔄 多线路切换 | 播放失败时自动尝试备用线路 |
| 📽️ 画中画 (PiP) | 将播放窗口剥离为浮动窗口，常驻最前 |
| 🎬 图像调节 | 亮度 / 对比度 / 饱和度等参数动态调节 |
| ⚡ 硬件加速 | 基于 FFmpeg 8 + DirectX 渲染 |

## 下载与运行

前往 [Releases](https://github.com/fallssyj/WSTV/releases) 页面下载最新版本，解压后运行 `WSTV.exe`。

> **系统要求**：Windows 10 / 11（x64）

## 从源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download)。

```bash
git clone https://github.com/fallssyj/WSTV.git
cd WSTV/src
dotnet run --project WSTV.csproj
```

## 使用说明

1. 启动后进入「订阅」页面，添加 M3U 链接或本地文件
2. 点击「刷新」拉取频道列表
3. 在 EPG 页面添加 XMLTV 节目单源（可选）
4. 双击频道开始播放

## 格式说明

### 多线路

在 M3U 订阅中，**相同 `tvg-name`（或 EXTINF 显示名）的条目会自动合并为同一频道的多条线路**，播放时若当前线路失败，自动依次尝试剩余线路，切换频道时失败记录重置。

```m3u
#EXTINF:-1 tvg-id="1" tvg-name="mytv",mytv
http://source1.example.com/mytv/stream.m3u8
#EXTINF:-1 tvg-id="1" tvg-name="mytv",mytv
http://source2.example.com/mytv/index.m3u8
```

以上两条条目会合并为同一频道，线路 1、线路 2。

支持的流协议：`http` / `https` / `rtsp` / `rtmp` / `udp` / `rtp`

除 M3U 外，订阅源也可使用 JSON 格式（`Channel[]`，每个对象包含 `links` 数组）。

```json
[
  {
    "Links": [
      "http://source1.example.com/mytv/stream.m3u8",
      "http://source2.example.com/mytv/index.m3u8"
    ],
    "TvgName": "mytv",
    "TvgId": "1",
    "GroupTitle": "mytv",
    "ExtinfName": "mytv",
    "Favorite": false,
    "TvgLogo": "http://source2.example.com/mytv.png"
  }
]
```

### EPG 匹配

EPG 节目单采用**五级精确匹配**（不做模糊/包含匹配，按优先级依次回退）：

| 级别 | 匹配依据 | 说明 |
|------|----------|------|
| ① | `tvg-id` 精确匹配 | 最优先，与 XMLTV `channel id` 完全一致时直接命中 |
| ② | `tvg-id` 归一化 | 转小写并去除空格 / 连字符 / 下划线后比较 |
| ③ | `tvg-id` 去质量后缀 | 在②基础上剥离 `高清 / HD / 4K / 标清` 等后缀后比较 |
| ④ | 频道名称归一化 | 以频道显示名代替 `tvg-id` 重复②流程 |
| ⑤ | 频道名称去质量后缀 | 在④基础上剥离质量后缀后比较 |

> 推荐在 M3U 中填写 `tvg-id` 并与 XMLTV 的 `channel id` 保持一致，可获得最精准的节目单匹配效果。

## 依赖

| 包 | 版本 | 用途 |
|----|------|------|
| [FlyleafLib](https://github.com/SuRGeoNix/Flyleaf) | 3.10.2 | 播放引擎 |
| Flyleaf.FFmpeg.Bindings | 8.0.1 | FFmpeg 绑定 |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM 框架 |
| Microsoft.Xaml.Behaviors.Wpf | 1.1.142 | 交互行为 |

## 许可

[MIT License](LICENSE) — Copyright © 2026 syj
