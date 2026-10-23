/**
 * @file App-resource build integration tests using real Assets tasks and SDK
 * build/publish. The source app inherits product targets and references a small
 * fixture library; the external consumer restores a locally packed backend NuGet.
 * The source fixture is not a full backend application. Both paths check catalog
 * determinism, resource bytes, names/aliases, metadata, activation, missing outputs,
 * removal/rename/Clean, configuration handover and ownership/collision protection.
 * Additional cases cover APNG rejection, publish RelativePath and symlink safety.
 *
 * Requires .NET 10, package feeds/caches and a completed Release backend build:
 * packing uses --no-build --no-restore. Source targets restore/build the host task.
 * Fixtures use temporary directories, removed afterward; their owned build and
 * custom publish outputs are deliberately mutated and cleaned by the tests.
 * Some symlink checks are skipped on Windows. Do not overlap with backend builds.
 * These are managed publish tests, not NativeAOT/browser runs; most file bytes are
 * opaque fixtures and make no image-decoding or font-rendering claim.
 *
 * @see ../../CONTRIBUTING.md#testing-and-validation
 */
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, realpathSync, renameSync, rmSync, statSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../..', import.meta.url));
const backend = join(root, 'src/Wasm.FluentUIWebComponents/Wasm.FluentUIWebComponents.csproj');
const taskProject = join(root, 'tools/Wasm.FluentUIWebComponents.Assets/Wasm.FluentUIWebComponents.Assets.csproj');
const dotnet = process.env.DOTNET_HOST_PATH || 'dotnet';
const catalogName = 'maui-assets.json';

async function run(args, cwd = root) {
    return await new Promise((done, reject) => {
        const child = spawn(dotnet, args, { cwd, stdio: ['ignore', 'pipe', 'pipe'] });
        let output = '';
        child.stdout.on('data', chunk => { output += chunk; });
        child.stderr.on('data', chunk => { output += chunk; });
        child.on('error', reject);
        child.on('close', code => {
            if (code === 0) {
                done(output);
            } else {
                reject(new Error(`${dotnet} ${args.join(' ')}\n${output}`));
            }
        });
    });
}

function write(path, value) {
    mkdirSync(dirname(path), { recursive: true });
    writeFileSync(path, value);
}

function xml(value) {
    // MSBuild item/property values need escaping as well as XML escaping.
    return value.replaceAll('%', '%25').replaceAll(';', '%3b').replaceAll('&', '&amp;')
        .replaceAll('"', '&quot;').replaceAll('<', '&lt;');
}

function temporary(t, parent) {
    mkdirSync(parent, { recursive: true });
    // macOS tmpdir can itself contain /var -> /private/var. Fixtures use a real
    // root; intentional redirects are created explicitly in the safety tests.
    const directory = realpathSync(mkdtempSync(join(parent, 'browser-assets-')));
    t.after(() => { rmSync(directory, { recursive: true, force: true }); });
    return directory;
}

function fixture(directory, reference = '', properties = '') {
    const project = join(directory, 'Consumer.csproj');
    const published = join(directory, 'published');
    const output = join(directory, 'bin/Release/net10.0');
    const state = join(directory, 'obj/Release/net10.0/maui-browser-assets');
    write(join(directory, 'Program.cs'), 'System.Console.WriteLine("Managed-only packaging fixture, not NativeAOT verification.");\n');
    write(join(directory, 'Assets.items'), '<Project />');
    write(project, `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <RuntimeIdentifier></RuntimeIdentifier>
    <MauiBrowserEnableAppAssets>true</MauiBrowserEnableAppAssets>
    <PublishAot>false</PublishAot>
    <PublishTrimmed>false</PublishTrimmed>
    <SelfContained>false</SelfContained>
    <UseAppHost>false</UseAppHost>
    <DotNetJsApi>false</DotNetJsApi>
    <MSBuildEnableWorkloadResolver>false</MSBuildEnableWorkloadResolver>
    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
    <PublishDir>$(MSBuildProjectDirectory)/published/</PublishDir>
    <DefaultItemExcludes>$(DefaultItemExcludes);published/**;custom/**;Resources/**</DefaultItemExcludes>
${properties}
  </PropertyGroup>
  <ItemGroup>${reference}</ItemGroup>
  <Import Project="Assets.items" />
</Project>`);
    return { directory, project, published, output, state };
}

function items(consumer, declarations) {
    write(join(consumer.directory, 'Assets.items'), `<Project><ItemGroup>${declarations}</ItemGroup></Project>`);
}

function catalog(directory) {
    return JSON.parse(readFileSync(join(directory, catalogName), 'utf8'));
}

async function publish(consumer, extra = [], configuration = 'Release') {
    return await run(['publish', consumer.project, '-c', configuration, '-m:1', '-nodeReuse:false', '--nologo', ...extra]);
}

async function generate(consumer, extra = []) {
    return await run(['msbuild', consumer.project, '-t:GenerateMauiBrowserAppAssets', '-p:Configuration=Release',
        '-m:1', '-nodeReuse:false', '-nologo', ...extra]);
}

