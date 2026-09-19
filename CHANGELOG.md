# Changelog

All notable changes to this fork are documented here. Upstream releases are tracked at
[lekakid/LC-FontPatcher](https://github.com/lekakid/LC-FontPatcher/releases).

## Unreleased (fork)

### Added

- Ready-made Chinese font bundle `00 zh` based on GNU Unifont 18.0.01: dynamic TMP font
  asset, 16px pixel grid (80 pt = 5× sampling), Point filtering, metrics normalized to
  the game font (ascent 0.8em, line height 1.09em), TMP Distance Field material with
  `_GradientScale = padding + 1` (mirroring the upstream kr/jp bundles).
- Alternative bundle variants `01 zh bilinear` (80 pt, Bilinear) and `02 zh native`
  (16 pt, Point) under `test-bundles/`.
- Unity batchmode build pipeline (`tools/build-bundles.sh` +
  `tools/unity-editor/BuildUnifontBundles.cs`): builds StandaloneWindows64 bundles for
  shipping and StandaloneOSX bundles for in-editor validation; extracts TMP Essential
  Resources with original GUIDs intact (ImportPackage is unreliable in batchmode).
- In-editor validation (`tools/unity-editor/ValidateUnifontBundles.cs`): loads the
  bundles through `AssetBundle.LoadFromFile`, exercises on-demand glyph generation
  including the full 通用规范汉字表 (8107 codepoints, 0 missing) and a full text layout
  through the game's fallback material path.
- Offline bundle audit (`tools/audit_bundle.py`, UnityPy): asserts transparent atlas
  content, Dynamic population mode, RASTER render mode and SDF material properties on
  the serialized bundles.
- Offline coverage check (`tools/check_coverage.py`, fontTools): 通用规范汉字表 coverage
  of the font cmap — 100%.
- tcli-free Thunderstore packaging (`tools/package-thunderstore.py`) used automatically
  when `tcli` is unavailable.
- Verbose font diagnostics (`[Debug] Log = true`): per-font material/shader/SDF
  property dump with explicit `absent` markers, atlas info, embedded-font check, and a
  `GetFallbackMaterial` postfix logging the material TMP derives in-game.

### Fixed

- `NullReferenceException` in every Harmony patch when the font directory is missing
  (regexes are now initialized before any fallible code; missing directory is logged
  and loading returns early).
- Bundle load order is now deterministic (files sorted by name, Ordinal), matching the
  documented `00 → 01 → 02` fallback order.
- `.manifest` / `.meta` files next to bundles are skipped instead of failing to load.
- Null `_MainTex` in serialized font materials (historical bundle bug that NRE'd
  `TMP_MaterialManager.GetFallbackMaterial` in-game): fixed at build time via asset
  creation order + post-save verification, plus a runtime self-heal that re-binds
  `atlasTextures[0]`.
- Solid text-colored boxes behind every CJK glyph (historical bundle bug): the shipped
  atlas page is now explicitly zeroed (`Texture2D.Reinitialize` leaves content
  undefined) and the material uses the TMP Distance Field shader with proper SDF
  properties so TMP derives fallback materials correctly.
- Runtime-created atlas pages now inherit filter/wrap mode from the first page
  (postfix on `TMP_FontAsset.SetupNewAtlasTexture`).
- Null-safe `TMP_Text.font` setter patch.

### Changed

- Build is cross-platform: `LethalCompanyDir` overridable via `-p:`, `tcli` optional,
  local test deployment via `TestDeployPath`, forward-slash MSBuild paths.
- Upstream `00 default` bundle is not redistributed (license status unconfirmed);
  Latin text renders via the game font regardless.
