"""Package an unsigned .app for the user's existing on-device signing flow; fail on size."""
import argparse,json,pathlib,plistlib,sys,zipfile
LIMIT=500_000_000
def measure(root):return sum(p.stat().st_size for p in root.rglob('*') if p.is_file() and not p.is_symlink())
def main():
    p=argparse.ArgumentParser();p.add_argument('--app',required=True,type=pathlib.Path);p.add_argument('--output',required=True,type=pathlib.Path)
    a=p.parse_args();app=a.app.resolve()
    if app.suffix!='.app' or not (app/'Info.plist').is_file():raise SystemExit('Expected a built iOS .app')
    with (app/'Info.plist').open('rb') as stream:info=plistlib.load(stream)
    executable=info.get('CFBundleExecutable','')
    if not executable or pathlib.Path(executable).name!=executable or not (app/executable).is_file():
        raise SystemExit('Missing application executable')
    if 'iPhoneOS' not in info.get('CFBundleSupportedPlatforms',[]):raise SystemExit('Expected a device build, not a simulator app')
    size=measure(app);a.output.parent.mkdir(parents=True,exist_ok=True)
    report={'installed_app_bytes':size,'base_limit_bytes':LIMIT,'passes':size<=LIMIT,
            'image_cache_budget_bytes':300_000_000,'update_reserve_bytes':100_000_000,
            'user_data_budget_bytes':50_000_000,'operating_target_bytes':900_000_000,
            'note':'IPA ZIP size is not installed size. Device filesystem/OS installation staging is not measured here.'}
    a.output.with_suffix('.storage.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    if size>LIMIT:raise SystemExit(f'Base app {size:,} bytes exceeds {LIMIT:,}; artifact rejected.')
    with zipfile.ZipFile(a.output,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        for f in sorted(app.rglob('*')):
            relative=pathlib.PurePosixPath('Payload')/app.name/f.relative_to(app).as_posix()
            if f.is_symlink():
                import os,stat
                info=zipfile.ZipInfo(str(relative));info.create_system=3;info.external_attr=(stat.S_IFLNK|0o777)<<16
                archive.writestr(info,os.readlink(f))
            elif f.is_file():archive.write(f,str(relative))
    print(json.dumps(report,indent=2))
if __name__=='__main__':main()
