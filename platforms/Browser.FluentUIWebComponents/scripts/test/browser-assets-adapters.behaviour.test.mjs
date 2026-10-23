/**
 * @file Image/font adapter boundary tests using the production TypeScript with
 * DOM and FontFace doubles. Exercises event ordering, four Aspects, request
 * identity, reentrancy, source clearing, failures, release/disposal, font-family
 * tokens, deduplication, fallback and completion-triggered layout invalidation.
 *
 * Requires product npm dependencies only. esbuild bundles run in isolated VMs;
 * no .NET build or site output is involved. Captured callbacks and manually
 * completed promises make races deterministic, but do not verify real decoders,
 * font metrics, native browser events or rendered appearance.
 *
 * @see ../../CONTRIBUTING.md#testing-and-validation
 */
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

async function bundle(name) {
    const source = fileURLToPath(new URL(`../../src/Browser.FluentUIWebComponents/web/${name}.ts`, import.meta.url));
    const result = await build({ entryPoints: [source], bundle: true, write: false, platform: 'node', format: 'cjs' });
    return result.outputFiles[0].text;
}
const imagesCode = await bundle('images');
const fontsCode = await bundle('fonts');

function imagesHarness(options = {}) {
    const completions = [], reports = [], lookups = [], images = [];
    class Image extends EventTarget {
        style = {};
        attributes = new Map();
        parent = undefined;
        captured = new Map();
        addEventListener(name, callback) {
            super.addEventListener(name, callback);
            this.captured.set(name, callback);
        }
        set src(value) {
            assert.ok(this.captured.has('load') && this.captured.has('error'), 'listeners precede src');
            this.attributes.set('src', value);
            options.onSource?.(this);
        }
        removeAttribute(name) { this.attributes.delete(name); }
        remove() {
            if (this.parent) {
                this.parent.children.splice(this.parent.children.indexOf(this), 1);
                this.parent = undefined;
            }
        }
    }
    const element = {
        children: [],
        ownerDocument: { createElement(tag) { assert.equal(tag, 'img'); const image = new Image(); images.push(image); return image; } },
        append(image) { this.children.push(image); image.parent = this; },
    };
    const module = { exports: {} };
    runInNewContext(imagesCode, { module, exports: module.exports });
    const adapter = new module.exports.BrowserImages(
        name => {
            lookups.push(name);
            return options.resolve ? options.resolve(name) : `https://example.test/sub/assets/${name}`;
        },
        (...args) => { completions.push(args); options.complete?.(...args); },
        message => { reports.push(message); options.report?.(message); },
    );
    return { adapter, element, images, completions, reports, lookups };
}

test('images use the exact catalog URL and install load/error before src (including synchronous load)', () => {
    const h = imagesHarness({ resolve: () => 'https://example.test/sub/logo.svg', onSource: image => image.dispatchEvent(new Event('load')) });
    h.adapter.setSource(7, h.element, 'logo.png', 1);
    assert.deepEqual(h.lookups, ['logo.png']);
    assert.equal(h.images[0].attributes.get('src'), 'https://example.test/sub/logo.svg');
    assert.deepEqual(h.completions, [[7, 1, true]]);
    h.images[0].captured.get('error')();
    assert.equal(h.completions.length, 1);
    assert.equal(h.reports.length, 0);
});

test('A -> B -> A, duplicate generations, and captured stale event callbacks cannot complete the new image', () => {
    const h = imagesHarness();
    for (const name of ['a.png', 'b.png', 'a.png']) { h.adapter.setSource(4, h.element, name, 9); }
    h.images[0].captured.get('load')();
    h.images[1].captured.get('error')();
    assert.deepEqual(h.completions, []);
    h.images[2].captured.get('load')();
    h.images[2].captured.get('load')();
    assert.deepEqual(h.completions, [[4, 9, true]]);
    assert.equal(h.element.children.length, 1);
    assert.equal(h.images[0].attributes.has('src'), false);
});

