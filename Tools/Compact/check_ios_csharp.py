"""Compile actual application sources with iOS player symbols using an imported Unity project.

This catches conditional-source errors on Windows. It does not run IL2CPP or emulate iOS.
"""
import argparse
import json
import pathlib
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--unity-data', required=True, type=pathlib.Path)
    args = parser.parse_args()
    candidates = list((ROOT / 'Library/Bee/artifacts').glob('*/Assembly-CSharp.rsp'))
    if not candidates:
        raise SystemExit('Import and compile the Unity project first.')
    response = max(candidates, key=lambda p: p.stat().st_mtime)
    output = ROOT / 'TestResults/ios-csharp'
    output.mkdir(parents=True, exist_ok=True)
    lines = []
    for line in response.read_text(encoding='utf-8-sig').splitlines():
        if line.startswith('-define:'):
            symbol = line[len('-define:'):]
            if symbol.startswith(('UNITY_EDITOR', 'UNITY_STANDALONE', 'PLATFORM_STANDALONE')) or symbol in ['ENABLE_MONO', 'UNITY_64']:
                continue
        if line.startswith(('-out:', '-refout:')):
            continue
        if line.startswith('-r:') and ('UnityEditor' in line or 'Mono.Data.Sqlite.dll' in line):
            continue
        lines.append(line)
    lines += ['-define:UNITY_IOS', '-define:UNITY_IPHONE', '-define:PLATFORM_IOS', '-define:ENABLE_IL2CPP',
              '-define:MDPRO3_COMPACT', '-out:"TestResults/ios-csharp/Assembly-CSharp.dll"',
              '-refout:"TestResults/ios-csharp/Assembly-CSharp.ref.dll"']
    modified = output / 'player-symbols.rsp'
    modified.write_text('\n'.join(lines), encoding='utf-8')
    dotnet = args.unity_data / 'NetCoreRuntime' / ('dotnet.exe' if __import__('sys').platform == 'win32' else 'dotnet')
    compiler = args.unity_data / 'DotNetSdkRoslyn/csc.dll'
    result = subprocess.run([str(dotnet), str(compiler), '@' + str(modified)], cwd=ROOT, capture_output=True, text=True, errors='replace')
    (output / 'compiler.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    (output / 'result.json').write_text(json.dumps(dict(exit_code=result.returncode,
        note='Windows reference assemblies with iOS player symbols; not an iOS build.'), indent=2), encoding='utf-8')
    for line in (result.stdout + result.stderr).splitlines():
        if ': error ' in line:
            print(line)
    print('iOS player-symbol C# compiler exit:', result.returncode)
    return result.returncode


if __name__ == '__main__':
    raise SystemExit(main())
