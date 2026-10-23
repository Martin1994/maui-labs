/** Style only backend-owned inputs through Fluent's exported CSS parts. */
export function createInputStyles(): HTMLStyleElement {
    const style = document.createElement('style');
    style.textContent = `
        fluent-text-input[data-maui-input]::part(control) {
            font: inherit;
            text-align: inherit;
            letter-spacing: inherit;
        }
        fluent-text-input[data-maui-input][data-maui-input-color]::part(control) {
            color: var(--maui-input-color);
        }
        fluent-text-input[data-maui-input][data-maui-placeholder-color]::part(control)::placeholder {
            color: var(--maui-placeholder-color);
        }
        fluent-text-input[data-maui-input]::part(root) {
            min-height: 32px;
            height: 100%;
        }
    `;
    return style;
}