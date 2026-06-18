# 值守录屏工具

[中文](./README.md) | [English](./README.en.md)

基于 `C#`、`.NET 8`、`WPF` 和 `FFmpeg` 开发的 Windows 值守录屏工具。

## 项目简介

本项目用于 Windows 桌面长期值守录屏，支持分段录像、托盘驻留、开机启动、守护拉起、定时清理和系统声音采集。

## 主要功能

- 每 10 分钟自动切片录制并保存录像文件
- 通过 WASAPI loopback 回采系统声音，并与 FFmpeg 录屏合成
- 支持开机启动和静默驻留托盘
- 内置 `WatchDog` 守护程序，主进程异常退出后可自动拉起
- 支持录像格式、保存路径、显示器目标和音频设备配置
- 支持录像目录和最多 5 个附加目录的定时清理
- 支持退出密码、单实例唤起、最近日志和最近录像展示

## 界面截图

<img width="692" height="293" alt="Main Window" src="https://github.com/user-attachments/assets/4aee490c-17b6-42c8-a8db-333c9210a720" />

<img width="433" height="331" alt="Settings Window" src="https://github.com/user-attachments/assets/d726d921-e779-464a-a61d-49394aa8d2e2" />

## 技术栈

- C#
- .NET 8
- WPF
- HandyControl
- FFmpeg
- NAudio

## 目录结构

- `src/RecorderApp`：主录屏程序
- `src/WatchDog`：守护程序
- `images`：图标和界面资源
- `Tools/ffmpeg/bin/ffmpeg.exe`：推荐的 FFmpeg 放置位置

## 快速开始

1. 安装 `.NET 8 SDK` 和对应的 Windows Desktop Runtime。
2. 在 Windows 10/11 上编译或发布本解决方案。
3. 将 `ffmpeg.exe` 放到 `Tools/ffmpeg/bin/` 下，或在支持的环境中由程序首次运行时准备。
4. 通过 `RecorderApp.exe` 或打包后的启动脚本启动程序。

## 构建

```bash
dotnet build RecorderSuite.sln -c Release
```

## 使用说明

- 默认录像目录为程序目录下的 `Record/`。
- 录像按日期自动分目录保存，文件名格式为 `yyyy-MM-dd_HH-mm.ext`。
- 程序支持启动后自动录制，并可常驻系统托盘。
- 退出程序前可进行密码校验。
- 当前清理规则已改为按目录删除过期录像，而不是只删除有限数量的单个文件。

## 发布包

- 打包产物：`dist/Recorder.zip`
- 发布页面：GitHub Releases
