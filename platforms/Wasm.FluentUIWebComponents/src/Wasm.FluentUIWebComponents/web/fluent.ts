// Keep registration side effects internal, so the public .d.ts needs no Fluent/npm dependencies.
import './components/define.js';
import '@fluentui/web-components/button/define.js';
import '@fluentui/web-components/text/define.js';
import '@fluentui/web-components/text-input/define.js';
import { Updates } from '@microsoft/fast-element';

export async function initializeFluent(): Promise<void> {
    await customElements.whenDefined('fluent-button');
    await customElements.whenDefined('fluent-text');
    await customElements.whenDefined('fluent-text-input');
    await document.fonts.ready;
    // Initialization must not depend on a frame being granted to a hidden tab.
    Updates.process();
}