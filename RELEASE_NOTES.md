# CC-Stats (Windows) v0.1.0

The first Windows release of CC-Stats, a system tray app for monitoring your Claude API headroom in real time. Port of [rajish/cc-hdrm](https://github.com/rajish/cc-hdrm) (macOS).

## Highlights

- **Ring gauges** showing 5-hour and 7-day headroom at a glance
- **One-click OAuth** sign-in via your browser (no API keys needed)
- **Real-time polling** with configurable intervals (10s to 30m)
- **Interactive charts** with time range selection, gap/outage visualization, and usage insights
- **System tray integration** with dynamic gauge icon and rich context menu
- **Multi-account support** for managing multiple Anthropic accounts
- **Windows-native** — toast notifications, launch at login, taskbar docking

## Installation

Download `CCStats-v0.1.0-win-x64.exe` from the release assets and run it. No installer needed — it's a self-contained single-file executable.

## Requirements

- Windows 10 (build 17763) or later
- An Anthropic account (Pro, Max 5x, or Max 20x)

## Known Limitations

- macOS and Linux support is shelved for this release
- Chart data accumulates over time — analytics are most useful after several hours of polling
- Export and prune database features are placeholder (coming in v0.2.0)
