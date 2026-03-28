# Changelog

All notable changes to CC-Stats (Windows) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-03-28

### Added
- Windows system tray app (Avalonia 11 + ReactiveUI + .NET 8)
- Ring gauges for 5-hour and 7-day headroom with animated fill and slope indicators
- OAuth sign-in via Anthropic (PKCE flow, polished browser callback page)
- Real-time background polling engine (configurable 10s-30m interval)
- LiveCharts2 sparkline with inline expansion and pop-out analytics window
- Analytics window with time range selection (24h/7d/30d/All), step-area and bar charts
- Headroom breakdown bar, pattern detection cards, tier recommendations, usage insights
- Gap/outage visualization (gray/red bands in charts)
- Dynamic system tray icon (32x32 gauge rendered via RenderTargetBitmap)
- Enhanced tray context menu (status, analytics, settings, sign out, quit)
- FluentAvalonia settings (alert thresholds, notifications, polling, custom limits, billing)
- Database management (SQLite with clear/export/prune controls)
- Multi-account credential storage (DPAPI encrypted, per-account files)
- Dock/undock to taskbar with bottom-anchored growth
- Always-on-top pin toggle
- Human-readable countdown labels ("resets in 6d 22h")
- Extra usage tracking with 4-tier color ramp
- Windows toast notifications
- Launch at login (Registry Run key)
- Accurate tier detection from Anthropic profile API
