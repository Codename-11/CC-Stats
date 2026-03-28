# Windows Port Assessment

## Bottom line

`cc-hdrm` is a good candidate for a Windows fork, but this is a real port, not a build-target change.

- The app is currently a macOS-only SwiftUI/AppKit tray utility.
- The product behavior is portable.
- The current UI code is not.
- Preserving the same clean tray-first UX on Windows is feasible if we rebuild the shell and views on a Windows-capable UI stack.

## What the repo is today

The repo is built around:

- `SwiftUI` for the popover, analytics window, settings, onboarding, gauges, cards, and charts
- `AppKit` for tray/menu bar integration, custom windows, custom icon drawing, browser launching, and click handling
- `UserNotifications` for alerts
- `Security` Keychain APIs for token storage
- `ServiceManagement` for launch-at-login
- `Network` (`NWListener`) for the localhost OAuth callback server
- `SQLite3` for local historical storage

The UX intent is very clear in the repo docs: this is supposed to feel like system infrastructure, with the tray icon as the primary interface and the popover as a lightweight secondary surface.

## Portability map

### Mostly portable at the product-logic level

These can be ported with low conceptual risk:

- `cc-hdrm/Models/`
- `cc-hdrm/Services/APIClient.swift`
- `cc-hdrm/Services/TokenRefreshService.swift`
- `cc-hdrm/Services/TokenExpiryChecker.swift`
- `cc-hdrm/Services/HeadroomAnalysisService.swift`
- `cc-hdrm/Services/SlopeCalculationService.swift`
- `cc-hdrm/Services/SubscriptionPatternDetector.swift`
- `cc-hdrm/Services/TierRecommendationService.swift`
- `cc-hdrm/Services/SubscriptionValueCalculator.swift`
- `cc-hdrm/Services/ValueInsightEngine.swift`
- `cc-hdrm/Services/HistoricalDataService.swift`
- `cc-hdrm/Services/DatabaseManager.swift`
- most of `cc-hdrm/State/AppState.swift` logic

These are business rules, API handling, quota math, trend analysis, and storage behavior. They should be rewritten, not mechanically translated, but their behavior can be preserved closely.

### Must be replaced for Windows

These are macOS-specific:

- `cc-hdrm/App/AppDelegate.swift`
- `cc-hdrm/App/cc_hdrmApp.swift`
- `cc-hdrm/Views/AnalyticsWindow.swift`
- `cc-hdrm/Views/AnalyticsPanel.swift`
- `cc-hdrm/Views/OnboardingWindowController.swift`
- `cc-hdrm/Views/GaugeIcon.swift`
- `cc-hdrm/Views/InteractionOverlay.swift`
- `cc-hdrm/Views/UpdateBadgeView.swift`
- `cc-hdrm/Services/OAuthService.swift`
- `cc-hdrm/Services/OAuthCallbackServer.swift`
- `cc-hdrm/Services/OAuthKeychainService.swift`
- `cc-hdrm/Services/KeychainService.swift`
- `cc-hdrm/Services/LaunchAtLoginService.swift`
- `cc-hdrm/Services/NotificationService.swift`
- `cc-hdrm/Services/PatternNotificationService.swift`
- `cc-hdrm/Services/ExtraUsageAlertService.swift`

### Must be redone in a new UI framework

All SwiftUI view files should be treated as design references, not portable implementation:

- `cc-hdrm/Views/PopoverView.swift`
- `cc-hdrm/Views/FiveHourGaugeSection.swift`
- `cc-hdrm/Views/SevenDayGaugeSection.swift`
- `cc-hdrm/Views/HeadroomRingGauge.swift`
- `cc-hdrm/Views/Sparkline.swift`
- `cc-hdrm/Views/AnalyticsView.swift`
- `cc-hdrm/Views/UsageChart.swift`
- `cc-hdrm/Views/SettingsView.swift`
- the rest of `cc-hdrm/Views/`

## Recommendation

### Primary recommendation: `Avalonia 11` + `ReactiveUI` + `.NET 8`

This is the best fit if the goal is:

