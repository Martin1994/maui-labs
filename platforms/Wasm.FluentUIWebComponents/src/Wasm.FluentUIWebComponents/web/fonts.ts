interface FontRequest {
    readonly family: string;
    face?: FontFace;
}

// Different application instances must never accidentally address an old face.
let nextFamily = 0;

/** Lazily loads app-owned FontFaces, returning a safe CSS family before loading finishes. */
export class BrowserFonts {
    private readonly requests = new Map<string, FontRequest>();
    private disposed = false;

    constructor(
        private readonly resolve: (name: string) => string | undefined,
        private readonly requestLayout: () => void,
        private readonly report: (message: string) => void,
    ) {}

    getFamily(filename: string): string {
        const existing = this.requests.get(filename);
        if (existing) { return existing.family; }
        if (this.disposed) { return 'sans-serif'; }
        const state: FontRequest = { family: `maui-font-${++nextFamily}` };
        this.requests.set(filename, state);
        const current = (): boolean => !this.disposed && this.requests.get(filename) === state;
        const failed = (error: unknown): void => {
            if (!current()) { return; }
            if (state.face) { document.fonts.delete(state.face); }
            try {
                this.report(`Unable to load font '${filename}': ${String(error)}`);
            } finally {
                if (current()) { this.requestLayout(); }
            }
        };
        try {
            const url = this.resolve(filename);
            if (!current()) { return state.family; }
            if (!url) { throw new Error('Font is not in the asset catalog.'); }
            // A CSS string, not a CSS identifier: quote URL metacharacters rather than
            // interpolating a filename/alias into a stylesheet or url(...) expression.
            const quotedUrl = url.replace(/["\\\n\r\f]/g, character => `\\${character.charCodeAt(0).toString(16)} `);
            const face = new FontFace(state.family, `url("${quotedUrl}")`);
            state.face = face;
            void face.load().then(() => {
                if (!current()) { return; }
                document.fonts.add(face);
                this.requestLayout();
            }).catch(failed);
        } catch (error) {
            failed(error);
        }
        return state.family;
    }

    dispose(): void {
        if (this.disposed) { return; }
        this.disposed = true;
        for (const state of this.requests.values()) {
            if (state.face) { document.fonts.delete(state.face); }
        }
        this.requests.clear();
    }
}