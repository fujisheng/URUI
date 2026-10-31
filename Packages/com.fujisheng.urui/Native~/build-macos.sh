#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")" && pwd)
if [[ "$(uname -s)" != Darwin || "$(uname -m)" != arm64 ]]; then
  echo "This package validates macOS ARM64 only." >&2; exit 1
fi
build=${1:-"$root/build-macos"}
cmake -S "$root" -B "$build" -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES=arm64 -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0 -DURUI_BUILD_TESTS=ON
cmake --build "$build" --config Release --parallel 2
ctest --test-dir "$build" --output-on-failure
echo "Built: $build/libURUIBridge.dylib"
echo "To install deliberately: cp the built dylib to Runtime/Plugins/macOS/libURUIBridge.dylib in an embedded copy of this package."