async function clean(consumer, configuration) {
    return await run(['clean', consumer.project, '-c', configuration, '-p:BuildProjectReferences=false',
        '-m:1', '-nodeReuse:false', '--nologo']);
}

function ownershipFiles(consumer) {
    const directory = join(consumer.directory, 'obj/maui-browser-assets/ownership');
    return readdirSync(directory, { recursive: true }).filter(name => name.endsWith('.json')).map(name => join(directory, name));
}

function snapshot(directory) {
    const result = new Map();
    for (const name of readdirSync(directory, { recursive: true })) {
        const path = join(directory, name);
        if (statSync(path).isFile()) {
            result.set(name, readFileSync(path));
        }
    }
    return result;
}

// Invoke the real host task without the SDK first writing into a redirected output.
// SDK integration is covered separately by the build/publish lifecycle tests.
async function own(consumer, { destination = '', clean: cleaning = false, assets = [],
    configuration = 'Release', targetFramework = 'net10.0', runtimeIdentifier = '', project = consumer.project } = {}) {
    const probe = join(consumer.directory, 'Ownership.proj');
    const assembly = join(dirname(taskProject), 'bin/Release/net10.0/Wasm.FluentUIWebComponents.Assets.dll');
    write(probe, `<Project>
  <UsingTask TaskName="MauiBrowser.Build.AssetOwnership" AssemblyFile="${xml(assembly)}" />
  <ItemGroup>${assets.map(([source, target]) => `<Asset Include="${xml(source)}" TargetPath="${xml(target)}" />`).join('')}</ItemGroup>
  <Target Name="Check">
    <AssetOwnership StateDirectory="${xml(join(consumer.directory, 'obj/maui-browser-assets'))}"
      ProjectFullPath="${xml(project)}" Configuration="${xml(configuration)}"
      TargetFramework="${xml(targetFramework)}" RuntimeIdentifier="${xml(runtimeIdentifier)}"
      Destination="${xml(destination)}" Assets="@(Asset)" Clean="${cleaning}" />
  </Target>
</Project>`);
    return await run(['msbuild', probe, '-t:Check', '-m:1', '-nodeReuse:false', '-nologo']);
}

function assertNoTools(directory) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
        assert.doesNotMatch(entry.name, /Wasm\.FluentUIWebComponents\.(Assets|CodeGen)|Microsoft\.Build|Microsoft\.CodeAnalysis/u);
        if (entry.isDirectory()) {
            assertNoTools(join(directory, entry.name));
        }
    }
    const deps = join(directory, 'Consumer.deps.json');
    if (existsSync(deps)) {
        assert.doesNotMatch(readFileSync(deps, 'utf8'), /Wasm\.FluentUIWebComponents\.(Assets|CodeGen)|Microsoft\.Build|Microsoft\.CodeAnalysis/u);
    }
}

