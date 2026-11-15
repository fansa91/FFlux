<p align="center">
  <img width="128" align="center" src="FFlux/Assets/StoreLogo.scale-400.png">
</p>
<h1 align="center">
  FFlux
</h1>
<p align="center">
  Windows 上的本地音乐 / 视频播放器
</p>

FFlux 是一个面向 Windows 桌面的媒体播放器，把一个音乐库和一个视频播放器装进同一个窗口：左侧是音乐、视频、网络、播放队列，展开后是专辑、艺术家、文件夹、播放列表，底部是常驻的播放条，随时可以铺开成全屏播放页。

播放内核是 [FFmpegInteropX](https://github.com/ffmpeginteropx/FFmpegInteropX)，因此 MKV、FLAC、HEVC、ASS 字幕这些 Windows 自带解码器不支持的格式都能直接播放，不依赖系统解码包。界面用 WinUI 3 写成，随应用一起发布的是解压即用的绿色版——不需要安装包，也不会把文件散落到系统目录。

## 功能

- **音乐库与视频库**：扫描系统的音乐 / 视频库，也可以在设置里追加任意文件夹；子目录会被自动合并，不会重复扫描
- **专辑封面**：优先读文件夹里的 `cover.jpg` / `folder.png` 或音频内嵌封面，都没有时用 FFmpeg 从音频里取图
- **视频缩略图**：向系统外壳请求真实画面帧，而不是一个通用图标
- **播放历史**：首页的「最近播放」写在应用数据库里，重启后仍在
- **在线链接**：把网络流地址存下来，下次直接点开
- **播放列表**：自建播放列表，支持拖放排序
- **文件夹浏览与搜索**：按目录结构翻文件，或整库搜索
- **播放器**：播放队列、音轨 / 字幕轨切换、倍速、手势调节音量与进度、把当前画面另存为图片
- **多语言界面**，跟随系统语言

## 下载

在 [Releases](https://github.com/fansa91/FFlux/releases) 下载最新版。每次发布提供两个文件，功能完全一样，区别只在运行时是否随包：

| 下载 | 体积 | 运行时要求 |
| --- | --- | --- |
| `FFlux-<版本>-win-x64-self-contained.zip` | 大 | **不需要**。解压后直接运行 `FFlux.exe` |
| `FFlux-<版本>-win-x64-framework-dependent.zip` | 小 | 需预装 [.NET 11 桌面运行时](https://dotnet.microsoft.com/download) 与 Windows App Runtime 2.6 |

拿不准就选第一个。

解压到任意目录即可使用，卸载等于删除该目录。用户数据单独存放在 `%LOCALAPPDATA%\FFlux`，重装不会丢。

**系统要求**：Windows 10 1903（build 18362）及以上，或 Windows 11；x64。

## 从源码构建

需要 Windows 上的 .NET 11 SDK（或 Visual Studio 2026 + WinUI 应用开发工作负载）。

```powershell
# 构建
dotnet build FFlux/FFlux.csproj -c Release -p:Platform=x64

# 发布成可分发目录，不依赖运行时
dotnet publish FFlux/FFlux.csproj -c Release -r win-x64 -p:Platform=x64 `
  -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:PublishAot=true

# 发布成依赖运行时的精简版
dotnet publish FFlux/FFlux.csproj -c Release -r win-x64 -p:Platform=x64 `
  -p:SelfContained=false -p:WindowsAppSDKSelfContained=false -p:PublishAot=false
```

用 Visual Studio 打开 `FFlux.slnx`，平台选 x64，F5 即可调试。

## 版本与发布

版本号定义在 [`Directory.Build.props`](Directory.Build.props) 的 `<Version>`，起始版本 **0.1.0**。

发布流程只有一条：[`.github/workflows/release.yml`](.github/workflows/release.yml)。推一个 `v` 开头的 tag 就会触发构建，并把上面两个 zip 挂到对应版本的 Release 上。

```shell
git tag v0.1.1
git push origin v0.1.1
```

tag 里的版本号会通过 `-p:Version=` 注入编译，写进程序集元数据，所以设置页底部显示的版本、`FFlux.exe` 文件属性里的版本、zip 文件名三者始终一致。

## 项目结构

| 项目 | 内容 |
| --- | --- |
| `FFlux` | WinUI 3 应用本体：页面、控件、应用入口 |
| `FFlux.Core` | 与界面无关的逻辑：库扫描、数据库、播放服务、视图模型 |
| `FFlux.UI` | 共享的 XAML 样式与控件资源字典 |
| `FFlux.Lively` | 与 Lively Wallpaper 的联动组件 |
| `FFlux.Core.Tests` | 单元测试（TUnit） |

数据都放在 `%LOCALAPPDATA%\FFlux`：

- `fflux.db` —— 媒体库缓存、播放历史、播放列表、自定义库根目录
- `streams.json` —— 在线链接
- `startup-error.log` —— 启动失败时的异常栈

## 参与贡献

发现 bug、想提建议或提问，欢迎开 issue；PR 同样欢迎。新代码请保持与现有风格一致，逻辑层改动请补上对应的单元测试：

```powershell
dotnet build FFlux.Core.Tests/FFlux.Core.Tests.csproj -c Release -p:Platform=x64
cd FFlux.Core.Tests/bin/x64/Release/net11.0-windows10.0.26100.0
dotnet FFlux.Core.Tests.dll
```

## 致谢

FFlux 源自 [Screenbox](https://github.com/huynhsontung/Screenbox)，在把它从 UWP 迁移到 WinUI 3、改成免安装绿色版的过程中逐步长成现在这个样子。界面设计、图标与多语言资源都来自上游，第三方组件的许可声明见 [NOTICE.md](NOTICE.md)。

开源协议见 [LICENSE](LICENSE)。
