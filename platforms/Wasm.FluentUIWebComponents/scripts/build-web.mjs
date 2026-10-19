import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { copyFile, mkdir, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { prepareRuntimeTypes } from './prepare-types.mjs';
import { runTypeScript } from './typescript-cli.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const backend = resolve(root, 'src/Wasm.FluentUIWebComponents');
const dotnet = process.env.DOTNET_HOST_PATH || 'dotnet';
const project = resolve(backend, 'Wasm.FluentUIWebComponents.csproj');
const buildDirectory = process.env.MAUI_BROWSER_BUILD_DIRECTORY || execFileSync(dotnet, [
    'msbuild', project, '-getProperty:BrowserBuildDirectory', '-property:Configuration=Release', '-nologo',
], { cwd: root, encoding: 'utf8' }).trim();
// MSBuild already generates the contract when building the library. A standalone
// npm build requests the same target, rather than parsing C# or reading a stale DLL.
if (process.env.MAUI_BROWSER_INTEROP_GENERATED !== '1') {
    execFileSync(dotnet, [
        'msbuild', project, '-property:Configuration=Release',
        '-target:GenerateBrowserInterop', '-verbosity:minimal', '-nologo',
    ], { cwd: root, stdio: 'inherit' });
}
const runtimeTypes = await prepareRuntimeTypes(root, buildDirectory);
const declarationDirectory = resolve(buildDirectory, 'declarations');

// Use the compiler CLI, not its JS implementation API. All imports use real source paths.
// Override tsconfig's version-independent default with this build's MSBuild-selected directory.
runTypeScript(['--project', resolve(backend, 'tsconfig.json'), '--outDir', declarationDirectory]);
const output = resolve(buildDirectory, 'wwwroot');
await mkdir(output, { recursive: true });
await copyFile(resolve(declarationDirectory, 'maui-browser.d.ts'), resolve(output, 'maui-browser.d.ts'));
await copyFile(runtimeTypes, resolve(output, 'dotnet.d.ts'));

// noResolve restricts the graph to these shipped files plus standard libraries.
// A private or npm declaration accidentally referenced by the public API must fail.
const publicConfig = resolve(buildDirectory, 'tsconfig.public.json');
await writeFile(publicConfig, JSON.stringify({
    compilerOptions: {
        target: 'ES2022', module: 'ESNext', moduleResolution: 'Bundler',
        strict: true, noEmit: true, noResolve: true, noUncheckedSideEffectImports: true, types: [],
    },
    files: ['./wwwroot/maui-browser.d.ts', './wwwroot/dotnet.d.ts'],
}, null, 2));
runTypeScript(['--project', publicConfig]);

await build({
    absWorkingDir: root,
    entryPoints: ['src/Wasm.FluentUIWebComponents/web/maui-browser.ts'],
    outfile: resolve(output, 'maui-browser.js'),
    bundle: true, format: 'esm', target: 'es2022', legalComments: 'inline',
});