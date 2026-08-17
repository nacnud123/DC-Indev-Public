#!/usr/bin/env bash
# Packages this project into a zip for students: everything needed to open,
# build, and edit the project, with build artifacts/junk stripped out.
#
# Usage:
#   ./build.sh                     Build-check, then write dist/VoxelEngine-Student-<date>.zip
#   ./build.sh out.zip             Same, but write to a specific path
#   ./build.sh --skip-build        Skip the "does it compile" check (faster)
set -euo pipefail

PROJECT_NAME="VoxelEngine"
CALLER_PWD="$PWD"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

SKIP_BUILD=0
OUT_ZIP=""
for arg in "$@"; do
    if [[ "$arg" == "--skip-build" ]]; then
        SKIP_BUILD=1
    else
        OUT_ZIP="$arg"
    fi
done

if [[ -z "$OUT_ZIP" ]]; then
    OUT_ZIP="${SCRIPT_DIR}/dist/${PROJECT_NAME}-Student-$(date +%Y-%m-%d).zip"
elif [[ "$OUT_ZIP" != /* ]]; then
    # A relative path is resolved against the directory the script was invoked from
    # (not the script's own directory, and not the temp staging dir used below).
    OUT_ZIP="${CALLER_PWD}/${OUT_ZIP}"
fi
mkdir -p "$(dirname "$OUT_ZIP")"

# Folders/files that are build output, IDE-local state, or otherwise not part
# of "the project" a student needs to open/build/edit. Notably `building/` and
# `Building/` are stale publish dumps (~245MB each, including old release
# zips and an unrelated PDF) that have no business in a source handout.
# All directory excludes are anchored with a leading slash (root of the transfer
# only) - without it, rsync matches the name at ANY depth, which previously ate
# the real Terrain/Blocks/Building/ source folder along with the root-level junk.
EXCLUDES=(
    --exclude='/bin/'
    --exclude='/obj/'
    --exclude='/building/'
    --exclude='/Building/'
    --exclude='/Block Art/'
    --exclude='/dist/'
    --exclude='/.idea/'
    --exclude='/.vs/'
    --exclude='/.git/'
    --exclude='/.claude/'
    --exclude='*.user'
    --exclude='.DS_Store'
    --exclude='Thumbs.db'
)

if [[ "$SKIP_BUILD" -eq 0 ]]; then
    # The solution, not the client .csproj: that only covers the client and VoxelEngine.Common,
    # so a handout could ship with a dedicated server that doesn't compile and nobody would know
    # until a student tried to run it.
    echo "==> Verifying the client and server both build cleanly (use --skip-build to skip this)..."
    dotnet build "${PROJECT_NAME}.sln" -c Release --nologo
fi

echo "==> Staging a clean copy of the project..."
STAGE_ROOT="$(mktemp -d)"
STAGE_DIR="${STAGE_ROOT}/${PROJECT_NAME}"
mkdir -p "$STAGE_DIR"
rsync -a "${EXCLUDES[@]}" ./ "$STAGE_DIR"/

echo "==> Zipping..."
rm -f "$OUT_ZIP"
(cd "$STAGE_ROOT" && zip -r -q "$OUT_ZIP" "$PROJECT_NAME")

rm -rf "$STAGE_ROOT"

echo "==> Done: $OUT_ZIP ($(du -h "$OUT_ZIP" | cut -f1))"
