/**
 * Production Picker/Switch adapters with controlled Fluent/DOM boundaries.
 * Tests state, duplicate labels, disabled selection, native-close ordering and
 * disposal. Does not substitute for published native browser interaction tests.
 * Requires product npm dependencies; no .NET or native publishing required.
 */
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

async function bundle(name) {
    const result = await build({
        entryPoints: [fileURLToPath(new URL(`../../src/Browser.FluentUIWebComponents/web/${name}.ts`, import.meta.url))],
        bundle: true, write: false, platform: 'node', format: 'cjs',
        plugins: [{ name: 'fluent-boundary', setup(builder) {
            builder.onResolve({ filter: /^@(?:fluentui|microsoft)\// }, args => ({ path: args.path, namespace: 'boundary' }));
            builder.onLoad({ filter: /.*/, namespace: 'boundary' }, () => ({ contents: `
                export const Dropdown = globalThis.TestElement;
                export const Switch = globalThis.TestElement;
                export const Updates = { process() {} };
            ` }));
        } }],
    });
    return result.outputFiles[0].text;
}
const pickerCode = await bundle('picker');
const switchCode = await bundle('switch');

function harness(code) {
    const tasks = [], callbacks = [], opens = [], focus = [];
    class Element extends EventTarget {
        ownerDocument;
        isConnected = true;
        disabled = false;
        selected = false;
        checked = false;
        options = [];
        children = [];
        attributes = new Map();
        captured = new Map();
        open = false;
        constructor(doc) { super(); this.ownerDocument = doc; }
        append(child) { this.children.push(child); }
        replaceChildren(...children) { this.children = children; }
        remove() { this.isConnected = false; }
        contains(target) { return target === this || this.children.includes(target); }
        setAttribute(name, value) { this.attributes.set(name, value); }
        removeAttribute(name) { this.attributes.delete(name); }
        getAttribute(name) { return this.attributes.get(name) ?? null; }
        matches(selector) { assert.equal(selector, ':popover-open'); return this.open; }
        focus() { this.ownerDocument.activeElement = this; }
        blur() { this.ownerDocument.activeElement = null; }
        showPopover() { this.changeOpen(true); }
        hidePopover() { this.changeOpen(false); }
        changeOpen(open) {
            const event = new Event('beforetoggle', { cancelable: open });
            event.newState = open ? 'open' : 'closed';
            this.dispatchEvent(event);
            if (!event.defaultPrevented) { this.open = open; }
        }
        addEventListener(name, callback, options) {
            super.addEventListener(name, callback, options);
            this.captured.set(name, callback);
        }
    }
    const document = new Element();
    document.createElement = () => new Element(document);
    const element = new Element(document);
    element.shadowRoot = new Element(document);
    element.control = new Element(document);
    const module = { exports: {} };
    runInNewContext(code, { module, exports: module.exports, TestElement: Element, AbortController,
        queueMicrotask: callback => tasks.push(callback) });
    const flush = () => { while (tasks.length) { tasks.shift()(); } };
    return { module, document, element, callbacks, opens, focus, flush };
}

function picker() {
    const h = harness(pickerCode);
    h.adapter = new h.module.exports.BrowserPicker(h.element,
        index => { h.callbacks.push(index); h.onSelection?.(index); },
        open => { h.opens.push(open); h.onOpen?.(open); }, focused => h.focus.push(focused), () => {});
    h.items = (items, index) => {
        h.adapter.clear();
        items.forEach(text => h.adapter.add(text));
        h.adapter.commit(index);
        h.flush();
    };
    h.selected = () => h.element.listbox.options.map(option => option.selected);
    return h;
}

test('Picker preserves duplicate/empty labels and -1 selection without user events', () => {
    const h = picker();
    h.items(['same', 'same', ''], 1);
    assert.deepEqual([...h.selected()], [false, true, false]);
    assert.deepEqual([...h.element.listbox.options.map(option => option.value)], ['0', '1', '2']);
    h.adapter.select(-1);
    h.flush();
    assert.deepEqual([...h.selected()], [false, false, false]);
    h.items([], -1);
    assert.equal(h.element.listbox.options.length, 0);
    assert.deepEqual(h.callbacks, []);
});

test('Picker retains and updates selection while disabled and applies it after reconnect', () => {
    const h = picker();
    h.items(['one', 'two'], 0);
    h.adapter.setEnabled(false);
    h.adapter.select(1);
    h.flush();
    assert.deepEqual([...h.selected()], [false, true]);
    h.element.dispatchEvent(new Event('change'));
    assert.deepEqual(h.callbacks, []);
    h.element.isConnected = false;
    h.adapter.select(0);
    h.flush();
    h.element.isConnected = true;
    h.adapter.setEnabled(true);
    h.adapter.flush();
    assert.deepEqual([...h.selected()], [true, false]);
});

test('Picker native close wins over selection flush queued before the toggle event', () => {
    const h = picker();
    h.items(['one', 'two'], 0);
    const listbox = h.element.listbox;
    listbox.showPopover();
    h.onSelection = index => h.adapter.select(index);
    listbox.options[0].selected = false;
    listbox.options[1].selected = true;
    h.element.dispatchEvent(new Event('change'));
    listbox.hidePopover();
    h.flush();
    assert.equal(listbox.open, false);
    assert.deepEqual(h.opens, [true, false]);
    assert.deepEqual(h.callbacks, [1]);
});

test('Picker reentrant selection is canonical; disposal cancels listeners and pending work', () => {
    const h = picker();
    h.items(['one', 'two'], 0);
    h.onSelection = () => h.adapter.select(0);
    h.element.listbox.options[0].selected = false;
    h.element.listbox.options[1].selected = true;
    h.element.dispatchEvent(new Event('change'));
    h.flush();
    assert.deepEqual([...h.selected()], [true, false]);
    h.adapter.select(1);
    h.adapter.dispose();
    h.element.captured.get('change')({ target: h.element });
    h.flush();
    assert.deepEqual(h.callbacks, [1]);
});

test('Picker reports a pending open canceled by disabling and focuses programmatic opens', () => {
    const h = picker();
    h.items(['one'], 0);
    h.adapter.setOpen(true);
    h.adapter.setEnabled(false);
    h.flush();
    assert.deepEqual(h.opens, [false]);
    assert.equal(h.element.listbox.open, false);
    h.adapter.setEnabled(true);
    h.adapter.setOpen(true);
    h.flush();
    assert.equal(h.document.activeElement, h.element.control);
    assert.equal(h.element.listbox.open, true);
});

test('Picker cancels opening if the managed Opened callback disposes it', () => {
    const h = picker();
    h.items(['one'], 0);
    const listbox = h.element.listbox;
    h.onOpen = open => { if (open) { h.adapter.dispose(); } };
    h.adapter.setOpen(true);
    h.flush();
    assert.equal(listbox.open, false);
    assert.equal(listbox.isConnected, false);
});

test('Switch uses final change state, not click or input; managed writes do not echo', () => {
    const h = harness(switchCode);
    const adapter = new h.module.exports.BrowserSwitch(h.element, value => h.callbacks.push(value), value => h.focus.push(value));
    const change = () => {
        h.element.checked = !h.element.checked; // Fluent updates state before change.
        h.element.dispatchEvent(new Event('change'));
        h.element.dispatchEvent(new Event('input'));
    };
    h.element.dispatchEvent(new Event('click'));
    assert.deepEqual(h.callbacks, []);
    change();
    change();
    assert.deepEqual(h.callbacks, [true, false]);
    h.element.checked = true;
    assert.deepEqual(h.callbacks, [true, false]);
    h.element.disabled = true;
    change();
    h.element.disabled = false;
    assert.equal(adapter.focus(true), true);
    assert.equal(adapter.focus(false), false);
    h.element.dispatchEvent(new Event('focus'));
    h.element.dispatchEvent(new Event('blur'));
    assert.deepEqual(h.focus, [true, false]);
    adapter.dispose();
    change();
    h.element.captured.get('change')({ target: h.element });
    h.element.captured.get('focus')();
    assert.equal(adapter.focus(true), false);
    assert.deepEqual(h.callbacks, [true, false]);
    assert.deepEqual(h.focus, [true, false]);
});