async function exerciseLifecycle(t, consumer, fullPublish = true) {
    const external = join(consumer.directory, '../linked sources');
    const special = '字 space #%25;data';
    const values = new Map([
        ['Resources/image.PNG', Buffer.from([0, 1, 2, 255])],
        ['Resources/photo.jpg', Buffer.from([255, 216, 255, 217])],
        ['Resources/photo.jpeg', Buffer.from([255, 216, 255, 217, 0])],
        ['Resources/icon.svg', Buffer.from('<svg xmlns="http://www.w3.org/2000/svg"/>')],
        ['Resources/font.ttf', Buffer.from([0, 1, 0, 0, 42])],
        ['Resources/font.otf', Buffer.from('OTTO fixture bytes')],
        [`Resources/${special}.bin`, Buffer.from([0, 255, 128])],
    ]);
    // Format fixtures test byte-preserving packaging, not decoder validity or font rendering.
    for (const [name, bytes] of values) {
        write(join(consumer.directory, name), bytes);
    }
    write(join(external, 'linked.svg'), '<svg/>');
    write(join(external, 'outside.txt'), 'outside');
    const declarations = `
      <MauiImage Include="Resources/image.PNG" Link="nested/renamed.PNG" Resize="false" />
      <MauiImage Include="Resources/photo.jpg;Resources/photo.jpeg;Resources/icon.svg" />
      <MauiImage Include="${xml(join(external, 'linked.svg'))}" Link="virtual/linked.svg" />
      <MauiFont Include="Resources/font.ttf" Link="font links/font renamed.ttf" />
      <MauiFont Include="Resources/font.otf" />
      <MauiAsset Include="Resources/${xml(special)}.bin" LogicalName="ignored.bin" Link="nested/${xml(special)}.bin" />
      <MauiAsset Include="Resources/photo.jpg" LogicalName="logical/priority.jpg" />
      <MauiAsset Include="${xml(join(consumer.directory, 'Resources/font.ttf'))}" />
      <MauiAsset Include="${xml(join(external, 'outside.txt'))}" />`;
    items(consumer, declarations);
    await publish(consumer, fullPublish ? [] : ['--no-restore', '-p:BuildProjectReferences=false']);
    const expected = {
        version: 1, application: 'Consumer',
        images: {
            'icon.png': 'maui-assets/images/icon.svg', 'icon.svg': 'maui-assets/images/icon.svg',
            'linked.png': 'maui-assets/images/linked.svg', 'linked.svg': 'maui-assets/images/linked.svg',
            'photo.jpeg': 'maui-assets/images/photo.jpeg', 'photo.jpg': 'maui-assets/images/photo.jpg',
            'renamed.PNG': 'maui-assets/images/renamed.PNG',
        },
        fonts: { 'font renamed.ttf': 'maui-assets/fonts/font renamed.ttf', 'font.otf': 'maui-assets/fonts/font.otf' },
        raw: {
            'Resources/font.ttf': 'maui-assets/raw/Resources/font.ttf',
            'logical/priority.jpg': 'maui-assets/raw/logical/priority.jpg',
            [`nested/${special}.bin`]: `maui-assets/raw/nested/${special}.bin`,
            'outside.txt': 'maui-assets/raw/outside.txt',
        },
    };
    const originalCatalog = readFileSync(join(consumer.state, catalogName));
    const timestamp = statSync(join(consumer.state, catalogName)).mtimeMs;
    for (const directory of [consumer.output, consumer.published]) {
        assert.deepEqual(catalog(directory), expected);
        assert.equal(catalog(directory).images['RENAMED.PNG'], undefined, 'Lookup is ordinal, not normalized to lowercase');
        assert.deepEqual(readFileSync(join(directory, expected.images['renamed.PNG'])), values.get('Resources/image.PNG'));
        assert.deepEqual(readFileSync(join(directory, expected.images['photo.jpg'])), values.get('Resources/photo.jpg'));
        assert.deepEqual(readFileSync(join(directory, expected.images['photo.jpeg'])), values.get('Resources/photo.jpeg'));
        assert.deepEqual(readFileSync(join(directory, expected.images['icon.png'])), values.get('Resources/icon.svg'));
        assert.ok(!existsSync(join(directory, 'maui-assets/images/icon.png')), 'SVG alias never produces a renamed or rasterized PNG file');
        assert.equal(readFileSync(join(directory, expected.images['linked.svg']), 'utf8'), '<svg/>');
        assert.deepEqual(readFileSync(join(directory, expected.fonts['font renamed.ttf'])), values.get('Resources/font.ttf'));
        assert.deepEqual(readFileSync(join(directory, expected.fonts['font.otf'])), values.get('Resources/font.otf'));
        assert.deepEqual(readFileSync(join(directory, expected.raw[`nested/${special}.bin`])), values.get(`Resources/${special}.bin`));
        assertNoTools(directory);
        write(join(directory, 'host-owned.txt'), 'retain host file');
        write(join(directory, 'maui-assets/raw/host-owned.txt'), 'retain nested host file');
    }

    await t.test('unchanged catalog, missing files, source updates, ordering and configuration isolation', async () => {
        const incremental = ['--no-restore', '-p:BuildProjectReferences=false'];
        await publish(consumer, incremental);
        assert.equal(statSync(join(consumer.state, catalogName)).mtimeMs, timestamp);
        for (const directory of [consumer.output, consumer.published]) {
            rmSync(join(directory, catalogName));
            rmSync(join(directory, expected.images['icon.png']));
        }
        await publish(consumer, incremental);
        assert.deepEqual(catalog(consumer.published), expected);
        assert.deepEqual(readFileSync(join(consumer.output, expected.images['icon.svg'])), values.get('Resources/icon.svg'));
        write(join(consumer.directory, 'Resources/icon.svg'), '<svg><!-- updated --></svg>');
        await publish(consumer, incremental);
        assert.equal(readFileSync(join(consumer.published, expected.images['icon.png']), 'utf8'), '<svg><!-- updated --></svg>');
        items(consumer, declarations.trim().split('\n').reverse().join('\n'));
        await generate(consumer);
        assert.deepEqual(readFileSync(join(consumer.state, catalogName)), originalCatalog);
        assert.equal(statSync(join(consumer.state, catalogName)).mtimeMs, timestamp);
        await generate(consumer, ['-p:Configuration=Debug']);
        assert.ok(existsSync(join(consumer.directory, 'obj/Debug/net10.0/maui-browser-assets/maui-assets.json')));
        assert.ok(existsSync(join(consumer.state, catalogName)));
    });

    await t.test('rename, removal, changed publish dir, file-directory transitions and Clean use ownership', async () => {
        const custom = join(consumer.directory, 'custom');
        await publish(consumer, ['--no-restore', '-p:BuildProjectReferences=false', `-p:PublishDir=${custom}/`]);
        write(join(custom, 'host.txt'), 'retain custom host');
        items(consumer, '<MauiAsset Include="Resources/icon.svg" LogicalName="transition" />');
        await publish(consumer, ['--no-restore', '-p:BuildProjectReferences=false']);
        for (const directory of [consumer.output, consumer.published, custom]) {
            assert.ok(!existsSync(join(directory, 'maui-assets/images/icon.svg')));
            assert.ok(!existsSync(join(directory, expected.raw[`nested/${special}.bin`])));
        }
        assert.ok(!existsSync(join(custom, catalogName)), 'A retired publish directory must not advertise removed assets');
        items(consumer, '<MauiAsset Include="Resources/icon.svg" LogicalName="transition/child" />');
        await publish(consumer, ['--no-restore', '-p:BuildProjectReferences=false']);
        assert.ok(existsSync(join(consumer.published, 'maui-assets/raw/transition/child')));
        items(consumer, '<MauiAsset Include="Resources/icon.svg" LogicalName="TRANSITION" />');
        await publish(consumer, ['--no-restore', '-p:BuildProjectReferences=false']);
        assert.ok(existsSync(join(consumer.published, 'maui-assets/raw/TRANSITION')));
        items(consumer, '');
        await publish(consumer, ['--no-restore', '-p:BuildProjectReferences=false']);
        assert.deepEqual(catalog(consumer.published), { version: 1, application: 'Consumer', images: {}, fonts: {}, raw: {} });
        for (const directory of [consumer.output, consumer.published]) {
            assert.deepEqual(readdirSync(join(directory, 'maui-assets/raw')), ['host-owned.txt']);
        }
        // Clean must remove CURRENT nonempty assets as well as catalogs from old destinations.
        items(consumer, '<MauiImage Include="Resources/icon.svg" />');
        await publish(consumer, ['--no-restore', '-p:BuildProjectReferences=false']);
        await run(['clean', consumer.project, '-c', 'Release', '-p:BuildProjectReferences=false', '-m:1', '-nodeReuse:false', '--nologo']);
        for (const directory of [consumer.output, consumer.published, custom]) {
            assert.ok(!existsSync(join(directory, catalogName)), `${directory}: Clean catalog`);
            assert.ok(!existsSync(join(directory, 'maui-assets/images/icon.svg')), `${directory}: Clean image`);
        }
        assert.ok(existsSync(join(custom, 'host.txt')));
        for (const directory of [consumer.output, consumer.published]) {
            assert.equal(readFileSync(join(directory, 'host-owned.txt'), 'utf8'), 'retain host file');
            assert.equal(readFileSync(join(directory, 'maui-assets/raw/host-owned.txt'), 'utf8'), 'retain nested host file');
        }
        await publish(consumer, ['--no-restore', '-p:BuildProjectReferences=false']);
        assert.ok(existsSync(join(consumer.published, 'maui-assets/images/icon.svg')));
    });

    await t.test('removing a globbed source removes its owned outputs without deleting host files', async () => {
        const source = join(consumer.directory, 'Resources/glob/optional.png');
        write(source, Buffer.from([1, 2, 3]));
        items(consumer, '<MauiImage Include="Resources/glob/*.png" />');
        await publish(consumer, ['--no-restore', '-p:BuildProjectReferences=false']);
        assert.ok(existsSync(join(consumer.published, 'maui-assets/images/optional.png')));
        rmSync(source);
        await publish(consumer, ['--no-restore', '-p:BuildProjectReferences=false']);
        for (const directory of [consumer.output, consumer.published]) {
            assert.deepEqual(catalog(directory).images, {});
            assert.ok(!existsSync(join(directory, 'maui-assets/images/optional.png')));
            assert.ok(existsSync(join(directory, 'maui-assets/raw/host-owned.txt')));
        }
    });

    await t.test('Debug -> Release -> Debug shares PublishDir and older Clean preserves the active owner', async () => {
        // The source fixture's library must also be built for the newly used Debug configuration.
        const incremental = ['--no-restore'];
        const source = join(consumer.directory, 'Resources/config.txt');
        let previous;
        for (const [configuration, name, older] of [
            ['Debug', 'debug.txt', null], ['Release', 'release.txt', 'Debug'], ['Debug', 'debug-again.txt', 'Release'],
        ]) {
            write(source, name);
            items(consumer, `<MauiAsset Include="Resources/config.txt" LogicalName="${name}" />`);
            await publish(consumer, incremental, configuration);
            assert.deepEqual(catalog(consumer.published).raw, { [name]: `maui-assets/raw/${name}` });
            assert.equal(readFileSync(join(consumer.published, `maui-assets/raw/${name}`), 'utf8'), name);
            if (previous) {
                assert.ok(!existsSync(join(consumer.published, `maui-assets/raw/${previous}`)), 'Handover removes retired assets');
            }
            const journal = ownershipFiles(consumer).map(path => JSON.parse(readFileSync(path, 'utf8')))
                .find(entry => entry.destination === consumer.published);
            assert.deepEqual(journal.owner, { project: consumer.project, configuration, targetFramework: 'net10.0', runtimeIdentifier: '' });
            if (older) {
                const active = snapshot(consumer.published);
                await clean(consumer, older);
                assert.deepEqual(snapshot(consumer.published), active, 'Cleaning an older configuration must not delete active publish assets');
            }
            previous = name;
        }
        await clean(consumer, 'Debug');
        assert.ok(!existsSync(join(consumer.published, catalogName)));
        assert.ok(!existsSync(join(consumer.published, `maui-assets/raw/${previous}`)));
        assert.equal(readFileSync(join(consumer.published, 'maui-assets/raw/host-owned.txt'), 'utf8'), 'retain nested host file');
    });
}

