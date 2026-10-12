import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { copyFile, mkdir } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import ts from 'typescript';
import { prepareRuntimeTypes } from './prepare-types.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const backend = resolve(root, 'src/Wasm.FluentUIWebComponents');
const runtimeTypes = await prepareRuntimeTypes(root);
const tsc = createRequire(import.meta.url).resolve('typescript/bin/tsc');

// esbuild does not type-check or emit declarations; tsc operates on the same TS sources.
execFileSync(process.execPath, [tsc, '--project', resolve(backend, 'tsconfig.json')], { cwd: root, stdio: 'inherit' });
const output = resolve(backend, 'wwwroot');
await mkdir(output, { recursive: true });
await copyFile(resolve(backend, 'obj/browser-declarations/maui-browser.d.ts'), resolve(output, 'maui-browser.d.ts'));
await copyFile(runtimeTypes, resolve(output, 'dotnet.d.ts'));

// Check the distributed declarations in isolation: no private bridge/Fluent declarations
// are copied here. An accidental public dependency on them must fail the library build.
const declarations = ts.createProgram([resolve(output, 'maui-browser.d.ts')], {
    target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext,
    moduleResolution: ts.ModuleResolutionKind.Bundler, strict: true, noEmit: true, types: [],
});
const diagnostics = ts.getPreEmitDiagnostics(declarations);
if (diagnostics.length) {
    console.error(ts.formatDiagnosticsWithColorAndContext(diagnostics, {
        getCanonicalFileName: name => {
            return name;
        },
        getCurrentDirectory: () => {
            return root;
        },
        getNewLine: () => {
            return '\n';
        },
    }));
    throw new Error('The public browser declarations are not self-contained');
}
const distributed = new Set([resolve(output, 'maui-browser.d.ts'), resolve(output, 'dotnet.d.ts')]);
for (const file of declarations.getSourceFiles()) {
    if (!declarations.isSourceFileDefaultLibrary(file) && !distributed.has(resolve(file.fileName))) {
        throw new Error(`Public declarations depend on an undistributed file: ${file.fileName}`);
    }
}

await build({
    absWorkingDir: root,
    entryPoints: ['src/Wasm.FluentUIWebComponents/web/maui-browser.ts'],
    outfile: 'src/Wasm.FluentUIWebComponents/wwwroot/maui-browser.js',
    bundle: true, format: 'esm', target: 'es2022', legalComments: 'inline',
});