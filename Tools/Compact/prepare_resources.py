"""Prepare a pinned, auditable iOS resource subset; never fetch all card images."""
import argparse
import concurrent.futures
import hashlib
import http.client
import json
import pathlib
import shutil
import time
import urllib.parse
import urllib.request
import urllib.error

ROOT = pathlib.Path(__file__).resolve().parents[2]
REVISION = 'b36a95a24ff1a2f77ca6b9f9e0c30bc3e1cfa4d7'
API = 'https://code.moenext.com/api/v4/projects/4479/repository'
LOCK = ROOT / 'Tools/Compact/resources.lock.json'
TREES = ['iOS/MasterDuel/Background/Back0001', 'iOS/MasterDuel/Mate/M09755_Model',
         'iOS/MasterDuel/Protector/1070001', 'iOS/MasterDuel/Protector/1070002']
FILES = ['iOS/MasterDuel/Built-in/' + n for n in
         ['shaders', 'sprites', 'materials', 'outduel', 'textures', 'duel']]
FILES += ['iOS/MasterDuel/Material/' + n for n in
          ['CardInfoColorMat', 'GUI_CommonShopButton_N', 'GUI_CommonShopButton_N_Over']]
FILES += ['iOS/MasterDuel/' + n for n in ['Frame/ProfileFrameMat1030001',
          'Mat/Mat_002_near', 'Mat/Mat_002_far', 'Grave/Grave_002_near', 'Grave/Grave_002_far',
          'AvatarStand/AvatarStand_002_near', 'AvatarStand/AvatarStand_002_far']]


def request(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'MDPro3-Compact-build'}), timeout=180) as r:
        return r.read()


def raw_url(path):
    return API + '/files/' + urllib.parse.quote(path, safe='') + '/raw?ref=' + REVISION


def tree_files(path):
    result = []
    page = 1
    while True:
        url = API + '/tree?' + urllib.parse.urlencode(dict(ref=REVISION, path=path, per_page=100, page=page))
        entries = json.loads(request(url))
        for entry in entries:
            if entry['type'] == 'blob':
                result.append(entry['path'])
            else:
                result.extend(tree_files(entry['path']))
        if len(entries) < 100:
            return result
        page += 1


def destination(path):
    return ROOT / 'Assets/StreamingAssets/CompactBase' / pathlib.PurePosixPath(path)


def metadata(path):
    url = API + '/files/' + urllib.parse.quote(path, safe='') + '?ref=' + REVISION
    with urllib.request.urlopen(urllib.request.Request(url, method='HEAD'), timeout=60) as response:
        digest = response.headers['X-Gitlab-Content-Sha256']
        size = int(response.headers['X-Gitlab-Size'])
    if not digest or len(digest) != 64:
        raise RuntimeError('Missing upstream content digest: ' + path)
    return dict(path=path, bytes=size, sha256=digest)


