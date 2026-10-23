import type { BrowserImports } from './generated/contracts.g.js';

export type { BrowserApplicationExports, BrowserImports, BrowserThemeName } from './generated/contracts.g.js';
export {
    BrowserModuleName,
    BrowserApplicationAssemblyName,
    resolveBrowserApplicationExports as resolveBrowserExports,
} from './generated/contracts.g.js';

/** Scalar JS properties; attributes and CSS declarations use separate operations. */
export type BrowserPropertyValue = string | number | boolean | null;

export interface BrowserBridge {
    imports: BrowserImports;
    dispose(): void;
}