test('all MAUI aspects affect the real img; Fluent minimum sizes and baseline cannot enlarge the frame', () => {
    const h = imagesHarness();
    h.adapter.setAspect(1, 'Center');
    h.adapter.setSource(1, h.element, 'a.png', 1);
    const image = h.images[0];
    assert.equal(image.style.objectFit, 'none');
    for (const [aspect, fit] of [['AspectFit', 'contain'], ['AspectFill', 'cover'], ['Fill', 'fill'], ['Center', 'none']]) {
        h.adapter.setAspect(1, aspect);
        assert.equal(image.style.objectFit, fit);
    }
    assert.equal(image.style.objectPosition, 'center');
    assert.equal(image.style.minWidth, '0');
    assert.equal(image.style.minHeight, '0');
    assert.equal(image.style.display, 'block');
    assert.equal(image.style.width, '100%');
    assert.equal(image.style.height, '100%');
});

test('empty source clears without fetching src="", preserves aspect, and only removes owned children', () => {
    const h = imagesHarness();
    const foreign = {};
    h.element.children.push(foreign);
    h.adapter.setAspect(1, 'AspectFill');
    h.adapter.setSource(1, h.element, 'a.png', 1);
    h.adapter.setSource(1, h.element, '', 2);
    h.images[0].captured.get('load')();
    assert.deepEqual(h.element.children, [foreign]);
    assert.deepEqual(h.lookups, ['a.png']);
    assert.deepEqual(h.completions, []);
    h.adapter.setSource(1, h.element, 'b.png', 3);
    assert.equal(h.images[1].style.objectFit, 'cover');
});

test('missing catalog entry fails once without guessing an SVG alias or a relative URL', () => {
    const h = imagesHarness({ resolve: () => undefined });
    h.adapter.setSource(1, h.element, 'logo.png', 12);
    assert.deepEqual(h.lookups, ['logo.png']);
    assert.deepEqual(h.completions, [[1, 12, false]]);
    assert.match(h.reports[0], /catalog/);
    assert.equal(h.element.children.length, 0);
    assert.equal(h.images[0].attributes.has('src'), false);
});

test('decode/network error removes broken image and reports nonfatally once', () => {
    const h = imagesHarness();
    h.adapter.setSource(1, h.element, 'broken.png', 1);
    h.images[0].captured.get('error')();
    h.images[0].captured.get('load')();
    assert.deepEqual(h.completions, [[1, 1, false]]);
    assert.equal(h.reports.length, 1);
    assert.equal(h.element.children.length, 0);
});

test('release/dispose invalidate queued callbacks and dispose refuses new work', () => {
    const h = imagesHarness();
    h.adapter.setSource(1, h.element, 'a.png', 1);
    h.adapter.release(1);
    h.images[0].captured.get('error')();
    h.adapter.setSource(1, h.element, 'b.png', 2);
    h.adapter.dispose();
    h.adapter.dispose();
    h.images[1].captured.get('load')();
    h.adapter.setSource(1, h.element, 'c.png', 3);
    assert.deepEqual(h.completions, []);
    assert.deepEqual(h.reports, []);
    assert.equal(h.images.length, 2);
    assert.equal(h.element.children.length, 0);
});

test('error reporting and completion may reenter source updates without overwriting their new request', () => {
    const h = imagesHarness({ report: () => h.adapter.setSource(1, h.element, 'new.png', 2) });
    h.adapter.setSource(1, h.element, 'old.png', 1);
    h.images[0].captured.get('error')();
    assert.deepEqual(h.completions, []);
    h.images[1].captured.get('load')();
    assert.deepEqual(h.completions, [[1, 2, true]]);
    const reentrant = imagesHarness({ complete: () => reentrant.adapter.setSource(3, reentrant.element, 'b.png', 8) });
    reentrant.adapter.setSource(3, reentrant.element, 'a.png', 7);
    reentrant.images[0].captured.get('load')();
    assert.equal(reentrant.element.children[0], reentrant.images[1]);
});

