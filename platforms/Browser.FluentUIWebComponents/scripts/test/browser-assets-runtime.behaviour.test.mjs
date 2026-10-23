/**
 * @file Asset-catalog and bootstrap boundary tests. Executes the production
 * resolver/bootstrap through in-memory esbuild bundles, with bridge, Fluent,
 * contract exports, runtime builder and fetch replaced by explicit doubles.
 * Covers module-relative URLs, literal filename encoding, Unicode, malformed
 * catalogs, catalog-before-Main ordering, empty apps, failure/retry and disposal.
 *
 * Requires product npm dependencies only; no generated contract, .NET build,
 * HTTP server or browser. The fixed module URL is a fixture: these tests do not
 * verify an actual deployment, NativeAOT startup or resource decoding.
 *
 * @see ../../CONTRIBUTING.md#testing-and-validation
 */
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const web = new URL('../../src/Browser.FluentUIWebComponents/web/', import.meta.url);
const bundle = async (name, plugins = []) => (await build({
    entryPoints: [fileURLToPath(new URL(name, web))], bundle: true, write: false,
    platform: 'node', format: 'cjs', plugins,
    define: { 'import.meta.url': JSON.stringify('https://example.test/nested/app/maui-browser.js') },
})).outputFiles[0].text;
const assetsCode = await bundle('assets.ts');
const bootstrapCode = await bundle('maui-browser.ts', [{
    name: 'host-boundaries',
    setup(builder) {
        builder.onResolve({ filter: /^\.\/(bridge|fluent|contracts)\.js$/ }, args => ({ path: args.path, namespace: 'boundary' }));
        builder.onLoad({ filter: /.*/, namespace: 'boundary' }, () => ({ contents: `
            export class DomBrowserBridge {
                imports = {};
                constructor(element, getApi, onError, assets) { stats.bridges++; stats.assets = assets; }
                dispose() { stats.disposed++; }
            }
            export async function initializeFluent() { stats.fluent++; }
            export const BrowserModuleName = 'maui-browser';
            export const BrowserApplicationAssemblyName = 'Backend.dll';
            export const resolveBrowserExports = x => x;
        ` }));
    },
}]);

const empty = () => ({ version: 1, application: 'App', images: {}, fonts: {}, raw: {} });
function evaluate(code, extra = {}) {
    const module = { exports: {} };
    runInNewContext(code, { module, exports: module.exports, URL, AbortSignal, console, ...extra });
    return module.exports;
}

test('catalog resolves literal names under module directory, including SVG aliases and URL metacharacters', () => {
    const { BrowserAssetCatalog } = evaluate(assetsCode);
    const value = empty();
    value.images = { 'logo.png': 'maui-assets/images/logo.svg', 'logo.svg': 'maui-assets/images/logo.svg' };
    value.raw = { '配置/a #%25.txt': 'maui-assets/raw/配置/a #%25.txt' };
    const catalog = new BrowserAssetCatalog(value, 'App', new URL('https://example.test/nested/app/'));
    assert.equal(catalog.resolve('images', 'logo.png'), 'https://example.test/nested/app/maui-assets/images/logo.svg');
    assert.equal(catalog.resolve('images', 'logo.svg'), catalog.resolve('images', 'logo.png'));
    assert.equal(catalog.resolve('images', 'LOGO.png'), undefined, 'Lookup is ordinal, not case folded');
    assert.equal(catalog.resolve('images', '../file.png'), undefined);
    assert.equal(catalog.resolve('raw', '配置/a #%25.txt'), 'https://example.test/nested/app/maui-assets/raw/%E9%85%8D%E7%BD%AE/a%20%23%2525.txt');
});

