import { access, copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { createHash } from 'node:crypto';

/** Resolve the declaration from the actual restored graph, not a duplicated package version. */
export async function prepareRuntimeTypes(root) {
    const backend = resolve(root, 'src/Wasm.FluentUIWebComponents');
    let source;
    let packageIdentity;
    if (process.env.DOTNET_DECLARATIONS) {
        // Explicit escape hatch for editing on a machine without the matching .NET SDK.
        // The caller must supply the declaration from the same LLVM runtime package.
        source = resolve(process.env.DOTNET_DECLARATIONS);
        packageIdentity = 'explicit DOTNET_DECLARATIONS override';
    } else {
        const assetsFile = resolve(process.env.MAUI_BROWSER_PROJECT_ASSETS ?? resolve(backend, 'obj/project.assets.json'));
        let assets;
        try {
            assets = JSON.parse(await readFile(assetsFile, 'utf8'));
        } catch (error) {
            throw new Error(`Restore the backend NuGet packages before building TypeScript (${assetsFile}), or set DOTNET_DECLARATIONS to a matching dotnet.d.ts.`, { cause: error });
        }
        const matches = Object.entries(assets.libraries ?? {}).filter(([name]) => {
            return name.toLowerCase().startsWith('runtime.browser-wasm.microsoft.dotnet.ilcompiler.llvm/');
        });
        if (matches.length !== 1) {
            throw new Error('Expected exactly one NativeAOT LLVM browser runtime in project.assets.json');
        }
        const [identity, library] = matches[0];
        if (typeof library.path !== 'string') {
            throw new Error(`No restored package path for ${identity}`);
        }
        packageIdentity = identity;
        for (const folder of Object.keys(assets.packageFolders ?? {})) {
            const candidate = resolve(folder, library.path, 'runtimes/browser-wasm/native-hidden/dotnetjs/dotnet.d.ts');
            try {
                await access(candidate);
                source = candidate;
                break;
            } catch (error) {
                if (error.code !== 'ENOENT') {
                    throw error;
                }
            }
        }
        if (!source) {
            throw new Error(`The restored ${identity} package does not contain dotnet.d.ts; restore it or review the runtime version.`);
        }
    }
    const destination = resolve(backend, 'web/dotnet.d.ts');
    await mkdir(dirname(destination), { recursive: true });
    if (resolve(source) !== destination) {
        await copyFile(source, destination);
    }
    const evidence = resolve(backend, 'obj/browser-declarations');
    await mkdir(evidence, { recursive: true });
    await writeFile(resolve(evidence, 'runtime-types.json'), JSON.stringify({
        packageIdentity, source, sha256: createHash('sha256').update(await readFile(source)).digest('hex'),
    }, null, 2));
    console.log(`TypeScript runtime declarations: ${packageIdentity}`);
    return destination;
}