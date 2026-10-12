#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${EMSDK:?Set EMSDK to your Emscripten 3.1.57 installation}"
export EMSDK MSBuildEnableWorkloadResolver=false
source "$EMSDK/emsdk_env.sh"
version=$(emcc --version)
[[ "$version" == *" 3.1.57 ("* ]] || { echo 'Emscripten 3.1.57 is required.' >&2; exit 1; }
mkdir -p artifacts
run=$(date -u +%Y%m%dT%H%M%SZ)
{
    dotnet --version
    emcc --version
    npm ci --ignore-scripts
    dotnet publish samples/BrowserSample/BrowserSample.csproj -c Release -r browser-wasm -p:RestoreLockedMode=true \
        -bl:"artifacts/publish-$run.binlog" -o artifacts/wwwroot "$@" \
        -p:EnableTrimAnalyzer=true -p:EnableAotAnalyzer=true \
        -p:SuppressTrimAnalysisWarnings=false -p:SuppressAotAnalysisWarnings=false -p:TrimmerSingleWarn=false
} 2>&1 | tee "artifacts/publish-$run.log"