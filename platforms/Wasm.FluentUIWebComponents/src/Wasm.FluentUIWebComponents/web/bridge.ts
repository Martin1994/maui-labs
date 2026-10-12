import type { Button } from '@fluentui/web-components/button/class.js';
import type { BrowserApplicationExports, BrowserBridge, BrowserImports } from './contracts.js';

function isButton(element: HTMLElement): element is Button {
    return element.localName === 'fluent-button';
}

// DOM nodes and listeners stay JS-owned; MAUI computes container frames.
export function createBridge(
    viewport: HTMLElement,
    getApi: () => BrowserApplicationExports,
    onError: (message: string) => void,
): BrowserBridge {
    const elements = new Map<number, HTMLElement>();
    const clicks = new Map<number, EventListener>();
    const scrolls = new Map<number, EventListener>();
    const timers = new Map<number, number>();
    const kinds = new Set(['window', 'page', 'layout', 'label', 'button', 'content', 'border', 'scroll']);
    let sequence = 0;
    let observer: ResizeObserver | undefined;
    let mountedId = 0;
    let frameId = 0;
    let stopped = false;

    const get = (id: number): HTMLElement => {
        if (!Number.isInteger(id) || id <= 0 || id > 0x7fffffff) {
            throw new Error(`Invalid MAUI element handle: ${id}`);
        }
        const element = elements.get(id);
        if (!element) {
            throw new Error(`Stale MAUI element handle: ${id}`);
        }
        return element;
    };
    const invoke = (action: () => void): void => {
        try {
            action();
        } catch (error) {
            onError(error instanceof Error ? error.message : String(error));
        }
    };
    const requestLayout = (): void => {
        if (stopped || !mountedId || frameId) {
            return;
        }
        frameId = requestAnimationFrame(() => {
            frameId = 0;
            if (stopped || !mountedId) {
                return;
            }
            invoke(() => {
                return getApi().LayoutTick(viewport.clientWidth, viewport.clientHeight);
            });
        });
    };
    const unmount = (id: number): void => {
        if (mountedId !== id || !mountedId) {
            return;
        }
        observer?.disconnect();
        observer = undefined;
        cancelAnimationFrame(frameId);
        frameId = 0;
        mountedId = 0;
        get(id).remove();
    };
    const unsubscribeClick = (id: number): void => {
        const element = get(id);
        const callback = clicks.get(id);
        if (callback) {
            element.removeEventListener('click', callback);
        }
        clicks.delete(id);
    };
    const stop = (): void => {
        stopped = true;
        if (mountedId) {
            unmount(mountedId);
        }
        for (const timer of timers.values()) {
            clearTimeout(timer);
        }
        timers.clear();
    };
    const imports: BrowserImports = {
        report(message) {
            onError(message);
        },
        create(kind) {
            if (stopped) {
                throw new Error('MAUI bridge is stopped');
            }
            if (!kinds.has(kind)) {
                throw new Error(`Unsupported element: ${kind}`);
            }
            if (sequence >= 0x7fffffff) {
                throw new Error('MAUI element handle limit reached');
            }
            const element = document.createElement(kind === 'button' ? 'fluent-button' : kind === 'label' ? 'span' : 'div');
            const id = ++sequence;
            // The Fluent default is neutral. Do not promote every MAUI button to a brand CTA.
            element.dataset.kind = kind;
            element.style.position = 'absolute';
            element.style.boxSizing = 'border-box';
            if (kind === 'label') {
                element.style.whiteSpace = 'pre-wrap';
                element.style.overflowWrap = 'break-word';
                element.style.lineHeight = '1.3';
            }
            if (kind === 'scroll') {
                element.style.overflowY = 'auto';
                element.style.overflowX = 'hidden';
            }
            elements.set(id, element);
            return id;
        },
        style(id, name, value) {
            get(id).style.setProperty(name, value);
        },
        text(id, value) {
            get(id).textContent = value;
        },
        enabled(id, value) {
            const element = get(id);
            if (isButton(element)) {
                element.disabled = !value;
            }
        },
        automationId(id, value) {
            get(id).dataset.automationId = value;
        },
        frame(id, x, y, width, height) {
            if (![x, y, width, height].every(Number.isFinite)) {
                throw new Error('Non-finite MAUI frame');
            }
            Object.assign(get(id).style, { left: `${x}px`, top: `${y}px`, width: `${width}px`, height: `${height}px` });
        },
        measure(id, width, height, returnHeight) {
            const element = get(id);
            const saved = element.style.cssText;
            try {
                Object.assign(element.style, {
                    width: 'max-content', height: 'auto',
                    maxWidth: Number.isFinite(width) ? `${Math.max(0, width)}px` : 'none', maxHeight: 'none',
                });
                const bounds = element.getBoundingClientRect();
                return returnHeight ? bounds.height : bounds.width;
            } finally {
                element.style.cssText = saved;
            }
        },
        insert(parent, child, index) {
            const container = get(parent);
            const element = get(child);
            element.remove();
            container.insertBefore(element, container.children[index] ?? null);
        },
        remove(id) {
            get(id).remove();
        },
        release(id) {
            const element = get(id);
            unmount(id);
            unsubscribeClick(id);
            const onScroll = scrolls.get(id);
            if (onScroll) {
                element.removeEventListener('scroll', onScroll);
            }
            scrolls.delete(id);
            elements.delete(id);
            element.remove();
        },
        mount(id) {
            if (stopped) {
                throw new Error('MAUI bridge is stopped');
            }
            if (mountedId) {
                throw new Error('MAUI bridge supports one mounted window');
            }
            viewport.append(get(id));
            mountedId = id;
            observer = new ResizeObserver(requestLayout);
            observer.observe(viewport);
            requestLayout();
        },
        unmount,
        subscribeClick(id) {
            if (stopped) {
                throw new Error('MAUI bridge is stopped');
            }
            if (clicks.has(id)) {
                throw new Error('Duplicate MAUI button subscription');
            }
            const element = get(id);
            const callback = (): void => {
                if (stopped || !elements.has(id)) {
                    return;
                }
                invoke(() => {
                    return getApi().OnClick(id);
                });
            };
            clicks.set(id, callback);
            element.addEventListener('click', callback);
        },
        unsubscribeClick,
        subscribeScroll(id) {
            if (stopped) {
                throw new Error('MAUI bridge is stopped');
            }
            if (scrolls.has(id)) {
                throw new Error('Duplicate scroll subscription');
            }
            const element = get(id);
            const callback = (): void => {
                if (stopped || !elements.has(id)) {
                    return;
                }
                invoke(() => {
                    return getApi().OnScroll(id, element.scrollLeft, element.scrollTop);
                });
            };
            scrolls.set(id, callback);
            element.addEventListener('scroll', callback, { passive: true });
        },
        requestLayout,
        schedule(id, milliseconds) {
            if (stopped) {
                return;
            }
            clearTimeout(timers.get(id));
            timers.set(id, setTimeout(() => {
                timers.delete(id);
                if (!stopped) {
                    invoke(() => {
                        return getApi().RunScheduled(id);
                    });
                }
            }, milliseconds));
        },
        cancelSchedule(id) {
            clearTimeout(timers.get(id));
            timers.delete(id);
        },
        stop,
    };
    const dispose = (): void => {
        stop();
        for (const id of elements.keys()) {
            imports.release(id);
        }
    };
    return { imports, dispose };
}