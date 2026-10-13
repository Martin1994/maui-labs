/** @returns {Promise<number>} */
export function afterNextFrame() {
    return new Promise(resolve => {
        requestAnimationFrame(() => {
            // performance.now() is relative to the browser's navigation time origin,
            // not to when this module, the runtime, or C# Main started executing.
            resolve(performance.now());
        });
    });
}

export function getWasmUrl() {
    return new URL('./dotnet.native.wasm', import.meta.url).href;
}

export function getDownloadedWasmSize() {
    const entries = performance.getEntriesByName(getWasmUrl(), 'resource');
    for (let index = entries.length - 1; index >= 0; index--) {
        const entry = entries[index];
        if (entry instanceof PerformanceResourceTiming && Number.isSafeInteger(entry.decodedBodySize) && entry.decodedBodySize > 0) {
            return entry.decodedBodySize;
        }
    }
    return -1;
}

/** @param {string} url */
export function openExternal(url) {
    const destination = new URL(url);
    if (destination.protocol !== 'https:' && destination.protocol !== 'http:') {
        throw new Error('Only HTTP(S) repository links are supported');
    }
    window.open(destination.href, '_blank', 'noopener,noreferrer');
}