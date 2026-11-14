cmake_minimum_required(VERSION 3.20)
project(MDPro3Native LANGUAGES C CXX)
set(CMAKE_POLICY_VERSION_MINIMUM 3.5)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(YGO "${CMAKE_CURRENT_SOURCE_DIR}/../Tools/YGO Classes")
set(CORE "${CMAKE_CURRENT_SOURCE_DIR}/vendor/ygopro-core")
if(NOT EXISTS "${CORE}/ocgapi.cpp")
  message(FATAL_ERROR "Run python Tools/Compact/prepare_native.py first; native source must be pinned.")
endif()
set(EVENT__DISABLE_OPENSSL ON CACHE BOOL "" FORCE)
set(EVENT__DISABLE_TESTS ON CACHE BOOL "" FORCE)
set(EVENT__DISABLE_REGRESS ON CACHE BOOL "" FORCE)
set(EVENT__DISABLE_BENCHMARK ON CACHE BOOL "" FORCE)
set(EVENT__DISABLE_SAMPLES ON CACHE BOOL "" FORCE)
set(EVENT__LIBRARY_TYPE STATIC CACHE STRING "" FORCE)
add_subdirectory(vendor/libevent EXCLUDE_FROM_ALL)
file(GLOB CORE_CPP CONFIGURE_DEPENDS "${CORE}/*.cpp")
file(GLOB LUA_C CONFIGURE_DEPENDS "${YGO}/lua/src/*.c")
list(FILTER LUA_C EXCLUDE REGEX "/lua(c)?\\.c$")
# This core includes the Lua headers as C++ and expects the same linkage as upstream premake.
set_source_files_properties(${LUA_C} PROPERTIES LANGUAGE CXX)
set(SERVER_CPP data_manager.cpp deck_manager.cpp game.cpp gframe.cpp netserver.cpp replay.cpp single_duel.cpp tag_duel.cpp)
list(TRANSFORM SERVER_CPP PREPEND "${YGO}/ygoserver/")
set(IRR_FILES os.cpp CAttributes.cpp CFileList.cpp CFileSystem.cpp CLimitReadFile.cpp CMemoryFile.cpp
  CReadFile.cpp CWriteFile.cpp CXMLReader.cpp CXMLWriter.cpp CZipReader.cpp)
list(TRANSFORM IRR_FILES PREPEND "${YGO}/irrlicht/source/Irrlicht/")
file(GLOB LZMA_C "${YGO}/ygoserver/lzma/*.c")
add_library(mdpro3_runtime STATIC ${CORE_CPP} ${LUA_C} ${SERVER_CPP} ${IRR_FILES} ${LZMA_C}
  "${YGO}/sqlite3/sqlite3.c" "${YGO}/ygoserver/spmemvfs/spmemvfs.c" server_bridge.cpp)
set_source_files_properties("${YGO}/ygoserver/gframe.cpp" PROPERTIES COMPILE_DEFINITIONS "main=mdpro3_server_main")
target_compile_definitions(mdpro3_runtime PRIVATE YGOPRO_SERVER_MODE SERVER_ZIP_SUPPORT SERVER_PRO2_SUPPORT
  SERVER_PRO3_SUPPORT SERVER_TAG_SURRENDER_CONFIRM _IRR_STATIC_LIB_ LUA_COMPAT_5_2
  NO_IRR_COMPILE_WITH_ZIP_ENCRYPTION_ NO_IRR_COMPILE_WITH_BZIP2_
  NO__IRR_COMPILE_WITH_MOUNT_ARCHIVE_LOADER_ NO__IRR_COMPILE_WITH_PAK_ARCHIVE_LOADER_
  NO__IRR_COMPILE_WITH_NPK_ARCHIVE_LOADER_ NO__IRR_COMPILE_WITH_TAR_ARCHIVE_LOADER_
  NO__IRR_COMPILE_WITH_WAD_ARCHIVE_LOADER_)
if(CMAKE_SYSTEM_NAME STREQUAL "iOS")
  # Lua's ISO fallback calls system(), which is unavailable on iOS.
  set_property(SOURCE "${YGO}/lua/src/loslib.c" APPEND PROPERTY COMPILE_DEFINITIONS LUA_USE_IOS)
endif()
target_include_directories(mdpro3_runtime PRIVATE "${CORE}" "${YGO}/lua/src" "${YGO}/sqlite3"
  "${YGO}/ygoserver" "${YGO}/ygoserver/spmemvfs" "${YGO}/irrlicht/include"
  "${YGO}/irrlicht/source/Irrlicht")
target_link_libraries(mdpro3_runtime PRIVATE event_core_static)
if(WIN32)
  target_compile_definitions(mdpro3_runtime PRIVATE _IRR_WCHAR_FILESYSTEM)
  file(GLOB ZLIB_C "${YGO}/irrlicht/source/Irrlicht/zlib/*.c")
  target_sources(mdpro3_runtime PRIVATE ${ZLIB_C})
  target_link_libraries(mdpro3_runtime PRIVATE ws2_32 shell32 iphlpapi)
else()
  target_compile_definitions(mdpro3_runtime PRIVATE NO_IRR_USE_NON_SYSTEM_ZLIB_)
  target_link_libraries(mdpro3_runtime PRIVATE event_pthreads_static z)
endif()
option(MDPRO3_BUILD_SMOKE "Link and run the native server lifecycle smoke test" OFF)
if(MDPRO3_BUILD_SMOKE)
  add_executable(mdpro3_native_smoke smoke.cpp)
  target_link_libraries(mdpro3_native_smoke PRIVATE mdpro3_runtime)
endif()
if(APPLE)
  target_link_libraries(mdpro3_runtime PRIVATE "-framework CoreFoundation")
  # Merge each dependency exactly once; ocgcore and ygoserver share one native core.
  add_custom_command(TARGET mdpro3_runtime POST_BUILD COMMAND xcrun libtool -static
    -o "${CMAKE_CURRENT_BINARY_DIR}/libMDPro3Native.a" "$<TARGET_FILE:mdpro3_runtime>"
    "$<TARGET_FILE:event_core_static>" "$<TARGET_FILE:event_pthreads_static>" VERBATIM)
endif()
