#!/usr/bin/env bash
# Dev launcher for CC-Stats — logs visible, Ctrl+C kills cleanly
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$SCRIPT_DIR/windows/CCStats.Desktop/CCStats.Desktop.csproj"
DOTNET="/c/Program Files/dotnet/dotnet.exe"

if [ ! -f "$DOTNET" ]; then
    DOTNET="dotnet"
fi

if [ ! -f "$PROJECT" ]; then
    echo "Error: Could not find project at $PROJECT" >&2
    exit 1
fi

# Kill any stale instances
taskkill //F //IM CCStats.exe 2>/dev/null || true

# Trap Ctrl+C and kill the child process
cleanup() {
    echo ""
    echo "Stopping CC-Stats..."
    taskkill //F //IM CCStats.exe 2>/dev/null || true
    exit 0
}
trap cleanup INT TERM

echo "Starting CC-Stats... (Ctrl+C to stop)"
echo "Logs will appear below."
echo "---"

# Run with console logging enabled (not exec — we need the trap to work)
"$DOTNET" run --project "$PROJECT" "$@" &
CHILD_PID=$!
wait $CHILD_PID 2>/dev/null
