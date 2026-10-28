#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release -j
mkdir -p ../Assets/Plugins
find build \( -name 'libRmlUnityBridge.so' -o -name 'libRmlUnityBridge.dylib' \) -exec cp {} ../Assets/Plugins/ \;
