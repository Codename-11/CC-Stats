# CC-Stats (Windows) — Roadmap & TODO

## v0.1.0 (Current)
- [x] Ring gauges (5h/7d) with animated fill and slope indicators
- [x] OAuth sign-in (PKCE, Anthropic endpoints)
- [x] Real-time polling with configurable interval
- [x] LiveCharts2 sparkline with inline expansion and popout
- [x] Analytics window (step-area/bar charts, time ranges, insights)
- [x] Headroom breakdown bar, pattern detection, tier recommendations
- [x] Gap/outage visualization (gray/red bands)
- [x] System tray icon with dynamic gauge rendering
- [x] FluentAvalonia settings (thresholds, notifications, polling, limits)
- [x] Multi-account support (DPAPI encrypted, per-account files)
- [x] SQLite database (polls, rollups, reset events, outages)
- [x] Windows toast notifications
- [x] Launch at login (Registry)
- [x] Dock/undock, always-on-top toggle
- [x] Account naming on sign-in, friendly name in footer

## v0.1.1 (In Progress)
- [ ] Projected exhaustion line on sparkline (dashed forecast)
- [ ] Session-aware adaptive polling (fast when Claude active, slow when idle)
- [ ] Reset event markers in charts (vertical lines at reset boundaries)
- [ ] Threshold notification wiring (toast on warning/critical cross)
- [ ] Usage budget calculator ("~3.2h of active coding left")
- [ ] Peak hours heatmap (7d × 24h usage intensity grid)
- [ ] Quick-copy status to clipboard (right-click gauge)
- [ ] Sound alert option on threshold cross (opt-in)

## v0.2.0 (Planned)
- [ ] Agents-Observer integration (correlate session data with headroom usage)
- [ ] Export database to CSV (wired implementation)
- [ ] Prune database by date range (wired implementation)
- [ ] Full account switching with per-account database isolation
- [ ] Onboarding wizard (first-run setup flow)
- [ ] Custom themes / accent color picker

## v0.3.0 (Future)
- [ ] PromoClock integration (https://promoclock.co/en) — optional setting to sync usage data with PromoClock for team-wide visibility and scheduling around reset windows
- [ ] macOS/Linux support via Avalonia cross-platform
- [ ] WebSocket transport for browser-based dashboard
- [ ] Webhook notifications (Slack, Discord, email)
- [ ] API server mode (expose headroom data as local REST API)
- [ ] Plugin system for custom visualizations

## Integration Ideas
- **PromoClock** (https://promoclock.co/en): Sync reset windows and usage data for team coordination. Could show "team headroom" view and help schedule heavy sessions across team members' reset cycles.
- **Agents-Observer**: Read local Claude Code session data to show per-session usage impact and predict headroom burn rate from session characteristics.
- **Claude Code hooks**: Auto-pause/warn Claude Code when headroom is critically low.
