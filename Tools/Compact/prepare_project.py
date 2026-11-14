"""Assemble a compact Unity source snapshot from the pinned upstream checkout."""
import argparse
import concurrent.futures
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
GIT = ['git', '-c', 'core.safecrlf=false']
ENV = {**os.environ, 'GIT_NO_LAZY_FETCH': '1', 'GIT_TERMINAL_PROMPT': '0', 'GCM_INTERACTIVE': 'never'}
SOURCE_PREFIXES = (
    'Assets/AddressableAssetsData/', 'Assets/Addressables/Text/', 'Assets/Animation/',
    'Assets/Fonts/', 'Assets/Materials/', 'Assets/Plugins/Managed/', 'Assets/Prefabs/',
    'Assets/Resources/', 'Assets/Scenes/', 'Assets/Scripts/', 'Assets/Shaders/',
    'Assets/Texture/', 'Assets/Tools/', 'Data/locales/zh-CN/', 'Data/Windbot/',
    'Data/pack/', 'Packages/', 'ProjectSettings/', 'Tools/YGO Classes/irrlicht/',
    'Tools/YGO Classes/lua/', 'Tools/YGO Classes/sqlite3/', 'Tools/YGO Classes/ygoserver/',
)
PORT_PREFIXES = (
    '.github/workflows/', 'Assets/Scripts/MDPro3/Compact', 'Assets/Scripts/Editor/CompactIOSBuild',
    'Assets/Scripts/Tools/AssetExtraction', 'Native/', 'Tests/Compact/', 'Tools/Compact/', 'Docs/iOS/',
)
DEFAULT_ICONS = re.compile(r'(?:1010001|1010002|1030001|1090001|1100001|1110001|1130001|1000001|DeckCase0001)')
SYSTEM_AUDIO = ('Assets/Addressables/Sound/SE_SYS/',)
DEFAULT_BGM = {
    'BGM_MENU_01', 'BGM_DUEL_NORMAL_01', 'BGM_DUEL_KEYCARD_01', 'BGM_DUEL_CLIMAX_01',
}


def git(*args, **kwargs):
    return subprocess.check_output([*GIT, *args], cwd=ROOT, env=ENV, **kwargs)


def include(path):
    if path in ('.editorconfig', '.gitignore', 'README.md', 'Assets/Plugins/Windows/sqlite3.dll'):
        return True
    if path.startswith('Data/') and '/' not in path[len('Data/'):]:
        return True
    if path.startswith('Assets/Addressables/Icon/'):
        return bool(DEFAULT_ICONS.search(PurePosixPath(path).name)) or path.endswith('.meta') and '/' not in path[len('Assets/Addressables/Icon/'):]
    if path.startswith('Assets/Addressables/Sound/'):
        return path.startswith(SYSTEM_AUDIO) or PurePosixPath(path).name.split('.')[0] in DEFAULT_BGM or path.endswith('.meta') and '/' not in path[len('Assets/Addressables/Sound/'):]
    if path.startswith('Assets/') and path.endswith('.meta') and path.count('/') == 1:
        return True
    return path.startswith(SOURCE_PREFIXES) and not path.startswith((
        'Tools/YGO Classes/irrlicht/media/', 'Assets/Texture/PSD/',
        'Assets/Tools/AssetStudio/', 'Assets/Tools/AssetBundles-Browser-1.7.0/',
    ))


def missing(objects):
    query = '\n'.join(sorted(set(objects))) + '\n'
    output = git('cat-file', '--batch-check=%(objectname) %(objectsize)', input=query.encode())
    return [row.split()[0].decode() for row in output.splitlines() if row.endswith(b' missing')]


def fetch_objects(objects):
    def retrieve(oid):
        url = f'https://code.moenext.com/api/v4/projects/sherry_chaos%2FMDPro3/repository/blobs/{oid}/raw'
        for attempt in range(4):
            try:
                request = urllib.request.Request(url, headers={'User-Agent': 'MDPro3-Compact-build'})
                with urllib.request.urlopen(request, timeout=40) as response:
                    data = response.read()
                if git('hash-object', '-w', '--stdin', input=data).decode().strip() != oid:
                    raise OSError('Upstream blob did not match Git object ID')
                return oid
            except Exception:
                if attempt == 3:
                    raise
                time.sleep(2 ** attempt)
    print(f'Fetching {len(objects)} missing objects independently', flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:
        for index, _ in enumerate(pool.map(retrieve, objects), start=1):
            if index % 10 == 0 or index == len(objects):
                print(f'Verified {index}/{len(objects)} objects', flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('destination', type=Path)
    parser.add_argument('--fetch', action='store_true')
    args = parser.parse_args()
    upstream = {}
    for row in git('ls-tree', '-rz', 'HEAD').split(b'\0'):
        if row:
            metadata, raw_path = row.split(b'\t', 1)
            mode, kind, oid = metadata.decode().split()
            path = raw_path.decode()
            if kind == 'blob' and include(path):
                upstream[path] = oid
    remove = set(git('diff', '--name-only', '--diff-filter=D', '-z', 'HEAD').decode().split('\0'))
    changed = set(git('diff', '--name-only', '--diff-filter=AM', '-z', 'HEAD').decode().split('\0'))
    new = [p for p in git('ls-files', '--others', '--exclude-standard', '-z').decode().split('\0')
           if p.startswith(PORT_PREFIXES)]
    files = {p: oid for p, oid in upstream.items() if p not in remove and p not in changed}
    absent = missing(files.values())
    if absent and args.fetch:
        fetch_objects(absent)
        absent = missing(absent)
    if absent:
        raise SystemExit(f'{len(absent)} upstream objects missing. Run again with --fetch.')
    out = args.destination.resolve()
    if out == ROOT or ROOT.is_relative_to(out):
        raise SystemExit('Choose a separate destination')
    out.mkdir(parents=True, exist_ok=True)
    with subprocess.Popen([*GIT, 'cat-file', '--batch'], cwd=ROOT, env=ENV,
                          stdin=subprocess.PIPE, stdout=subprocess.PIPE) as proc:
        for name, oid in files.items():
            proc.stdin.write((oid + '\n').encode())
            proc.stdin.flush()
            header = proc.stdout.readline().split()
            if len(header) != 3 or header[0].decode() != oid:
                raise RuntimeError('Git returned an unexpected object for ' + name)
            content = proc.stdout.read(int(header[2]))
            if proc.stdout.read(1) != b'\n':
                raise RuntimeError('Truncated Git object: ' + name)
            target = out / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(content)
        proc.stdin.close()
        if proc.wait():
            raise RuntimeError('git cat-file failed')
    for name in sorted((changed & upstream.keys()) | set(new)):
        target = out / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes((ROOT / name).read_bytes())
    print(f'Assembled {len(files)} upstream files and port changes in {out}', flush=True)


if __name__ == '__main__':
    main()
