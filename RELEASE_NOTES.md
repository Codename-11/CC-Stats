PromoClock integration, credential recovery, and reliability improvements.

## What's Changed

### Features
- **PromoClock Integration** — Connect to [promoclock.co](https://promoclock.co/en) to show peak/off-peak status in the gauge header. Configure with API key and Team ID in Settings.
- **Test Notification** — Send a test Windows toast notification from Settings → Notifications to verify alerts work.
- **`/release` Skill** — Automated release workflow for Claude Code: version bump, changelog, release notes, tag, and push.
- **Periodic Update Check** — Now checks for updates every 6 hours instead of only at startup.

### Improvements
- **Default poll interval** changed from 60s to 120s, reducing idle API calls by half. Adaptive polling still speeds to 15s when Claude Code is active.
- **Chart animations** — Series objects are now stable across refreshes with smooth 300ms transitions instead of full redraws.
- **Chart legends** — Full key showing 5h (green), 7d (blue dashed), Reset (orange), Gap (gray) in both inline and popout charts.
- **Freshness text** now shows "Xs ago · tap to refresh" for clearer click-to-refresh UX.

### Bug Fixes
- **Credential recovery** — After self-update, if main credentials.dat is missing, automatically recovers from per-account files.
- **Polling stall** — SessionDetectionService crash (Win32Exception on process access) no longer kills the poll loop.
- **Sparkline instant display** — Seeds from Claude Code's local statusline cache so chart appears immediately on startup.
- **Version display** — Fixed version stuck at 0.1.0 after self-update; release workflow now injects version from git tag.
- **Update badge** — Replaced truncated "v0" text with clean ↑ icon; full version shown in tooltip.
- **Settings gear** — Increased click target from 20px to 28px to fix missed clicks.
- **Self-update confirmation** — Now requires two clicks (check → confirm) instead of downloading immediately.

## Install

Download `CCStats-v0.2.0-win-x64.exe` from the assets below and run it. Self-contained — no .NET runtime needed.

**Requirements:** Windows 10 (build 17763) or later.