test('app assets source and local NuGet consumers use real SDK build/publish and ownership cleanup', async t => {
    const sourceRoot = temporary(t, join(root, 'artifacts'));
        // Keep the asset task tests independent of changes to the runtime interop contract.
        // browser.build.test.mjs separately exercises the complete backend ProjectReference.
        const library = join(sourceRoot, 'source library/Library.csproj');
        write(library, `<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup><RuntimeIdentifier></RuntimeIdentifier><PublishTrimmed>false</PublishTrimmed></PropertyGroup>
    <ItemGroup><MauiImage Include="missing-library-only.png" Resize="true" /></ItemGroup>
</Project>`);
        write(join(dirname(library), 'Library.cs'), 'public sealed class Library\n{\n}\n');
        const source = fixture(join(sourceRoot, 'source head'), `<ProjectReference Include="${xml(library)}" />`);
    await t.test('source app head automatically imports product targets without collecting library items', async subtest => {
        await exerciseLifecycle(subtest, source);
    });

    await t.test('external app consumes local buildTransitive package with no source/tool imports or npm', async subtest => {
        const external = temporary(subtest, tmpdir());
        const version = `0.0.0-assets.${randomUUID()}`;
        const id = 'Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents';
        const feed = join(external, 'feed');
        await run(['pack', backend, '-c', 'Release', '--no-build', '--no-restore', '-p:IsPackable=true',
            `-p:PackageVersion=${version}`, '-o', feed, '--nologo']);
        const packages = join(external, 'packages');
        const backendAssets = JSON.parse(readFileSync(join(dirname(backend), 'obj/project.assets.json'), 'utf8'));
        const folders = Object.keys(backendAssets.packageFolders).join(';');
        const consumer = fixture(join(external, 'package head'), `<PackageReference Include="${id}" Version="${version}" />`,
            `    <RestorePackagesPath>${xml(packages)}</RestorePackagesPath>
    <RestoreFallbackFolders>${folders.replaceAll('&', '&amp;')}</RestoreFallbackFolders>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>`);
        write(join(consumer.directory, 'NuGet.config'), `<configuration>
  <packageSources><clear/><add key="fixture" value="${xml(feed)}" /></packageSources>
  <packageSourceMapping><clear/><packageSource key="fixture"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>`);
        await exerciseLifecycle(subtest, consumer);
        const packageDirectory = join(packages, id.toLowerCase(), version);
        assert.ok(existsSync(join(packageDirectory, 'tools/net10.0/Wasm.FluentUIWebComponents.Assets.dll')));
        assert.deepEqual(readdirSync(join(packageDirectory, 'tools/net10.0')), ['Wasm.FluentUIWebComponents.Assets.dll']);
        const imports = readFileSync(join(consumer.directory, 'obj/Consumer.csproj.nuget.g.targets'), 'utf8');
        assert.match(imports, /buildTransitive/u);
        assert.ok(!existsSync(join(consumer.directory, 'node_modules')));
        assert.ok(!existsSync(join(consumer.directory, 'package.json')));
    });
});

