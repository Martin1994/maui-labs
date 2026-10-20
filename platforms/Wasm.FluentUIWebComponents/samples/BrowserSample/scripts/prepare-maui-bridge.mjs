import { access, copyFile, rm } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';

const root = fileURLToPath(new URL('..', import.meta.url));
const published = join(root, 'artifacts/maui');
const assets = ['maui-browser.js', 'maui-browser.d.ts', 'dotnet.d.ts'];

// The publish output is the only contract with .NET, regardless of reference type.
// Validate all inputs before replacing editor resources or clearing TS output.
for (const name of assets) {
    try {
        await access(join(published, name));
    } catch (error) {
        throw new Error(`Missing published MAUI asset: ${name}. Run npm run build:maui-app first.`, { cause: error });
    }
}
for (const name of assets) {
    await copyFile(join(published, name), join(root, 'web', name));
}
await rm(join(root, 'artifacts/typescript'), { recursive: true, force: true });
