/**
 * @file Managed asset integration runner. Compiles backend C# sources with real
 * MAUI references and fixtures/assets-managed.cs, replacing only the JS boundary.
 * Runs each scenario in a separate process to isolate MAUI static state. Covers
 * normal font initialization, embedded-font diagnostics, image loading, handler
 * reuse, reentrant callbacks and disconnect/reconnect behavior.
 *
 * Requires .NET 10 and a restored backend: obj/project.assets.json and its portable
 * reference DLLs supply the fixture's references. This runner does not restore or
 * build the backend. The temporary SDK project builds in Debug with one MSBuild
 * node and is deleted from artifacts afterward. No generated TS contract, native
 * publish, trimming validation or browser execution is involved.
 *
 * @see ../../CONTRIBUTING.md#testing-and-validation
 */
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));
const source = join(root, 'src/Wasm.FluentUIWebComponents');
const escapeXml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');

test('managed assets: real MAUI font initialization and image lifecycle', async t => {
    const assetsPath = join(source, 'obj/project.assets.json');
    assert.ok(existsSync(assetsPath), 'Restore the backend before running managed asset tests.');
    const assets = JSON.parse(readFileSync(assetsPath, 'utf8'));
    const references = [];
    for (const [library, target] of Object.entries(assets.targets['net10.0'])) {
        for (const file of Object.keys(target.compile ?? {}).filter(file => file.endsWith('.dll'))) {
            const candidate = Object.keys(assets.packageFolders)
                .map(folder => join(folder, assets.libraries[library].path, file)).find(existsSync);
            assert.ok(candidate, `Missing restored reference ${library}/${file}`);
            references.push(`<Reference Include="${escapeXml(basename(file, '.dll'))}" HintPath="${escapeXml(candidate)}" />`);
        }
    }
    const artifacts = join(root, 'artifacts');
    mkdirSync(artifacts, { recursive: true });
    const directory = mkdtempSync(join(artifacts, 'assets-managed-'));
    t.after(() => rmSync(directory, { recursive: true, force: true }));
    writeFileSync(join(directory, 'Directory.Build.props'), '<Project/>');
    writeFileSync(join(directory, 'Directory.Build.targets'), '<Project/>');
    writeFileSync(join(directory, 'Fixture.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
        <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AllowUnsafeBlocks>true</AllowUnsafeBlocks>
        <EnableDefaultCompileItems>false</EnableDefaultCompileItems><MSBuildEnableWorkloadResolver>false</MSBuildEnableWorkloadResolver>
        <EnableNETAnalyzers>false</EnableNETAnalyzers>
      </PropertyGroup>
      <ItemGroup>
        ${references.join('\n')}
        <Compile Include="${escapeXml(source)}/**/*.cs"
          Exclude="${escapeXml(source)}/obj/**;${escapeXml(source)}/bin/**;${escapeXml(source)}/Interop/Browser*.cs" />
        <Compile Include="Browser.cs" />
        <Compile Include="${escapeXml(join(root, 'scripts/test/fixtures/assets-managed.cs'))}" />
      </ItemGroup>
    </Project>`);
    // Replace only the JS boundary. Signatures are derived from the real imports so
    // this fixture also compiles all production handlers/hosting against their APIs.
    const imports = ['Browser.cs', 'Browser.Assets.cs'].map(file => readFileSync(join(source, 'Interop', file), 'utf8')).join('\n');
    const methods = [...imports.matchAll(/internal static partial (\w+\??) (\w+)\(([^;]*)\);/g)].map(([, type, name, rawParameters]) => {
        const parameters = rawParameters.replace(/\[JSStringUnion\(typeof\(\w+\)\)\]\s*/g, '');
        let body = type === 'void' ? '' : `return ${type === 'string' ? '"Light"' : 'default'};`;
        if (name === 'Create') { body = 'return ++NextId;'; }
        if (name === 'ImageSource') { body = 'Sources.Add((id, name, request));'; }
        if (name === 'ReportResource') { body = 'Reports.Add(message);'; }
        if (name === 'Report') { body = 'FatalReports.Add(message);'; }
        if (name === 'FontFamily') { body = 'FontFiles.Add(filename); return "maui-test-font";'; }
        if (name === 'Style') { body = 'Styles[(id, name)] = value;'; }
        if (name === 'Measure') { body = 'throw new Exception("Images must not use intrinsic DOM measurement.");'; }
        return `internal static ${type} ${name}(${parameters}) { ${body} }`;
    });
    writeFileSync(join(directory, 'Browser.cs'), `namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;
      internal static class Browser {
        internal static int NextId;
        internal static readonly List<(int Id, string Name, int Request)> Sources = new();
        internal static readonly List<string> Reports = new();
        internal static readonly List<string> FatalReports = new();
        internal static readonly List<string> FontFiles = new();
        internal static readonly Dictionary<(int, string), string> Styles = new();
        ${methods.join('\n')}
      }`);
    const result = spawnSync('dotnet', ['build', join(directory, 'Fixture.csproj'), '-m:1', '-nodeReuse:false'], {
        cwd: root, encoding: 'utf8', timeout: 120_000,
    });
    assert.ifError(result.error);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    for (const scenario of ['baseline', 'reuse', 'reentrant-reuse', 'reentrant-reuse-source', 'reentrant-reuse-disconnect', 'disconnect', 'embedded-fonts']) {
      await t.test(scenario, () => {
        const run = spawnSync('dotnet', [join(directory, 'bin/Debug/net10.0/Fixture.dll'), scenario], {
          cwd: root, encoding: 'utf8', timeout: 30_000,
        });
        assert.ifError(run.error);
        assert.equal(run.status, 0, run.stdout + run.stderr);
        assert.match(run.stdout, /Managed asset assertions passed/);
      });
    }
});