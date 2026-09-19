#!/usr/bin/env bash
# Build Unifont font AssetBundles with Unity batchmode and validate them in-editor.
# Usage: tools/build-bundles.sh [--full-hanzi]
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORKSPACE="$(dirname "$REPO_ROOT")"            # workspace holding unity-project/, fonts/, logs/
PROJ="$WORKSPACE/unity-project"
UNITY_BIN="${UNITY_BIN:-/Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app/Contents/MacOS/Unity}"
LOGS="$WORKSPACE/logs"
OUT_OSX="$WORKSPACE/bundles-osx"
mkdir -p "$LOGS" "$OUT_OSX"

FULL_HANZI=0
[ "${1:-}" = "--full-hanzi" ] && FULL_HANZI=1

# Unity requires the Editor scripts inside Assets/Editor; canonical copies live here.
mkdir -p "$PROJ/Assets/Editor"
cp "$REPO_ROOT/tools/unity-editor/"*.cs "$PROJ/Assets/Editor/"

# TMP shaders ship only inside "TMP Essential Resources.unitypackage" (a tar.gz), and
# AssetDatabase.ImportPackage is unreliable in batchmode. Extract it into Assets directly;
# Unity imports everything on startup. Skipped once present.
PKGDIR=$(ls -d "$PROJ/Library/PackageCache"/com.unity.textmeshpro@* 2>/dev/null | head -1)
if [ -n "${PKGDIR:-}" ] && [ -d "$PROJ/Assets" ] && [ ! -d "$PROJ/Assets/TextMesh Resources" ]; then
  echo "== extracting TMP Essential Resources into Assets =="
  TMPD=$(mktemp -d)
  tar xzf "$PKGDIR/Package Resources/TMP Essential Resources.unitypackage" -C "$TMPD"
  find "$TMPD" -name pathname | while read -r p; do
    dest_rel="$(cat "$p")"
    asset_file="$(dirname "$p")/asset"
    if [ -f "$asset_file" ]; then
      mkdir -p "$PROJ/$(dirname "$dest_rel")"
      cp "$asset_file" "$PROJ/$dest_rel"
    fi
  done
  rm -rf "$TMPD"
fi

echo "== stage 1: build bundles (StandaloneWindows64 + StandaloneOSX) =="
LCFP_REPO_ROOT="$REPO_ROOT" LCFP_OSX_OUT="$OUT_OSX" \
  "$UNITY_BIN" -batchmode -nographics -quit \
  -projectPath "$PROJ" \
  -executeMethod BuildUnifontBundles.BuildAll \
  -logFile "$LOGS/unity-build.log"
echo "build exit: $?"

echo "== stage 2: validate macOS bundles in-editor =="
REPORT="$WORKSPACE/logs/validation-report.txt"
LCFP_OSX_OUT="$OUT_OSX" LCFP_VALIDATION_REPORT="$REPORT" \
  LCFP_HANZI_FILE="$([ "$FULL_HANZI" = 1 ] && echo "$WORKSPACE/data/tongyong-guifan-hanzibiao.txt" || echo "")" \
  "$UNITY_BIN" -batchmode -nographics -quit \
  -projectPath "$PROJ" \
  -executeMethod ValidateUnifontBundles.Run \
  -logFile "$LOGS/unity-validate.log"
echo "validate exit: $?"

echo "== report =="
cat "$REPORT"
