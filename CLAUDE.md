# CC-Stats (Windows)

Windows system tray app for monitoring Claude API headroom. Port of [rajish/cc-hdrm](https://github.com/rajish/cc-hdrm) (macOS).

## Architecture

Avalonia 11 + ReactiveUI + .NET 8 desktop app. Two projects in `windows/`:

| Project | Purpose |
|---------|---------|
| `CCStats.Core` | Models, state, services (platform-agnostic) |
| `CCStats.Desktop` | Avalonia UI, controls, tray icon, views |

### Key Patterns
- **MVVM** via ReactiveUI — `MainWindowViewModel` is the primary VM
- **AppState** is an immutable record — new state = `state with { ... }`
- **App.axaml.cs** is the composition root — creates all 14 services, wires events
- **Dual-mode VM** — preview states (F5 cycling) vs real services (`ConnectServices()`)
- **SizeToContent** flyout — bottom-anchored via `BoundsProperty` subscription

### Services (CCStats.Core/Services/)
`PreferencesManager` `SecureStorageService` `OAuthService` `TokenRefreshService` `APIClient` `PollingEngine` `SlopeCalculationService` `DatabaseManager` `HistoricalDataService` `NotificationService` `UpdateCheckService` `SessionDetectionService` `LocalCacheService`

### Custom Controls (CCStats.Desktop/Controls/)
`HeadroomRingGauge` `CountdownLabel` `ExtraUsageBar` `GaugeIcon` `HeadroomColors` `SparklineControl` `TimeRangeSelector`

## Build & Dev

```bash
./run_dev.sh                    # Bash (kills stale process, Ctrl+C works)
.\run_dev.ps1                   # PowerShell
dotnet build windows/CCStats.Windows.sln
dotnet run --project windows/CCStats.Desktop/CCStats.Desktop.csproj
```

F5 in-app cycles preview states. The app auto-kills stale processes on dev restart.

## Conventions

- **Commits**: [Conventional Commits](https://www.conventionalcommits.org/) — `feat`, `fix`, `docs`, `refactor`, `chore`
- **Branches**: `feature/<name>`, `fix/<name>`, `docs/<name>`
- **Versioning**: [SemVer](https://semver.org/)

## System Reference

ClawPort API (no auth on LAN): http://172.16.24.250:3100
- Library: GET /api/library, POST /api/library
- Project: GET/PATCH/DELETE /api/library/{slug}
- Full agent API reference: curl http://172.16.24.250:3100/api/settings/agent-ref
