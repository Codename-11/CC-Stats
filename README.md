# CC-Stats (Windows)

Windows system tray app for monitoring your Claude API headroom. Never get surprise-throttled mid-task.

> **Windows port** of [rajish/cc-hdrm](https://github.com/rajish/cc-hdrm) (macOS). Built with Avalonia, .NET 8, and LiveCharts2.

## Features

### At a Glance
- **System tray headroom gauge** — always-visible with color-coded severity (green / yellow / orange / red) and burn rate arrows (→ ↗ ⬆)
- **One-click sign-in** — OAuth via your browser, no API keys or config files
- **Zero tokens spent** — reads quota data, not the chat API

### Popover Detail
- **Ring gauges** — 5-hour and 7-day headroom with animated fill and slope indicators
- **Reset countdowns** — relative ("resets in 2h 13m") and absolute ("at 4:52 PM")
- **Extra usage tracking** — dollar-based spend vs. limit with color-coded progress bar
- **24-hour sparkline** — step-area chart; click to expand inline or pop out

### Analytics
- **Historical charts** — 24h, 7d, 30d, and All views with step-area and bar chart
- **Subscription breakdown** — used vs. unused dollars prorated from your plan
- **Pattern detection** — identifies overpaying, underpowering, usage decay, and suggests tier changes
- **Cycle-over-cycle comparison** — utilization across billing periods
- **Gap & outage visualization** — gray bands for polling gaps, red bands for API outages

### Notifications
- **Threshold alerts** — configurable warnings at customizable headroom levels
- **Extra usage alerts** — notifications at 50%, 75%, 90% of extra usage
- **Windows toast notifications** — native system notifications

### Data & Settings
- **Local SQLite storage** — every poll persisted, tiered rollups for efficient querying
- **Configurable retention** — 30 days to 5 years
- **Configurable poll interval** — 10 seconds to 30 minutes
- **Multi-account support** — sign in with multiple Anthropic accounts
- **Database management** — clear, export, and prune controls

## Installation

### From GitHub Releases

Download the latest `CCStats-vX.Y.Z-win-x64.exe` from [Releases](../../releases) and run it.

Self-contained single-file executable — no .NET SDK or runtime install needed.

### From Source

```bash
# Prerequisites: .NET 8 SDK
git clone https://github.com/rajish/cc-hdrm.git
cd cc-hdrm
dotnet run --project windows/CCStats.Desktop/CCStats.Desktop.csproj
```

## Requirements

- Windows 10 (build 17763) or later
- An Anthropic account (Pro, Max 5x, or Max 20x)

## Development

```bash
./run_dev.sh          # Bash — kills stale processes, Ctrl+C works
.\run_dev.ps1         # PowerShell
dotnet build windows/CCStats.Windows.sln
```

Press **F5** in the app to cycle through preview states (Signed Out → Authorizing → Connected → Critical → Disconnected).

### Tech Stack

| Component | Technology |
|-----------|-----------|
| UI Framework | Avalonia 11.3 |
| Theme | FluentAvalonia 2.2 (Windows 11 style) |
| State | ReactiveUI + MVVM |
| Charts | LiveCharts2 (SkiaSharp) |
| Database | SQLite (Microsoft.Data.Sqlite) |
| Credentials | DPAPI encryption |
| Notifications | Microsoft.Toolkit.Uwp.Notifications |
| Target | .NET 8 (net8.0-windows10.0.17763.0) |

### Project Structure

```
windows/
├── CCStats.Core/              # Platform-agnostic business logic
│   ├── Models/                # AppState, HeadroomState, WindowState, etc.
│   ├── State/                 # Immutable AppState record
│   ├── Services/              # OAuth, Polling, API, DB, Preferences, etc.
│   └── Formatting/            # Date/time formatting utilities
└── CCStats.Desktop/           # Avalonia desktop app
    ├── Controls/              # Custom controls (ring gauge, sparkline, etc.)
    ├── Services/              # Tray icon, toast notifications, launch-at-login
    ├── ViewModels/            # MVVM ViewModels
    └── Views/                 # AXAML views
```

## Upstream

This is a Windows port of [rajish/cc-hdrm](https://github.com/rajish/cc-hdrm), a macOS SwiftUI/AppKit menu bar app. The Windows version aims for feature parity using platform-native technologies.

## License

Same license as upstream. See [LICENSE](LICENSE).
