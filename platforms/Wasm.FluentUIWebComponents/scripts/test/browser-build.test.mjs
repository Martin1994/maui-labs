import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { runTypeScript } from '../typescript-cli.mjs';

const root = fileURLToPath(new URL('../..', import.meta.url));
const backend = resolve(root, 'src/Wasm.FluentUIWebComponents');
const project = join(backend, 'Wasm.FluentUIWebComponents.csproj');
const dotnet = process.env.DOTNET_HOST_PATH || 'dotnet';
const assetNames = ['maui-browser.js', 'maui-browser.d.ts', 'dotnet.d.ts'];
const consumerTargets = join(backend, 'buildTransitive/Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents.targets');
const consumerItems = ['ContentWithTargetPath', '_NoneWithTargetPath', 'WasmExtraFilesToDeploy'];

async function run(args) {
    return await new Promise((resolveResult, reject) => {
        const child = spawn(dotnet, args, { cwd: root, stdio: ['ignore', 'pipe', 'pipe'] });
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
                reject(new Error(`${args.join(' ')}\n${output}`));
            } else {
                resolveResult(output);
            }
        });
    });
}

async function directory(configuration) {
    const output = await run(['msbuild', project, '-getProperty:BrowserBuildDirectory',
        `-property:Configuration=${configuration}`, '-nologo']);
    return output.trim();
}