test('app assets reject ambiguous names, metadata, unsupported formats and unsafe destinations', async t => {
    const parent = temporary(t, join(root, 'artifacts'));
    const consumer = fixture(join(parent, 'validation'));
    for (const name of ['one.svg', 'two.svg', 'one.png', 'three.gif', 'font.woff', 'font.ttf', 'raw.txt']) {
        write(join(consumer.directory, name), 'byte-preserving fixture');
    }
    const invalid = [
        ['<MauiImage Include="one.svg;one.png" />', /alias|collid/u],
        ['<MauiImage Include="one.svg" /><MauiImage Include="two.svg" Link="ONE.svg" />', /collid/u],
        ['<MauiImage Include="one.svg" Link="one.png" />', /extension/u],
        ['<MauiImage Include="three.gif" />', /Supported/u],
        ['<MauiFont Include="font.woff" />', /Supported/u],
        ['<MauiImage Include="absent.png" />', /does not exist/u],
        ...['BaseSize="24,24"', 'TintColor="red"', 'Color="blue"', 'ForegroundFile="two.svg"',
            'ForegroundScale="0.5"', 'Resize="true"', 'Resize="nonsense"']
            .map(metadata => [`<MauiImage Include="one.svg" ${metadata} />`, /unsupported/u]),
        ...['../escape', '/absolute', 'C:\\absolute', 'x//y', 'x/./y', 'x/../y', 'a?', 'CON', 'x.']
            .map(name => [`<MauiAsset Include="raw.txt" LogicalName="${name}" />`, /invalid logical/u]),
        ['<MauiImage Include="one.svg" Link="../bad.svg" />', /invalid logical/u],
        ['<MauiAsset Include="raw.txt" Link="a" /><MauiAsset Include="one.svg" Link="a/b" />', /collid/u],
        ['<MauiAsset Include="raw.txt" Link="A/x" /><MauiAsset Include="one.svg" Link="a/y" />', /collid/u],
        ['<MauiFont Include="font.ttf" Link="font.ttf" /><MauiFont Include="font.ttf" Link="FONT.ttf" />', /collid/u],
    ];
    for (const [declarations, error] of invalid) {
        items(consumer, declarations);
        await assert.rejects(generate(consumer), error, declarations);
    }
    items(consumer, '<MauiImage Include="one.svg" Resize="false" />');
    await generate(consumer);
    const stable = readFileSync(join(consumer.state, catalogName));
    items(consumer, '<MauiImage Include="one.svg" Resize="true" />');
    await assert.rejects(generate(consumer), /Resize/u);
    assert.deepEqual(readFileSync(join(consumer.state, catalogName)), stable, 'Validation failure preserves the previous catalog');

    for (const [metadata, value] of [['ForegroundFile', 'two.svg'], ['ForegroundScale', '0.75']]) {
        write(join(consumer.directory, 'Assets.items'), `<Project>
  <ItemDefinitionGroup><MauiImage><${metadata}>${value}</${metadata}></MauiImage></ItemDefinitionGroup>
  <ItemGroup><MauiImage Include="one.svg" /></ItemGroup>
</Project>`);
        await assert.rejects(generate(consumer), new RegExp(`unsupported ${metadata}`, 'u'));
        assert.deepEqual(readFileSync(join(consumer.state, catalogName)), stable, 'Evaluated defaults cannot bypass composition checks');
        items(consumer, `<MauiImage Include="one.svg" /><MauiImage Update="one.svg" ${metadata}="${value}" />`);
        await assert.rejects(generate(consumer), new RegExp(`unsupported ${metadata}`, 'u'));
    }

    items(consumer, '<MauiAsset Include="raw.txt" LogicalName="owned.txt" />');
    write(join(consumer.published, 'maui-assets/raw/owned.txt'), 'unowned');
    await assert.rejects(publish(consumer), /unowned/u);
    assert.equal(readFileSync(join(consumer.published, 'maui-assets/raw/owned.txt'), 'utf8'), 'unowned');
    rmSync(join(consumer.published, 'maui-assets/raw/owned.txt'));
    items(consumer, `<MauiAsset Include="raw.txt" LogicalName="owned.txt" />
      <Content Include="one.svg" TargetPath="maui-assets/raw/owned.txt" CopyToOutputDirectory="Always" CopyToPublishDirectory="Always" />`);
    await assert.rejects(publish(consumer), /collid|same relative path/u);

        items(consumer, `<MauiAsset Include="raw.txt" LogicalName="owned.txt" />
            <None Update="one.svg" TargetPath="maui-assets/raw/owned.txt" />`);
        await publish(consumer);
        assert.equal(readFileSync(join(consumer.published, 'maui-assets/raw/owned.txt'), 'utf8'), 'byte-preserving fixture',
                'Items not copied by the SDK are not output collisions');

    if (process.platform !== 'win32') {
        items(consumer, '<MauiAsset Include="raw.txt" LogicalName="escape/owned.txt" />');
        const outside = join(parent, 'outside');
        mkdirSync(outside);
        symlinkSync(outside, join(consumer.published, 'maui-assets/raw/escape'));
        await assert.rejects(publish(consumer), /symbolic link/u);
        assert.deepEqual(readdirSync(outside), []);
    }
});

