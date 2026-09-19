#!/usr/bin/env python3
"""Package the plugin as a Thunderstore zip without tcli.

Reads thunderstore.toml for metadata and copy rules, reads the plugin version
from FontPatcher.csproj, generates manifest.json, and zips:

    manifest.json, icon.png, README.md, LICENSE,
    plugins/FontPatcher.dll,
    config/FontPatcher/<variant>/<bundle>

Usage: package-thunderstore.py [--out build/LeKAKiD-FontPatcher.zip]
"""
import argparse
import json
import re
import sys
import zipfile
from pathlib import Path

try:
    import tomllib
except ModuleNotFoundError:  # python < 3.11
    tomllib = None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=Path(__file__).resolve().parent.parent, type=Path)
    parser.add_argument("--out", default=None)
    args = parser.parse_args()
    repo: Path = args.repo

    cfg = tomllib.loads((repo / "thunderstore.toml").read_text(encoding="utf-8")) if tomllib else None
    if cfg is None:
        print("tomllib unavailable; cannot parse thunderstore.toml", file=sys.stderr)
        return 1

    package = cfg["package"]
    build = cfg["build"]

    csproj = (repo / "FontPatcher.csproj").read_text(encoding="utf-8")
    version = re.search(r"<Version>([^<]+)</Version>", csproj).group(1)

    out = Path(args.out) if args.out else repo / build["outdir"].lstrip("./") / f"{package['namespace']}-{package['name']}.zip"

    manifest = {
        "name": package["name"],
        "version_number": version,
        "website_url": package["websiteUrl"],
        "description": package["description"],
        "dependencies": dict(package.get("dependencies", {})),
    }

    out.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("manifest.json", json.dumps(manifest, indent=2))
        zf.write(repo / build["icon"], "icon.png")
        zf.write(repo / build["readme"], "README.md")
        for rule in build.get("copy", []):
            source: Path = repo / rule["source"].lstrip("./")
            target: str = rule["target"].strip("/")
            if source.is_file():
                zf.write(source, f"{target}/{source.name}" if target not in ("", ".") else source.name)
                continue
            for f in sorted(source.rglob("*")):
                if f.is_file() and f.suffix != ".zip" and f.suffix != ".manifest" and f.suffix != ".meta":
                    rel = f.relative_to(source)
                    zf.write(f, f"{target}/{rel}" if target not in ("", ".") else str(rel))

    print(f"wrote {out} ({out.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
