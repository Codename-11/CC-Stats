# Changelog

All notable changes to CC-Stats (Windows) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-03-29

### Added
- PromoClock integration (https://promoclock.co/en) — peak/off-peak badge in gauge header, API key + Team ID settings, 5-minute polling
- Test notification button in Settings → Notifications for verifying toast alerts work
- `/release` skill for Claude Code — automated version bump, changelog, release notes, tag, and push
- Credential recovery after self-update — falls back to per-account files if main credentials.dat is missing
- Periodic update check every 6 hours (was startup-only)

### Changed
- Default poll interval from 60s to 120s (halves idle API calls; adaptive polling still speeds to 15s when Claude active)

### Fixed
- Polling stall caused by SessionDetectionService crash (Win32Exception on process access) — wrapped with fallback
- Sparkline "Building history..." delay — seeds 2 data points from local cache on startup
- Chart flash on data refresh — series objects now stable with 300ms animation
- Version stuck at 0.1.0 after self-update — release workflow injects version from git tag
- Update badge showing truncated "v0" — replaced with clean ↑ icon
- Settings gear click sometimes unresponsive — increased hit area from 20px to 28px
- Self-update ran without confirmation — now requires two clicks

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
