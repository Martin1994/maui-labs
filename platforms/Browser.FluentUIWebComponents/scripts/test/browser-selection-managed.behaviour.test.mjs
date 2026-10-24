/** Real MAUI selection handlers with only JS imports replaced. Requires restored
 * backend references and .NET 10, not Wasm publishing or a browser. */
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));
const source = join(root, 'src/Browser.FluentUIWebComponents');
const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');

test('managed Picker, Switch and themes: bindings, notifications, colors and lifecycle', () => {
    const assets = JSON.parse(readFileSync(join(source, 'obj/project.assets.json'), 'utf8'));
    const references = [];
    for (const [library, target] of Object.entries(assets.targets['net10.0'])) {
        for (const file of Object.keys(target.compile ?? {}).filter(file => file.endsWith('.dll'))) {
            const candidate = Object.keys(assets.packageFolders)
                .map(folder => join(folder, assets.libraries[library].path, file)).find(existsSync);
            assert.ok(candidate, `Missing restored reference ${library}/${file}`);
            references.push(`<Reference Include="${xml(basename(file, '.dll'))}" HintPath="${xml(candidate)}" />`);
        }
    }
    mkdirSync(join(root, 'artifacts'), { recursive: true });
    const directory = mkdtempSync(join(root, 'artifacts/selection-managed-'));
    try {
        writeFileSync(join(directory, 'Directory.Build.props'), '<Project/>');
        writeFileSync(join(directory, 'Directory.Build.targets'), '<Project/>');
        writeFileSync(join(directory, 'Fixture.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
            <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AllowUnsafeBlocks>true</AllowUnsafeBlocks>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems><MSBuildEnableWorkloadResolver>false</MSBuildEnableWorkloadResolver>
            <EnableNETAnalyzers>false</EnableNETAnalyzers>
          </PropertyGroup>
          <ItemGroup>${references.join('\n')}
            <Compile Include="${xml(source)}/**/*.cs" Exclude="${xml(source)}/obj/**;${xml(source)}/bin/**;${xml(source)}/Interop/Browser*.cs" />
            <Compile Include="Browser.cs" />
            <Compile Include="${xml(join(root, 'scripts/test/fixtures/selection-managed.cs'))}" />
          </ItemGroup>
        </Project>`);
        const imports = ['Browser.cs', 'Browser.Assets.cs'].map(file => readFileSync(join(source, 'Interop', file), 'utf8')).join('\n');
        const methods = [...imports.matchAll(/internal static partial (\w+\??) (\w+)\(([^;]*)\);/g)].map(([, type, name, parameters]) => {
            parameters = parameters.replace(/\[JSStringUnion\(typeof\(\w+\)\)\]\s*/g, '');
            let body = type === 'void' ? '' : `return ${type === 'string' ? '"light"' : 'default'};`;
            if (name === 'Create') { body = 'Tags[++NextId] = tagName; return NextId;'; }
            if (name === 'PickerCommit' || name === 'PickerSelection') { body = 'Selection[id] = index;'; }
            if (name === 'SetBooleanProperty') { body = 'Booleans[(id, name)] = value;'; }
            if (name === 'SetAttribute') { body = 'Attributes[(id, name)] = value;'; }
            if (name === 'Style') { body = 'Styles[(id, name)] = value;'; }
            if (name === 'PickerClear') { body = 'Items[id] = new();'; }
            if (name === 'PickerAdd') { body = 'Items[id].Add(text);'; }
            if (name === 'Report') { body = 'throw new Exception(message);'; }
            return `internal static ${type} ${name}(${parameters}) { ${body} }`;
        });
        writeFileSync(join(directory, 'Browser.cs'), `namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;
          internal static class Browser {
            internal static int NextId;
            internal static readonly Dictionary<int, string> Tags = new();
            internal static readonly Dictionary<int, int> Selection = new();
            internal static readonly Dictionary<int, List<string>> Items = new();
            internal static readonly Dictionary<(int, string), bool> Booleans = new();
            internal static readonly Dictionary<(int, string), string?> Attributes = new();
            internal static readonly Dictionary<(int, string), string> Styles = new();
            ${methods.join('\n')}
          }`);
        for (const args of [['build', join(directory, 'Fixture.csproj'), '-m:1', '-nodeReuse:false'],
            [join(directory, 'bin/Debug/net10.0/Fixture.dll')]]) {
            const result = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8', timeout: 120_000 });
            assert.ifError(result.error);
            assert.equal(result.status, 0, result.stdout + result.stderr);
        }
    } finally {
        rmSync(directory, { recursive: true, force: true });
    }
});