function xml(value) {
    return value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
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

function createConsumer(parent, name, { assetsDirectory, reference, properties = '', explicitImport = false }) {
    const directory = join(parent, name);
    const assets = join(directory, assetsDirectory);
    const appDirectories = new Set(['wwwroot', assetsDirectory]);
    const appFiles = [];
    const appSource = 'export const fixture = "App-owned JavaScript must not be rewritten";\n';
    for (const appDirectory of appDirectories) {
        mkdirSync(join(directory, appDirectory), { recursive: true });
        const appFile = join(directory, appDirectory, 'main.js');
        writeFileSync(appFile, appSource);
        appFiles.push(appFile);
    }
    const sentinel = join(directory, 'unused assets/maui-browser.js');
    mkdirSync(join(directory, 'unused assets'), { recursive: true });
    writeFileSync(sentinel, 'An unrelated directory is not owned by asset preparation.\n');
    const globs = [...appDirectories].map(appDirectory => {
        return `    <Content Include="${xml(appDirectory)}/*" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
    <WasmExtraFilesToDeploy Include="${xml(appDirectory)}/*" />`;
    }).join('\n');
    const checks = assetNames.map(name => {
        return `    <Error Condition="!Exists('$(MSBuildProjectDirectory)/$(MauiBrowserAssetsDirectory)/${name}')" Text="Missing prepared browser asset: ${name}" />`;
    }).join('\n');
    const importElement = explicitImport ? `  <Import Project="${xml(consumerTargets)}" />\n` : '';
    const consumerProject = join(directory, 'Consumer.csproj');
    writeFileSync(consumerProject, `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- This is an asset-pipeline class library, not a NativeAOT application. -->
    <PublishTrimmed>false</PublishTrimmed>
    <SelfContained>false</SelfContained>
    <MauiBrowserAssetsDirectory>${xml(assetsDirectory)}</MauiBrowserAssetsDirectory>
${properties}
  </PropertyGroup>
  <ItemGroup>
${reference}
${globs}
  </ItemGroup>
${importElement}  <Target Name="Check" BeforeTargets="CoreCompile" DependsOnTargets="PrepareMauiBrowserAssets">
${checks}
    <Message Importance="high" Text="Browser fixture assets checked before CoreCompile." />
  </Target>
</Project>\n`);
    return { directory, project: consumerProject, assets, appFiles, appSource, sentinel };
}

function assertPrepared(consumer, canonicalDirectory) {
    for (const name of assetNames) {
        assert.deepEqual(readFileSync(join(consumer.assets, name)), readFileSync(join(canonicalDirectory, name)),
            `${name} must mirror the canonical provider`);
    }
    for (const appFile of consumer.appFiles) {
        assert.equal(readFileSync(appFile, 'utf8'), consumer.appSource);
    }
    assert.equal(readFileSync(consumer.sentinel, 'utf8'),
        'An unrelated directory is not owned by asset preparation.\n');
}

function itemPath(consumer, item) {
    return resolve(consumer.directory, item.FullPath || item.Identity);
}

function assertFiltered(consumer, result) {
    const mirrors = assetNames.map(name => {
        return join(consumer.assets, name);
    });
    for (const itemName of consumerItems) {
        const items = result.Items[itemName];
        assert.ok(Array.isArray(items), `Missing evaluated item list: ${itemName}`);
        for (const item of items) {
            assert.ok(!mirrors.includes(itemPath(consumer, item)),
                `${itemName} must not publish/deploy the local mirror ${item.Identity}`);
        }
        if (itemName === '_NoneWithTargetPath') {
            assert.ok(items.some(item => {
                return itemPath(consumer, item) === consumer.sentinel;
            }), 'A matching filename outside MauiBrowserAssetsDirectory must remain in None');
        }
        // SDKs may omit Content items from None; Content and the native deploy list
        // must both retain the app's own files while filtering only the three mirrors.
        if (itemName !== '_NoneWithTargetPath') {
            for (const appFile of consumer.appFiles) {
                assert.ok(items.some(item => {
                    return itemPath(consumer, item) === appFile;
                }), `${itemName} lost the app-owned file ${appFile}`);
            }
        }
    }
}

async function assertConsumerBuild(consumer, canonicalDirectory, cold) {
    const args = ['msbuild', consumer.project, '-target:Build;AssignTargetPaths',
        '-property:Configuration=Release', '-getProperty:TargetDir', `-getItem:${consumerItems.join(',')}`, '-nologo'];
    if (cold) {
        args.push('-restore');
    }
    const result = await runJson(args);
    assertFiltered(consumer, result);
    assertPrepared(consumer, canonicalDirectory);
    const output = resolve(consumer.directory, result.Properties.TargetDir);
    for (const name of assetNames) {
        assert.deepEqual(readFileSync(join(output, name)), readFileSync(join(canonicalDirectory, name)));
    }
    const files = readdirSync(output, { recursive: true });
    assert.ok(!files.some(name => {
        return /Wasm\.FluentUIWebComponents\.CodeGen|Microsoft\.CodeAnalysis|Microsoft\.Build|BuildHost/u.test(name);
    }), 'Build-only CodeGen, Roslyn and MSBuild dependencies must not reach the consumer output');

    // Inspect the actual SDK publish item collector without invoking LLVM/native compilation.
    // Keep reference negotiation in the same MSBuild invocation as publication collection.
    const publish = await runJson(['msbuild', consumer.project, '-target:Build;GetCopyToPublishDirectoryItems',
        '-property:Configuration=Release', '-getProperty:TargetDir',
        '-getTargetResult:GetCopyToPublishDirectoryItems', '-nologo']);
    const items = publish.TargetResults.GetCopyToPublishDirectoryItems.Items;
    assert.ok(Array.isArray(items), 'The SDK publish collector must return its actual items');
    for (const name of assetNames) {
        const matches = items.filter(item => {
            return item.TargetPath?.replaceAll('\\', '/') === name;
        });
        assert.equal(matches.length, 1, `${name} must have exactly one logical publish destination`);
        assert.equal(itemPath(consumer, matches[0]), resolve(canonicalDirectory, name),
            `${name} must publish from its canonical provider, not the editor mirror`);
    }
    for (const item of items) {
        for (const name of assetNames) {
            assert.notEqual(itemPath(consumer, item), join(consumer.assets, name),
                `Publish collector retained the local mirror ${item.Identity}`);
        }
    }
    for (const appFile of consumer.appFiles) {
        assert.ok(items.some(item => {
            return itemPath(consumer, item) === appFile;
        }), `Publish collector lost the app-owned file ${appFile}`);
    }
}

async function exerciseConsumer(consumer, canonicalDirectory, { prepareWithoutRestore = false } = {}) {
    for (const name of assetNames) {
        assert.ok(!existsSync(join(consumer.assets, name)), 'The consumer must start without generated assets');
    }
    if (prepareWithoutRestore) {
        const consumerRestore = join(consumer.directory, 'obj/project.assets.json');
        assert.ok(!existsSync(consumerRestore));
        await run(['msbuild', consumer.project, '-target:PrepareMauiBrowserAssets',
            '-property:Configuration=Release', '-nologo']);
        assertPrepared(consumer, canonicalDirectory);
        assert.ok(!existsSync(consumerRestore), 'Explicit preparation must not require a consumer restore');
        // Return to a cold filesystem so Check proves build-time preparation as well.
        for (const name of assetNames) {
            rmSync(join(consumer.assets, name));
        }
    }
    await assertConsumerBuild(consumer, canonicalDirectory, true);
    // Re-evaluation now includes warm Content, None and WasmExtraFilesToDeploy globs.
    await assertConsumerBuild(consumer, canonicalDirectory, false);
    rmSync(join(consumer.assets, 'maui-browser.d.ts'));
    await run(['msbuild', consumer.project, '-target:PrepareMauiBrowserAssets',
        '-property:Configuration=Release', '-nologo']);
    assertPrepared(consumer, canonicalDirectory);
}

test('clean builds, configuration isolation, incremental output and build-only consumer reference', async t => {
    mkdirSync(join(root, 'artifacts'), { recursive: true });
    const temporary = mkdtempSync(join(root, 'artifacts/interop-build-'));
    t.after(() => {
        rmSync(temporary, { recursive: true, force: true });
    });
    const release = await directory('Release');
    const debug = await directory('Debug');
    assert.notEqual(release, debug);
    // Clean only this product's build outputs; no source or published app is touched.
    await run(['clean', project, '-c', 'Release', '--nologo']);
    await run(['clean', project, '-c', 'Debug', '--nologo']);
    await run(['restore', project, '-property:RestoreLockedMode=true', '--nologo']);
    await Promise.all(['Debug', 'Release'].map(configuration => {
        return run(['build', project, '-c', configuration, '--no-restore', '--nologo']);
    }));

    for (const path of [release, debug]) {
        for (const name of ['maui-browser.js', 'maui-browser.d.ts', 'dotnet.d.ts']) {
            assert.ok(existsSync(join(path, 'wwwroot', name)), `Missing ${path}/${name}`);
        }
        const declarations = readFileSync(join(path, 'wwwroot/maui-browser.d.ts'), 'utf8');
        assert.doesNotMatch(declarations, /#browser-interop|contracts\.g|components\/define/u);
    }
    const generator = join(root, 'tools/Wasm.FluentUIWebComponents.CodeGen/bin/Release/net10.0/Wasm.FluentUIWebComponents.CodeGen.dll');
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
    assert.equal(statSync(contract).mtimeMs, modified);
    await run(['clean', project, '-c', 'Debug', '--nologo']);
    assert.ok(existsSync(contract), 'Cleaning Debug must not remove shared generated source');
    assert.ok(existsSync(join(release, 'wwwroot/maui-browser.js')));

    // A compile-only browser consumer does not need a native LLVM compiler host.
    // It verifies the same ProjectReference content-copy path used by applications.
    const consumer = join(temporary, 'Consumer.csproj');
    writeFileSync(consumer, `<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="${xml(project)}" />
  </ItemGroup>
</Project>\n`);
    await run(['build', consumer, '-c', 'Release', '--nologo']);
    const output = join(temporary, 'bin/Release/net10.0/browser-wasm');
    const files = readdirSync(output, { recursive: true });
    assert.ok(files.includes('maui-browser.js'));
    assert.ok(files.includes('maui-browser.d.ts'));
    assert.ok(files.includes('dotnet.d.ts'));
    assert.ok(!files.some(name => {
        return /Wasm\.FluentUIWebComponents\.CodeGen|Microsoft\.CodeAnalysis|Microsoft\.Build|BuildHost/u.test(name);
    }), 'The generator and Roslyn must not be copied to a consumer');
    assert.equal(readFileSync(join(output, 'maui-browser.js'), 'utf8'),
        readFileSync(join(release, 'wwwroot/maui-browser.js'), 'utf8'));

    const designTime = join(temporary, 'design-time');
    await run(['msbuild', project, '-target:GenerateBrowserInterop', '-property:DesignTimeBuild=true',
        `-property:BrowserBuildDirectory=${designTime}`, '-nologo']);
    assert.ok(!existsSync(designTime), 'Design-time generation must be skipped');

    // Keep all consumer scenarios after the initial builds/cleans, never in parallel.
    await t.test('source opt-in assets in a relative directory with spaces', async () => {
        const sourceConsumer = createConsumer(temporary, 'source-consumer', {
            assetsDirectory: 'web source',
            reference: `    <ProjectReference Include="${xml(project)}" />`,
            explicitImport: true,
        });
        await exerciseConsumer(sourceConsumer, join(release, 'wwwroot'), { prepareWithoutRestore: true });
    });

    await t.test('real local package auto-imports buildTransitive assets', async () => {
        const metadata = await runJson(['msbuild', project, '-property:Configuration=Release',
            '-getProperty:PackageId,ProjectAssetsFile', '-nologo']);
        const packageId = metadata.Properties.PackageId;
        assert.ok(packageId, 'The backend must expose its actual PackageId');
        const version = `0.0.0-browser-assets.${randomUUID()}`;
        const feed = join(temporary, 'feed');
        mkdirSync(feed);
        await run(['pack', project, '--no-build', '--no-restore', '-c', 'Release',
            '-p:IsPackable=true', `-p:PackageVersion=${version}`, '-o', feed, '--nologo']);
        assert.ok(existsSync(join(feed, `${packageId}.${version}.nupkg`)), 'A real fixture package must be produced');

        const backendAssets = JSON.parse(readFileSync(resolve(backend, metadata.Properties.ProjectAssetsFile), 'utf8'));
        const fallbackFolders = Object.keys(backendAssets.packageFolders);
        assert.ok(fallbackFolders.length > 0, 'Reuse the restored backend dependency folders');
        const packages = join(temporary, 'packages');
        const packageConsumer = createConsumer(temporary, 'package-consumer', {
            assetsDirectory: 'wwwroot',
            reference: `    <PackageReference Include="${xml(packageId)}" Version="${xml(version)}" />`,
            properties: `    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <RestorePackagesPath>${xml(packages)}</RestorePackagesPath>
    <RestoreFallbackFolders>${xml(fallbackFolders.join(';'))}</RestoreFallbackFolders>`,
        });
        // No clear: inherit the approved parent feeds/mappings, adding only our exact test ID.
        writeFileSync(join(packageConsumer.directory, 'NuGet.config'), `<configuration>
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
        assert.ok(!existsSync(packageDirectory), 'The unique test package must not come from a stale cache');
        await exerciseConsumer(packageConsumer, join(packageDirectory, 'browser-assets'));

        const restored = JSON.parse(readFileSync(join(packageConsumer.directory, 'obj/project.assets.json'), 'utf8'));
        assert.equal(restored.libraries[`${packageId}/${version}`]?.type, 'package',
            'The consumer must resolve the exact package, not a project reference');
        const packageMetadata = JSON.parse(readFileSync(join(packageDirectory, '.nupkg.metadata'), 'utf8'));
        assert.equal(resolve(packageMetadata.source), resolve(feed), 'The fixture must restore from its local feed');
        const imports = readFileSync(join(packageConsumer.directory, 'obj/Consumer.csproj.nuget.g.targets'), 'utf8');
        assert.ok(imports.includes('buildTransitive'), 'NuGet must generate the transitive target import');
        assert.ok(imports.includes('Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents.targets'));
        for (const name of assetNames) {
            assert.deepEqual(readFileSync(join(packageDirectory, 'browser-assets', name)),
                readFileSync(join(release, 'wwwroot', name)), `The real package must contain canonical ${name}`);
        }
    });
});

test('TypeScript sample registers cold outputs and emits them before native deployment', async t => {
    const sample = join(root, 'samples/BrowserSample');
    const sampleProject = join(sample, 'BrowserSample.csproj');
    const scripts = ['main.js', 'sample-diagnostics.js'];
    const saved = new Map(scripts.map(name => {
        const path = join(sample, 'wwwroot', name);
        return [path, existsSync(path) ? readFileSync(path) : undefined];
    }));
    t.after(() => {
        for (const [path, contents] of saved) {
            if (contents === undefined) {
                rmSync(path, { force: true });
            } else {
                writeFileSync(path, contents);
            }
        }
    });
    for (const path of saved.keys()) {
        rmSync(path, { force: true });
    }
    const expected = ['wwwroot/index.html', ...scripts.map(name => {
        return `wwwroot/${name}`;
    })].sort();
    const assertDeployItems = result => {
        const items = result.Items.WasmExtraFilesToDeploy.map(item => {
            return item.Identity.replaceAll('\\', '/');
        }).sort();
        assert.deepEqual(items, expected, 'Only app HTML and emitted JS should use native extra-file deployment');
    };
    // Evaluation must register JS paths even before tsc has created those files.
    const cold = await runJson(['msbuild', sampleProject,
        '-getItem:WasmExtraFilesToDeploy', '-nologo']);
    assertDeployItems(cold);
    await run(['restore', project, '--locked-mode', '--nologo']);
    for (const phase of ['cold', 'warm']) {
        // This target needs the backend, not the sample's native compiler-host package.
        const built = await runJson(['msbuild', sampleProject, '-target:BuildBrowserBootstrap',
            '-property:Configuration=Release', '-getItem:WasmExtraFilesToDeploy,FileWrites', '-nologo']);
        assertDeployItems(built);
        for (const name of scripts) {
            const path = join(sample, 'wwwroot', name);
            assert.ok(existsSync(path), `${phase} build did not emit ${name}`);
            assert.ok(built.Items.FileWrites.some(item => {
                return resolve(sample, item.Identity) === path;
            }), `Clean must know about generated ${name}`);
        }
    }
    runTypeScript(['--project', join(sample, 'tsconfig.json'), '--noEmit'], { cwd: sample, packageRoot: sample });
    const diagnosticsUrl = pathToFileURL(join(sample, 'wwwroot/sample-diagnostics.js'));
    const diagnostics = await import(diagnosticsUrl.href);
    assert.deepEqual(Object.keys(diagnostics).sort(),
        ['afterNextFrame', 'getDownloadedWasmSize', 'getWasmUrl', 'openExternal'].sort());
    assert.equal(diagnostics.getWasmUrl(), new URL('./dotnet.native.wasm', diagnosticsUrl).href);
    assert.match(readFileSync(join(sample, 'wwwroot/main.js'), 'utf8'), /from ['"]\.\/maui-browser\.js['"]/u);
});

test('sample frontend scripts work outside the checkout with their own npm installation', async t => {
    const sample = join(root, 'samples/BrowserSample');
    const isolated = mkdtempSync(join(tmpdir(), 'fluent-browser-sample-'));
    t.after(() => {
        rmSync(isolated, { recursive: true, force: true });
    });
    mkdirSync(join(isolated, 'web'));
    for (const name of ['package.json', 'package-lock.json', '.gitignore', 'tsconfig.json']) {
        copyFileSync(join(sample, name), join(isolated, name));
    }
    // Public assets are supplied by the backend, just as with a restored NuGet package.
    const assets = join(await directory('Release'), 'wwwroot');
    for (const name of assetNames) {
        copyFileSync(join(assets, name), join(isolated, 'web', name));
    }
    for (const name of ['main.ts', 'sample-diagnostics.ts']) {
        copyFileSync(join(sample, 'web', name), join(isolated, 'web', name));
    }
    for (const args of [['ci', '--ignore-scripts', '--no-audit'], ['run', 'build'], ['run', 'check']]) {
        await new Promise((resolveResult, reject) => {
            const child = spawn('npm', args, {
                cwd: isolated, shell: process.platform === 'win32', stdio: ['ignore', 'pipe', 'pipe'],
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
                if (code === 0) {
                    resolveResult();
                } else {
                    reject(new Error(`Isolated npm ${args.join(' ')} failed: ${output}`));
                }
            });
        });
    }
    assert.ok(existsSync(join(isolated, 'node_modules/typescript/package.json')));
    assert.deepEqual(readdirSync(join(isolated, 'wwwroot')).sort(), ['main.js', 'sample-diagnostics.js']);
    const paths = await runJson(['msbuild', join(sample, 'BrowserSample.csproj'),
        '-getProperty:SiteDirectory,SiteArchive', '-nologo']);
    assert.equal(resolve(paths.Properties.SiteDirectory), join(sample, 'artifacts/wwwroot'));
    assert.equal(resolve(paths.Properties.SiteArchive), join(sample, 'artifacts/BrowserSample-site.zip'));
});