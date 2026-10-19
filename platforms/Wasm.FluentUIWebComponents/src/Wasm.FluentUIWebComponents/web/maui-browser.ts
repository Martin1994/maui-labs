import type { DotnetHostBuilder } from './dotnet.js';
import type { BrowserApplicationExports, BrowserBridge } from './contracts.js';
import { BrowserModuleName, BrowserApplicationAssemblyName, resolveBrowserExports } from './contracts.js';
import { DomBrowserBridge } from './bridge.js';
import { initializeFluent } from './fluent.js';

/** Owns a running browser application. Repeated disposal is harmless. */
export interface MauiApplicationHandle {
    dispose(): void;
}

let applicationActive = false;

/** Start a MAUI app with the builder exported by its matching NativeAOT LLVM dotnet.js. */
export async function startMaui(
    dotnet: DotnetHostBuilder,
    mainAssembly: string,
    element: HTMLElement | null = document.getElementById('app'),
): Promise<MauiApplicationHandle> {
    if (applicationActive) {
        throw new Error('A MAUI application is already starting or running on this page');
    }
    applicationActive = true;
    const onError = (error: unknown): void => {
        const message = error instanceof Error ? error.message : String(error);
        if (element) {
            element.dataset.state = 'failed';
        }
        console.error(message);
        element?.dispatchEvent(new CustomEvent<string>('maui-error', { detail: message }));
    };
    let api: BrowserApplicationExports | undefined;
    let bridge: BrowserBridge | undefined;
    let disposed = false;
    let ownsApplication = false;
    const getApi = (): BrowserApplicationExports => {
        if (!api) {
            throw new Error('MAUI browser callbacks have not been initialized');
        }
        return api;
    };
    const dispose = (): void => {
        if (disposed) {
            return;
        }
        disposed = true;
        try {
            if (ownsApplication) {
                getApi().Shutdown();
            }
        } finally {
            ownsApplication = false;
            applicationActive = false;
            bridge?.dispose();
        }
    };

    try {
        if (!element) {
            throw new Error('MAUI app element was not found');
        }
        bridge = new DomBrowserBridge(element, getApi, onError);
        await initializeFluent();
        const runtime = await dotnet.withMainAssembly(mainAssembly).create();
        runtime.setModuleImports(BrowserModuleName, bridge.imports);
        api = resolveBrowserExports(await runtime.getAssemblyExports(BrowserApplicationAssemblyName));
        const exitCode = await runtime.runMain(mainAssembly, []);
        if (exitCode !== 0) {
            throw new Error(`MAUI Main exited with code ${exitCode}`);
        }
        ownsApplication = true;

        // Main completed successfully; this does not imply a first frame has rendered.
        element.dataset.state = 'ready';
        return { dispose };
    } catch (error) {
        try {
            dispose();
        } catch (cleanupError) {
            onError(cleanupError);
        }
        onError(error);
        throw error;
    }
}