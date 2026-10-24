import { Updates } from '@microsoft/fast-element';
import { Dropdown } from '@fluentui/web-components/dropdown/class.js';
import type { Listbox } from '@fluentui/web-components/listbox/class.js';
import type { DropdownOption } from '@fluentui/web-components/option/class.js';

/** Owns the Fluent dropdown's internal listbox; these are not MAUI child views. */
export class BrowserPicker {
    readonly #element: Dropdown;
    readonly #listbox: Listbox;
    readonly #abort = new AbortController();
    readonly #onSelection: (index: number) => void;
    readonly #onOpen: (open: boolean) => void;
    readonly #onFocus: (focused: boolean) => void;
    readonly #requestLayout: () => void;
    #items: string[] = [];
    #options: DropdownOption[] = [];
    #selectedIndex = -1;
    #open = false;
    #disposed = false;
    #queued = false;
    #flushing = false;
    #styles: HTMLStyleElement | undefined;

    constructor(element: HTMLElement, onSelection: (index: number) => void,
        onOpen: (open: boolean) => void, onFocus: (focused: boolean) => void, requestLayout: () => void) {
        if (!(element instanceof Dropdown)) {
            throw new Error('Picker requires a Fluent dropdown');
        }
        this.#element = element;
        this.#onSelection = onSelection;
        this.#onOpen = onOpen;
        this.#onFocus = onFocus;
        this.#requestLayout = requestLayout;
        this.#listbox = element.ownerDocument.createElement('fluent-listbox') as Listbox;
        element.append(this.#listbox);
        const options = { signal: this.#abort.signal };
        element.addEventListener('change', event => {
            // Option events bubble too; only Fluent's final dropdown event commits.
            if (this.#disposed || this.#flushing || element.disabled || event.target !== element) {
                return;
            }
            this.#selectedIndex = this.#options.findIndex(option => option.selected);
            this.#onSelection(this.#selectedIndex);
        }, options);
        this.#listbox.addEventListener('beforetoggle', event => {
            if (this.#disposed) {
                return;
            }
            // Record native closes synchronously: the selection's queued flush
            // runs before the deferred toggle event and must not reopen the popup.
            const open = (event as ToggleEvent).newState === 'open' && !element.disabled;
            this.#open = open;
            this.#onOpen(open);
            if ((event as ToggleEvent).newState === 'open'
                && (this.#disposed || !element.isConnected || element.disabled || !this.#open)) {
                // Opening is cancelable. Managed Opened callbacks may remove the
                // view or reject opening before showPopover rechecks connectivity.
                event.preventDefault();
            }
        }, options);
        element.addEventListener('focusin', () => {
            if (!this.#disposed) {
                this.#onFocus(true);
            }
        }, options);
        element.addEventListener('focusout', event => {
            if (!this.#disposed && !element.contains(event.relatedTarget as Node | null)) {
                this.#onFocus(false);
            }
        }, options);
    }

    clear(): void { this.#items = []; }
    add(text: string): void { this.#items.push(text); }

    commit(index: number): void {
        this.#options = this.#items.map((text, i) => {
            const option = this.#element.ownerDocument.createElement('fluent-option') as DropdownOption;
            option.textContent = text;
            // Index identity preserves duplicate labels and empty strings.
            option.value = String(i);
            return option;
        });
        this.#listbox.replaceChildren(...this.#options);
        this.select(index);
        this.#requestLayout();
    }

    select(index: number): void {
        this.#selectedIndex = index;
        this.#queue();
    }

    setEnabled(enabled: boolean): void {
        this.#element.disabled = !enabled;
        // Leave a pending open intact until flush can report its cancellation to
        // MAUI, even if the popover never opened and emits no native close event.
        this.#queue();
    }

    setOpen(open: boolean): void {
        this.#open = open;
        this.#queue();
    }

    focus(focused: boolean): boolean {
        this.flush();
        const control = this.#element.control;
        if (!control) {
            return false;
        }
        if (focused) {
            control.focus();
        } else {
            control.blur();
        }
        return this.#element.contains(this.#element.ownerDocument.activeElement);
    }

    #queue(): void {
        if (this.#disposed || this.#queued) {
            return;
        }
        this.#queued = true;
        queueMicrotask(() => {
            this.#queued = false;
            this.flush();
        });
    }

    flush(): void {
        if (this.#disposed || this.#flushing || !this.#element.isConnected) {
            return;
        }
        this.#flushing = true;
        try {
            // Complete Fluent's control template before applying MAUI state. The
            // first measure also reaches here after a previously detached mount.
            Updates.process();
            if (!this.#styles && this.#element.shadowRoot) {
                this.#styles = this.#element.ownerDocument.createElement('style');
                // Fluent 3.1.3 does not expose a control CSS part. Keep this
                // version-specific compatibility boundary local to the adapter.
                this.#styles.textContent = `
                    :host { color: var(--maui-picker-color, var(--colorNeutralForeground1)); }
                    :host(:state(placeholder-shown)) { color: var(--maui-picker-placeholder-color, var(--colorNeutralForeground4)); }
                    .control { font: inherit; letter-spacing: inherit; text-align: inherit;
                        min-width: 0; height: 100%; align-items: var(--maui-picker-alignment, center); }
                    ::slotted(button) { text-align: inherit; min-width: 0; }
                `;
                this.#element.shadowRoot.append(this.#styles);
            }
            // Public Fluent references also make initial selection deterministic
            // before the browser delivers its first asynchronous slotchange.
            this.#element.listbox = this.#listbox;
            this.#listbox.options = this.#options;
            Updates.process();
            this.#options.forEach((option, i) => {
                // Fluent's selectOption indexes only enabled options. MAUI retains
                // selection while disabled, so update all option states directly.
                option.selected = i === this.#selectedIndex;
            });
            this.#listbox.selectedIndex = this.#selectedIndex;
            const control = this.#element.control;
            if (control) {
                const label = this.#element.getAttribute('aria-label');
                if (label) {
                    control.setAttribute('aria-label', label);
                } else {
                    control.removeAttribute('aria-label');
                }
            }
            Updates.process();
            if (this.#open && this.#element.disabled) {
                this.#open = false;
                this.#onOpen(false);
                if (this.#disposed) {
                    return;
                }
            }
            const open = this.#listbox.matches(':popover-open');
            if (this.#open && !open && !this.#element.disabled) {
                this.#element.control?.focus();
                // Focus is application code too; it may retire this adapter.
                if (!this.#disposed && this.#element.isConnected && this.#open && !this.#element.disabled) {
                    this.#listbox.showPopover();
                }
            } else if (!this.#open && open) {
                this.#listbox.hidePopover();
            }
        } finally {
            this.#flushing = false;
        }
    }

    dispose(): void {
        this.#disposed = true;
        this.#abort.abort();
        if (this.#listbox.matches(':popover-open')) {
            this.#listbox.hidePopover();
        }
        this.#styles?.remove();
        this.#listbox.remove();
        this.#options = [];
        this.#items = [];
    }
}