"""Check the published numeric identity and mode-specific package contents."""

import hashlib
import json
from pathlib import Path
import sys


def check_skia_licenses(package: Path) -> None:
    source = package / 'licenses/SkiaSharp'
    entries = json.loads((source / 'license-inventory.json').read_text(encoding='utf-8-sig'))
    allowed = {
        'skiasharp-4.154.0/LICENSE.txt',
        'skiasharp.nativeassets.linux-4.154.0/LICENSE.txt',
        'skiasharp.nativeassets.linux-4.154.0/THIRD-PARTY-NOTICES.txt',
        'harfbuzzsharp-14.2.1.400/LICENSE.txt',
        'skiasharp-2.88.9/LICENSE.txt',
        'skiasharp-2.88.9/THIRD-PARTY-NOTICES.txt',
        'skiasharp.nativeassets.linux-2.88.9/LICENSE.txt',
        'skiasharp.nativeassets.linux-2.88.9/THIRD-PARTY-NOTICES.txt',
        'harfbuzzsharp-8.3.1.1/LICENSE.txt',
    }
    if not isinstance(entries, list) or len(entries) != len(allowed):
        raise ValueError('invalid Skia license inventory')
    for entry in entries:
        relative = entry.get('file')
        if relative not in allowed:
            raise ValueError(f'unsafe or duplicate Skia license path: {relative}')
        allowed.remove(relative)
        if hashlib.sha256((source / relative).read_bytes()).hexdigest() != entry.get('sha256', '').lower():
            raise ValueError(f'Skia license SHA-256 mismatch: {relative}')


def check_config_isolation(package: Path, rid: str) -> None:
    platform = rid.split('-')[0]
    extension = '.exe' if platform == 'win' else ''
    frameworks = []
    for name in ('THSSS', 'Config', 'ConfigApp/Config'):
        executable = package / (name + extension)
        if not executable.is_file() or executable.stat().st_size == 0:
            raise ValueError(f'missing executable: {executable}')
        config = json.loads((package / (name + '.runtimeconfig.json')).read_text(encoding='utf-8-sig'))
        frameworks.append(config['runtimeOptions']['tfm'])
    if frameworks[0] not in ('net8.0', 'net10.0') or len(set(frameworks)) != 1:
        raise ValueError(f'package framework mismatch: {frameworks}')

    launcher = json.loads((package / 'Config.deps.json').read_text(encoding='utf-8-sig'))
    if any(name.split('/')[0].startswith(('Avalonia', 'SkiaSharp', 'HarfBuzzSharp')) for name in launcher['libraries']):
        raise ValueError('root Config launcher must be BCL-only')
    for directory, name, version, harfbuzz in ((package, 'THSSS', '4.154.0', '14.2.1.400'), (package / 'ConfigApp', 'Config', '2.88.9', '8.3.1.1')):
        deps = json.loads((directory / (name + '.deps.json')).read_text(encoding='utf-8-sig'))
        libraries = deps['libraries']
        if 'SkiaSharp/' + version not in libraries:
            raise ValueError(f'incorrect isolated SkiaSharp version: {directory}')
        if 'HarfBuzzSharp/' + harfbuzz not in libraries:
            raise ValueError(f'incorrect isolated HarfBuzzSharp version: {directory}')
        if name == 'Config' and 'Avalonia/11.3.9' not in libraries:
            raise ValueError(f'missing Avalonia GUI dependency: {directory}')
        if name == 'Config' and 'Tmds.DBus.Protocol/0.21.3' not in libraries:
            raise ValueError(f'missing patched D-Bus dependency: {directory}')
        if any('NoDependencies' in key for key in libraries):
            raise ValueError(f'NoDependencies cannot supply Linux fontconfig: {directory}')
        assets = [asset for target in deps['targets'].values() for library in target.values()
                  for section in ('native', 'runtimeTargets') for asset in library.get(section, {})]
        for native in ('SkiaSharp', 'HarfBuzzSharp'):
            filename = {'win': f'lib{native}.dll', 'linux': f'lib{native}.so', 'osx': f'lib{native}.dylib'}[platform]
            if not any(Path(asset).name == filename for asset in assets):
                raise ValueError(f'missing native dependency record: {directory / filename}')
            if not any(path.is_file() and path.stat().st_size
                       and (directory != package or 'ConfigApp' not in path.relative_to(package).parts)
                       for path in directory.rglob(filename)):
                raise ValueError(f'missing native asset: {directory / filename}')


def check_archive(package: Path, mode: str, rid: str) -> None:
    if mode not in ("auto", "software", "off"):
        raise ValueError(f"invalid math mode: {mode!r}")
    check_config_isolation(package, rid)
    check_skia_licenses(package)
    record = json.loads(
        (package / "THSSS.math-mode.json").read_text(encoding="utf-8-sig")
    )
    if not isinstance(record, dict) or record.get("mode") != mode:
        raise ValueError("published math mode does not match requested mode")
    digest = record.get("sha256")
    if (
        not isinstance(digest, str)
        or len(digest) != 64
        or any(c not in "0123456789abcdefABCDEF" for c in digest)
    ):
        raise ValueError("invalid math mode SHA-256 record")
    if (
        digest.lower()
        != hashlib.sha256((package / "THSSS.dll").read_bytes()).hexdigest()
    ):
        raise ValueError("math mode SHA-256 mismatch")
    libraries = {
        "win": "thsss_legacy_math.dll",
        "linux": "libthsss_legacy_math.so",
        "osx": "libthsss_legacy_math.dylib",
    }
    source = package / "licenses/LegacyMath/source"
    if mode == "off":
        for name in (
            *libraries.values(),
            "GameplayMath.manifest.json",
            "licenses/LegacyMath/source",
        ):
            if (package / name).exists() or (package / name).is_symlink():
                raise ValueError(f"disabled math artifact remains: {name}")
        return
    for path in (
        package / libraries[rid.split("-")[0]],
        *(source / name for name in ("NOTICE.md", "build.py", "software_x87.cpp")),
    ):
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError(f"missing numeric library/source: {path}")


if __name__ == "__main__":
    check_archive(Path(sys.argv[1]), sys.argv[2], sys.argv[3])
