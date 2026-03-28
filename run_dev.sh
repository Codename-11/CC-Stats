#!/usr/bin/env bash
# Dev launcher for CC Stats - runs directly in bash so Ctrl+C works
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

# Kill any stale instances so we get a fresh build
taskkill //F //IM CCStats.Desktop.exe 2>/dev/null || true

echo "Starting CC Stats... (Ctrl+C to stop)"
exec "$DOTNET" run --project "$PROJECT" "$@"
