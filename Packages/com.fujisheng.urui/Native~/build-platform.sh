#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "$0")" && pwd)
platform=${1:?Usage: build-platform.sh PLATFORM BUILD_DIRECTORY}
build=${2:?Provide a build directory outside the package}
mkdir -p "$build"
build=$(cd "$build" && pwd)
marker="$build/urui-platform.txt"
if [[ -f "$build/CMakeCache.txt" ]] && { [[ ! -f "$marker" ]] || [[ "$(cat "$marker")" != "$platform" ]]; }; then
  echo "Existing CMake cache has no matching URUI platform marker. Use a new build directory for $platform." >&2
  exit 2
fi
urui_cmake=${URUI_CMAKE:-cmake}
args=(-S "$root" -B "$build" -DCMAKE_BUILD_TYPE=Release -DURUI_BUILD_TESTS=OFF -DURUI_DEBUG="${URUI_DEBUG:-OFF}")

case "$platform" in
  macos-arm64|macos-x86_64)
    arch=${platform#macos-}
    args+=(-DCMAKE_OSX_ARCHITECTURES="$arch" -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0)
    plugin="$build/libURUIBridge.dylib"
    ;;
  android-arm64|android-x86_64)
    : "${ANDROID_NDK_ROOT:?Set ANDROID_NDK_ROOT to an installed Android NDK}"
    abi=arm64-v8a
    if [[ "$platform" == android-x86_64 ]]; then abi=x86_64; fi
    args+=(-DCMAKE_TOOLCHAIN_FILE="$ANDROID_NDK_ROOT/build/cmake/android.toolchain.cmake"
      -DANDROID_ABI="$abi" -DANDROID_PLATFORM=android-23 -DANDROID_STL=c++_static
      -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON)
    plugin="$build/libURUIBridge.so"
    ;;
  webgl)
    : "${EMSCRIPTEN:?Set EMSCRIPTEN to the directory containing emcc}"
    args+=(-DCMAKE_TOOLCHAIN_FILE="$EMSCRIPTEN/cmake/Modules/Platform/Emscripten.cmake")
    plugin="$build/libURUIBridge.a"
    ;;
  ios-arm64)
    if ! xcodebuild -license check >/dev/null 2>&1; then
      echo "An installed Xcode with its license accepted manually is required. Set DEVELOPER_DIR if needed." >&2
      exit 1
    fi
    sdk=$(xcrun --sdk iphoneos --show-sdk-path)
    args+=(-DCMAKE_SYSTEM_NAME=iOS -DCMAKE_OSX_SYSROOT="$sdk"
      -DCMAKE_OSX_ARCHITECTURES=arm64 -DCMAKE_OSX_DEPLOYMENT_TARGET=13.0
      -DCMAKE_TRY_COMPILE_TARGET_TYPE=STATIC_LIBRARY)
    plugin="$build/libURUIBridge.a"
    ;;
  windows-x86_64)
    : "${URUI_ZIG:?Set URUI_ZIG to an installed Zig executable}"
    export URUI_ZIG
    export ZIG_GLOBAL_CACHE_DIR="${ZIG_GLOBAL_CACHE_DIR:-$build/zig-global-cache}"
    export ZIG_LOCAL_CACHE_DIR="${ZIG_LOCAL_CACHE_DIR:-$build/zig-local-cache}"
    for mode in cc c++ ar ranlib rc; do
      printf '#!/usr/bin/env bash\nexec "${URUI_ZIG:?}" %s ' "$mode" > "$build/zig-$mode"
      if [[ "$mode" == cc || "$mode" == c++ ]]; then
        printf '%s ' '-target x86_64-windows-gnu' >> "$build/zig-$mode"
      fi
      printf '"$@"\n' >> "$build/zig-$mode"
      chmod +x "$build/zig-$mode"
    done
    cat > "$build/windows-zig.cmake" <<EOF
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)
set(CMAKE_C_COMPILER "$build/zig-cc")
set(CMAKE_CXX_COMPILER "$build/zig-c++")
set(CMAKE_AR "$build/zig-ar")
set(CMAKE_RANLIB "$build/zig-ranlib")
set(CMAKE_RC_COMPILER "$build/zig-rc")
set(CMAKE_TRY_COMPILE_TARGET_TYPE STATIC_LIBRARY)
EOF
    args+=(-DCMAKE_TOOLCHAIN_FILE="$build/windows-zig.cmake")
    plugin="$build/URUIBridge.dll"
    ;;
  *)
    echo "Unknown platform: $platform" >&2
    exit 2
    ;;
esac

"$urui_cmake" "${args[@]}"
printf '%s\n' "$platform" > "$marker"
target=URUIBridge
if [[ "$platform" == webgl || "$platform" == ios-arm64 ]]; then target=URUIBridgePlugin; fi
"$urui_cmake" --build "$build" --target "$target" --config Release --parallel 2
test -f "$plugin"
"${URUI_PYTHON:-python3}" "$root/check-plugin.py" "$platform" "$plugin"
echo "Built $platform plugin: $plugin"
