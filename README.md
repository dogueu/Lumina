# Lumina

<div align="center">

**Ultra-lightweight, modern DDC/CI monitor brightness manager & OSD for Windows 11.**

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=flat&logo=windows&logoColor=white)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-F5A623?style=flat&logo=open-source-initiative&logoColor=white)](LICENSE)
[![Idle RAM](https://img.shields.io/badge/Idle%20RAM-~8%20MB-2ea44f?style=flat)](https://github.com/dogueu/Lumina)

[ **Türkçe Dokümantasyon** ](README.tr.md)

</div>

---

## Features

- **Hardware DDC/CI Control:** Directly controls the true panel backlight of external monitors via the DDC/CI protocol; no fake dark software overlays.
- **Windows 11 Fluent Design:** Modern, sleek On-Screen Display (OSD) and flyout matching Windows 11 native aesthetics, smooth entrance animations, and rounded corners.
- **Zero Background Overhead:** 0% CPU consumption while idle; no background timers or polling loops. Idles at only **~8 MB RAM**.
- **Customizable Global Hotkeys:** Adjust brightness on the fly from within any app or game with customizable global shortcuts.
- **Tray Wheel Control:** Hover over the system tray icon and scroll the mouse wheel up or down to adjust brightness instantly.
- **Custom Monitor Names:** Assign friendly labels to each detected display (e.g. *Main Display*, *Gaming Monitor*).
- **State Memory & Auto-Startup:** Remembers the last brightness level per monitor across reboots; optional silent launch on Windows startup.

---

## Getting Started

### 1. Portable Single-File (Recommended)
Zero installation or dependencies required:
1. Download the latest `Lumina.exe` from the [Releases](../../releases) section.
2. Place it in a permanent folder (e.g. *Documents* or *C:\Tools\Lumina*).
3. Double-click to run. Lumina immediately nests in the system tray!

### 2. Building from Source
Requirements:
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 (Build 1903+) or Windows 11

```bash
# Clone the repository
git clone https://github.com/dogueu/Lumina.git
cd Lumina

# Run in development mode
dotnet run

# Publish as a standalone single-file executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o Publish
```

---

## Controls & Default Shortcuts

| Action | Default | Description |
|---|---|---|
| **Increase Brightness** | `Ctrl + Alt + ↑` | Increases brightness by the step percentage and displays the OSD. |
| **Decrease Brightness** | `Ctrl + Alt + ↓` | Decreases brightness by the step percentage and displays the OSD. |
| **Flyout Menu** | Tray Left-Click | Opens the Windows 11-style multi-monitor brightness slider panel. |
| **Quick Adjust** | Tray Wheel Scroll | Adjusts brightness seamlessly when scrolling over the tray icon. |
| **Options Menu** | Tray Right-Click | Settings window, refresh monitors, and exit. |

> *Note: Shortcuts can be customized to any key or combination (such as `F9`/`F10` or `Page Up`/`Page Down`) in the Settings window.*

---

## Built With

- **Framework:** [.NET 8 Windows Presentation Foundation (WPF)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
- **UI & Styling:** [WPF-UI (Fluent 2 Design)](https://github.com/lepoco/wpfui)
- **Monitor API:** Windows Physical Monitor API (`dxva2.dll` / DDC/CI)
- **Native Integration:** Win32 P/Invoke (`dwmapi.dll`, `user32.dll`, `shell32.dll`)

---

## License

This project is licensed under the [MIT License](LICENSE).
