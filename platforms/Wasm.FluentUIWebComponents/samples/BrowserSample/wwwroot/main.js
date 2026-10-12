import { dotnet } from './dotnet.js';
import { startMaui } from './maui-browser.js';

try {
    await startMaui(dotnet, 'FluentBrowserSample');
} catch (error) {
    console.error(error);
    // Only startup failure uses host HTML; the entire application UI is MainPage.xaml.
    const message = document.createElement('pre');
    message.textContent = `Unable to start MAUI: ${error instanceof Error ? error.message : String(error)}`;
    document.getElementById('app')?.append(message);
}