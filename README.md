<h1 align="center">CC-Stats (Windows)</h1>

<p align="center">
  Claude API headroom monitor for your Windows system tray.<br>
  Never get surprise-throttled mid-task.
</p>

<p align="center">
  <a href="https://github.com/Codename-11/CC-Stats/releases"><img src="https://img.shields.io/github/v/release/Codename-11/CC-Stats?include_prereleases&label=release" alt="Release"></a>
  <a href="https://github.com/Codename-11/CC-Stats/actions/workflows/windows-ci.yml"><img src="https://github.com/Codename-11/CC-Stats/actions/workflows/windows-ci.yml/badge.svg" alt="CI"></a>
  <a href="https://opensource.org/licenses/MIT"><img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="MIT"></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8.0-purple.svg" alt=".NET 8"></a>
</p>

<p align="center">
  <a href="#installation">Install</a> ·
  <a href="#features">Features</a> ·
  <a href="#development">Dev</a> ·
  <a href="./TODO.md">Roadmap</a> ·
  <a href="./CHANGELOG.md">Changelog</a>
</p>

---

> **Windows port** of [rajish/cc-hdrm](https://github.com/rajish/cc-hdrm) (macOS SwiftUI). Built with Avalonia 11, .NET 8, ReactiveUI, and LiveCharts2.

## Features

| Feature | Description |
|---------|-------------|
| **Ring Gauges** | 5-hour and 7-day headroom with animated fill, color-coded severity, and trend badges (→ Stable, ↗ Rising, ⬆ Rapid) |
| **One-Click OAuth** | Sign in via your browser — no API keys, no config files |
| **Zero Tokens Spent** | Reads quota data only, never the chat API |
| **Inline Analytics** | Click sparkline to expand — time ranges (24h/7d/30d), insights, breakdown bar |
| **Popout Charts** | Borderless analytics window with step-area and bar charts, zoom, gap/outage visualization |
| **Pattern Detection** | Identifies overpaying, underpowering, usage decay, usage spikes — dismissible cards |
| **Threshold Alerts** | Configurable warnings + Windows toast notifications, billing-period-aware |
| **Extra Usage Tracking** | Dollar-based spend vs. limit, 4-tier color ramp, "entered extra usage" alerts |
| **Multi-Account** | Sign in with multiple Anthropic accounts, hot-swap from tray or footer flyout |
| **Adaptive Polling** | Auto-speeds to 15s when Claude Code is running, slows when idle |
| **Projected Exhaustion** | Dashed forecast line showing when headroom hits 0% at current burn rate |
| **Budget Calculator** | "~2h 15m of active coding left" estimate from slope + remaining headroom |
| **Local Cache Fallback** | Reads Claude Code's statusline cache for instant data on startup |
| **Dock to Taskbar** | Bottom-anchored, grows upward, always-on-top pin toggle |
| **Quick Copy** | Right-click gauge → copy formatted status to clipboard |

## Installation

### From GitHub Releases

Download `CCStats-v0.1.0-win-x64.exe` from [Releases](../../releases) and run it.

Self-contained single-file executable — no .NET SDK or runtime needed.

### From Source

```bash
git clone https://github.com/Codename-11/CC-Stats.git
cd CC-Stats
dotnet run --project windows/CCStats.Desktop/CCStats.Desktop.csproj
```

**Requirements:** Windows 10 (build 17763+), .NET 8 SDK (for source builds only).

## Development

```bash
./run_dev.sh          # Bash — auto-kills stale processes, Ctrl+C works
.\run_dev.ps1         # PowerShell
dotnet build windows/CCStats.Windows.sln
```

Press **F5** in the app to cycle preview states (Signed Out → Authorizing → Connected → Critical → Disconnected).

### Tech Stack

| Component | Technology |
|-----------|-----------|
| UI | [Avalonia 11.3](https://avaloniaui.net) + [FluentAvalonia 2.2](https://github.com/amwx/FluentAvalonia) |
| State | [ReactiveUI](https://reactiveui.net) + MVVM |
| Charts | [LiveCharts2](https://livecharts2.com) (SkiaSharp) |
| Database | SQLite ([Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/)) |
| Credentials | DPAPI encryption |
| Notifications | [Microsoft.Toolkit.Uwp.Notifications](https://learn.microsoft.com/en-us/windows/apps/design/shell/tiles-and-notifications/send-local-toast) |
| Target | .NET 8 (`net8.0-windows10.0.17763.0`) |

### Project Structure

```
windows/
├── CCStats.Core/              # Platform-agnostic: models, state, services
│   ├── Models/                # AppState, HeadroomState, WindowState, etc.
│   ├── State/                 # Immutable AppState record
│   ├── Services/              # OAuth, Polling, API, DB, Preferences (14 services)
│   └── Formatting/            # Date/time formatting
└── CCStats.Desktop/           # Avalonia desktop app
    ├── Controls/              # Ring gauge, countdown, sparkline, etc.
    ├── Services/              # Tray icon, toast, launch-at-login
    ├── ViewModels/            # MVVM ViewModels
    └── Views/                 # AXAML views
```

## Upstream

Windows port of [rajish/cc-hdrm](https://github.com/rajish/cc-hdrm) (macOS SwiftUI/AppKit menu bar app). Feature parity achieved with 6 additional Windows-exclusive features (multi-account, adaptive polling, projected exhaustion, budget calculator, local cache, click-to-refresh).

## License

Same license as upstream. See [LICENSE](LICENSE).
