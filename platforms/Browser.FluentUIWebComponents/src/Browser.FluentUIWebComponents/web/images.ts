interface ImageRequest {
    readonly request: number;
    readonly image: HTMLImageElement;
    readonly load: () => void;
    readonly error: () => void;
    settled: boolean;
}

/** Static catalog images only. Each request owns a fresh img and its event identity. */
export class BrowserImages {
    private readonly requests = new Map<number, ImageRequest>();
    private readonly aspects = new Map<number, string>();
    private disposed = false;

    constructor(
        private readonly resolve: (name: string) => string | undefined,
        private readonly complete: (id: number, request: number, success: boolean) => void,
        private readonly report: (message: string) => void,
    ) {}

    setSource(id: number, element: HTMLElement, name: string, request: number): void {
        if (this.disposed) { return; }
        this.clear(id);
        if (!name) { return; }

        const image = element.ownerDocument.createElement('img');
        image.alt = '';
        Object.assign(image.style, {
            display: 'block', width: '100%', height: '100%',
            minWidth: '0', minHeight: '0', maxWidth: 'none', maxHeight: 'none',
            objectPosition: 'center',
        });
        const current = (): boolean => !this.disposed && this.requests.get(id) === state;
        const finish = (success: boolean, message?: string): void => {
            if (!current() || state.settled) { return; }
            state.settled = true;
            unlisten();
            if (!success) {
                image.removeAttribute('src');
                image.remove();
                this.report(message ?? `Unable to load image '${name}'.`);
            }
            // Reporting and managed completion can both reenter the adapter.
            if (current()) { this.complete(id, request, success); }
        };
        const load = (): void => finish(true);
        const error = (): void => finish(false);
        const unlisten = (): void => {
            image.removeEventListener('load', load);
            image.removeEventListener('error', error);
        };
        const state: ImageRequest = { request, image, load, error, settled: false };
        this.requests.set(id, state);
        this.applyAspect(id, image);
        // Install handlers before src: even a synchronous cache/test completion is safe.
        image.addEventListener('load', load);
        image.addEventListener('error', error);
        try {
            const url = this.resolve(name);
            if (!current()) { return; }
            if (!url) {
                finish(false, `Image '${name}' is not in the asset catalog.`);
                return;
            }
            element.append(image);
            image.src = url;
        } catch (error) {
            finish(false, `Unable to load image '${name}': ${String(error)}`);
        }
    }

    setAspect(id: number, aspect: string): void {
        if (this.disposed) { return; }
        this.aspects.set(id, aspect);
        const state = this.requests.get(id);
        if (state) { this.applyAspect(id, state.image); }
    }

    private applyAspect(id: number, image: HTMLImageElement): void {
        switch (this.aspects.get(id)) {
            case 'AspectFill': image.style.objectFit = 'cover'; break;
            case 'Fill': image.style.objectFit = 'fill'; break;
            case 'Center': image.style.objectFit = 'none'; break;
            default: image.style.objectFit = 'contain'; break;
        }
    }

    private clear(id: number): void {
        const state = this.requests.get(id);
        this.requests.delete(id);
        if (!state) { return; }
        state.image.removeEventListener('load', state.load);
        state.image.removeEventListener('error', state.error);
        state.image.removeAttribute('src');
        state.image.remove();
    }

    release(id: number): void {
        this.clear(id);
        this.aspects.delete(id);
    }

    dispose(): void {
        if (this.disposed) { return; }
        this.disposed = true;
        for (const id of this.requests.keys()) { this.clear(id); }
        this.aspects.clear();
    }
}