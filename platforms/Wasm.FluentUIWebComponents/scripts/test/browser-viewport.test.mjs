/**
 * @file Layout-scheduler boundary tests for the production DOM bridge, bundled
 * in memory by esbuild and run in isolated VMs. DOM, ResizeObserver, animation
 * frames, FontFaceSet events, theme and input services are controlled doubles.
 * Covers fractional viewport sizes, coalescing, zero-size deferral, font/managed
 * invalidation and stale callbacks across unmount, release, stop and remount.
 *
 * Requires product npm dependencies only, not .NET, generated contracts or a
 * browser. No site is produced. These tests verify scheduling and sizing messages,
 * not browser geometry, real font loading or scrollbar feedback behavior.
 *
 * @see ../../CONTRIBUTING.md#testing-and-validation
 */
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const source = fileURLToPath(new URL('../../src/Wasm.FluentUIWebComponents/web/bridge.ts', import.meta.url));
const bundle = await build({
    entryPoints: [source], bundle: true, write: false, platform: 'node', format: 'cjs',
    plugins: [{
        name: 'viewport-test-boundaries',
        setup(builder) {
            builder.onResolve({ filter: /^\.\/(theme|input|input\.styles)\.js$/ }, args => ({ path: args.path, namespace: 'boundary' }));
            builder.onLoad({ filter: /.*/, namespace: 'boundary' }, () => ({ contents: `
                export class BrowserTheme {
                    getSystemTheme() { return 'Light'; }
                    getColor() { return ''; }
                    start() {}
                    apply() {}
                    dispose() {}
                }
                export class BrowserTextInput { constructor() { throw Error('Unexpected input'); } }
                export function createInputStyles() { throw Error('Unexpected input styles'); }
            ` }));
        },
    }],
});

function harness(t) {
    class Element {
        style = {};
        children = [];
        parent = undefined;
        get clientWidth() { throw Error('Do not read rounded clientWidth'); }
        get clientHeight() { throw Error('Do not read rounded clientHeight'); }
        getBoundingClientRect() { throw Error('Do not read transformed border-box bounds'); }
        append(element) {
            element.remove();
            element.parent = this;
            this.children.push(element);
        }
        remove() {
            if (this.parent) {
                this.parent.children.splice(this.parent.children.indexOf(this), 1);
                this.parent = undefined;
            }
        }
    }
    const viewport = new Element();
    const fonts = new EventTarget();
    const observers = [], frames = new Map(), layouts = [], errors = [];
    let sequence = 0;
    class Observer {
        constructor(callback) { this.callback = callback; observers.push(this); }
        observe(target) { this.target = target; }
        disconnect() { this.target = undefined; }
        // Explicit target allows testing stale deliveries after disconnect.
        emit(width, height, target = viewport, extra = {}) {
            this.callback([{ target, contentRect: { width, height }, ...extra }], this);
        }
    }
    const module = { exports: {} };
    runInNewContext(bundle.outputFiles[0].text, {
        module, exports: module.exports,
        document: { fonts, createElement: () => new Element() },
        HTMLUnknownElement: class {}, ResizeObserver: Observer,
        requestAnimationFrame(callback) { const id = ++sequence; frames.set(id, callback); return id; },
        cancelAnimationFrame(id) { frames.delete(id); },
    }, { filename: source });
    const bridge = new module.exports.DomBrowserBridge(viewport, () => ({
        LayoutTick(width, height) { layouts.push([width, height]); },
    }), message => errors.push(message), { resolve() { return undefined; } });
    t.after(() => { bridge.dispose(); assert.deepEqual(errors, []); });
    const mount = () => {
        const root = bridge.imports.create('div');
        bridge.imports.mount(root);
        return root;
    };
    const flush = () => {
        const pending = [...frames.values()];
        frames.clear();
        for (const callback of pending) { callback(16); }
    };
    return { bridge, viewport, fonts, observers, frames, layouts, mount, flush };
}

test('first layout waits for fractional observer size, never integer fallback', t => {
    const h = harness(t);
    h.mount();
    h.bridge.imports.requestLayout();
    h.fonts.dispatchEvent(new Event('loadingdone'));
    assert.equal(h.frames.size, 0);
    h.flush();
    assert.deepEqual(h.layouts, []);
    h.observers[0].emit(1492.571533203125, 760.0000610351562);
    assert.equal(h.frames.size, 1);
    h.flush();
    assert.deepEqual(h.layouts, [[1492.571533203125, 760.0000610351562]]);
});

