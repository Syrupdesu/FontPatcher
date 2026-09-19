#!/usr/bin/env bash
# Build Unifont font AssetBundles with Unity batchmode and validate them in-editor.
# Usage: tools/build-bundles.sh [--full-hanzi]
#
# Self-contained: fonts and the hanzi table come from this repository; the Unity
# project and all scratch output live under .workspace/ (gitignored).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORKSPACE="$REPO_ROOT/.workspace"               # Unity project + scratch (gitignored)
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

# Sync the font files from the repository into the Unity project (per-variant copies;
# each bundle embeds its own font data). Skipped when already present.
FONT_VER="18.0.01"
for variant in default zh-bilinear zh-native; do
  dest="$PROJ/Assets/Fonts/$variant"
  if [ ! -f "$dest/unifont-$FONT_VER.otf" ]; then
    mkdir -p "$dest"
    cp "$REPO_ROOT/fonts/unifont-$FONT_VER.otf" "$dest/"
  fi
done

# TMP shaders ship only inside "TMP Essential Resources.unitypackage" (a tar.gz), and
# AssetDatabase.ImportPackage is unreliable in batchmode. Extract it into Assets directly;
# Unity imports everything on startup. The .meta files MUST be copied too — they carry the
# original GUIDs that TMP Settings and the TMP shaders reference each other by; without
# them Unity mints new GUIDs and every cross-reference in TMP Settings breaks. Skipped
# once present.
PKGDIR=$(ls -d "$PROJ/Library/PackageCache"/com.unity.textmeshpro@* 2>/dev/null | head -1)
if [ -n "${PKGDIR:-}" ] && [ -d "$PROJ/Assets" ] && [ ! -f "$PROJ/Assets/TextMesh Pro/.lcfp-extracted" ]; then
  echo "== extracting TMP Essential Resources into Assets =="
  rm -rf "$PROJ/Assets/TextMesh Pro" "$PROJ/Assets/TextMesh Pro.meta"
  TMPD=$(mktemp -d)
  tar xzf "$PKGDIR/Package Resources/TMP Essential Resources.unitypackage" -C "$TMPD"
  find "$TMPD" -name pathname | while read -r p; do
    entry_dir="$(dirname "$p")"
    dest_rel="$(cat "$p")"
    mkdir -p "$PROJ/$(dirname "$dest_rel")"
    [ -f "$entry_dir/asset" ] && cp "$entry_dir/asset" "$PROJ/$dest_rel"
    if [ -f "$entry_dir/asset.meta" ]; then
      cp "$entry_dir/asset.meta" "$PROJ/$dest_rel.meta"
    elif [ ! -f "$entry_dir/asset" ]; then
      # Folder entry: no asset file, meta was stored next to pathname.
      cp "$entry_dir/$(basename "$dest_rel").meta" "$PROJ/$dest_rel.meta" 2>/dev/null
    fi
  done
  rm -rf "$TMPD"
  touch "$PROJ/Assets/TextMesh Pro/.lcfp-extracted"
fi

echo "== stage 1: build bundles (StandaloneWindows64 + StandaloneOSX) =="
LCFP_REPO_ROOT="$REPO_ROOT" LCFP_OSX_OUT="$OUT_OSX" \
  "$UNITY_BIN" -batchmode -nographics -quit \
  -projectPath "$PROJ" \
  -executeMethod BuildUnifontBundles.BuildAll \
  -logFile "$LOGS/unity-build.log"
echo "build exit: $?"

echo "== stage 1b: audit shipped bundles (serialized content) =="
"$WORKSPACE/.venv/bin/python" "$REPO_ROOT/tools/audit_bundle.py" \
  "$REPO_ROOT/assets/FontPatcher/default/00 zh" \
  "$REPO_ROOT/test-bundles/zh-bilinear/01 zh bilinear" \
  "$REPO_ROOT/test-bundles/zh-native/02 zh native"

echo "== stage 2: validate macOS bundles in-editor =="
# Run with a graphics device: the validation drives TextMeshPro layout,
# which is safer with a real GfxDevice.
REPORT="$LOGS/validation-report.txt"
LCFP_OSX_OUT="$OUT_OSX" LCFP_VALIDATION_REPORT="$REPORT" \
  LCFP_HANZI_FILE="$([ "$FULL_HANZI" = 1 ] && echo "$REPO_ROOT/data/tongyong-guifan-hanzibiao.txt" || echo "")" \
  "$UNITY_BIN" -batchmode -quit \
  -projectPath "$PROJ" \
  -executeMethod ValidateUnifontBundles.Run \
  -logFile "$LOGS/unity-validate.log"
echo "validate exit: $?"

echo "== report =="
cat "$REPORT"
