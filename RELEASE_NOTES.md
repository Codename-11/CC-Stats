UX polish and multi-account fix -- dark toast theme, chart legend improvements, and PromoClock cleanup.

## What's Changed

### Bug Fixes
- **Multi-account OAuth** -- adding a second account no longer overwrites the first; correctly detects add-account vs re-auth flow
- **Toast notifications** -- dark theme backgrounds with status-colored borders (was light colors jarring on dark app)
- **Data source badge** -- renamed misleading "Live" to "Local" for cache source; CredentialsOnly gets unique purple color; added hover tooltip
- **Chart legend** -- wraps at narrow widths instead of truncating; syncs visibility with series toggles; clearer labels ("5h reset" / "7d reset" / "Gap")

### Improvements
- **PromoClock simplified** -- settings now shows a single "Peak Hours Indicator" toggle; removed API Key and Team ID fields
- **Badge visibility** -- increased background opacity for better contrast on dark theme
- **Error toast icon** -- changed from ambiguous "!" to cross mark

## Install

Download `CCStats-v0.3.1-win-x64.exe` from the assets below and run it. Self-contained -- no .NET runtime needed.

On first launch, CC-Stats creates a Start Menu shortcut and registers in Add/Remove Programs. To uninstall, use Add/Remove Programs or run `CCStats.exe --uninstall`.

**Requirements:** Windows 10 (build 17763) or later.