test('APNG detection rejects real animation while retaining static PNG and opaque fixture bytes unchanged', async t => {
    const parent = temporary(t, join(root, 'artifacts'));
    const consumer = fixture(join(parent, 'png'));
    // Self-authored 1x1 RGBA fixtures, with real CRCs and zlib frame data. The APNG
    // has two frames (red/blue), acTL, fcTL, IDAT and fdAT; no image library is used.
    const animated = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACGFjVEwAAAACAAAAAPONk3AAAAAaZmNUTAAAAAAAAAABAAAAAQAAAAAAAAAAAAEACgAAWn8w0AAAAA1JREFUeJxj+M/A8B8ABQAB/4mZPR0AAAAaZmNUTAAAAAEAAAABAAAAAQAAAAAAAAAAAAEACgAAwQzaBAAAABFmZEFUAAAAAnicY2Bg+P8fAAMCAf/1e6XXAAAAAElFTkSuQmCC', 'base64');
    const staticPng = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==', 'base64');
    const fixtures = new Map([
        ['static.png', staticPng],
        ['opaque.png', Buffer.from('opaque acTL fixture bytes')],
        ['truncated.png', animated.subarray(0, 16)],
        ['not-a-chunk.png', Buffer.concat([staticPng, Buffer.from('acTL')])],
    ]);
    for (const [name, bytes] of fixtures) {
        write(join(consumer.directory, name), bytes);
    }
    items(consumer, `<MauiImage Include="${[...fixtures.keys()].join(';')}" />`);
    await publish(consumer);
    for (const [name, bytes] of fixtures) {
        assert.deepEqual(readFileSync(join(consumer.published, 'maui-assets/images', name)), bytes);
    }
    const previous = snapshot(consumer.published);
    const previousCatalog = readFileSync(join(consumer.state, catalogName));
    write(join(consumer.directory, 'animated.PNG'), animated);
    items(consumer, '<MauiImage Include="animated.PNG" Resize="false" />');
    await assert.rejects(publish(consumer), /unsupported animated PNG \(APNG\)/u);
    assert.deepEqual(readFileSync(join(consumer.directory, 'animated.PNG')), animated);
    assert.deepEqual(readFileSync(join(consumer.state, catalogName)), previousCatalog);
    assert.deepEqual(snapshot(consumer.published), previous, 'Reject before changing catalog or previous output');
});

