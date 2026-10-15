import { setTheme } from '@fluentui/web-components/theme/set-theme.js';
import { webDarkTheme, webLightTheme } from '@fluentui/tokens';
import type { BrowserThemeName } from './contracts.js';

function resolveTheme(theme: BrowserThemeName): typeof webLightTheme {
    switch (theme) {
        case 'light':
            return webLightTheme;
        case 'dark':
            return webDarkTheme;
        default: {
            // Check JS/interop callers at runtime, and require an explicit mapping
            // if the TypeScript union is extended in the future.
            const unsupported: never = theme;
            throw new Error(`Unsupported browser theme: ${String(unsupported)}`);
        }
    }
}

/** Browser preference is input to MAUI; only MAUI chooses the effective app theme. */
export class BrowserTheme {
    readonly #element: HTMLElement;
    readonly #preference = matchMedia('(prefers-color-scheme: dark)');
    readonly #onChanged: (theme: BrowserThemeName) => void;
    readonly #previousColorScheme: string;
    readonly #previousColorSchemePriority: string;
    #listening = false;
    #disposed = false;

    constructor(element: HTMLElement, onChanged: (theme: BrowserThemeName) => void) {
        // Fluent's setTheme treats body/html as global and does not theme nodes
        // inside another shadow root. Fail explicitly instead of leaking or
        // silently skipping the app-local tokens.
        if (element === document.body || !document.body?.contains(element)) {
            throw new Error('Use a dedicated light-DOM mount element inside document.body for the MAUI app');
        }
        this.#element = element;
        this.#onChanged = onChanged;
        this.#previousColorScheme = element.style.getPropertyValue('color-scheme');
        this.#previousColorSchemePriority = element.style.getPropertyPriority('color-scheme');
        this.apply(this.getSystemTheme());
    }

    getSystemTheme(): BrowserThemeName {
        return this.#preference.matches ? 'dark' : 'light';
    }

    getColor(token: string, theme: BrowserThemeName): string {
        const preset = resolveTheme(theme);
        // Deliberately expose only the semantic colors consumed by MAUI resources.
        // Values come from the same preset objects used by apply(), never a C# copy.
        switch (token) {
            case 'colorBrandForeground1':
            case 'colorNeutralForeground1':
            case 'colorNeutralForeground3':
            case 'colorNeutralBackground3':
            case 'colorNeutralBackground1':
            case 'colorNeutralStroke2':
                return preset[token];
            default:
                throw new Error(`Unsupported Fluent color token: ${token}`);
        }
    }

    start(): BrowserThemeName {
        if (this.#disposed) {
            throw new Error('Browser theme is disposed');
        }
        if (!this.#listening) {
            this.#preference.addEventListener('change', this.#changed);
            this.#listening = true;
        }
        // Resynchronize changes that happened while Wasm/app startup was pending.
        return this.getSystemTheme();
    }

    readonly #changed = (): void => {
        if (!this.#disposed) {
            this.#onChanged(this.getSystemTheme());
        }
    };

    apply(theme: BrowserThemeName): void {
        const preset = resolveTheme(theme);
        if (this.#disposed) {
            return;
        }
        // Scope tokens and native scrollbar appearance to this app, not the host page.
        setTheme(preset, this.#element);
        this.#element.style.setProperty('color-scheme', theme);
    }

    dispose(): void {
        if (this.#disposed) {
            return;
        }
        this.#disposed = true;
        this.#preference.removeEventListener('change', this.#changed);
        this.#listening = false;
        setTheme(null, this.#element);
        this.#element.style.setProperty('color-scheme', this.#previousColorScheme, this.#previousColorSchemePriority);
    }
}