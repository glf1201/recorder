# 录屏软件项目

## 技术栈
- C#
- .NET 8
- WPF
- FFmpeg

## 主要功能：
- ✅ 分段录像，自动录制系统声音，每段录像10分钟，自动保存和分割
- ✅ 可设置开机自动录像
- ✅ 可监控磁盘容量
- ✅ 可设置锁屏密码
- ✅ 可设置录像格式和录像保存位置
- ✅ 可设置自动删除
- ✅ 可设置除录像文件夹外其他文件的自动删除
- <img width="692" height="293" alt="image" src="https://github.com/user-attachments/assets/4aee490c-17b6-42c8-a8db-333c9210a720" />
- <img width="433" height="331" alt="image" src="https://github.com/user-attachments/assets/d726d921-e779-464a-a61d-49394aa8d2e2" />


## 目录说明
- `src/RecorderApp`：主录屏程序
- `src/WatchDog`：守护程序
- `Tools/ffmpeg/bin/ffmpeg.exe`：建议放置 FFmpeg 可执行文件

## 运行前提
1. 安装 .NET 8 SDK 和对应 Windows Desktop Runtime。
2. 推荐联网运行首启，让程序自动下载 `FFmpeg`；也可以手动将 `ffmpeg.exe` 放入 `Tools/ffmpeg/bin/`，或加入系统 `PATH`。
3. 在 Windows 10/11 上编译运行。

## 说明
- 默认保存目录为程序目录下 `Record/`。
- 默认格式为 `mkv`，按日期自动分目录，文件名为 `yyyy-MM-dd_HH-mm.ext`。
- 启动后可自动开始录制，也支持设置退出密码。
- 当前版本已接入 `FFmpeg` 自动部署与音频设备枚举；安装包封装与升级覆盖安装仍未接入。
