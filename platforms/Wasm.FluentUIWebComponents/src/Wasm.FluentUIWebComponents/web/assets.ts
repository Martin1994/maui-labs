export type BrowserAssetKind = 'images' | 'fonts' | 'raw';

function record(value: unknown, context: string): Record<string, unknown> {
    if (!value || typeof value !== 'object' || Array.isArray(value)) {
        throw new Error(`Invalid MAUI asset catalog ${context}`);
    }
    return value as Record<string, unknown>;
}

function relativePath(value: string): boolean {
    return value.split('/').every(part => part.length > 0 && part !== '.' && part !== '..'
        && !/[\\\u0000-\u001f\u007f<>:"|?*]/.test(part) && !/[. ]$/.test(part));
}

/** Validated, app-scoped logical names. Never falls back to fetching an unknown name. */
export class BrowserAssetCatalog {
    readonly #maps = new Map<BrowserAssetKind, Map<string, string>>();

    constructor(value: unknown, application: string, baseUrl: URL) {
        const catalog = record(value, 'root');
        if (catalog.version !== 1 || catalog.application !== application) {
            throw new Error(`MAUI asset catalog version/application mismatch (expected version 1, '${application}')`);
        }
        for (const kind of ['images', 'fonts', 'raw'] as const) {
            const entries = record(catalog[kind], kind);
            const map = new Map<string, string>();
            for (const [name, path] of Object.entries(entries)) {
                if (!relativePath(name) || (kind !== 'raw' && name.includes('/'))
                    || typeof path !== 'string'
                    || !relativePath(path) || !path.startsWith(`maui-assets/${kind}/`)) {
                    throw new Error(`Invalid MAUI asset catalog ${kind} entry '${name}'`);
                }
                // Build owns collision checks using .NET OrdinalIgnoreCase. JS Unicode
                // case expansion is not equivalent (e.g. sharp S); lookup stays ordinal.
                // Manifest paths are literal filenames, not URL-encoded strings. Encode
                // each segment once so %, #, Unicode and spaces cannot change URL meaning.
                map.set(name, new URL(path.split('/').map(encodeURIComponent).join('/'), baseUrl).href);
            }
            this.#maps.set(kind, map);
        }
    }

    resolve(kind: BrowserAssetKind, name: string): string | undefined {
        return this.#maps.get(kind)?.get(name);
    }
}

export async function loadBrowserAssets(application: string, moduleUrl: string): Promise<BrowserAssetCatalog> {
    const base = new URL('.', moduleUrl);
    const url = new URL('maui-assets.json', base);
    try {
        const response = await fetch(url, { signal: AbortSignal.timeout(30_000) });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }
        return new BrowserAssetCatalog(await response.json(), application.replace(/\.dll$/, ''), base);
    } catch (error) {
        throw new Error(`Unable to load MAUI asset catalog at ${url.href}: ${String(error)}`);
    }
}