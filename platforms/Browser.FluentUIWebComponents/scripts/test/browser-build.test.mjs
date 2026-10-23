/**
 * @file Backend build/distribution integration tests using real dotnet, npm and
 * TypeScript CLI processes. Cleans and rebuilds the checkout backend in Debug and
 * Release; checks shared contract equality/mtime, configuration-local outputs,
 * design-time skips and clean retaining npm dependencies. A separate cold source
 * fixture starts without node_modules, restore outputs or generated declarations.
 *
 * Source and locally packed/restored NuGet consumers perform real managed SDK
 * publish. Tests check canonical bridge files occur once and host tools do not leak.
 * An isolated frontend installs its own npm dependencies and exercises preparation,
 * TS emit, byte-preserving site assembly, stale removal and missing/colliding inputs.
 * Its native/runtime files are explicit mocks, not executable NativeAOT evidence.
 *
 * Requires .NET 10, Node/npm and configured feeds or complete dependency caches;
 * no Emscripten/browser is needed. Temporary fixtures are deleted, but checkout
 * Release outputs/shared generated files remain and Debug is cleaned again.
 * Do not overlap with other backend/tool builds or sample publishes, even across
 * configurations. The aggregate command serializes this suite with asset builds.
 *
 * @see ../../CONTRIBUTING.md#testing-and-validation
 */
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { copyFileSync, cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, realpathSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = fileURLToPath(new URL('../..', import.meta.url));
const backend = resolve(root, 'src/Browser.FluentUIWebComponents');
const project = join(backend, 'Browser.FluentUIWebComponents.csproj');
const sample = join(root, 'samples/BrowserSample');
const dotnet = process.env.DOTNET_HOST_PATH || 'dotnet';
const assetNames = ['maui-browser.js', 'maui-browser.d.ts', 'dotnet.d.ts'];
const nativeNames = ['dotnet.native.wasm', 'dotnet.native.js', 'dotnet.js'];
const appScripts = ['main.js', 'sample-diagnostics.js'];
const consumerTargets = join(backend, 'buildTransitive/Microsoft.Maui.Platforms.Browser.FluentUIWebComponents.targets');
const toolLeakage = /Browser\.FluentUIWebComponents\.(?:CodeGen|Assets)|Microsoft\.CodeAnalysis|Microsoft\.Build|BuildHost/u;

async function command(executable, args, cwd = root) {
    return await new Promise((resolveResult, reject) => {
        const child = spawn(executable, args, {
            cwd,
            shell: executable === 'npm' && process.platform === 'win32',
            stdio: ['ignore', 'pipe', 'pipe'],
        });
        let output = '';
        child.stdout.on('data', chunk => {
            output += chunk;
        });
        child.stderr.on('data', chunk => {
            output += chunk;
        });
        child.on('error', reject);
        child.on('close', code => {
            if (code !== 0) {
                reject(new Error(`${executable} ${args.join(' ')}\n${output}`));
            } else {
                resolveResult(output);
            }
        });
    });
}

async function run(args) {
    return await command(dotnet, args);
}

async function npm(directory, ...args) {
    return await command('npm', args, directory);
}

async function runJson(args) {
    const output = await run(args);
    // MSBuild emits its JSON object after any target/task logging.
    const start = output.lastIndexOf('\n{');
    const json = start >= 0 ? output.slice(start + 1) : output.trim();
    try {
        return JSON.parse(json);
    } catch (error) {
        throw new Error(`Expected MSBuild JSON for ${args.join(' ')}\n${output}`, { cause: error });
    }
}

async function directory(configuration) {
    const output = await run(['msbuild', project, '-getProperty:BrowserBuildDirectory',
        `-property:Configuration=${configuration}`, '-nologo']);
    return output.trim();
}

function xml(value) {
    return value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
}

function write(path, contents) {
    mkdirSync(dirname(path), { recursive: true });
    writeFileSync(path, contents);
}

function temporaryDirectory(t, prefix) {
    // Node resolves imported module URLs through macOS's /var -> /private/var symlink.
    const temporary = realpathSync(mkdtempSync(prefix));
    t.after(() => {
        rmSync(temporary, { recursive: true, force: true });
    });
    return temporary;
}

function snapshot(directory) {
    const result = new Map();
    function visit(relative) {
        for (const entry of readdirSync(join(directory, relative), { withFileTypes: true })) {
            const path = join(relative, entry.name);
            if (entry.isDirectory()) {
                visit(path);
            } else {
                assert.ok(entry.isFile(), `Unexpected non-file asset: ${path}`);
                result.set(path.replaceAll('\\', '/'), readFileSync(join(directory, path)));
            }
        }
    }
    visit('');
    return result;
}

function assertNoTools(directory) {
    for (const name of snapshot(directory).keys()) {
        assert.doesNotMatch(name, toolLeakage, 'Build-only dependencies must not reach consumer output');
    }
}

function assertAssets(directory, canonicalDirectory) {
    for (const name of assetNames) {
        assert.deepEqual(readFileSync(join(directory, name)), readFileSync(join(canonicalDirectory, name)), name);
    }
}

function createConsumer(parent, name, { reference, properties = '' }) {
    const directory = join(parent, name);
    const consumerProject = join(directory, 'Consumer.csproj');
    const appSource = 'export const fixture = "App-owned JavaScript must not be rewritten";\n';
    write(join(directory, 'web source/main.js'), appSource);
    write(join(directory, 'metadata/nested/info.json'), '{"fixture":"publish content"}\n');
    write(join(directory, 'Fixture.cs'), 'public static class Fixture\n{\n    public static void Main()\n    {\n    }\n}\n');
    // LLVM is private to the backend. This consumer has no compiler package and is
    // deliberately a managed-only app head: the ordinary SDK Publish pipeline
    // must collect and COPY Content, not merely return a simulated publish list.
    // These are local properties, not global overrides of the backend build.
    write(consumerProject, `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RuntimeIdentifier></RuntimeIdentifier>
    <MauiBrowserEnableAppAssets>true</MauiBrowserEnableAppAssets>
    <PublishAot>false</PublishAot>
    <PublishTrimmed>false</PublishTrimmed>
    <SelfContained>false</SelfContained>
    <UseAppHost>false</UseAppHost>
    <DotNetJsApi>false</DotNetJsApi>
    <!-- This managed-only fixture tests Content publishing, not SDK wasm-tools. -->
    <MSBuildEnableWorkloadResolver>false</MSBuildEnableWorkloadResolver>
    <PublishDir>$(MSBuildProjectDirectory)/published/</PublishDir>
    <DefaultItemExcludes>$(DefaultItemExcludes);published/**</DefaultItemExcludes>
${properties}
  </PropertyGroup>
  <ItemGroup>
${reference}
    <Content Include="web source/main.js" TargetPath="main.js" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
    <Content Include="metadata/**/*" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
</Project>\n`);
    assert.doesNotMatch(readFileSync(consumerProject, 'utf8'), /<Import\b|MauiBrowserAssetsDirectory|WasmExtraFilesToDeploy/u,
        'Consumers must neither import backend source targets nor declare asset mirrors');
    return { directory, project: consumerProject, published: join(directory, 'published'), appSource };
}

async function exerciseConsumer(consumer, canonicalDirectory) {
    let destinations;
    for (const phase of ['cold', 'warm', 'deleted-output']) {
        if (phase === 'deleted-output') {
            rmSync(consumer.published, { recursive: true, force: true });
        }
        // A real dotnet publish, including the SDK's resolved-file copy targets.
        await run(['publish', consumer.project, '-c', 'Release', '--nologo']);
        const collected = await runJson(['msbuild', consumer.project,
            '-target:Build;GetCopyToPublishDirectoryItems', '-property:Configuration=Release',
            '-getProperty:TargetDir,PublishDir,PublishAot',
            '-getTargetResult:GetCopyToPublishDirectoryItems', '-nologo']);
        assert.equal(collected.Properties.PublishAot, 'false', 'This fixture must not claim NativeAOT coverage');
        assert.equal(resolve(collected.Properties.PublishDir), consumer.published);
        const items = collected.TargetResults.GetCopyToPublishDirectoryItems.Items;
        assert.ok(Array.isArray(items), 'Inspect the actual SDK publish collector');
        destinations = [];
        for (const name of assetNames) {
            const matches = items.filter(item => {
                return item.TargetPath?.replaceAll('\\', '/') === name;
            });
            assert.equal(matches.length, 1, `${phase}: ${name} must have exactly one SDK publish destination`);
            const item = matches[0];
            assert.equal(resolve(consumer.directory, item.FullPath || item.Identity), resolve(canonicalDirectory, name),
                `${name} must come from its canonical provider`);
            const canonicalItems = items.filter(candidate => {
                return resolve(consumer.directory, candidate.FullPath || candidate.Identity) === resolve(canonicalDirectory, name);
            });
            assert.equal(canonicalItems.length, 1, `${name} must not also publish under a second path`);
            destinations.push(item.TargetPath.replaceAll('\\', '/'));
            assert.ok(!existsSync(join(consumer.directory, 'web source', name)), 'No consumer-side mirror is needed');
        }
        assertAssets(consumer.published, canonicalDirectory);
        assert.deepEqual(JSON.parse(readFileSync(join(consumer.published, 'maui-assets.json'), 'utf8')),
            { version: 1, application: 'Consumer', images: {}, fonts: {}, raw: {} },
            'Managed-only app heads explicitly opt in to the mandatory empty catalog');
        const output = resolve(consumer.directory, collected.Properties.TargetDir);
        assertAssets(output, canonicalDirectory);
        assert.ok(existsSync(join(consumer.published, 'Consumer.dll')), 'Publish must really copy the managed fixture');
        assert.ok(!existsSync(join(consumer.published, 'dotnet.native.wasm')), 'Managed fixture is not an AOT application');
        assert.equal(readFileSync(join(consumer.published, 'main.js'), 'utf8'), consumer.appSource);
        assert.equal(readFileSync(join(consumer.directory, 'web source/main.js'), 'utf8'), consumer.appSource);
        assert.deepEqual(readFileSync(join(consumer.published, 'metadata/nested/info.json')),
            readFileSync(join(consumer.directory, 'metadata/nested/info.json')));
        assertNoTools(output);
        assertNoTools(consumer.published);
    }
    return destinations.sort();
}

async function exerciseFrontend(t, publishedBackend) {
    const isolated = temporaryDirectory(t, join(tmpdir(), 'fluent-browser-frontend-'));
    for (const name of ['package.json', 'package-lock.json', '.gitignore', 'tsconfig.json']) {
        copyFileSync(join(sample, name), join(isolated, name));
    }
    cpSync(join(sample, 'scripts'), join(isolated, 'scripts'), { recursive: true });
    // The real checkout may retain ignored JS from the former wwwroot tsc output.
    // Copy the handwritten HTML, not those stale local build products.
    write(join(isolated, 'wwwroot/index.html'), readFileSync(join(sample, 'wwwroot/index.html')));
    // Copy handwritten TS only: never inherit generated editor files or a local npm install.
    for (const name of ['main.ts', 'sample-diagnostics.ts']) {
        write(join(isolated, 'web', name), readFileSync(join(sample, 'web', name)));
    }
    const published = join(isolated, 'artifacts/maui');
    const compiled = join(isolated, 'artifacts/typescript');
    const site = join(isolated, 'artifacts/wwwroot');
    for (const name of assetNames) {
        write(join(published, name), readFileSync(join(publishedBackend, name)));
    }
    write(join(published, 'maui-assets.json'), readFileSync(join(publishedBackend, 'maui-assets.json')));
    // EXPLICIT MOCK native/runtime files: only site assembly is tested here.
    // These bytes are not executable and do not validate NativeAOT or browser startup.
    for (const name of nativeNames) {
        write(join(published, name), `MOCK native assembly fixture: ${name}\n`);
    }
    write(join(published, 'symbols/fixture.pdb'), Buffer.from([0, 255, 13, 10, 128]));
    write(join(published, 'dotnet.native.js.map'), '{"fixture":"native source map"}\n');
    write(join(published, 'metadata/nested/runtime.json'), '{"fixture":"nested runtime metadata"}\n');
    write(join(isolated, 'wwwroot/static/nested/asset.bin'), Buffer.from([0, 1, 2, 255]));
    // Use real tsc output to test maps and declarations as well as handwritten static files.
    const tsconfig = JSON.parse(readFileSync(join(isolated, 'tsconfig.json'), 'utf8'));
    tsconfig.compilerOptions.sourceMap = true;
    tsconfig.compilerOptions.declaration = true;
    tsconfig.compilerOptions.declarationMap = true;
    write(join(isolated, 'tsconfig.json'), JSON.stringify(tsconfig, null, 2));
    const handwritten = new Map(['main.ts', 'sample-diagnostics.ts'].map(name => {
        return [name, readFileSync(join(isolated, 'web', name))];
    }));
    const inputs = [published, join(isolated, 'wwwroot'), compiled];
    function assertSite() {
        const expected = new Map();
        for (const input of inputs) {
            for (const [name, contents] of snapshot(input)) {
                assert.ok(!expected.has(name), `Unexpected fixture collision: ${name}`);
                expected.set(name, contents);
            }
        }
        assert.deepEqual(snapshot(site), expected, 'The site must preserve EVERY input file byte-for-byte');
        assert.ok(!existsSync(join(isolated, 'artifacts/site-staging')), 'Staging must be cleaned');
    }

    await npm(isolated, 'ci', '--ignore-scripts', '--no-audit');
    assert.ok(existsSync(join(isolated, 'node_modules/typescript/package.json')));
    assert.ok(!existsSync(join(isolated, 'BrowserSample.csproj')),
        'Frontend fixture intentionally has no .NET project, source checkout or NuGet asset graph');
    await npm(isolated, 'run', 'prepare:maui-bridge');
    assertAssets(join(isolated, 'web'), published);
    assert.deepEqual(readdirSync(join(isolated, 'web')).sort(), [...assetNames, ...handwritten.keys()].sort(),
        'Preparation supplies exactly the three public resources, not native/runtime files');
    for (const [name, contents] of handwritten) {
        assert.deepEqual(readFileSync(join(isolated, 'web', name)), contents);
    }
    // build:ts must prepare again, not rely on a previous explicit prepare:maui-bridge.
    rmSync(join(isolated, 'web/maui-browser.d.ts'));
    await npm(isolated, 'run', 'build:ts');
    assertAssets(join(isolated, 'web'), published);
    const beforeCheck = snapshot(compiled);
    await npm(isolated, 'run', 'check');
    assert.deepEqual(snapshot(compiled), beforeCheck, 'check must not emit or clear TypeScript output');
    for (const name of ['main.js', 'main.js.map', 'main.d.ts', 'main.d.ts.map', 'sample-diagnostics.js']) {
        assert.ok(existsSync(join(compiled, name)), `Missing real TypeScript output: ${name}`);
    }
    assert.match(readFileSync(join(compiled, 'main.js'), 'utf8'), /from ['"]\.\/maui-browser\.js['"]/u);
    const diagnosticsUrl = pathToFileURL(join(compiled, 'sample-diagnostics.js'));
    const diagnostics = await import(diagnosticsUrl.href);
    assert.deepEqual(Object.keys(diagnostics).sort(),
        ['afterNextFrame', 'getDownloadedWasmSize', 'getWasmUrl', 'openExternal'].sort());
    assert.equal(diagnostics.getWasmUrl(), new URL('./dotnet.native.wasm', diagnosticsUrl).href);

    await npm(isolated, 'run', 'build:site');
    assertSite();
    // A second build must remove obsolete TS output AND obsolete final-site files.
    write(join(isolated, 'web/obsolete.ts'), 'export const obsolete = true;\n');
    write(join(published, 'metadata/obsolete.json'), '{}\n');
    write(join(isolated, 'wwwroot/static/obsolete.css'), '/* old static asset */\n');
    await npm(isolated, 'run', 'build:ts');
    await npm(isolated, 'run', 'build:site');
    assert.ok(existsSync(join(site, 'obsolete.js')));
    rmSync(join(isolated, 'web/obsolete.ts'));
    rmSync(join(published, 'metadata/obsolete.json'));
    rmSync(join(isolated, 'wwwroot/static/obsolete.css'));
    write(join(site, 'stale-site-only.txt'), 'Must disappear on successful assembly\n');
    await npm(isolated, 'run', 'build:ts');
    assert.ok(!existsSync(join(compiled, 'obsolete.js')), 'build:ts must clear stale compiler output');
    assert.ok(!existsSync(join(compiled, 'obsolete.d.ts.map')), 'No stale declaration maps');
    await npm(isolated, 'run', 'build:site');
    assertSite();

    // Validate all three prepare inputs BEFORE modifying editor resources or TS output.
    for (const missing of assetNames) {
        const path = join(published, missing);
        const saved = readFileSync(path);
        for (const name of assetNames) {
            write(join(isolated, 'web', name), Buffer.concat([
                readFileSync(join(published, name)), Buffer.from('\n// Previous editor resource fixture\n'),
            ]));
        }
        const beforeWeb = snapshot(join(isolated, 'web'));
        const beforeTs = snapshot(compiled);
        const beforeSite = snapshot(site);
        rmSync(path);
        try {
            await assert.rejects(npm(isolated, 'run', 'prepare:maui-bridge'), /Missing published MAUI asset/u);
            await assert.rejects(npm(isolated, 'run', 'build:ts'), /Missing published MAUI asset/u);
            assert.deepEqual(snapshot(join(isolated, 'web')), beforeWeb);
            assert.deepEqual(snapshot(compiled), beforeTs);
            assert.deepEqual(snapshot(site), beforeSite);
        } finally {
            write(path, saved);
            for (const name of assetNames) {
                copyFileSync(join(published, name), join(isolated, 'web', name));
            }
        }
    }
    for (const [input, names] of [[published, [...assetNames, ...nativeNames]], [compiled, appScripts],
        [join(isolated, 'wwwroot'), ['index.html']]]) {
        for (const missing of names) {
            const path = join(input, missing);
            const saved = readFileSync(path);
            const previousSite = snapshot(site);
            rmSync(path);
            try {
                await assert.rejects(npm(isolated, 'run', 'build:site'), /Missing published MAUI asset|Missing compiled application script|ENOENT/u);
                assert.deepEqual(snapshot(site), previousSite, `${missing}: failure must preserve the complete previous site`);
            } finally {
                write(path, saved);
            }
        }
    }
    // Exercise every pair of owners, including nested files and file/directory clashes.
    for (const [owner, name] of [[join(isolated, 'wwwroot'), 'dotnet.js'],
        [compiled, 'maui-browser.js'], [join(isolated, 'wwwroot'), 'main.js'],
        [join(isolated, 'wwwroot'), 'metadata/nested/runtime.json'],
        [join(isolated, 'wwwroot'), 'symbols']]) {
        const path = join(owner, name);
        assert.ok(!existsSync(path), `Collision fixture must not overwrite an existing input: ${path}`);
        const previousSite = snapshot(site);
        write(path, 'Deliberate conflicting fixture file\n');
        try {
            await assert.rejects(npm(isolated, 'run', 'build:site'), /EEXIST|EISDIR|ENOTDIR/u);
            assert.deepEqual(snapshot(site), previousSite, `Collision ${name} must not destroy the previous site`);
            assert.ok(!existsSync(join(isolated, 'artifacts/site-staging')), 'Failed assembly must clean staging');
        } finally {
            rmSync(path);
        }
    }
    await npm(isolated, 'run', 'build:site');
    assertSite();

    // Inject a rename error at each promotion boundary; the filesystem and rollback are real.
    for (const blocked of ['wwwroot', 'site-staging']) {
        const injection = join(isolated, 'fail-site-promotion.mjs');
        write(injection, `import fs from 'node:fs/promises';
import { syncBuiltinESMExports } from 'node:module';
import { basename } from 'node:path';
const rename = fs.rename;
fs.rename = async function (source, destination) {
    if (basename(source) === ${JSON.stringify(blocked)}) {
        const error = new Error('Injected site promotion failure');
        error.code = 'EACCES';
        throw error;
    }
    return await rename(source, destination);
};
syncBuiltinESMExports();
await import('./scripts/build-site.mjs');
`);
        const previousSite = snapshot(site);
        await assert.rejects(command(process.execPath, [injection], isolated), /Injected site promotion failure/u);
        assert.deepEqual(snapshot(site), previousSite, 'Promotion failure must preserve or restore the previous site');
        assert.ok(!existsSync(join(isolated, 'artifacts/site-staging')));
        assert.ok(!readdirSync(join(isolated, 'artifacts')).some(name => {
            return name.startsWith('site-backup-');
        }), 'Successful rollback must leave no backup directory');
    }
}

test('clean builds, configuration isolation, incremental contract and real managed publish consumers', async t => {
    mkdirSync(join(root, 'artifacts'), { recursive: true });
    const temporary = temporaryDirectory(t, join(root, 'artifacts/interop-build-'));
    const release = await directory('Release');
    const debug = await directory('Debug');
    assert.notEqual(release, debug);
    await run(['clean', project, '-c', 'Release', '--nologo']);
    await run(['clean', project, '-c', 'Debug', '--nologo']);
    await run(['restore', project, '-property:RestoreLockedMode=true', '--nologo']);
    // Bootstrap dependencies once, and serialize builds to avoid shared tool locks
    // and simultaneous cold npm ci calls racing on node_modules.
    await run(['msbuild', project, '-target:RestoreBrowserDependencies', '-nologo']);
    for (const configuration of ['Debug', 'Release']) {
        await run(['build', project, '-c', configuration, '--no-restore', '--nologo']);
    }
    for (const path of [release, debug]) {
        for (const name of assetNames) {
            assert.ok(existsSync(join(path, 'wwwroot', name)), `Missing ${path}/${name}`);
        }
        assert.doesNotMatch(readFileSync(join(path, 'wwwroot/maui-browser.d.ts'), 'utf8'),
            /#browser-interop|contracts\.g|components\/define/u);
    }
    const generator = join(root, 'tools/Browser.FluentUIWebComponents.CodeGen/bin/Release/net10.0/Browser.FluentUIWebComponents.CodeGen.dll');
    const debugContract = join(temporary, 'debug.g.ts');
    const releaseContract = join(temporary, 'release.g.ts');
    await run([generator, project, debugContract, '--configuration', 'Debug']);
    await run([generator, project, releaseContract, '--configuration', 'Release']);
    assert.equal(readFileSync(debugContract, 'utf8'), readFileSync(releaseContract, 'utf8'),
        'The backend wire contract must not depend on build configuration');
    const contract = join(backend, 'web/generated/contracts.g.ts');
    assert.equal(readFileSync(contract, 'utf8'), readFileSync(releaseContract, 'utf8'));
    const content = readFileSync(contract, 'utf8');
    const modified = statSync(contract).mtimeMs;
    await run(['build', project, '-c', 'Release', '--no-restore', '--nologo']);
    assert.equal(readFileSync(contract, 'utf8'), content);
    assert.equal(statSync(contract).mtimeMs, modified, 'Unchanged generation must retain its timestamp');
    const npmPackage = join(root, 'node_modules/typescript/package.json');
    const npmModified = statSync(npmPackage).mtimeMs;
    await run(['clean', project, '-c', 'Debug', '--nologo']);
    assert.ok(existsSync(contract), 'Cleaning Debug must not remove shared generated source');
    assert.ok(!existsSync(join(debug, 'wwwroot/maui-browser.js')), 'Clean must remove configuration-specific assets');
    assert.ok(existsSync(join(release, 'wwwroot/maui-browser.js')), 'Clean must not remove other configurations');
    assert.equal(statSync(npmPackage).mtimeMs, npmModified, 'Clean must retain restored product npm dependencies');

    const designTime = join(temporary, 'design-time');
    await run(['msbuild', project, '-target:RestoreBrowserDependencies;GenerateBrowserInterop;BuildBrowserAssets',
        '-property:DesignTimeBuild=true', `-property:BrowserBuildDirectory=${designTime}`, '-nologo']);
    assert.ok(!existsSync(designTime), 'Design-time browser generation/build must be skipped');

    const sourceConsumer = createConsumer(temporary, 'source consumer', {
        reference: `    <ProjectReference Include="${xml(project)}" />`,
    });
    let sourcePaths;
    await t.test('source ProjectReference publishes canonical Content without any explicit import', async () => {
        sourcePaths = await exerciseConsumer(sourceConsumer, join(release, 'wwwroot'));
    });
    await t.test('real local package restores and publishes identical buildTransitive Content', async () => {
        const metadata = await runJson(['msbuild', project, '-property:Configuration=Release',
            '-getProperty:PackageId,ProjectAssetsFile', '-nologo']);
        const packageId = metadata.Properties.PackageId;
        assert.ok(packageId);
        const version = `0.0.0-browser-assets.${randomUUID()}`;
        const feed = join(temporary, 'feed');
        mkdirSync(feed);
        await run(['pack', project, '--no-build', '--no-restore', '-c', 'Release',
            '-p:IsPackable=true', `-p:PackageVersion=${version}`, '-o', feed, '--nologo']);
        assert.ok(existsSync(join(feed, `${packageId}.${version}.nupkg`)));
        const backendAssets = JSON.parse(readFileSync(resolve(backend, metadata.Properties.ProjectAssetsFile), 'utf8'));
        const fallbackFolders = Object.keys(backendAssets.packageFolders);
        assert.ok(fallbackFolders.length > 0);
        const packages = join(temporary, 'packages');
        const packageConsumer = createConsumer(temporary, 'package consumer', {
            reference: `    <PackageReference Include="${xml(packageId)}" Version="${xml(version)}" />`,
            properties: `    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <RestorePackagesPath>${xml(packages)}</RestorePackagesPath>
    <RestoreFallbackFolders>${xml(fallbackFolders.join(';'))}</RestoreFallbackFolders>`,
        });
        // Keep the approved parent feeds/mappings; add only the unique fixture ID.
        write(join(packageConsumer.directory, 'NuGet.config'), `<configuration>
  <packageSources>
    <add key="browser-assets-test" value="${xml(feed)}" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="browser-assets-test">
      <package pattern="${xml(packageId)}" />
    </packageSource>
  </packageSourceMapping>
</configuration>\n`);
        const packageDirectory = join(packages, packageId.toLowerCase(), version);
        assert.ok(!existsSync(packageDirectory), 'Do not satisfy the fixture package from stale cache');
        const packagePaths = await exerciseConsumer(packageConsumer, join(packageDirectory, 'browser-assets'));
        assert.deepEqual(packagePaths, sourcePaths, 'Source and package publish filenames must be identical');
        const restored = JSON.parse(readFileSync(join(packageConsumer.directory, 'obj/project.assets.json'), 'utf8'));
        assert.equal(restored.libraries[`${packageId}/${version}`]?.type, 'package');
        const packageMetadata = JSON.parse(readFileSync(join(packageDirectory, '.nupkg.metadata'), 'utf8'));
        assert.equal(resolve(packageMetadata.source), resolve(feed), 'Restore must use the isolated local feed');
        const imports = readFileSync(join(packageConsumer.directory, 'obj/Consumer.csproj.nuget.g.targets'), 'utf8');
        assert.match(imports, /buildTransitive/u);
        assert.match(imports, /Microsoft\.Maui\.Platforms\.Browser\.FluentUIWebComponents\.targets/u);
        assertAssets(join(packageDirectory, 'browser-assets'), join(release, 'wwwroot'));
        assertAssets(packageConsumer.published, sourceConsumer.published);
    });
    await t.test('isolated sample frontend assembles complete publish output without source/cache queries', async frontendTest => {
        await exerciseFrontend(frontendTest, sourceConsumer.published);
    });
});

test('standard backend build restores npm and generates all assets in a cold source fixture', async t => {
    const checkout = temporaryDirectory(t, join(tmpdir(), 'fluent-backend-cold-'));
    const product = join(checkout, 'platforms/Browser.FluentUIWebComponents');
    const repository = resolve(root, '../..');
    for (const name of ['Directory.Packages.props', 'eng/Versions.props']) {
        write(join(checkout, name), readFileSync(join(repository, name)));
    }
    for (const name of ['Directory.Build.props', 'Directory.Build.targets', 'global.json',
        'NuGet.config', 'package.json', 'package-lock.json']) {
        write(join(product, name), readFileSync(join(root, name)));
    }
    for (const name of ['build-web.mjs', 'prepare-types.mjs', 'typescript-cli.mjs']) {
        write(join(product, 'scripts', name), readFileSync(join(root, 'scripts', name)));
    }
    for (const name of ['src/Browser.FluentUIWebComponents', 'tools']) {
        cpSync(join(root, name), join(product, name), {
            recursive: true,
            filter(source) {
                return !['bin', 'obj', 'generated', 'node_modules', 'dotnet.d.ts'].includes(basename(source));
            },
        });
    }
    const relativeProject = 'src/Browser.FluentUIWebComponents/Browser.FluentUIWebComponents.csproj';
    const copiedBackend = join(product, 'src/Browser.FluentUIWebComponents');
    const npmLock = readFileSync(join(product, 'package-lock.json'));
    assert.ok(!existsSync(join(product, 'node_modules')));
    assert.ok(!existsSync(join(copiedBackend, 'obj')));
    assert.ok(!existsSync(join(copiedBackend, 'web/generated/contracts.g.ts')));
    // No prior restore, npm command or special environment-variable assignment.
    await command(dotnet, ['build', relativeProject, '-c', 'Release', '--nologo'], product);
    assert.ok(existsSync(join(product, 'node_modules/typescript/package.json')));
    assert.ok(existsSync(join(copiedBackend, 'web/generated/contracts.g.ts')));
    const output = join(copiedBackend, 'bin/Release/net10.0/browser-wasm');
    assertAssets(output, join(copiedBackend, 'obj/browser/Release/net10.0/browser-wasm/wwwroot'));
    assert.ok(existsSync(join(output, 'Microsoft.Maui.Platforms.Browser.FluentUIWebComponents.dll')));
    assertNoTools(output);
    assert.deepEqual(readFileSync(join(product, 'package-lock.json')), npmLock, 'npm ci must not rewrite the lockfile');
});

test('backend dependency restore target installs only when absent and skips design-time', async t => {
    const source = readFileSync(project, 'utf8');
    const target = source.match(/<Target\s+Name="RestoreBrowserDependencies"[\s\S]*?<\/Target>/u)?.[0];
    assert.ok(target, 'The backend must expose automatic npm dependency restore');
    assert.match(source, /<Target\s+Name="BuildBrowserAssets"[^>]*DependsOnTargets="RestoreBrowserDependencies;GenerateBrowserInterop"/u);
    assert.match(target, /Command="npm ci"/u);
    // Execute the production target verbatim in a tiny, dependency-free fixture.
    // This tests its condition and working directory, NOT a full cold backend build.
    const isolated = temporaryDirectory(t, join(tmpdir(), 'fluent-browser-restore-'));
    const fixtureProject = join(isolated, 'src/backend/Restore.proj');
    write(fixtureProject, `<Project>\n${target}\n</Project>\n`);
    write(join(isolated, 'package.json'), JSON.stringify({
        name: 'browser-restore-fixture', version: '1.0.0', private: true,
        scripts: { postinstall: 'node install.mjs' },
    }, null, 2));
    write(join(isolated, 'package-lock.json'), JSON.stringify({
        name: 'browser-restore-fixture', version: '1.0.0', lockfileVersion: 3, requires: true,
        packages: { '': { name: 'browser-restore-fixture', version: '1.0.0', hasInstallScript: true } },
    }, null, 2));
    write(join(isolated, 'install.mjs'), `import { appendFileSync, mkdirSync } from 'node:fs';
appendFileSync('restores.log', 'restore\\n');
mkdirSync('node_modules', { recursive: true });
`);
    const args = ['msbuild', fixtureProject, '-target:RestoreBrowserDependencies', '-nologo'];
    await run([...args, '-property:DesignTimeBuild=true']);
    assert.ok(!existsSync(join(isolated, 'restores.log')), 'Design-time must not invoke npm even when dependencies are absent');
    await run(args);
    assert.equal(readFileSync(join(isolated, 'restores.log'), 'utf8'), 'restore\n');
    assert.ok(existsSync(join(isolated, 'node_modules')));
    await run(args);
    assert.equal(readFileSync(join(isolated, 'restores.log'), 'utf8'), 'restore\n', 'Warm builds must not run npm ci');
    rmSync(join(isolated, 'node_modules'), { recursive: true });
    await run(args);
    assert.equal(readFileSync(join(isolated, 'restores.log'), 'utf8'), 'restore\nrestore\n');
});

test('sample MSBuild only publishes MAUI; npm owns preparation, TypeScript and site assembly', async () => {
    const sampleProject = join(sample, 'BrowserSample.csproj');
    const source = readFileSync(sampleProject, 'utf8');
    assert.doesNotMatch(source, /<Import\b|<Exec\b|WasmExtraFilesToDeploy|BrowserSampleJavaScript|BrowserSampleTypeScript/u);
    const properties = await runJson(['msbuild', sampleProject,
        '-getProperty:PublishDir,SiteDirectory,SiteArchive', '-getItem:WasmExtraFilesToDeploy,Content', '-nologo']);
    assert.equal(resolve(properties.Properties.PublishDir), join(sample, 'artifacts/maui'));
    assert.equal(resolve(properties.Properties.SiteDirectory), join(sample, 'artifacts/wwwroot'));
    assert.equal(resolve(properties.Properties.SiteArchive), join(sample, 'artifacts/BrowserSample-site.zip'));
    assert.deepEqual(properties.Items.WasmExtraFilesToDeploy, [], 'Evaluation must not register frontend deployment items');
    assert.deepEqual(properties.Items.Content, [], 'The sample must not publish its frontend through MSBuild Content');
    const evaluated = await run(['msbuild', sampleProject, '-preprocess', '-nologo']);
    assert.doesNotMatch(evaluated, /<Target\s+Name="(?:BuildBrowserBootstrap|PrepareMauiBrowserAssets|GetMauiBrowserAssets|BuildBrowserAssets|RestoreBrowserDependencies)"/u);
    assert.doesNotMatch(evaluated, /<Exec\b[^>]*Command="[^"\n]*npm\b/u);
    assert.doesNotMatch(evaluated, /buildTransitive[/\\]Microsoft\.Maui\.Platforms\.Browser\.FluentUIWebComponents\.targets/u,
        'The source sample must not import package bridge Content a second time');
    assert.match(evaluated, /<Target\s+Name="GenerateMauiBrowserAppAssets"/u,
        'The product build entry must import app-head asset processing for source consumers');
    const packaged = readFileSync(consumerTargets, 'utf8');
    assert.doesNotMatch(packaged, /<Target\b|<Exec\b|Remove=|MauiBrowserAssetsDirectory/u,
        'Package bridge assets remain plain Content, with no frontend preparation pipeline');
    assert.match(packaged, /<Import Project="MauiBrowser\.AppAssets\.targets"/u);
    const manifest = JSON.parse(readFileSync(join(sample, 'package.json'), 'utf8'));
    assert.equal(manifest.scripts.build, 'npm run build:maui-app && npm run build:ts && npm run build:site');
    assert.equal(manifest.scripts['build:maui-app'], 'dotnet publish BrowserSample.csproj');
    assert.equal(manifest.scripts['prepare:maui-bridge'], 'node scripts/prepare-maui-bridge.mjs');
    assert.equal(manifest.scripts['build:ts'], 'npm run prepare:maui-bridge && tsc --project tsconfig.json');
    assert.equal(manifest.scripts['build:site'], 'node scripts/build-site.mjs');
    assert.equal(manifest.scripts.check, 'tsc --project tsconfig.json --noEmit');
    const tsconfig = JSON.parse(readFileSync(join(sample, 'tsconfig.json'), 'utf8'));
    assert.equal(tsconfig.compilerOptions.rootDir, 'web');
    assert.equal(tsconfig.compilerOptions.outDir, 'artifacts/typescript');
    assert.equal(tsconfig.compilerOptions.rootDirs, undefined);
    assert.equal(tsconfig.compilerOptions.paths, undefined);
    for (const name of ['prepare-maui-bridge.mjs', 'build-site.mjs']) {
        assert.doesNotMatch(readFileSync(join(sample, 'scripts', name), 'utf8'),
            /node:child_process|ProjectAssetsFile|project\.assets\.json|BrowserBuildDirectory|\.nuget|GetMauiBrowserAssets|PrepareMauiBrowserAssets|src\/Browser\.FluentUIWebComponents/u,
            'Frontend scripts must consume published files, never query source builds or NuGet caches');
    }
});