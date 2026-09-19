#!/usr/bin/env python3
"""Offline audit of built font AssetBundles (CPU-truth on serialized content).

Checks, per bundle:
- atlas texture content: a Dynamic font asset ships an unused page; every texel must
  be 0. Non-zero background = solid boxes behind every glyph in-game.
- font assets: name, populationMode (1=Dynamic), renderMode (4118=RASTER), padding,
  atlas size, version.
- materials: _GradientScale == padding + 1, _WeightBold present (SDF property set that
  TMP_MaterialManager.GetFallbackMaterial copies onto the game font's material clone).

Usage: audit_bundle.py <bundle> [<bundle>...]  — exits 1 on any failure.
"""
import sys
from collections import Counter

import UnityPy


def audit(path: str) -> bool:
    ok = True
    print(f"=== {path}")
    env = UnityPy.load(path)
    textures = {}
    font_assets = []
    materials = {}
    for obj in env.objects:
        if obj.type.name == "Texture2D":
            t = obj.read()
            textures[obj.path_id] = t
        elif obj.type.name == "MonoBehaviour":
            d = obj.read_typetree()
            if "m_AtlasPopulationMode" in d:
                font_assets.append(d)
        elif obj.type.name == "Material":
            d = obj.read()
            materials[obj.path_id] = d

    for fa in font_assets:
        name = fa.get("m_Name")
        mode = fa.get("m_AtlasPopulationMode")
        padding = fa.get("m_AtlasPadding")
        width = fa.get("m_AtlasWidth")
        height = fa.get("m_AtlasHeight")
        render_mode = fa.get("m_AtlasRenderMode")
        version = fa.get("m_Version")
        print(f"  font asset '{name}': mode={mode} renderMode={render_mode} padding={padding} "
              f"atlas={width}x{height} version={version}")
        if mode != 1:
            print(f"    FAIL: populationMode {mode} != 1 (Dynamic)")
            ok = False
        if render_mode != 4118:
            print(f"    FAIL: renderMode {render_mode} != 4118 (RASTER)")
            ok = False

    for path_id, mat in materials.items():
        floats = {f[0]: f[1] for f in mat.m_SavedProperties.m_Floats}
        name = mat.m_Name
        # Only audit the TMP font-asset materials; a bundled UnityEngine.Font also
        # carries its own unrelated default material ("Font Material").
        if not any(s in name for s in ("Atlas Material", "Normal Material", "Transmit Material")):
            continue
        gs = floats.get("_GradientScale")
        wb = floats.get("_WeightBold")
        tw = floats.get("_TextureWidth")
        print(f"  material '{name}': _GradientScale={gs} _WeightBold={wb} _TextureWidth={tw}")
        if gs is None:
            print("    FAIL: missing _GradientScale (GetFallbackMaterial would take the wrong branch)")
            ok = False
        elif gs < 2:
            print(f"    FAIL: _GradientScale {gs} degenerate (must be padding+1 >= 2)")
            ok = False
        if wb is None:
            print("    FAIL: missing _WeightBold (not a TMP Distance Field material)")
            ok = False

    for path_id, t in textures.items():
        if t.m_Width == 0 or t.m_Height == 0:
            print(f"  texture '{t.m_Name}': {t.m_Width}x{t.m_Height} format={t.m_TextureFormat} (skipped)")
            continue
        if t.m_TextureFormat != 1:  # Alpha8
            print(f"    FAIL: texture '{t.m_Name}' format {t.m_TextureFormat} != Alpha8")
            ok = False
            continue
        img = t.image
        hist = Counter(img.tobytes())
        nonzero = sum(n for v, n in hist.items() if v != 0)
        top = ", ".join(f"alpha={v} x{n}" for v, n in hist.most_common(3))
        print(f"  texture '{t.m_Name}': {t.m_Width}x{t.m_Height} Alpha8, {top}")
        if nonzero:
            print(f"    FAIL: {nonzero} non-zero background texels (would paint boxes)")
            ok = False
    return ok


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    ok = all(audit(p) for p in sys.argv[1:])
    print("AUDIT:", "PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