def fetch(entry):
    target = destination(entry['path'])
    size = entry['bytes']
    expected = entry['sha256']
    if not 0 < size <= 100_000_000:
        raise RuntimeError('Unexpected resource size: ' + entry['path'])

    def matches(path):
        if not path.is_file() or path.stat().st_size != size:
            return False
        with path.open('rb') as stream:
            return hashlib.file_digest(stream, 'sha256').hexdigest() == expected

    if matches(target):
        return entry
    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_name(target.name + '.part')
    for attempt in range(5):
        try:
            if not matches(partial):
                offset = partial.stat().st_size if partial.exists() else 0
                if offset >= size:
                    partial.unlink()
                    offset = 0
                headers = {'User-Agent': 'MDPro3-Compact-build', 'Accept-Encoding': 'identity'}
                if offset:
                    headers['Range'] = f'bytes={offset}-'
                # The public raw endpoint sometimes ignores Range. A 200 always restarts the file.
                req = urllib.request.Request(raw_url(entry['path']) + f'&attempt={attempt}', headers=headers)
                with urllib.request.urlopen(req, timeout=45) as response:
                    if response.status == 206:
                        if response.headers.get('Content-Range') != f'bytes {offset}-{size - 1}/{size}':
                            raise OSError('Unexpected Content-Range')
                        mode = 'ab'
                    elif response.status == 200:
                        offset, mode = 0, 'wb'
                    else:
                        raise OSError(f'Unexpected status {response.status}')
                    with partial.open(mode) as output:
                        while chunk := response.read(256 * 1024):
                            offset += len(chunk)
                            if offset > size:
                                raise OSError('Response larger than pinned resource')
                            output.write(chunk)
                if not matches(partial):
                    raise OSError('Resource is incomplete or its SHA-256 does not match')
            partial.replace(target)
            print(entry['path'], size, flush=True)
            return entry
        except (OSError, http.client.HTTPException) as error:
            if attempt == 4:
                raise RuntimeError('Could not verify ' + entry['path']) from error
            print(f"Retrying {entry['path']}: {error}", flush=True)
            time.sleep(2 ** attempt)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--update-lock', action='store_true', help='Explicitly resolve and record resource hashes')
    parser.add_argument('--data-only', action='store_true', help='Stage local text/script data without downloading bundles')
    args = parser.parse_args()
    if args.update_lock:
        paths = set(FILES)
        for path in TREES:
            try:
                children = tree_files(path)
                paths.update(children if children else [path])
            except urllib.error.HTTPError as error:
                if error.code != 404:
                    raise
                paths.add(path)  # Mate may be a single bundle rather than a folder.
        with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
            entries = list(pool.map(metadata, sorted(paths)))
        LOCK.write_text(json.dumps(dict(revision=REVISION, files=entries), indent=2) + '\n', encoding='utf-8')
    else:
        lock = json.loads(LOCK.read_text(encoding='utf-8'))
        if lock['revision'] != REVISION:
            raise RuntimeError('Resource lock revision mismatch')
        entries = lock['files']
    if not args.data_only:
        with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
            entries = list(pool.map(fetch, entries))
    if sum(e['bytes'] for e in entries) > 200_000_000:
        raise RuntimeError('Base asset subset exceeds 200 MB; review dependencies before expanding')

    data_root = ROOT / 'Assets/StreamingAssets/CompactData'
    # Only package the selected locale, bot executable data, and common text/rule/script data.
    inputs = [p for p in (ROOT / 'Data').rglob('*') if p.is_file() and '.git' not in p.parts
              and (('locales' not in p.parts and p.name not in ['config.conf', 'FileGroups.json'])
                   or 'zh-CN' in p.parts)]
    inputs += [p for p in (ROOT / 'WindBot').rglob('*') if p.is_file() and p.suffix in ['.ydk', '.json', '.conf']]
    if not any(p.name == 'cards.cdb' for p in inputs) or not any(p.name == 'script.zip' for p in inputs):
        raise RuntimeError('Required text database or duel scripts are missing')
    manifest = []
    for source in sorted(inputs):
        relative = source.relative_to(ROOT)
        output = data_root / relative
        output.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, output)
        data = output.read_bytes()
        manifest.append(dict(path=relative.as_posix(), bytes=len(data), sha256=hashlib.sha256(data).hexdigest()))
    version = hashlib.sha256(json.dumps(manifest, sort_keys=True).encode()).hexdigest()
    (data_root / 'manifest.json').write_text(json.dumps(dict(version=version, files=manifest), indent=2) + '\n', encoding='utf-8')
    report = dict(asset_revision=REVISION, bundled_asset_bytes=sum(e['bytes'] for e in entries),
                  copied_data_bytes=sum(e['bytes'] for e in manifest), data_files=len(manifest),
                  card_image_files=0, note='Not final app size. Unity, Addressables and IL2CPP must be measured after Xcode link.')
    report['all_bundles_verified'] = all(destination(e['path']).exists() and
        hashlib.sha256(destination(e['path']).read_bytes()).hexdigest() == e['sha256'] for e in entries)
    reports = ROOT / 'TestResults/compact'
    reports.mkdir(parents=True, exist_ok=True)
    (reports / 'resources.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
