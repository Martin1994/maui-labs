import { constants } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { access, copyFile, mkdir, readdir, rename, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('..', import.meta.url));
const published = join(root, 'artifacts/maui');
const compiled = join(root, 'artifacts/typescript');
const site = join(root, 'artifacts/wwwroot');
const staging = join(root, 'artifacts/site-staging');
const backup = join(root, `artifacts/site-backup-${randomUUID()}`);

// Never assemble a runnable-looking site from a managed-only build or missing TS output.
for (const name of ['dotnet.native.wasm', 'dotnet.native.js', 'dotnet.js',
    'maui-browser.js', 'maui-browser.d.ts', 'dotnet.d.ts']) {
    try {
        await access(join(published, name));
    } catch (error) {
        throw new Error(`Missing published MAUI asset: ${name}. Run npm run build:maui-app first.`, { cause: error });
    }
}
for (const name of ['main.js', 'sample-diagnostics.js']) {
    try {
        await access(join(compiled, name));
    } catch (error) {
        throw new Error(`Missing compiled application script: ${name}. Run npm run build:ts first.`, { cause: error });
    }
}
await access(join(root, 'wwwroot/index.html'));

async function collect(source, relative = '') {
    for (const entry of await readdir(join(source, relative), { withFileTypes: true })) {
        const path = join(relative, entry.name);
        const destination = join(staging, path);
        if (entry.isDirectory()) {
            await mkdir(destination, { recursive: true });
            await collect(source, path);
        } else if (entry.isFile()) {
            // Fail on collisions rather than overwrite runtime files with application assets.
            await copyFile(join(source, path), destination, constants.COPYFILE_EXCL);
        } else {
            throw new Error(`Unsupported site asset: ${join(source, path)}`);
        }
    }
}

await rm(staging, { recursive: true, force: true });
await mkdir(staging, { recursive: true });
try {
    // Preserve the whole module, including declarations, symbols, maps and metadata.
    await collect(published);
    await collect(join(root, 'wwwroot'));
    await collect(compiled);
    let previousSite = false;
    try {
        await rename(site, backup);
        previousSite = true;
    } catch (error) {
        if (error.code !== 'ENOENT') {
            throw error;
        }
    }
    try {
        await rename(staging, site);
        console.log("Site built successfully.");
        console.log(site);
    } catch (error) {
        if (previousSite) {
            // If rollback itself fails, leave the uniquely named backup for recovery.
            await rename(backup, site);
        }
        throw error;
    }
    await rm(backup, { recursive: true, force: true });
} finally {
    await rm(staging, { recursive: true, force: true });
}
