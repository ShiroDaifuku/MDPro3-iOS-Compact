Native build inputs are pinned by `Tools/Compact/prepare_native.py`. The portable core is compiled once with Lua, SQLite, the legacy host, and Irrlicht's archive reader. This is not a renderer build.

On a macOS runner with Xcode and CMake:

```sh
python3 Tools/Compact/prepare_native.py
cmake -S Native -B Native/build-ios -DCMAKE_SYSTEM_NAME=iOS -DCMAKE_OSX_SYSROOT=iphoneos -DCMAKE_OSX_ARCHITECTURES=arm64 -DCMAKE_OSX_DEPLOYMENT_TARGET=15.0 -DCMAKE_BUILD_TYPE=Release
cmake --build Native/build-ios --parallel 3
mkdir -p Assets/Plugins/iOS
cp Native/build-ios/libMDPro3Native.a Assets/Plugins/iOS/
xcrun nm -gU Assets/Plugins/iOS/libMDPro3Native.a
```

The build must export `start_server`, `stop_server`, `mdpro3_server_ready`, `create_duel_v2`, `sqlite3_open_v2`, and core callback registration functions. A successful static archive is not proof of duel/protocol compatibility. The GitHub workflow must also link the Unity Xcode project and run device tests before a release is considered playable.
