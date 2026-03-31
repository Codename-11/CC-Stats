Peak hour chart overlays, OAuth reliability overhaul, debug log viewer, and 9 audit-driven fixes.

## What's Changed

### Features
- **Peak hour chart overlays** -- subtle amber bands on sparkline and analytics charts during weekday 9AM-5PM peak windows. Gated by the "Peak Hours Indicator" toggle in Settings.
- **Chart tooltip peak context** -- hovering data points shows "· Peak" or "· Off-Peak" when overlays are enabled
- **Debug log viewer** -- collapsible panel in Settings with monospace text, copy button, and 500-entry ring buffer. Helps diagnose auth and polling issues.
- **Re-auth from error banner** -- when session expires, tap the footer text to sign in directly (no need to navigate to Settings)
- **Re-auth button in Settings** -- amber "Re-auth" button next to the active account
- **DPAPI migration warning** -- toast notification when credentials can't be decrypted after switching machines

### Bug Fixes
- **OAuth token exchange fixed** -- re-added required `state` field to token exchange body and removed `anthropic-beta` header from token endpoint (was causing "Invalid request format" BadRequest)
- **Token refresh fixed** -- same header fix applied to refresh endpoint
- **Off-Peak badge color** -- was showing orange (warning) instead of green (success)
- **Re-auth no longer creates duplicate accounts** -- matches active account by ID instead of fragile tier string
- **Polling no longer freezes after re-auth** -- `_authFailed` flag properly cleared
- **Reset markers** -- changed to gold/yellow to distinguish from amber peak overlays
- **9 additional audit fixes** -- OAuth state timing, NeedsReauth detection, AccountId generation, ExpiresIn validation, DPAPI warnings, cross-thread safety, duplicate logging

## Install

Download `CCStats-v0.4.0-win-x64.exe` from the assets below and run it. Self-contained -- no .NET runtime needed.

**Requirements:** Windows 10 (build 17763) or later.
