#!/usr/bin/env bash
# Build CC-Stats locally as a self-contained single-file exe (mirrors CI).
# Usage:
#   ./build_local.sh                  # Build with csproj version
#   ./build_local.sh 0.3.0            # Build as v0.3.0
#   ./build_local.sh 0.3.0 --run      # Build and launch
#   ./build_local.sh --clean           # Clean + build
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SLN="$SCRIPT_DIR/windows/CCStats.Windows.sln"
CSPROJ="$SCRIPT_DIR/windows/CCStats.Desktop/CCStats.Desktop.csproj"
OUT_DIR="$SCRIPT_DIR/publish"
EXE="$OUT_DIR/CCStats.exe"
DOTNET="/c/Program Files/dotnet/dotnet.exe"
[ ! -f "$DOTNET" ] && DOTNET="dotnet"

VERSION=""
RUN=false
CLEAN=false

for arg in "$@"; do
    case "$arg" in
        --run)  RUN=true ;;
        --clean) CLEAN=true ;;
        *)      VERSION="$arg" ;;
    esac
done

# Read version from csproj if not specified
if [ -z "$VERSION" ]; then
    VERSION=$(grep -oP '<Version>\K[^<]+' "$CSPROJ" | head -1)
    echo "Using csproj version: $VERSION"
fi

echo ""
echo "  CC-Stats Local Build"
echo "  Version:  v$VERSION"
echo "  Output:   $OUT_DIR"
echo ""

# Kill stale instances
taskkill //F //IM CCStats.exe 2>/dev/null || true

if $CLEAN; then
    echo "[1/3] Cleaning..."
    "$DOTNET" clean "$SLN" --configuration Release --verbosity quiet 2>/dev/null || true
    rm -rf "$OUT_DIR"
fi

echo "[1/3] Restoring..."
"$DOTNET" restore "$SLN" --verbosity quiet

echo "[2/3] Publishing v$VERSION (self-contained, single-file)..."
"$DOTNET" publish "$CSPROJ" \
    --configuration Release \
    --runtime win-x64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:Version="$VERSION" \
    -p:AssemblyVersion="$VERSION.0" \
    -p:FileVersion="$VERSION.0" \
    --output "$OUT_DIR" \
    --verbosity quiet

SIZE=$(du -h "$EXE" | cut -f1)
echo ""
echo "[3/3] Build complete!"
echo "  Exe:      $EXE"
echo "  Size:     $SIZE"
echo ""

if $RUN; then
    echo "Launching..."
    "$EXE" &
fi
