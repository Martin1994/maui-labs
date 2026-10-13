const styles = new CSSStyleSheet();
styles.replaceSync(`
    :host {
        display: block;
        overflow-y: auto;
        overflow-x: hidden;
    }
`);

/** The host itself scrolls, so native scroll events and offsets need no proxy. */
export class MauiScrollView extends HTMLElement {
    constructor() {
        super();
        const root = this.attachShadow({ mode: 'open' });
        root.adoptedStyleSheets = [styles];
        root.append(document.createElement('slot'));
    }
}