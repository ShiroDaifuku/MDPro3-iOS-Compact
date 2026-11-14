"""Restore source dependencies intentionally absent from the upstream Unity checkout."""
import argparse
import pathlib
import shutil
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPINE = '5cb78e77d3711ddcd80c7bd043bb11cf70fcc88c'
SFB = '04a5d49ed2545556da8a7192e86c69bd47641f10'


def checkout(name, url, revision, paths):
    target = ROOT / 'Native/vendor' / name
    if not (target / '.git').exists():
        target.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(['git', 'init', str(target)], check=True)
        subprocess.run(['git', '-C', str(target), 'remote', 'add', 'origin', url], check=True)
        subprocess.run(['git', '-C', str(target), 'fetch', '--filter=blob:none', '--depth=1', 'origin', revision], check=True)
    subprocess.run(['git', '-C', str(target), 'sparse-checkout', 'set', *paths], check=True)
    subprocess.run(['git', '-C', str(target), 'checkout', '--detach', revision], check=True)
    return target


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--spine-source', type=pathlib.Path)
    parser.add_argument('--sfb-source', type=pathlib.Path)
    args = parser.parse_args()
    spine = args.spine_source or checkout('spine-runtimes', 'https://github.com/EsotericSoftware/spine-runtimes.git',
                                         SPINE, ['spine-csharp/src', 'spine-unity/Assets/Spine'])
    sfb = args.sfb_source or checkout('StandaloneFileBrowser', 'https://github.com/gkngkc/UnityStandaloneFileBrowser.git',
                                     SFB, ['Assets/StandaloneFileBrowser'])
    for source, expected in [(spine, SPINE), (sfb, SFB)]:
        revision = subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip()
        if revision != expected:
            raise RuntimeError('Dependency revision mismatch: ' + str(source))
    shutil.copytree(spine / 'spine-unity/Assets/Spine', ROOT / 'Assets/Spine', dirs_exist_ok=True)
    shutil.copytree(spine / 'spine-csharp/src', ROOT / 'Assets/Spine/spine-csharp', dirs_exist_ok=True)
    shutil.copytree(sfb / 'Assets/StandaloneFileBrowser', ROOT / 'Assets/Plugins/StandaloneFileBrowser',
                    dirs_exist_ok=True, ignore=shutil.ignore_patterns('Sample', 'Sample.meta'))
    for source, target in [(spine / 'LICENSE', ROOT / 'Assets/Spine/LICENSE.txt'),
                            (sfb / 'LICENSE.txt', ROOT / 'Assets/Plugins/StandaloneFileBrowser/LICENSE.txt')]:
        shutil.copyfile(source, target)
    print('Restored pinned source dependencies. Spine runtime version compatibility with prebuilt bundles needs device testing.')


if __name__ == '__main__':
    main()
