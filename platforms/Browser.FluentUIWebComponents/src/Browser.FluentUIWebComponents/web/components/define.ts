import { MauiScrollView } from './scroll-view.js';

// Module evaluation registers once. A conflicting definition is an error rather
// than permission to silently use a component with a different contract.
customElements.define('maui-scroll-view', MauiScrollView);