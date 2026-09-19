# LC-FontPatcher (Chinese / Unifont fork)

![image](https://github.com/lekakid/LC-FontPatcher/assets/1362809/c11faea3-9c86-495a-99d4-ed56742ecf66)

Change in-game font to other font asset  
Fix chat input bug on IME input

> **This is a fork** of [lekakid/LC-FontPatcher](https://github.com/lekakid/LC-FontPatcher)
> (v1.2.4, MIT) that adds complete Simplified/Traditional Chinese support out of the box,
> fixes several plugin bugs, and makes the whole build pipeline work on macOS.
> All upstream credit belongs to [LeKAKiD](https://github.com/lekakid); see
> [Licenses & credits](#licenses--credits).

## What this fork adds

### Ready-made Chinese font bundle (GNU Unifont 18.0.01)

The repo ships a prebuilt, ready-to-use Chinese font bundle — `config/FontPatcher/default/00 zh` —
so Simplified/Traditional Chinese, Japanese kana, CJK punctuation and fullwidth forms
render without tofu (□):

- **100% coverage of the 通用规范汉字表** (Table of General Standard Chinese Characters,
  8105 entries incl. 178 extension-B/F characters) — verified programmatically
  (see [Coverage](#coverage)).
- **Dynamic TMP font asset**: glyphs are rasterized on demand from the Unifont font data
  embedded in the bundle, so nothing is pre-baked and any character Unifont covers
  (planes 0–3, ~59k codepoints) can appear. Atlas pages (1024×1024 Alpha8, ~1 MB each)
  are created on demand; typical chat usage stays on the first page.
- **Pixel-perfect style**: Unifont's 16px bitmap grid matches the game's low-resolution
  aesthetic; atlas pages are rendered in 1-bit RASTER mode and sampled with Point
  filtering for crisp pixels.
- **Metrics normalized to the game font** (ascent 0.8em, line height 1.09em), so mixed
  Chinese/Latin lines share one baseline and line height.
- **Material matches upstream's proven setup**: TMP Distance Field material with
  `_GradientScale = padding + 1`, measured from the upstream kr/jp bundles and mirrored
  here, so TMP's fallback material derivation behaves exactly like it does for the
  Korean/Japanese bundles.

Latin characters are still rendered by the game's own font (keep
`UsingNormalIngameFont = true`); Unifont only supplies the glyphs the game font lacks.

### Plugin bug fixes

| Fix | Symptom before |
|---|---|
| Font-asset loading made null-safe | missing font folder → `NullReferenceException` on every patched font |
| Deterministic bundle load order | fallback order depended on filesystem enumeration order |
| `.manifest` / `.meta` files skipped | noise + failed loads in the font folder |
| New atlas pages inherit filter mode | multi-atlas fonts mixed crisp and blurred pages |
| Null-safe `TMP_Text.font` setter patch | rare NRE when a text resets its font |

### Cross-platform build (macOS / Linux)

The plugin and the font bundles can be built without Windows: the csproj accepts the
game path as an override, Thunderstore packaging works without `tcli`, and the font
bundles are built by Unity **batchmode** scripts (no Editor GUI).

## Install

1. Install [BepInExPack](https://thunderstore.io/c/lethal-company/p/BepInEx/BepInExPack/)
   (5.4.21+) for Lethal Company.
   - On CrossOver/Wine, set the `winhttp` DLL override to `native,builtin` for the bottle.
2. Copy the contents of a release zip (or `build/LeKAKiD-FontPatcher.zip` produced by this
   repo) into the game folder:

   ```
   BepInEx/plugins/FontPatcher.dll
   BepInEx/config/FontPatcher/default/00 zh
   ```

3. Launch the game. Chinese text renders in chat, HUD and the signal translator.

Optional: install the upstream
[LC-FontPatcher release](https://thunderstore.io/c/lethal-company/p/LeKAKiD/FontPatcher/)
alongside for its `00 default` (English/`$` fix), `01 kr` and `02 jp` bundles — this fork's
bundle coexists with them (bundles are tried in filename order: `00 default` → `00 zh` → ...).

## Config

`BepInEx/config/lekakid.lcfontpatcher.cfg`:

```Properties
[General]
UsingNormalIngameFont = true    # keep game font for Latin; Unifont fills the gaps
UsingTransmitIngameFont = true

[Path]
FontAssetsPath = FontPatcher\default   # folder under BepInEx/config/ to load bundles from

[Debug]
Log = true                      # verbose font diagnostics
```

The debug log prints, for every loaded font asset: material/shader name, `_MainTex`,
`_GradientScale` and other SDF properties (missing ones are marked `absent`), atlas page
count/size/filter mode, population mode and whether the source font data is embedded —
plus the fallback count for every patched game font and the exact material TMP derives
for fallback rendering.

## Alternative bundle variants

Two more builds are provided in `test-bundles/` for comparison. Copy one into
`BepInEx/config/FontPatcher/<name>/` and point `FontAssetsPath` at it:

| Bundle | Sampling | Filter | Character |
|---|---|---|---|
| `00 zh` (shipped) | 80 pt (5× grid) | Point | crisp pixels |
| `01 zh bilinear` | 80 pt (5× grid) | Bilinear | softer, most readable at small text sizes |
| `02 zh native` | 16 pt (1:1 grid) | Point | native pixel size; smallest memory footprint |

## Building from source

### Prerequisites

- .NET SDK (any recent version)
- Unity **2022.3.62f2** (the game's version) with the **Windows Build Support (Mono)**
  module — needed to build the plugin against game assemblies and Windows AssetBundles
- python3 with `fonttools` (coverage check) and `UnityPy` (bundle audit)

### Plugin

```bash
dotnet build -c Debug \
  -p:LethalCompanyDir="/path/to/Lethal Company/Lethal Company_Data/Managed" \
  -p:TestDeployPath="/path/to/Lethal Company/BepInEx/plugins/"

dotnet build -c Release \
  -p:LethalCompanyDir="..."    # Release also produces the Thunderstore zip
```

- `LethalCompanyDir` defaults to the Windows Steam path and can always be overridden.
- `tcli` is used for Thunderstore packaging when available; on machines where it is
  broken/missing, `tools/package-thunderstore.py` builds the same zip from
  `thunderstore.toml`. Disable packaging entirely with `-p:EnableTcliBuild=false`.

### Font bundles

Everything is self-contained (fonts and the hanzi table live in this repo; the Unity
project and scratch output live under `.workspace/`, gitignored):

```bash
tools/build-bundles.sh               # build + validate (sample character set)
tools/build-bundles.sh --full-hanzi  # also add all 8105 standard hanzi in-editor
```

The script runs three stages:

1. **Build** (`BuildUnifontBundles.BuildAll`, Unity batchmode): imports the font with
   embedded font data, creates `Normal`/`Transmit` font assets via
   `TMP_FontAsset.CreateFontAsset(font, 80|16, padding, GlyphRenderMode.RASTER, 1024,
   1024, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true)`, clears the atlas
   texture, assigns a TMP Distance Field material with `_GradientScale = padding + 1`,
   normalizes the face metrics to the game font ratios, then builds StandaloneWindows64
   (shipped) + StandaloneOSX (validation) AssetBundles.
2. **Audit** (`tools/audit_bundle.py`, UnityPy): reads the serialized bundles back and
   asserts the atlas is fully transparent, population mode is Dynamic, render mode is
   RASTER and the material carries the expected SDF properties. This catches the two
   historical bugs (null `_MainTex`, non-zero atlas background) at build time.
3. **Validate** (`ValidateUnifontBundles.Run`, Unity batchmode): loads the macOS bundles
   through `AssetBundle.LoadFromFile` and exercises on-demand glyph generation —
   including the entire 通用规范汉字表 — plus a full text-layout run through the game's
   fallback path (`SetArraySizes` → `GetFallbackMaterial`). Validation runs on in-memory
   copies because TMP 3.0.7's editor-only `SetupNewAtlasTexture` branch NREs on
   bundle-persistent assets (a player build compiles it out).

### Coverage

```bash
python3 tools/check_coverage.py fonts/unifont-18.0.01.otf data/tongyong-guifan-hanzibiao.txt
```

Current result: **8106/8106 (100%)** covered, 0 missing.

## Repository layout

```
├── src/                      # plugin source (Loader.cs = font loading + Harmony patches)
├── assets/FontPatcher/       # shipped bundle: config/FontPatcher/default/00 zh
├── test-bundles/             # alternative variants (bilinear / native)
├── tools/
│   ├── build-bundles.sh      # one-command bundle pipeline (build + audit + validate)
│   ├── audit_bundle.py       # UnityPy audit of serialized bundles
│   ├── check_coverage.py     # fontTools cmap coverage check
│   ├── package-thunderstore.py  # tcli-free Thunderstore packaging
│   └── unity-editor/         # Unity batchmode scripts (build + validation)
├── fonts/                    # GNU Unifont 18.0.01 (OTF) + license texts
├── data/                     # 通用规范汉字表 (8105 characters)
├── licenses/                 # Unifont license texts (also shipped in the package)
└── thunderstore.toml         # Thunderstore package manifest
```

The Unity project used by the pipeline lives under `.workspace/unity-project` (created
with `-createProject`, TMP **3.0.7** = the game's TMP version) and is gitignored.

## Notes & troubleshooting

- Upstream's `00 default` (English/`$` fix) bundle is not included here — its font files
  are only distributed through the upstream Thunderstore release and their redistribution
  status was not confirmed. Latin text renders via the game font regardless while
  `UsingNormalIngameFont = true`.
- If Chinese characters ever render as solid boxes again, set `[Debug] Log = true` and
  check the `[fallback material]` log lines — they show the shader and `_GradientScale`
  TMP actually derived.
- Memory: a full 通用规范汉字表 run allocates 75 atlas pages (~75 MB) at 80 pt but only
  4 pages (~4 MB) at 16 pt; pages are created lazily, so real usage is a fraction of that.

## Licenses & credits

- Original mod and plugin code: **MIT** — Copyright © LeKAKiD (`LICENSE`). This fork's
  changes are published under the same license.
- Font bundles embed **GNU Unifont 18.0.01**, dual-licensed **GPLv2+ with the GNU Font
  Embedding Exception** and **SIL OFL 1.1** (`fonts/` and `licenses/Unifont-*.txt`, also
  shipped in the package).
- 通用规范汉字表 data from [rime-aca/character_set](https://github.com/rime-aca/character_set).
- "Lethal Company" is a trademark of Zeekerss. This project is not affiliated with or
  endorsed by Zeekerss.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).
