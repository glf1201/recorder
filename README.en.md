# Recorder

[中文](./README.md) | [English](./README.en.md)

Windows unattended screen recording tool built with `C#`, `.NET 8`, `WPF`, and `FFmpeg`.

## Overview

This project is designed for long-running desktop recording on Windows. It supports segmented recording, tray mode, auto-start, watchdog recovery, scheduled cleanup, and system-audio capture.

## Features

- Automatic segmented recording every 10 minutes
- Uses WASAPI loopback to capture system audio and merge it into FFmpeg recording
- Supports auto-start with Windows and tray-only startup
- Includes `WatchDog` to restart the recorder if the main process exits unexpectedly
- Supports recording format, storage path, display target, and audio device configuration
- Supports scheduled cleanup for recording folders and up to 5 additional cleanup directories
- Supports exit password, single-instance activation, recent logs, and recent recordings

## Screenshots

<img width="692" height="293" alt="Main Window" src="https://github.com/user-attachments/assets/4aee490c-17b6-42c8-a8db-333c9210a720" />

<img width="433" height="331" alt="Settings Window" src="https://github.com/user-attachments/assets/d726d921-e779-464a-a61d-49394aa8d2e2" />

## Tech Stack

- C#
- .NET 8
- WPF
- HandyControl
- FFmpeg
- NAudio

## Project Structure

- `src/RecorderApp`: Main desktop recorder application
- `src/WatchDog`: Watchdog process
- `images`: Application icons and UI assets
- `Tools/ffmpeg/bin/ffmpeg.exe`: Recommended FFmpeg location

## Quick Start

1. Install `.NET 8 SDK` and the corresponding Windows Desktop Runtime.
2. Build or publish the solution on Windows 10/11.
3. Place `ffmpeg.exe` under `Tools/ffmpeg/bin/`, or let the application prepare it during first run if your setup supports it.
4. Start the app with `RecorderApp.exe` or the packaged launcher script.

## Build

```bash
dotnet build RecorderSuite.sln -c Release
```

## Notes

- The default recording directory is `Record/` under the application folder.
- Recordings are stored by date folder, using filenames like `yyyy-MM-dd_HH-mm.ext`.
- The recorder can start automatically after launch and can remain in the system tray.
- Exit protection supports password verification before shutdown.
- Cleanup rules now delete expired recording folders by directory.

## Release Package

- Packaged build output: `dist/Recorder.zip`
- Release page: GitHub Releases
