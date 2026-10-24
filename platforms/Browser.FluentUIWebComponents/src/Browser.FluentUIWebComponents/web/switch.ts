import { Switch } from '@fluentui/web-components/switch/class.js';

/** Fluent owns pointer/keyboard toggling; MAUI receives its final change event. */
export class BrowserSwitch {
    readonly #element: Switch;
    readonly #abort = new AbortController();
    #disposed = false;

    constructor(element: HTMLElement, onChange: (checked: boolean) => void, onFocus: (focused: boolean) => void) {
        if (!(element instanceof Switch)) {
            throw new Error('Switch requires a Fluent switch');
        }
        this.#element = element;
        const options = { signal: this.#abort.signal };
        element.addEventListener('change', event => {
            if (!this.#disposed && !element.disabled && event.target === element) {
                // Unlike toggle-button clicks, Fluent emits change AFTER checked
                // has changed. Assigning checked programmatically emits no change.
                onChange(element.checked);
            }
        }, options);
        element.addEventListener('focus', () => {
            if (!this.#disposed) {
                onFocus(true);
            }
        }, options);
        element.addEventListener('blur', () => {
            if (!this.#disposed) {
                onFocus(false);
            }
        }, options);
    }

    focus(focused: boolean): boolean {
        if (this.#disposed) {
            return false;
        }
        if (focused) {
            this.#element.focus();
        } else {
            this.#element.blur();
        }
        return !this.#disposed && this.#element.ownerDocument.activeElement === this.#element;
    }

    dispose(): void {
        this.#disposed = true;
        this.#abort.abort();
    }
}

export function createSwitchStyles(): HTMLStyleElement {
    const style = document.createElement('style');
    // Only explicit MAUI colors override Fluent. Retain native disabled and
    // forced-colors styling, and use the public thumb part rather than its DOM.
    style.textContent = `
        @media (forced-colors: none) {
            fluent-switch[data-maui-switch-track]:not(:state(disabled)) {
                background-color: var(--maui-switch-track);
                border-color: var(--maui-switch-track);
            }
            fluent-switch[data-maui-switch-thumb]:not(:state(disabled))::part(checked-indicator) {
                background-color: var(--maui-switch-thumb);
            }
        }
    `;
    return style;
}