test('SDK publish collision checks use RelativePath rather than retained TargetPath', async t => {
    const parent = temporary(t, join(root, 'artifacts'));
    const consumer = fixture(join(parent, 'publish-metadata'));
    write(join(consumer.directory, 'raw.txt'), 'asset bytes');
    write(join(consumer.directory, 'other.txt'), 'other SDK bytes');
    function inject(targetPath, relativePath) {
        write(join(consumer.directory, 'Assets.items'), `<Project>
  <ItemGroup><MauiAsset Include="raw.txt" LogicalName="owned.txt" /></ItemGroup>
  <Target Name="InjectResolvedMetadata" AfterTargets="ComputeFilesToPublish">
    <ItemGroup>
      <ResolvedFileToPublish Include="$(MSBuildProjectDirectory)/other.txt"
        TargetPath="${targetPath}" RelativePath="${relativePath}" CopyToPublishDirectory="Always" />
    </ItemGroup>
  </Target>
</Project>`);
    }
    inject('harmless.txt', 'maui-assets/raw/owned.txt');
    write(join(consumer.published, 'maui-assets/raw/owned.txt'), 'unrelated preexisting bytes');
    await assert.rejects(publish(consumer), /same relative path/u, 'Retain SDK duplicate-file protection');
    // SDK duplicate-file checks do not catch file/directory collisions. This must
    // reach the asset task and cannot pass solely because NETSDK1152 intervenes.
    inject('harmless.txt', 'maui-assets/raw/owned.txt/child');
    await assert.rejects(publish(consumer), /MAUI browser assets ownership:.*collid/u);
    assert.equal(readFileSync(join(consumer.published, 'maui-assets/raw/owned.txt'), 'utf8'), 'unrelated preexisting bytes');
    rmSync(join(consumer.published, 'maui-assets/raw/owned.txt'));
    inject('maui-assets/raw/owned.txt', 'harmless.txt');
    await publish(consumer);
    assert.equal(readFileSync(join(consumer.published, 'maui-assets/raw/owned.txt'), 'utf8'), 'asset bytes');
    assert.equal(readFileSync(join(consumer.published, 'harmless.txt'), 'utf8'), 'other SDK bytes');
});

