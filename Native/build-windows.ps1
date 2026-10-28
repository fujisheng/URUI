$ErrorActionPreference = "Stop"
cmake -S $PSScriptRoot -B "$PSScriptRoot/build" -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build "$PSScriptRoot/build" --config Release
$dest = Join-Path $PSScriptRoot "../Assets/Plugins/x86_64"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "$PSScriptRoot/build/Release/RmlUnityBridge.dll" $dest -Force
Write-Host "Copied RmlUnityBridge.dll to Unity Plugins."
