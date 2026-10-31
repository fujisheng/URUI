#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release "$@"
cmake --build build --config Release -j
if [[ "$(uname -s)" == Darwin ]]; then
  dest=../Assets/Plugins/macOS
  library=libRmlUnityBridge.dylib
else
  dest=../Assets/Plugins/x86_64
  library=libRmlUnityBridge.so
fi
mkdir -p "$dest"
cp "build/$library" "$dest/$library"
