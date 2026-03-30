Fixes for data display when API is unreachable and OAuth sign-in resilience.

## What's Changed

### Bug Fixes
- **Gauge preserves last known data** -- when polls fail (API down, token expired), gauges now show the last known 5h/7d values instead of resetting to 0%
- **Startup loads gauge values from DB** -- if no local cache exists, restores gauge data from the most recent SQLite poll so the app starts with real data
- **OAuth sign-in timeout** -- profile fetch during sign-in uses a 5-second timeout, preventing the app from hanging when the API is unreachable
- **Auth failure feedback** -- sign-in errors now show a toast notification instead of silently resetting to the sign-in screen
- **Auth errors preserve accounts** -- if sign-in fails but stored accounts exist, the app stays authenticated instead of losing account context
- **Account naming protected** -- poll failures no longer interfere with the new account naming prompt

## Install

Download `CCStats-v0.3.4-win-x64.exe` from the assets below and run it. Self-contained -- no .NET runtime needed.

**Requirements:** Windows 10 (build 17763) or later.