test('latest size wins within a frame, including auto-scrollbar viewport changes', t => {
    const h = harness(t);
    h.mount();
    h.observers[0].emit(1477.714, 745.143);
    h.bridge.imports.requestLayout();
    h.fonts.dispatchEvent(new Event('loadingerror'));
    h.observers[0].emit(1492.571, 760.143);
    assert.equal(h.frames.size, 1);
    h.flush();
    assert.deepEqual(h.layouts, [[1492.571, 760.143]]);
    assert.equal(h.frames.size, 0, 'No self-sustaining layout scheduler');
});

test('physical content box is used, not transformed bounds, border box or logical axes', t => {
    const h = harness(t);
    h.mount();
    h.observers[0].emit(601.375, 402.625, h.viewport, {
        contentBoxSize: [{ inlineSize: 402.625, blockSize: 601.375 }],
        borderBoxSize: [{ inlineSize: 430, blockSize: 630 }],
        devicePixelContentBoxSize: [{ inlineSize: 805, blockSize: 1203 }],
    });
    h.flush();
    assert.deepEqual(h.layouts, [[601.375, 402.625]]);
});

test('font and managed invalidations remeasure even at an unchanged observed size', t => {
    const h = harness(t);
    h.mount();
    h.observers[0].emit(320.25, 480.75);
    h.flush();
    for (const event of ['loadingdone', 'loadingerror']) {
        h.fonts.dispatchEvent(new Event(event));
        h.bridge.imports.requestLayout();
        assert.equal(h.frames.size, 1);
        h.flush();
    }
    assert.deepEqual(h.layouts, Array.from({ length: 3 }, () => [320.25, 480.75]));
});

test('zero-sized mount defers layout and resumes when visible dimensions arrive', t => {
    const h = harness(t);
    h.mount();
    for (const [width, height] of [[0, 0], [0, 40.5], [20.5, 0]]) {
        h.observers[0].emit(width, height);
        h.flush();
    }
    assert.deepEqual(h.layouts, []);
    h.observers[0].emit(20.5, 40.5);
    h.flush();
    h.observers[0].emit(0, 0);
    h.flush();
    h.fonts.dispatchEvent(new Event('loadingdone'));
    h.flush();
    assert.deepEqual(h.layouts, [[20.5, 40.5]], 'Hidden mount must not reuse its previous positive size');
    h.observers[0].emit(30.25, 50.75);
    h.flush();
    assert.deepEqual(h.layouts.at(-1), [30.25, 50.75]);
});

test('unrelated or invalid observer entries do not supply initial layout dimensions', t => {
    const h = harness(t);
    h.mount();
    h.observers[0].emit(100, 100, {});
    for (const [width, height] of [[NaN, 50], [50, Infinity], [-1, 50], [50, -1]]) {
        h.observers[0].emit(width, height);
    }
    assert.equal(h.frames.size, 0);
    h.observers[0].emit(100.5, 50.5);
    h.flush();
    assert.deepEqual(h.layouts, [[100.5, 50.5]]);
});

for (const action of ['unmount', 'release', 'stop', 'dispose']) {
    test(`${action} cancels layout and ignores already-delivered observer/frame callbacks`, t => {
        const h = harness(t);
        const root = h.mount();
        const observer = h.observers[0];
        observer.emit(200.5, 300.5);
        const pending = [...h.frames.values()];
        if (action === 'dispose') { h.bridge.dispose(); }
        else { h.bridge.imports[action](root); }
        observer.emit(900, 900);
        for (const callback of pending) { callback(32); }
        h.fonts.dispatchEvent(new Event('loadingdone'));
        h.bridge.imports.requestLayout();
        assert.equal(h.frames.size, 0);
        assert.deepEqual(h.layouts, []);
    });
}

test('remount awaits fresh dimensions and rejects stale callbacks without clearing its pending frame', t => {
    const h = harness(t);
    const root = h.mount();
    const oldObserver = h.observers[0];
    oldObserver.emit(100.5, 200.5);
    const oldFrame = [...h.frames.values()][0];
    h.bridge.imports.unmount(root);
    h.bridge.imports.mount(root);
    h.bridge.imports.requestLayout();
    oldObserver.emit(999, 999);
    assert.equal(h.frames.size, 0);
    h.observers[1].emit(300.25, 400.75);
    const freshFrames = [...h.frames.keys()];
    oldFrame(32);
    h.bridge.imports.requestLayout();
    assert.deepEqual([...h.frames.keys()], freshFrames);
    h.flush();
    assert.deepEqual(h.layouts, [[300.25, 400.75]]);
});