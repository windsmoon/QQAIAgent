# 视频元数据探测依赖

本目录保存 Windows x64 的 `ffprobe.exe`，来自本机通过 WinGet 安装的 Gyan FFmpeg 9.0 full build。`LICENSE` 和 `README.txt` 为该发行包附带的原始文件，发行包来源为 <https://www.gyan.dev/ffmpeg/builds/>。

视频理解工具只使用 ffprobe 读取视频文件大小和时长，不做转码，因此这里只保存所需的 `ffprobe.exe`。

Host 构建和发布会把本目录内容复制到程序目录的 `Third/FFmpeg/`。视频理解配置的 `FfprobePath` 默认指向 `Third/FFmpeg/ffprobe.exe`，不依赖系统 PATH。

二进制文件沿用项目现有的 Third 忽略规则，不纳入 Git。其他开发机器需要在本目录准备同名的 Windows x64 `ffprobe.exe` 及发行包许可文件，再构建或发布 Host。