function fontsHarness(options = {}) {
    const faces = [], reports = [], lookups = [], layouts = [];
    const hostFace = {};
    const fonts = new Set([hostFace]);
    class Face {
        constructor(family, source) {
            if (options.constructorError) { throw Error('FontFace unavailable'); }
            this.family = family;
            this.source = source;
            this.promise = new Promise((resolve, reject) => {
                this.succeed = () => resolve(this);
                this.fail = () => reject(Error('Invalid font'));
            });
            faces.push(this);
        }
        load() { return this.promise; }
    }
    const module = { exports: {} };
    runInNewContext(fontsCode, { module, exports: module.exports, FontFace: Face, document: { fonts } });
    const adapter = new module.exports.BrowserFonts(name => {
        lookups.push(name);
        return options.resolve ? options.resolve(name) : `https://example.test/sub/${name}`;
    }, () => layouts.push(true), message => reports.push(message));
    return { adapter, faces, fonts, hostFace, reports, lookups, layouts, Type: module.exports.BrowserFonts };
}

const flush = async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); };

test('fonts return a safe stable token immediately, deduplicate, and request layout explicitly on success', async () => {
    const h = fontsHarness();
    const family = h.adapter.getFamily('file with spaces.ttf');
    assert.match(family, /^maui-font-\d+$/);
    assert.equal(h.adapter.getFamily('file with spaces.ttf'), family);
    assert.equal(h.faces.length, 1);
    assert.equal(h.fonts.has(h.faces[0]), false, 'fallback remains until the face is loaded');
    h.faces[0].succeed();
    await flush();
    assert.equal(h.fonts.has(h.faces[0]), true);
    assert.equal(h.layouts.length, 1, 'no global FontFaceSet event is needed');
    assert.deepEqual(h.reports, []);
});

test('failed fonts retain a fallback token, report once, remeasure, and do not retry', async () => {
    const h = fontsHarness();
    const family = h.adapter.getFamily('bad.otf');
    h.faces[0].fail();
    await flush();
    assert.equal(h.adapter.getFamily('bad.otf'), family);
    assert.equal(h.faces.length, 1);
    assert.equal(h.reports.length, 1);
    assert.equal(h.layouts.length, 1);
    assert.deepEqual([...h.fonts], [h.hostFace]);
});

test('missing catalog and FontFace constructor errors are synchronous nonfatal failures with layout', () => {
    for (const options of [{ resolve: () => undefined }, { constructorError: true }]) {
        const h = fontsHarness(options);
        const family = h.adapter.getFamily('absent.ttf');
        assert.match(family, /^maui-font-\d+$/);
        assert.equal(h.adapter.getFamily('absent.ttf'), family);
        assert.equal(h.reports.length, 1);
        assert.equal(h.layouts.length, 1);
        assert.equal(h.faces.length, 0);
    }
});

test('font URL is escaped independently of family/alias; separate applications get distinct CSS tokens', () => {
    const h = fontsHarness({ resolve: () => 'https://example.test/a"\\\n.ttf' });
    const family = h.adapter.getFamily('"unsafe alias"');
    assert.match(family, /^maui-font-\d+$/);
    assert.equal(h.faces[0].source, 'url("https://example.test/a\\22 \\5c \\a .ttf")');
    const second = new h.Type(() => undefined, () => {}, () => {});
    assert.notEqual(second.getFamily('"unsafe alias"'), family);
});

test('dispose removes only owned fonts; late success/error cannot add faces, report, or request layout', async () => {
    const h = fontsHarness();
    h.adapter.getFamily('ready.ttf');
    h.adapter.getFamily('late.ttf');
    h.adapter.getFamily('error.ttf');
    h.faces[0].succeed();
    await flush();
    h.adapter.dispose();
    h.adapter.dispose();
    h.faces[1].succeed();
    h.faces[2].fail();
    await flush();
    assert.deepEqual([...h.fonts], [h.hostFace]);
    assert.equal(h.layouts.length, 1);
    assert.deepEqual(h.reports, []);
    assert.equal(h.adapter.getFamily('after.ttf'), 'sans-serif');
    assert.equal(h.faces.length, 3);
});