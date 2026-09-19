#!/usr/bin/env python3
"""Check Unifont coverage of 通用规范汉字表 (Table of General Standard Chinese Characters).

Usage: check_coverage.py <font.otf> <hanzibiao.txt> [out_report.md]
Requires: pip install fonttools
"""
import sys
from pathlib import Path

from fontTools.ttLib import TTFont


def load_hanzibiao(path: str) -> list[str]:
    chars: list[str] = []
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line:
            continue
        if line.startswith("#"):
            line = line[1:]
        chars.extend(ch for ch in line if not ch.isspace())
    return chars


def main() -> None:
    font_path, table_path = sys.argv[1], sys.argv[2]
    out_path = sys.argv[3] if len(sys.argv) > 3 else None

    font = TTFont(font_path)
    cmap = font.getBestCmap()
    name = font["name"].getDebugName(4) or font["name"].getDebugName(1)
    head = font["head"]
    hhea = font["hhea"]
    os2 = font["OS/2"]
    max_glyph_name = max(cmap.keys())

    chars = load_hanzibiao(table_path)
    unique = sorted(set(chars))
    missing = [c for c in unique if ord(c) not in cmap]
    bmp_missing = [c for c in missing if ord(c) <= 0xFFFF]

    lines = []
    lines.append(f"# Unifont coverage report")
    lines.append(f"- Font file: `{font_path}`")
    lines.append(f"- Font full name: {name}")
    lines.append(f"- unitsPerEm: {head.unitsPerEm}")
    lines.append(f"- hhea ascender/descender/lineGap: {hhea.ascent}/{hhea.descent}/{hhea.lineGap}")
    lines.append(f"- OS/2 sTypoAscender/Descender/LineGap: {os2.sTypoAscender}/{os2.sTypoDescender}/{os2.sTypoLineGap}")
    lines.append(f"- OS/2 usWinAscent/usWinDescent: {os2.usWinAscent}/{os2.usWinDescent}")
    lines.append(f"- Mapped codepoints in cmap: {len(cmap)}, max U+{max_glyph_name:04X}")
    lines.append(f"- Hanzi table: {len(chars)} chars read, {len(unique)} unique")
    lines.append(f"- Covered: {len(unique) - len(missing)}/{len(unique)} ({(len(unique)-len(missing))/len(unique)*100:.2f}%)")
    lines.append(f"- Missing total: {len(missing)} (of which BMP: {len(bmp_missing)}, non-BMP: {len(missing)-len(bmp_missing)})")
    if missing:
        lines.append("")
        lines.append("## Missing characters")
        for c in missing:
            lines.append(f"- U+{ord(c):04X} {c}")

    report = "\n".join(lines)
    print(report)
    if out_path:
        Path(out_path).write_text(report, encoding="utf-8")


if __name__ == "__main__":
    main()
