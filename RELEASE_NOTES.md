Major reliability and polish release — exponential backoff, data source tracking, 193 tests, self-registering install, and branch cleanup.

## What's Changed

### Features
- **Self-registering install** — first launch creates Start Menu shortcut and Add/Remove Programs entry; supports clean uninstall via `--uninstall` flag
- **Data source tracking** — color-coded badge (API/Cached/Live) with tooltip showing sync time and cache age
- **Polling feedback** — spinner during checks, freshness timer, click-to-refresh with status toast
- **Pill-shaped toast notifications** — success (green), error (red), info (blue) with slide-down animation
- **Reset event markers** — 5h resets (orange) and 7d resets (blue) shown on analytics charts
- **Declining trend detection** — new arrow when usage is dropping (green, good news)
- **Local build scripts** — `.\build_local.ps1` and `./build_local.sh` produce CI-matching exe

### Improvements
- **Rate limit resilience** — exponential backoff with local cache fallback; data keeps flowing during 429s
- **Stable charts** — no more flash/reset on poll refresh (LiveCharts2 objects reused)
- **Smarter predictions** — requires 3+ samples over 2+ minutes before reporting trends
- **Security hardening** — atomic file writes, DPAPI buffer clearing, thread-safe preferences, OAuth replay prevention
- **Re-auth account matching** — reconnects to existing account instead of creating duplicates
- **193 xUnit tests** — slopes, formatting, tiers, app state, account view model
- **CI runs tests** — every push now builds and tests

### Bug Fixes
- **Cache badge** — correctly shows "Cached" during rate limiting (was stuck on "Live")
- **Popout analytics** — blank render and resize issues fixed
- **7d chart zigzag** — reference line rendered as horizontal band in bar mode
- **Console encoding** — em dash replaced with -- for Windows codepage compatibility
- **Build script** — param block moved before variable assignments

### Other Changes
- Default branch renamed from `master` to `main`
- Adaptive polling interval: 30s when Claude active (was 15s, caused rate limiting)
- DB schema migrations with automatic versioning
- README updated with screenshots and reorganized feature docs

## Install

Download `CCStats-v0.3.0-win-x64.exe` from the assets below and run it. Self-contained -- no .NET runtime needed.

On first launch, CC-Stats creates a Start Menu shortcut and registers in Add/Remove Programs. To uninstall, use Add/Remove Programs or run `CCStats.exe --uninstall`.

**Requirements:** Windows 10 (build 17763) or later.
