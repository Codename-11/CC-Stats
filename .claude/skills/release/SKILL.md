---
name: release
description: Create a new release with version bump, changelog, release notes, commit, and tag
argument-hint: <patch|minor|major|x.y.z>
disable-model-invocation: true
allowed-tools: Bash, Read, Edit, Write, Grep, Glob
---

# Release Procedure

Create a release for CC-Stats (Windows). The argument specifies the version bump type or an explicit version number.

## Dynamic Context

Current version (from csproj):
!`grep '<Version>' windows/CCStats.Desktop/CCStats.Desktop.csproj | sed 's/.*<Version>//' | sed 's/<.*//'`

Last 5 tags:
!`git tag --sort=-v:refname | head -5`

Recent commits (last 30):
!`git log --oneline --no-decorate -30`

Current branch:
!`git branch --show-current`

Working tree clean?
!`git status --porcelain`

## Instructions

**Argument:** `$ARGUMENTS`

### Pre-flight Checks

1. **Verify clean working tree** — if there are uncommitted changes, STOP and warn the user. Releases must be made from a clean state.
2. **Verify on master branch** — warn if not on `master`. Offer to merge `windows-port-spike` first.
3. **Verify build succeeds** — run `dotnet build windows/CCStats.Windows.sln --configuration Release` and confirm it completes without errors.

### Step 1: Determine Version

Parse the argument:
- `patch` — bump PATCH (e.g., `0.1.4` → `0.1.5`)
- `minor` — bump MINOR, reset PATCH (e.g., `0.1.5` → `0.2.0`)
- `major` — bump MAJOR, reset MINOR+PATCH (e.g., `0.2.0` → `1.0.0`)
- `x.y.z` (explicit) — use as-is, validate it's greater than current
- No argument — analyze commits since last tag and recommend:
  - Any `feat!` or `BREAKING CHANGE` → MAJOR
  - Any `feat` → MINOR
  - Only `fix`/`docs`/`chore`/`refactor` → PATCH
  - Ask the user to confirm before proceeding

### Step 2: Update Version

Update the version in `windows/CCStats.Desktop/CCStats.Desktop.csproj`:
- `<Version>X.Y.Z</Version>`
- `<AssemblyVersion>X.Y.Z.0</AssemblyVersion>`
- `<FileVersion>X.Y.Z.0</FileVersion>`

Note: The release workflow also injects `-p:Version` from the git tag, but the csproj version should match for local/dev builds.

### Step 3: Rebuild

```bash
dotnet build windows/CCStats.Windows.sln --configuration Release
```

Verify no errors.

### Step 4: Update CHANGELOG.md

Add a new `## [X.Y.Z] - YYYY-MM-DD` section in `CHANGELOG.md` (keepachangelog format). Generate entries from commits since last tag using Added/Changed/Fixed sections.

### Step 5: Generate Release Notes

Write `RELEASE_NOTES.md` following the format in [release-notes.md](release-notes.md).

Analyze ALL commits since the last tag. Group by category, be thorough about user-facing changes.

**Show the draft to the user and ask for approval before continuing.**

### Step 6: Commit

Stage:
- `windows/CCStats.Desktop/CCStats.Desktop.csproj`
- `CHANGELOG.md`
- `RELEASE_NOTES.md`
- Any other changed files

Commit with message: `chore(release): bump version to X.Y.Z`

### Step 7: Tag and Push

```bash
git tag vX.Y.Z
git push origin master --tags
```

Tell the user:
> Release committed and tagged as `vX.Y.Z`. The GitHub Actions release workflow will build the exe and create the GitHub Release automatically.
>
> Check: https://github.com/Codename-11/CC-Stats/actions
