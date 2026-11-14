"""Fetch pinned portable core/libevent and patch the old host for the unified iOS library."""
import hashlib,json,pathlib,subprocess,urllib.request,zipfile,io,sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT=pathlib.Path(__file__).resolve().parents[2]
SOURCES={
    'ygopro-core':('Fluorohydride/ygopro-core','1f7769cd40dbb13fc6ec33de7ac99757425e1f22'),
    'libevent':('libevent/libevent','5df3037d10556bfcb675bc73e516978b75fc7bc7'),
}
def fetch(name,repo,revision):
    target=ROOT/'Native/vendor'/name
    stamp=target/'.source-revision'
    if stamp.exists() and stamp.read_text()==revision:return
    if target.exists():raise RuntimeError('Refusing to overwrite a different native source checkout: '+str(target))
    url='https://codeload.github.com/'+repo+'/zip/'+revision
    with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'MDPro3-iOS-build'}),timeout=120) as r:data=r.read()
    target.mkdir(parents=True)
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for entry in archive.infolist():
            relative=pathlib.PurePosixPath(entry.filename)
            if len(relative.parts)<2:continue
            relative=pathlib.Path(*relative.parts[1:]);output=(target/relative).resolve()
            if not output.is_relative_to(target.resolve()):raise RuntimeError('Unsafe upstream archive entry')
            if entry.is_dir():output.mkdir(parents=True,exist_ok=True)
            else:output.parent.mkdir(parents=True,exist_ok=True);output.write_bytes(archive.read(entry))
    stamp.write_text(revision)
    (target/'.archive-sha256').write_text(hashlib.sha256(data).hexdigest())
    print(name,revision,len(data),'downloaded bytes')
def replace(path,before,after):
    text=path.read_text(encoding='utf-8-sig')
    if after in text:return
    if text.count(before)!=1:raise RuntimeError('Unexpected native source at '+str(path))
    path.write_text(text.replace(before,after),encoding='utf-8',newline='\n')
def main():
    for name,(repo,revision) in SOURCES.items():fetch(name,repo,revision)
    ygo=ROOT/'Tools/YGO Classes'
    replace(ygo/'ygoserver/game.cpp','const unsigned short PRO_VERSION = 0x1361;',
            'const unsigned short PRO_VERSION = 0x1362;')
    replace(ygo/'ygoserver/game.cpp','while(NetServer::net_evbase) {',
            'while(NetServer::net_evbase) {\n        // Stop requests are serviced by the libevent thread, never by this polling thread.')
    replace(ygo/'ygoserver/game.cpp','#include <thread>',
            '#include <thread>\nextern "C" bool mdpro3_stop_requested();')
    replace(ygo/'ygoserver/game.cpp','"data/script.zip"','"Data/script.zip"')
    replace(ygo/'ygoserver/data_manager.cpp','cd.set_setcode(setcode);','write_setcode(cd.setcode, setcode);')
    for name in ['single_duel.cpp', 'tag_duel.cpp']:
        replace(ygo/'ygoserver'/name,'mt19937 rnd((uint_fast32_t)seed);','mtrandom rnd((uint_fast32_t)seed);')
    replace(ygo/'ygoserver/config.h','#ifdef _MSC_VER\n#define mywcsncasecmp',
            '#ifdef _WIN32\n#define mywcsncasecmp')
    replace(ygo/'ygoserver/netserver.cpp','#include <thread>',
            '#include <thread>\nextern "C" bool mdpro3_stop_requested();')
    replace(ygo/'ygoserver/netserver.cpp','\tevent_base_dispatch(net_evbase);',
            '\tauto stopTimer = event_new(net_evbase, -1, EV_PERSIST, [](evutil_socket_t, short, void*) {\n'
            '\t\tif (mdpro3_stop_requested()) NetServer::StopServer();\n'
            '\t}, nullptr);\n\ttimeval interval = {0, 100000};\n'
            '\tif (stopTimer) event_add(stopTimer, &interval);\n'
            '\tevent_base_dispatch(net_evbase);\n\tif (stopTimer) event_free(stopTimer);')
    # Sibling includes in the old server must point at the same core used by the static target.
    for path in (ygo/'ygoserver').glob('*'):
        if path.suffix not in ['.h','.cpp']:continue
        text=path.read_text(encoding='utf-8-sig')
        import re
        updated=re.sub(r'#include "\.\./ocgcore/([^\"]+)"',r'#include "\1"',text)
        if updated!=text:path.write_text(updated,encoding='utf-8',newline='\n')
    print('Native source prepared. Protocol is aligned; wire/duel compatibility still requires runtime tests.')
if __name__=='__main__':main()
