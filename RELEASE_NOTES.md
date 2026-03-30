Fix for sign-in screen appearing when stored accounts exist but token has expired.

## What's Changed

### Bug Fixes
- **Auth state persistence** -- the app no longer shows the sign-in screen when you have a stored account but the session token has expired. Instead, the error is shown in the status line and the account context is preserved. Previously, an expired token after self-update or idle period would drop you to the sign-in screen even though your account data was intact.

## Install

Download `CCStats-v0.3.2-win-x64.exe` from the assets below and run it. Self-contained -- no .NET runtime needed.

On first launch, CC-Stats creates a Start Menu shortcut and registers in Add/Remove Programs. To uninstall, use Add/Remove Programs or run `CCStats.exe --uninstall`.

**Requirements:** Windows 10 (build 17763) or later.
