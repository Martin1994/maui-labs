import { TextInput } from '@fluentui/web-components/text-input/class.js';
import { Updates } from '@microsoft/fast-element';

export interface InputSelection {
    start: number;
    length: number;
}

export interface InputTextState extends InputSelection {
    value: string;
}

export interface BrowserTextInputCallbacks {
    onText(state: InputTextState): void;
    onFocus(focused: boolean): void;
    onSelection(selection: InputSelection): void;
    onCompleted(): void;
}

/**
 * Bridges MAUI input semantics without replacing Fluent methods or prototypes.
 * Uses the public value property and delegated host focus. Fluent 3.1.3 lacks
 * arbitrary selection access and forwarding for some native input attributes;
 * those operations are isolated here behind a version-specific shadow lookup.
 * CSS parts are a styling contract, not a guarantee of a stable DOM API: recheck
 * this lookup and selection event routing on a Fluent upgrade.
 */
export class BrowserTextInput {
    readonly #host: TextInput;
    readonly #abort = new AbortController();
    readonly #callbacks: BrowserTextInputCallbacks;
    #composing = false;
    #pendingValue: string | undefined;
    #selection: InputSelection | undefined;
    readonly #attributes = new Map<string, string | null>();
    #lastText: string | undefined;
    #lastSelection = '';
    #disposed = false;

    constructor(host: HTMLElement, callbacks: BrowserTextInputCallbacks) {
        if (!(host instanceof TextInput) || !host.shadowRoot) {
            throw new Error('Expected a registered Fluent text-input');
        }
        this.#host = host;
        this.#callbacks = callbacks;
        host.setAttribute('data-maui-input', '');
        const options = { signal: this.#abort.signal };
        host.addEventListener('compositionstart', () => {
            this.#composing = true;
        }, options);
        host.addEventListener('compositionend', () => {
            this.#composing = false;
            if (this.#pendingValue !== undefined) {
                const value = this.#pendingValue;
                this.#pendingValue = undefined;
                this.setValue(value);
            }
            this.flush();
            this.#input();
        }, options);
        host.addEventListener('input', this.#input, options);
        host.addEventListener('focusin', () => {
            this.#callbacks.onFocus(true);
            this.#publishSelection();
        }, options);
        host.addEventListener('focusout', event => {
            const next = event.relatedTarget;
            if (!(next instanceof Node) || !host.shadowRoot?.contains(next)) {
                this.#callbacks.onFocus(false);
            }
        }, options);
        host.addEventListener('keydown', event => {
            if (event.key !== 'Enter') {
                return;
            }
            if (this.#composing || event.isComposing || event.keyCode === 229) {
                // Do not let Fluent's implicit-submit handler treat IME confirmation
                // as form submission. Keep the native composition default intact.
                event.stopPropagation();
                return;
            }
            this.#callbacks.onCompleted();
        }, { ...options, capture: true });
        for (const name of ['selectionchange', 'select', 'keyup', 'pointerup']) {
            host.shadowRoot.addEventListener(name, this.#publishSelection, { ...options, capture: true });
        }
    }

    #control(): HTMLInputElement | null {
        return this.#host.shadowRoot?.querySelector<HTMLInputElement>('input[part~="control"]') ?? null;
    }

    flush(): void {
        Updates.process();
        const input = this.#control();
        if (input) {
            for (const [name, value] of this.#attributes) {
                if (value === null && input.hasAttribute(name)) {
                    input.removeAttribute(name);
                } else if (value !== null && input.getAttribute(name) !== value) {
                    input.setAttribute(name, value);
                }
            }
        }
        if (!this.#composing && this.#selection) {
            const control = this.#control();
            if (control && control.selectionStart !== null) {
                const { start, length } = this.#selection;
                this.#selection = undefined;
                const end = Math.min(control.value.length, start + length);
                const position = Math.min(start, end);
                if (control.selectionStart !== position || control.selectionEnd !== end) {
                    control.setSelectionRange(position, end);
                }
            }
        }
    }

    setValue(value: string): void {
        if (this.#composing) {
            this.#pendingValue = value;
            return;
        }
        // Reassigning even the same native value can disturb selection/composition.
        if ((this.#host.value ?? '') !== value) {
            this.#host.value = value;
            this.#notifySelection(false);
        }
        this.#lastText = value;
    }

    setAttribute(name: string, value: string | null): void {
        this.#attributes.set(name, value);
        this.flush();
    }

    setSelection(start: number, length: number): void {
        if (!Number.isInteger(start) || !Number.isInteger(length) || start < 0 || length < 0) {
            throw new Error('Invalid input selection');
        }
        this.#selection = { start, length };
        this.flush();
    }

    focus(focused: boolean): boolean {
        this.flush();
        const control = this.#control();
        if (!control || !this.#host.isConnected) {
            return false;
        }
        if (focused) {
            // Fluent declares delegatesFocus: true on its shadow root.
            this.#host.focus();
        } else {
            control.blur();
        }
        return this.#host.shadowRoot?.activeElement === control;
    }

    readonly #input = (): void => {
        if (this.#composing || this.#disposed) {
            return;
        }
        const control = this.#control();
        if (!control) {
            return;
        }
        const start = control.selectionStart ?? 0;
        const length = (control.selectionEnd ?? start) - start;
        if (control.value !== this.#lastText) {
            this.#lastText = control.value;
            this.#callbacks.onText({ value: control.value, start, length });
        }
        this.#publishSelection();
    };

    readonly #publishSelection = (): void => {
        this.#notifySelection(true);
    };

    #notifySelection(requireFocus: boolean): void {
        if (this.#disposed || this.#composing) {
            return;
        }
        const control = this.#control();
        if (!control || (requireFocus && this.#host.shadowRoot?.activeElement !== control)) {
            return;
        }
        const start = control.selectionStart ?? 0;
        const length = (control.selectionEnd ?? start) - start;
        const key = `${start}:${length}`;
        if (key !== this.#lastSelection) {
            this.#lastSelection = key;
            this.#callbacks.onSelection({ start, length });
        }
    }

    dispose(): void {
        this.#disposed = true;
        this.#abort.abort();
        this.#pendingValue = undefined;
        this.#selection = undefined;
        this.#host.removeAttribute('data-maui-input');
    }
}