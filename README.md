# Lumina

<div align="center">

**Ultra-lightweight, zero-dependency DDC/CI monitor brightness manager & OSD for Windows 10/11.**

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20(2004%2B)%20%7C%2011-0078D6?style=flat&logo=windows&logoColor=white)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-F5A623?style=flat&logo=open-source-initiative&logoColor=white)](LICENSE)
[![Idle CPU](https://img.shields.io/badge/Idle%20CPU-0%25-2ea44f?style=flat)](https://github.com/dogueu/Lumina)

[ **Türkçe Dokümantasyon** ](README.tr.md)

</div>

---

## Features

- **Hardware DDC/CI Control:** Directly controls the true panel backlight of external monitors via the Windows Physical Monitor API (`dxva2.dll`); no software dimming overlays.
- **3 Appearance Modes:**
  - **Tinted Glass (`Renkli cam`):** Translucent surface with optional `Windows.UI.Composition` `HostBackdropBrush` blur and adjustable backdrop opacity.
  - **System (`Sistem`):** Classic Windows 11 Quick Settings aesthetic synchronized with your Windows accent color.
  - **Solid (`Katı`):** Crisp opaque dark/light panel matching your Windows theme.
- **Native-Style OSD:** Compact, click-through On-Screen Display (`192 × 46` DIP) with smooth entrance/exit transitions that preserves backdrop blur until hidden.
- **Zero Background Overhead:** Event-driven Win32 architecture (`RegisterHotKey`, `Shell_NotifyIcon`, `WM_DISPLAYCHANGE`); 0% CPU while idle with automatic background working-set trimming.
- **Per-Monitor Controls:** Custom friendly monitor names, per-monitor toggle for global hotkeys/tray wheel exclusion, and drag-and-drop monitor reordering synced across Settings and the tray flyout.
- **Tray Wheel & Global Hotkeys:** Scroll directly over the tray icon or press customizable global shortcuts (`Ctrl + Alt + ↑ / ↓` by default) from any app or full-screen game.
- **State Memory & Auto-Startup:** Remembers per-monitor brightness levels across reboots and supports optional silent startup via `HKCU\...\Run`.

---

## Getting Started

### 1. Portable Single-File (Recommended)
Zero installation or external runtime dependencies required:
1. Download the latest `Lumina.exe` from the [Releases](https://github.com/dogueu/Lumina/releases) section.
2. Place it in a permanent folder (e.g. *Documents* or *C:\Tools\Lumina*).
3. Double-click to run — Lumina nests cleanly in the system tray.

### 2. Building from Source
Requirements:
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 (Version 2004 / Build 19041+) or Windows 11

```bash
# Clone the repository
git clone https://github.com/dogueu/Lumina.git
cd Lumina

# Run in development mode
dotnet run

# Publish as a standalone compressed single-file executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o Publish
```

---

## Controls & Default Shortcuts

| Action | Default | Description |
|---|---|---|
| **Increase Brightness** | `Ctrl + Alt + ↑` | Increases brightness of active hotkey monitors and displays the OSD. |
| **Decrease Brightness** | `Ctrl + Alt + ↓` | Decreases brightness of active hotkey monitors and displays the OSD. |
| **Flyout Menu** | Tray Left-Click | Opens the multi-monitor slider panel and embedded Settings view. |
| **Quick Adjust** | Tray Wheel Scroll | Adjusts brightness seamlessly when scrolling over the tray icon. |
| **Options Menu** | Tray Right-Click | Quick access to Settings, monitor refresh, and Exit. |

---

## Architecture & Built With

- **Framework:** Pure [.NET 8 Windows Presentation Foundation (WPF)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/) — zero external NuGet packages.
- **Backdrop Compositor:** `Windows.UI.Composition` (`HostBackdropBrush` via WinRT interop).
- **Monitor API:** Windows Physical Monitor Configuration API (`dxva2.dll` / DDC/CI).
- **Native Integration:** Win32 P/Invoke (`user32.dll`, `shell32.dll`, `dwmapi.dll`, `shcore.dll`, `gdi32.dll`).

---

## License

- Application source code is licensed under the [MIT License](LICENSE).
- Embedded `Geist-SemiBold.ttf` font is licensed under the [SIL Open Font License 1.1](Fonts/OFL.txt) (Vercel).