test('ownership validates destination roots, ancestors, journals and catalogs before touching redirected contents',
    { skip: process.platform === 'win32' && 'Symlink fixtures require unprivileged directory symlink support' }, async t => {
        const parent = temporary(t, join(root, 'artifacts'));
        await run(['build', taskProject, '-c', 'Release', '-m:1', '-nodeReuse:false', '--nologo']);
        for (const kind of ['destination-root', 'destination-ancestor']) {
            await t.test(`new ${kind} redirects are rejected`, async () => {
                const consumer = fixture(join(parent, `new-${kind}`));
                const outside = join(parent, `new-outside-${kind}`);
                write(join(outside, 'host.txt'), 'unrelated host');
                const link = join(consumer.directory, 'redirect');
                symlinkSync(outside, link);
                const destination = kind === 'destination-root' ? link : join(link, 'nested/publish');
                const before = snapshot(outside);
                await assert.rejects(own(consumer, { destination, assets: [[join(consumer.directory, 'Program.cs'), catalogName]] }), /symbolic link/u);
                assert.deepEqual(snapshot(outside), before);
                assert.ok(!existsSync(join(outside, 'nested')), 'Do not create directories through an ancestor redirect');
            });
        }

        for (const kind of ['destination-root', 'destination-ancestor', 'asset-directory', 'asset-file',
            'destination-catalog', 'journal-root', 'journal-ancestor', 'journal-file', 'source-catalog']) {
            await t.test(`existing ${kind} redirects block reconciliation and Clean`, async () => {
                const consumer = fixture(join(parent, kind));
                const destination = join(consumer.directory, 'outputs/published');
                const catalogSource = join(consumer.state, catalogName);
                write(catalogSource, '{"version":1,"raw":{}}');
                const assets = [[join(consumer.directory, 'Program.cs'), 'maui-assets/raw/owned.txt'], [catalogSource, catalogName]];
                await own(consumer, { destination, assets });
                // Stand in for the subsequent SDK copy. Only host-task I/O is under test.
                write(join(destination, 'maui-assets/raw/owned.txt'), 'previous asset');
                write(join(destination, catalogName), readFileSync(catalogSource));
                write(join(destination, 'maui-assets/raw/host.txt'), 'unrelated nested host');
                write(join(destination, 'host.txt'), 'unrelated host');
                const journal = ownershipFiles(consumer)[0];
                const link = {
                    'destination-root': destination,
                    'destination-ancestor': dirname(destination),
                    'asset-directory': join(destination, 'maui-assets'),
                    'asset-file': join(destination, 'maui-assets/raw/owned.txt'),
                    'destination-catalog': join(destination, catalogName),
                    'journal-root': dirname(journal),
                    'journal-ancestor': join(consumer.directory, 'obj/maui-browser-assets'),
                    'journal-file': journal,
                    'source-catalog': catalogSource,
                }[kind];
                const outside = join(parent, `outside-${kind}`);
                mkdirSync(outside);
                const redirected = join(outside, 'redirected');
                renameSync(link, redirected);
                symlinkSync(redirected, link);
                write(join(outside, 'unrelated.txt'), 'retain outside');
                const before = snapshot(outside);
                const publishedBefore = snapshot(destination);
                await assert.rejects(own(consumer, { assets }), /symbolic link/u,
                    'Reconciliation checks all paths before reading an old catalog');
                assert.deepEqual(snapshot(outside), before);
                assert.deepEqual(snapshot(destination), publishedBefore);
                if (kind !== 'source-catalog') {
                    // The local generated catalog is not read during Clean.
                    await assert.rejects(own(consumer, { clean: true }), /symbolic link/u);
                    assert.deepEqual(snapshot(outside), before);
                    assert.deepEqual(snapshot(destination), publishedBefore);
                    await assert.rejects(own(consumer), /symbolic link/u, 'Removing all items cannot delete through redirects');
                    assert.deepEqual(snapshot(outside), before);
                } else {
                    items(consumer, '');
                    await assert.rejects(generate(consumer), /symbolic link/u, 'Catalog generation cannot replace a symlink');
                    await assert.rejects(clean(consumer, 'Release'), /symbolic link/u, 'Clean validates the local catalog before SDK Delete');
                    assert.deepEqual(snapshot(outside), before);
                }
            });
        }
    });

test('ownership handover records TFM/RID and never claims another project or unowned catalogs', async t => {
    const parent = temporary(t, join(root, 'artifacts'));
    const consumer = fixture(join(parent, 'provenance'));
    await run(['build', taskProject, '-c', 'Release', '-m:1', '-nodeReuse:false', '--nologo']);
    const assets = [[join(consumer.directory, 'Program.cs'), catalogName]];
    await own(consumer, { destination: consumer.published, assets });
    write(join(consumer.published, catalogName), readFileSync(assets[0][0]));
    const before = snapshot(consumer.published);
    await own(consumer, { destination: consumer.published, assets, targetFramework: 'net10.0-browser', runtimeIdentifier: 'browser-wasm' });
    await own(consumer, { clean: true });
    assert.deepEqual(snapshot(consumer.published), before, 'Cleaning the former TFM/RID does not remove the active output');
    assert.deepEqual(JSON.parse(readFileSync(ownershipFiles(consumer)[0], 'utf8')).owner,
        { project: consumer.project, configuration: 'Release', targetFramework: 'net10.0-browser', runtimeIdentifier: 'browser-wasm' });
    await assert.rejects(own(consumer, { destination: consumer.published, assets,
        project: join(consumer.directory, 'Different.csproj') }), /unowned/u);
    assert.deepEqual(snapshot(consumer.published), before);
    const unowned = join(consumer.directory, 'unowned');
    write(join(unowned, catalogName), 'host-owned catalog');
    await assert.rejects(own(consumer, { destination: unowned, assets }), /unowned/u);
    assert.equal(readFileSync(join(unowned, catalogName), 'utf8'), 'host-owned catalog');
});

test('app assets activation is limited to browser app heads, explicit managed opt-in and non-design-time builds', async t => {
    const parent = temporary(t, join(root, 'artifacts'));
    await run(['build', taskProject, '-c', 'Release', '-m:1', '-nodeReuse:false', '--nologo']);
    for (const [name, args, enabled] of [
        ['browser-head', ['-p:RuntimeIdentifier=browser-wasm'], true],
        ['library', ['-p:OutputType=Library'], false],
        ['design-time', ['-p:DesignTimeBuild=true'], false],
        ['managed-no-opt-in', ['-p:MauiBrowserEnableAppAssets=false'], false],
        ['managed-opt-in', [], true],
    ]) {
        const consumer = fixture(join(parent, name), '', name === 'browser-head' ? '<MauiBrowserEnableAppAssets></MauiBrowserEnableAppAssets>' : '');
        await generate(consumer, args);
        const state = name === 'browser-head' ? join(consumer.directory, 'obj/Release/net10.0/browser-wasm/maui-browser-assets') : consumer.state;
        assert.equal(existsSync(join(state, catalogName)), enabled, name);
    }
});