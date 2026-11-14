"""Fail builds with missing/corrupt bundles or extra packaged bulk resources."""
import hashlib
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]


def main():
    lock = json.loads((ROOT / 'Tools/Compact/resources.lock.json').read_text())
    base = ROOT / 'Assets/StreamingAssets'
    expected = {}
    for entry in lock['files']:
        expected['CompactBase/' + entry['path']] = entry
    manifest = json.loads((base / 'CompactData/manifest.json').read_text())
    for entry in manifest['files']:
        expected['CompactData/' + entry['path']] = entry
    for name, entry in expected.items():
        path = base / name
        if not path.is_file() or path.stat().st_size != entry['bytes'] or hashlib.sha256(path.read_bytes()).hexdigest() != entry['sha256']:
            raise SystemExit('Missing or corrupt compact resource: ' + name)
    for path in base.rglob('*'):
        if path.is_file() and path.suffix != '.meta':
            relative = path.relative_to(base).as_posix()
            if relative not in expected and relative != 'CompactData/manifest.json':
                raise SystemExit('Unexpected StreamingAssets input: ' + relative)
    print('PASS: all compact resources verified; no extra resource archives or card image libraries packaged.')


if __name__ == '__main__':
    main()
