/** Keep this private contract in sync with the managed BrowserApplication JSExports. */
export interface BrowserApplicationExports {
    LayoutTick(width: number, height: number): void;
    OnClick(id: number): void;
    OnScroll(id: number, x: number, y: number): void;
    RunScheduled(id: number): void;
    Shutdown(): void;
}

/** Functions registered as the managed Browser class's "maui-browser" imports. */
export interface BrowserImports {
    report(message: string): void;
    create(kind: string): number;
    style(id: number, name: string, value: string): void;
    text(id: number, value: string): void;
    enabled(id: number, value: boolean): void;
    automationId(id: number, value: string): void;
    frame(id: number, x: number, y: number, width: number, height: number): void;
    measure(id: number, width: number, height: number, returnHeight: boolean): number;
    insert(parent: number, child: number, index: number): void;
    remove(id: number): void;
    release(id: number): void;
    mount(id: number): void;
    unmount(id: number): void;
    subscribeClick(id: number): void;
    unsubscribeClick(id: number): void;
    subscribeScroll(id: number): void;
    requestLayout(): void;
    schedule(id: number, milliseconds: number): void;
    cancelSchedule(id: number): void;
    stop(): void;
}

export interface BrowserBridge {
    imports: BrowserImports;
    dispose(): void;
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null;
}

function assertBrowserExports(value: unknown): asserts value is BrowserApplicationExports {
    const methods = ['LayoutTick', 'OnClick', 'OnScroll', 'RunScheduled', 'Shutdown'] as const;
    if (!isRecord(value) || !methods.every(name => {
        return typeof value[name] === 'function';
    })) {
        throw new Error('The MAUI backend assembly does not expose the expected browser callbacks');
    }
}

/** The runtime returns any; validate the boundary instead of propagating it into the bridge. */
export function resolveBrowserExports(assembly: unknown): BrowserApplicationExports {
    let value: unknown = assembly;
    for (const name of ['Microsoft', 'Maui', 'Platforms', 'Wasm', 'FluentUIWebComponents', 'BrowserApplication']) {
        if (!isRecord(value)) {
            throw new Error('Invalid MAUI backend assembly exports');
        }
        value = value[name];
    }
    assertBrowserExports(value);
    return value;
}