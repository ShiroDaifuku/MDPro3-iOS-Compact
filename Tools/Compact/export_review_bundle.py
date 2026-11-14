"""Export local source changes without uploading upstream assets or generated binaries."""
import hashlib
import json
import pathlib
import subprocess
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
PREFIXES = ['.github/', 'Assets/Scripts/MDPro3/Compact', 'Assets/Scripts/Editor/CompactIOSBuild',
            'Assets/Scripts/Tools/AssetExtraction', 'Native/', 'Tests/Compact/', 'Tools/Compact/', 'Docs/iOS/']


def main():
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    patch = subprocess.check_output(['git', '-c', 'core.safecrlf=false', 'diff', '--binary', 'HEAD'], cwd=ROOT)
    paths = subprocess.check_output(['git', 'ls-files', '--others', '--exclude-standard', '-z'], cwd=ROOT).decode().split('\0')
    paths = [p for p in paths if p and any(p.startswith(prefix) for prefix in PREFIXES)]
    destination = ROOT / 'Artifacts/MDPro3-iOS-source-overlay.zip'
    destination.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(destination, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('tracked-changes.patch', patch)
        hashes = {}
        for path in sorted(paths):
            data = (ROOT / path).read_bytes()
            archive.writestr('new-files/' + path, data)
            hashes[path] = hashlib.sha256(data).hexdigest()
        archive.writestr('upstream.json', json.dumps(dict(repository='https://code.moenext.com/sherry_chaos/MDPro3',
            revision=revision, patch_sha256=hashlib.sha256(patch).hexdigest(), files=hashes), indent=2))
        archive.writestr('APPLY.txt', 'Clone the upstream repository and check out the revision in upstream.json.\n'
            'Run: git apply /path/to/tracked-changes.patch\n'
            'Copy the contents of new-files/ into the checkout.\n'
            'Then follow Docs/iOS/STATUS.md. This archive is source changes, not a complete Unity project or an IPA.\n')
    print(destination)
    print('New source files:', len(paths), '; patch bytes:', len(patch), '; ZIP bytes:', destination.stat().st_size)


if __name__ == '__main__':
    main()
