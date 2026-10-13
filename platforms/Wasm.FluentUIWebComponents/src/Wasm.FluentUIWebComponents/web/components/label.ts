const styles = new CSSStyleSheet();
styles.replaceSync(`
    :host {
        display: block;
        white-space: pre-wrap;
        overflow-wrap: break-word;
        line-height: 1.3;
        color: var(--colorNeutralForeground1, inherit);
    }
`);

/** Text remains in light DOM; the host is the MAUI measure/arrange target. */
export class MauiLabel extends HTMLElement {
    constructor() {
        super();
        const root = this.attachShadow({ mode: 'open' });
        root.adoptedStyleSheets = [styles];
        root.append(document.createElement('slot'));
    }
}