- Windows first
- tray app behavior
- reactive state updates
- responsive layouts
- custom visuals that stay close to the current app
- a path to future macOS/Linux support from the Windows fork

Why this is the best fit:

- The current app already maps well to MVVM-style state + services.
- `AppState` can become a reactive view model store cleanly.
- Custom gauges, sparklines, progress bars, and chart surfaces are straightforward to redraw.
- A hidden main app + tray icon + lightweight popover-like window is practical.
- SQLite, OAuth browser flow, notifications, and secure credential storage are all standard on .NET.
- It avoids shipping a full Chromium shell just to recreate a tiny tray utility.

### Secondary option: `Tauri 2` + `React`

Use this only if the priority shifts to:

- fastest UI iteration
- web-style responsive layout work
- eventual cross-platform desktop packaging via web tech

I would not choose it first for this app because the product wants to feel like a compact system utility, not a web app in a desktop wrapper.

## Recommended Windows architecture

### App shell

- tray icon with dynamic icon rendering
- borderless lightweight flyout window anchored near the system tray
- separate resizable analytics window
- no taskbar-first workflow

### State

- one reactive `AppState` equivalent
- background polling service writes state updates
- UI binds directly to derived state
- derived fields preserved: displayed window, headroom state, slope, extra usage mode, countdown text

### Platform adapters

- secure storage: Windows Credential Manager or DPAPI-backed store
- notifications: Windows toast notifications
- open browser: default browser launch
- launch at login: registry/task scheduler/startup shortcut
- OAuth callback listener: localhost HTTP listener

### Data layer

- keep SQLite
- preserve schema where practical so analytics logic stays comparable
- port query logic and rollup behavior directly

## What “same UI/UX” means in practice

These should remain effectively the same:

- tray-first information hierarchy
- promoted 7d logic when it is the tighter constraint
- gauge icon semantics
- headroom color scale
- 5h and 7d ring sections
- extra usage card
- sparkline tap-through to analytics
- analytics ranges and insight cards
- low-friction onboarding and silent background polling

These should be adapted to Windows rather than copied literally:

- menu bar popover behavior
- window chrome
- settings presentation
- notification styling
- launch-at-login behavior

The goal should be behavioral parity and visual equivalence, not pixel-for-pixel imitation of AppKit.

## Expected effort

For one experienced developer:

- tray shell + OAuth + polling + secure storage + dynamic tray icon + basic flyout: about 1-2 weeks full-time
- polished popover-equivalent UI with gauges, sparkline, settings, notifications: about 2-4 weeks full-time
- analytics window parity, historical insights, and cleanup: about 1-2 more weeks full-time

Realistically:

- usable Windows MVP: 2-3 weeks
- near-feature-parity polished fork: 4-6 weeks

This is not a weekend port.

## Suggested implementation order

1. Build a new Windows repo instead of trying to mutate this Swift codebase into multi-platform shape.
2. Port models, API client, token refresh, app-state rules, and polling logic first.
3. Implement secure token storage, OAuth browser flow, and localhost callback.
4. Implement tray icon rendering and tray text state transitions.
5. Build the flyout UI: auth state, 5h/7d sections, extra usage, sparkline, footer/settings.
6. Port SQLite history and analytics queries.
7. Rebuild analytics charts and cards.
8. Add notification thresholds, onboarding, and launch-at-login integration.
9. Backfill tests around the portable business rules before polishing UI edge cases.

## Fork strategy

I would not build the Windows port inside this repo as a second target.

Better approach:

- fork this repo on GitHub
- create a new sibling implementation repo or a clearly separated `windows/` app subtree
- use this repo as the product and logic reference
- keep a migration checklist tied to specific source files and behaviors

If you want the cleanest maintenance story, I would create:

- `cc-hdrm-windows`

and treat this repo as the reference implementation rather than the direct build source.

## Current local status

- local clone created at `C:\Users\Bailey\cc-hdrm`
- local branch created: `windows-port-spike`

GitHub CLI is available and authenticated on this machine, so the repo can be forked directly when you want to make that remote step.
