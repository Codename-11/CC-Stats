# Release Notes Format

Release notes are written to `RELEASE_NOTES.md` in the project root and committed as part of the release. GitHub Actions uses this file as the GitHub Release body (if present, overrides auto-generated notes).

## Template

```markdown
Brief 1-2 sentence summary of this release's focus.

## What's Changed

### Features
- **Feature Name** — Brief description of the new functionality

### Improvements
- **Improvement Name** — Brief description of what was enhanced

### Bug Fixes
- **Fix Name** — Brief description of what was fixed

### Other Changes
- Maintenance, refactoring, dependency updates
```

## Guidelines

- Keep descriptions user-focused and concise (1 line each)
- Use **bold** for feature/fix names
- Omit sections with no items
- **Be inclusive** — prioritize documenting all user-facing changes:
  - Gauge display changes
  - Chart/analytics improvements
  - Notification behavior changes
  - Settings additions
  - Account management updates
  - Polling/data collection changes
  - Self-update behavior
- Internal refactoring only mentioned if it affects user experience

## Install Section (always include)

```markdown
## Install

Download `CCStats-vX.Y.Z-win-x64.exe` from the assets below and run it. Self-contained — no .NET runtime needed.

**Requirements:** Windows 10 (build 17763) or later.
```