test('catalog rejects incompatible schema and unsafe or malformed maps', () => {
    const { BrowserAssetCatalog } = evaluate(assetsCode);
    const bad = [null, [], {}, { ...empty(), version: 2 }, { ...empty(), application: 'Other' },
        { ...empty(), images: [] }, { ...empty(), fonts: null }];
    for (const path of ['../x', '/maui-assets/images/a.png', 'https://evil.test/x', 'maui-assets/raw/x',
        'maui-assets/images/../x', 'maui-assets/images/a\\b.png', 'maui-assets/images//x']) {
        bad.push({ ...empty(), images: { 'x.png': path } });
    }
    for (const name of ['', '../x', '/x', 'folder/x.png', 'x?x.png']) {
        bad.push({ ...empty(), images: { [name]: 'maui-assets/images/x.png' } });
    }
    for (const value of bad) {
        assert.throws(() => new BrowserAssetCatalog(value, 'App', new URL('https://example.test/')), /catalog/);
    }
});

test('catalog preserves Unicode names without JavaScript case expansion', () => {
    const { BrowserAssetCatalog } = evaluate(assetsCode);
    const catalog = new BrowserAssetCatalog({ ...empty(), fonts: {
        'straße.ttf': 'maui-assets/fonts/straße.ttf',
        'strasse.ttf': 'maui-assets/fonts/strasse.ttf',
    } }, 'App', new URL('https://example.test/'));
    assert.notEqual(catalog.resolve('fonts', 'straße.ttf'), catalog.resolve('fonts', 'strasse.ttf'));
});

function startup(fetch) {
    const stats = { bridges: 0, disposed: 0, fluent: 0, mains: 0, shutdown: 0, requests: [], events: [], errors: [] };
    const element = { dataset: {}, dispatchEvent(event) { stats.events.push(event); } };
    const api = evaluate(bootstrapCode, {
        stats, fetch: async url => { stats.requests.push(url.href); return fetch(); },
        document: { getElementById: () => element },
        CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } },
        console: { error(message) { stats.errors.push(message); } },
    });
    const builder = {
        withMainAssembly() { return this; },
        async create() { return {
            setModuleImports() {},
            async getAssemblyExports() { return { Shutdown() { stats.shutdown++; } }; },
            async runMain() { assert.ok(stats.assets); stats.mains++; return 0; },
        }; },
    };
    return { ...api, stats, element, builder };
}

test('bootstrap requires catalog before Main, supports empty apps and releases ownership on disposal', async () => {
    const h = startup(() => ({ ok: true, json: async () => empty() }));
    const handle = await h.startMaui(h.builder, 'App.dll');
    assert.deepEqual(h.stats.requests, ['https://example.test/nested/app/maui-assets.json']);
    assert.equal(h.stats.mains, 1);
    assert.equal(h.element.dataset.state, 'ready');
    await assert.rejects(h.startMaui(h.builder, 'App'), /already/);
    handle.dispose(); handle.dispose();
    assert.equal(h.stats.shutdown, 1);
    assert.equal(h.stats.disposed, 1);
    (await h.startMaui(h.builder, 'App')).dispose();
    assert.equal(h.stats.shutdown, 2);
});

for (const [name, response] of [
    ['missing', () => ({ ok: false, status: 404 })],
    ['invalid schema', () => ({ ok: true, json: async () => ({ ...empty(), version: 2 }) })],
    ['SPA fallback', () => ({ ok: true, json: async () => { throw new SyntaxError('Unexpected HTML'); } })],
    ['network', () => { throw new TypeError('Failed to fetch'); }],
]) {
    test(`bootstrap ${name} catalog fails before managed startup and permits retry`, async () => {
        let failed = true;
        const h = startup(() => failed ? response() : { ok: true, json: async () => empty() });
        await assert.rejects(h.startMaui(h.builder, 'App'), /asset catalog/);
        assert.equal(h.stats.mains, 0);
        assert.equal(h.stats.bridges, 0);
        assert.equal(h.element.dataset.state, 'failed');
        assert.equal(h.stats.events[0].type, 'maui-error');
        failed = false;
        (await h.startMaui(h.builder, 'App')).dispose();
    });
}