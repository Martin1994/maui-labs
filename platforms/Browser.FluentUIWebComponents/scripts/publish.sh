#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export MSBuildEnableWorkloadResolver=false
# Either use an already initialized toolchain or load the explicitly configured SDK.
if [[ -n "${EMSDK:-}" && -f "$EMSDK/emsdk_env.sh" ]]; then
    source "$EMSDK/emsdk_env.sh"
fi
if ! command -v emcc >/dev/null 2>&1; then
    echo 'Initialize Emscripten 3.1.57, or set EMSDK to its installation directory.' >&2
    exit 1
fi
version=$(emcc --version)
if [[ "$version" != *" 3.1.57 ("* ]]; then
    echo 'Emscripten 3.1.57 is required.' >&2
    exit 1
fi
mkdir -p artifacts
run=$(date -u +%Y%m%dT%H%M%SZ)
{
    dotnet --version
    emcc --version
    npm ci --ignore-scripts
    dotnet publish samples/BrowserSample/BrowserSample.csproj -p:RestoreLockedMode=true \
        -bl:"artifacts/publish-$run.binlog" "$@"
} 2>&1 | tee "artifacts/publish-$run.log"