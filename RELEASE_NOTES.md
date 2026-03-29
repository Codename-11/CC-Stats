Fixes for account naming and version display after self-update.

## What's Changed

### Bug Fixes
- **Account naming** — The naming prompt on sign-in now correctly saves the user's chosen name. Previously, `SaveAndRefreshAccount` was overriding the display name with the subscription tier.
- **Version display** — Settings now shows the correct version after self-update by reading `FileVersionInfo` from the exe on disk, fixing cases where `GetEntryAssembly` returned stale or null version info in single-file publish scenarios.

## Install

Download `CCStats-v0.2.1-win-x64.exe` from the assets below and run it. Self-contained — no .NET runtime needed.

**Requirements:** Windows 10 (build 17763) or later.
