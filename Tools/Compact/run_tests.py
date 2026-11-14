"""Run platform-neutral production C# code with desktop Mono; no Unity license needed."""
import argparse,json,pathlib,subprocess,tempfile,sys
sys.stdout.reconfigure(encoding='utf-8')
def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--mono-root',required=True,type=pathlib.Path)
    parser.add_argument('--jpeg',type=pathlib.Path,default=pathlib.Path('Tests/Compact/Fixtures/white.jpg'))
    parser.add_argument('--database',type=pathlib.Path,default=pathlib.Path('Data/locales/zh-CN/cards.cdb'))
    parser.add_argument('--dotnet',type=pathlib.Path,help='Run compiled tests with x64 .NET 8 when Unity supplies a 32-bit Mono host')
    parser.add_argument('--output',type=pathlib.Path,default=pathlib.Path('TestResults/compact'))
    args=parser.parse_args();root=pathlib.Path(__file__).resolve().parents[2]
    args.output.mkdir(parents=True,exist_ok=True)
    sources=[root/'Assets/Scripts/MDPro3/Compact'/name for name in
             ['BoundedResourceCache.cs','CompactStoragePolicy.cs','JpegImageInfo.cs','LimitedWriteStream.cs',
              'ReplayRandom.cs','PrereleasePackFilter.cs','ReadOnlySqlite.cs']]
    sources.append(root/'Tests/Compact/CacheTests.cs')
    exe=(args.output/'CacheTests.exe').resolve()
    mono=args.mono_root/'bin'/('mono.exe' if sys.platform=='win32' else 'mono')
    compiler=args.mono_root/'lib/mono/4.5/csc.exe'
    import shutil
    native=root/'Assets/Plugins/Windows/sqlite3.dll'
    if sys.platform=='win32':shutil.copyfile(native,exe.parent/native.name)
    command=[str(mono),str(compiler),'/nologo','/target:exe','/reference:System.Data.dll',
             '/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',
             '/out:'+str(exe)]+[str(p) for p in sources]
    compile_result=subprocess.run(command,capture_output=True,encoding='utf-8',errors='replace')
    print(compile_result.stdout,compile_result.stderr)
    result={'compile_exit_code':compile_result.returncode,'compile_output':compile_result.stdout+compile_result.stderr}
    if compile_result.returncode==0:
        runtime = args.dotnet or mono
        if args.dotnet:
            exe.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0',
                'framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}),encoding='utf-8')
        with tempfile.TemporaryDirectory(prefix='mdpro3-cache-tests-',dir=str(args.output.resolve())) as tmp:
            run=subprocess.run([str(runtime),str(exe),tmp,str(args.jpeg.resolve()),str(args.database.resolve())],capture_output=True,encoding='utf-8',errors='replace')
        result.update(run_exit_code=run.returncode,run_output=run.stdout+run.stderr)
        print(run.stdout,run.stderr)
    (args.output/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    return result.get('run_exit_code',compile_result.returncode)
if __name__=='__main__':raise SystemExit(main())
