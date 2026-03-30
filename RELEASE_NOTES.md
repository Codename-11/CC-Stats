Fix for flat sparkline chart after restart or token expiry.

## What's Changed

### Bug Fixes
- **Sparkline history from DB** — the chart now loads historical usage data from SQLite on startup, independently of the poll cycle. Previously, the sparkline only showed 2 identical values seeded from the local cache (flat line) because DB history was only loaded in the PollCompleted handler — which never fires when polls fail due to expired tokens or rate limiting.

## Install

Download `CCStats-v0.3.3-win-x64.exe` from the assets below and run it. Self-contained — no .NET runtime needed.

**Requirements:** Windows 10 (build 17763) or later.
