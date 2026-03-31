# Changelog

All notable changes to CC-Stats (Windows) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.0] - 2026-03-30

### Added
- **Peak hour chart overlays** -- subtle amber background bands on sparkline and analytics charts during weekday 9AM-5PM windows (toggle in Settings > Peak Hours Indicator)
- **Chart tooltip peak context** -- hovering data points shows "Peak" or "Off-Peak" when overlays are enabled
- **Debug log viewer** -- collapsible log panel in Settings with copy-to-clipboard, monospace selectable text (500-entry ring buffer)
- **Re-auth from error banner** -- "Session expired · tap to sign in" footer text triggers OAuth directly
- **Re-auth button in Settings** -- amber "Re-auth" button next to active account
- **DPAPI migration warning** -- toast notification when credentials can't be decrypted (machine/user change)
- **Account removal safety tooltip** -- "Remove credentials only (usage history is preserved)"

### Fixed
- **OAuth token exchange BadRequest** -- re-added `state` to token exchange body and removed `anthropic-beta` header from token endpoint (matching upstream)
- **Token refresh failing** -- same `anthropic-beta` header removal fix applied to refresh endpoint
- **Off-Peak badge showing orange** -- `Contains("Peak")` matched "Off-Peak"; fixed to exact `== "Peak"` check
- **Reset vs peak overlay colors** -- reset markers changed to gold/yellow to distinguish from amber peak overlays
- **Re-auth creates duplicate account** -- re-auth now matches active account by ID, not fragile tier string
- **Polling frozen after re-auth** -- `_authFailed` flag now always cleared when auth completes
- **NeedsReauth fragile string matching** -- now based on `ConnectionStatus.TokenExpired`
- **AccountId null during naming** -- generated immediately on new account auth
- **OAuth state cleared too early** -- now cleared only after successful token exchange (retry-safe)
- **ExpiresIn=0 creates expired token** -- defaults to 24h if missing/zero
- **PollFailed during naming flow** -- fully skipped to prevent state interference
- **Duplicate log lines** -- single stderr output path (was writing via both Debug.WriteLine and Console.Error)
- **LogAdded cross-thread crash** -- marshaled to UI thread via SynchronizationContext

## [0.3.4] - 2026-03-30

### Fixed
- **Gauge shows last known data when API is down** -- PollFailed handler now preserves existing gauge values instead of resetting to 0% when the engine has no fresh data
- **Startup restores gauge values from DB** -- when no local cache exists, loads the most recent poll's 5h/7d values from SQLite so gauges show real data immediately
- **OAuth profile fetch timeout** -- profile API call during sign-in now uses 5s timeout instead of unlimited, preventing long hangs when API is unreachable
- **Auth failure shows toast** -- OAuth failures now show error toast ("Sign-in failed: ...") instead of silently resetting to sign-in screen
- **Auth errors preserve account state** -- if sign-in fails but stored accounts exist, app stays authenticated instead of dropping to sign-in
- **Account naming flow protected** -- PollFailed no longer interferes with the account naming prompt during new account creation

## [0.3.3] - 2026-03-30

### Fixed
- **Sparkline history from DB** — chart now loads historical data from SQLite on startup, showing real usage history even when polls are failing (expired token, rate limited). Previously only seeded 2 identical local cache values = flat line.

## [0.3.2] - 2026-03-30

### Fixed
- **Auth state persistence** — app no longer shows sign-in screen when stored accounts exist but token has expired; shows error in status instead of losing user context

## [0.3.1] - 2026-03-29

### Fixed
- **Multi-account OAuth** — adding a second account no longer overwrites the first; detects add-account vs re-auth by checking if polling is already running
- **Toast dark theme** — dark backgrounds with status-colored borders instead of jarring light colors
- **Data source badge** — renamed misleading "Live" to "Local", CredentialsOnly gets unique purple color, added hover tooltip, increased badge opacity
- **Chart legend** — wraps at narrow widths instead of truncating, syncs visibility with series toggles, clearer labels ("5h reset" / "7d reset" / "Gap")

### Changed
- **PromoClock** — simplified to "Peak Hours Indicator" toggle-only; removed API Key and Team ID fields, uses local time-based peak detection

## [0.3.0] - 2026-03-29

### Added
- **Self-registering install** — first launch creates Start Menu shortcut and Add/Remove Programs entry; `--uninstall` flag for clean removal
- **193 xUnit tests** covering slope calculation, date formatting, rate limit tiers, app state, and account management
- **Data source tracking** — UI badge shows API/Cached/Live with color-coded pill and detailed tooltip
- **Polling spinner** with freshness timer and click-to-refresh feedback
- **Pill-shaped toast notifications** with success/error/info types and slide-down animation
- **Reset event markers** on charts — 5h resets (orange), 7d resets (blue)
- **Declining trend level** (↘) for slope calculation when usage is dropping
- **DB schema migrations** with automatic versioning and account_id tracking
- **Local build scripts** (`build_local.ps1`, `build_local.sh`) matching CI output
- **Screenshots** in README with reorganized feature documentation
- **CI test step** — workflow now runs all 193 tests on every push

### Changed
- Branch renamed from `master` to `main`
- Adaptive polling reduced from 15s to 30s when Claude Code is active (prevents rate limiting)
- Slope calculation requires 3+ samples over 2+ minutes (prevents noisy predictions)
- Chart series/axis objects are now stable (eliminates chart flash on poll refresh)
- Analytics popout window: borderless with custom resize handles and centered drag bar

### Fixed
- Re-auth no longer creates duplicate accounts — matches existing accounts by tier
- Rate limiting handled with exponential backoff and local cache fallback
- Cache fallback correctly shows "Cached" badge (was showing "Live")
- Popout analytics window blank render — fixed SystemDecorations and resize
- 7-day reference line zigzag in bar mode — rendered as horizontal band
- OAuth port binding retry with state replay prevention
- Thread-safe preferences with atomic file writes
- Secure storage: DPAPI buffer clearing, typed exception handling
- Console encoding (em dash → -- for Windows codepage compatibility)
- `build_local.ps1` param block ordering (must precede assignments)

## [0.2.1] - 2026-03-29

### Fixed
- Account naming prompt no longer overridden by subscription tier — user's chosen name is now saved correctly
- Version display in settings now reads from exe file version info as fallback, fixing stale version after self-update

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
