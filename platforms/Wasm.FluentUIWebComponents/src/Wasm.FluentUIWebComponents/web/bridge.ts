import type { BrowserApplicationExports, BrowserBridge, BrowserImports, BrowserPropertyValue } from './contracts.js';
import { BrowserTheme } from './theme.js';

// DOM nodes and listeners stay JS-owned; MAUI computes container frames.
export class DomBrowserBridge implements BrowserBridge {
    readonly imports: BrowserImports;

    readonly #viewport: HTMLElement;
    readonly #getApi: () => BrowserApplicationExports;
    readonly #onError: (message: string) => void;
    readonly #theme: BrowserTheme;
    readonly #elements = new Map<number, HTMLElement>();
    readonly #clicks = new Map<number, EventListener>();
    readonly #scrolls = new Map<number, EventListener>();
    readonly #timers = new Map<number, number>();
    #sequence = 0;
    #observer: ResizeObserver | undefined;
    #mountedId = 0;
    #frameId = 0;
    #stopped = false;

    constructor(
        viewport: HTMLElement,
        getApi: () => BrowserApplicationExports,
        onError: (message: string) => void,
    ) {
        this.#viewport = viewport;
        this.#getApi = getApi;
        this.#onError = onError;
        this.#theme = new BrowserTheme(viewport, theme => {
            this.#invoke(() => {
            this.#getApi().OnSystemThemeChanged(theme);
            });
        });

        // Interop may invoke these as detached functions. Bind once, and expose
        // only the import contract rather than the class instance/prototype.
        this.imports = {
            report: this.#report.bind(this),
            getSystemTheme: this.#theme.getSystemTheme.bind(this.#theme),
            getThemeColor: this.#theme.getColor.bind(this.#theme),
            startThemeTracking: this.#theme.start.bind(this.#theme),
            applyTheme: this.#theme.apply.bind(this.#theme),
            create: this.#create.bind(this),
            style: this.#style.bind(this),
            property: this.#property.bind(this),
            attribute: this.#attribute.bind(this),
            frame: this.#frame.bind(this),
            measure: this.#measure.bind(this),
            insert: this.#insert.bind(this),
            remove: this.#remove.bind(this),
            release: this.#release.bind(this),
            mount: this.#mount.bind(this),
            unmount: this.#unmount.bind(this),
            subscribeClick: this.#subscribeClick.bind(this),
            unsubscribeClick: this.#unsubscribeClick.bind(this),
            subscribeScroll: this.#subscribeScroll.bind(this),
            requestLayout: this.#requestLayout,
            schedule: this.#schedule.bind(this),
            cancelSchedule: this.#cancelSchedule.bind(this),
            stop: this.#stop.bind(this),
        };
    }

    #get(id: number): HTMLElement {
        if (!Number.isInteger(id) || id <= 0 || id > 0x7fffffff) {
            throw new Error(`Invalid MAUI element handle: ${id}`);
        }
        const element = this.#elements.get(id);
        if (!element) {
            throw new Error(`Stale MAUI element handle: ${id}`);
        }
        return element;
    }

    #invoke(action: () => void): void {
        try {
            action();
        } catch (error) {
            this.#onError(error instanceof Error ? error.message : String(error));
        }
    }

    // Also passed directly to ResizeObserver, so retain the instance receiver.
    readonly #requestLayout = (): void => {
        if (this.#stopped || !this.#mountedId || this.#frameId) {
            return;
        }
        this.#frameId = requestAnimationFrame(() => {
            this.#frameId = 0;
            if (this.#stopped || !this.#mountedId) {
                return;
            }
            this.#invoke(() => {
                this.#getApi().LayoutTick(this.#viewport.clientWidth, this.#viewport.clientHeight);
            });
        });
    };

    #unmount(id: number): void {
        if (this.#mountedId !== id || !this.#mountedId) {
            return;
        }
        this.#observer?.disconnect();
        this.#observer = undefined;
        cancelAnimationFrame(this.#frameId);
        this.#frameId = 0;
        this.#mountedId = 0;
        this.#get(id).remove();
    }

    #unsubscribeClick(id: number): void {
        const element = this.#get(id);
        const callback = this.#clicks.get(id);
        if (callback) {
            element.removeEventListener('click', callback);
        }
        this.#clicks.delete(id);
    }

    #stop(): void {
        this.#stopped = true;
        this.#theme.dispose();
        if (this.#mountedId) {
            this.#unmount(this.#mountedId);
        }
        for (const timer of this.#timers.values()) {
            clearTimeout(timer);
        }
        this.#timers.clear();
    }

    #report(message: string): void {
        this.#onError(message);
    }

    #create(tagName: string): number {
        if (this.#stopped) {
            throw new Error('MAUI bridge is stopped');
        }
        if (tagName !== tagName.toLowerCase()) {
            throw new Error(`Expected a lowercase HTML tag name: ${tagName}`);
        }
        // Do not silently create an unupgraded custom element: setting its
        // properties before registration can shadow the component's setters.
        if (tagName.includes('-') && !customElements.get(tagName)) {
            throw new Error(`Custom element is not registered: ${tagName}`);
        }
        if (this.#sequence >= 0x7fffffff) {
            throw new Error('MAUI element handle limit reached');
        }
        const element = document.createElement(tagName);
        if (element instanceof HTMLUnknownElement) {
            throw new Error(`Unknown HTML element: ${tagName}`);
        }
        const id = ++this.#sequence;
        // These are the common MAUI layout contract, not control defaults.
        element.style.position = 'absolute';
        element.style.boxSizing = 'border-box';
        this.#elements.set(id, element);
        return id;
    }

    #style(id: number, name: string, value: string): void {
        this.#get(id).style.setProperty(name, value);
    }

    #property(id: number, name: string, value: BrowserPropertyValue): void {
        const element = this.#get(id);
        // This is a trusted Handler API, not a sandbox. Catch misspelled or
        // unsupported properties instead of silently creating expando fields.
        if (name === '__proto__' || name === 'prototype' || name === 'constructor' || !(name in element)) {
            throw new Error(`Unsupported element property: ${name}`);
        }
        if (!Reflect.set(element, name, value)) {
            throw new Error(`Element property is not writable: ${name}`);
        }
    }

    #attribute(id: number, name: string, value: string | null): void {
        const element = this.#get(id);
        if (value === null) {
            element.removeAttribute(name);
        } else {
            element.setAttribute(name, value);
        }
    }

    #frame(id: number, x: number, y: number, width: number, height: number): void {
        if (![x, y, width, height].every(Number.isFinite)) {
            throw new Error('Non-finite MAUI frame');
        }
        Object.assign(this.#get(id).style, {
            left: `${x}px`,
            top: `${y}px`,
            width: `${width}px`,
            height: `${height}px`,
        });
    }

    #measure(id: number, width: number, height: number, returnHeight: boolean): number {
        const element = this.#get(id);
        const saved = element.style.cssText;
        try {
            Object.assign(element.style, {
                width: 'max-content',
                height: 'auto',
                maxWidth: Number.isFinite(width) ? `${Math.max(0, width)}px` : 'none',
                maxHeight: 'none',
            });
            const bounds = element.getBoundingClientRect();
            return returnHeight ? bounds.height : bounds.width;
        } finally {
            element.style.cssText = saved;
        }
    }

    #insert(parent: number, child: number, index: number): void {
        const container = this.#get(parent);
        const element = this.#get(child);
        element.remove();
        container.insertBefore(element, container.children[index] ?? null);
    }

    #remove(id: number): void {
        this.#get(id).remove();
    }

    #release(id: number): void {
        const element = this.#get(id);
        this.#unmount(id);
        this.#unsubscribeClick(id);
        const onScroll = this.#scrolls.get(id);
        if (onScroll) {
            element.removeEventListener('scroll', onScroll);
        }
        this.#scrolls.delete(id);
        this.#elements.delete(id);
        element.remove();
    }

    #mount(id: number): void {
        if (this.#stopped) {
            throw new Error('MAUI bridge is stopped');
        }
        if (this.#mountedId) {
            throw new Error('MAUI bridge supports one mounted window');
        }
        this.#viewport.append(this.#get(id));
        this.#mountedId = id;
        this.#observer = new ResizeObserver(this.#requestLayout);
        this.#observer.observe(this.#viewport);
        this.#requestLayout();
    }

    #subscribeClick(id: number): void {
        if (this.#stopped) {
            throw new Error('MAUI bridge is stopped');
        }
        if (this.#clicks.has(id)) {
            throw new Error('Duplicate MAUI button subscription');
        }
        const element = this.#get(id);
        const callback = (): void => {
            if (this.#stopped || !this.#elements.has(id)) {
                return;
            }
            this.#invoke(() => {
                this.#getApi().OnClick(id);
            });
        };
        this.#clicks.set(id, callback);
        element.addEventListener('click', callback);
    }

    #subscribeScroll(id: number): void {
        if (this.#stopped) {
            throw new Error('MAUI bridge is stopped');
        }
        if (this.#scrolls.has(id)) {
            throw new Error('Duplicate scroll subscription');
        }
        const element = this.#get(id);
        const callback = (): void => {
            if (this.#stopped || !this.#elements.has(id)) {
                return;
            }
            this.#invoke(() => {
                this.#getApi().OnScroll(id, element.scrollLeft, element.scrollTop);
            });
        };
        this.#scrolls.set(id, callback);
        element.addEventListener('scroll', callback, { passive: true });
    }

    #schedule(id: number, milliseconds: number): void {
        if (this.#stopped) {
            return;
        }
        clearTimeout(this.#timers.get(id));
        this.#timers.set(id, setTimeout(() => {
            this.#timers.delete(id);
            if (!this.#stopped) {
                this.#invoke(() => {
                    this.#getApi().RunScheduled(id);
                });
            }
        }, milliseconds));
    }

    #cancelSchedule(id: number): void {
        clearTimeout(this.#timers.get(id));
        this.#timers.delete(id);
    }

    readonly dispose = (): void => {
        this.#stop();
        for (const id of this.#elements.keys()) {
            this.#release(id);
        }
    };
}