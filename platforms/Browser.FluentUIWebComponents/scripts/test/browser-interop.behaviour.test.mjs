/**
 * @file Contract-generator integration tests using real CodeGen, MSBuildWorkspace
 * and the TypeScript CLI. Covers conditional compilation, attribute aliases,
 * parameter escaping, unions/overloads, rejection diagnostics, deterministic
 * output/mtime, strict TypeScript positive/negative cases and the emitted resolver.
 *
 * Fixtures are independent restored SDK projects, not backend source or DLLs.
 * Requires product npm dependencies, .NET 10 and a built Release CodeGen tool;
 * npm run test:behaviour builds that tool in its pretest hook. A direct invocation
 * does not. Each fixture can restore packages and is removed from artifacts afterward.
 * This suite does not clean/build the backend or execute NativeAOT/browser code.
 *
 * @see ../../CONTRIBUTING.md#testing-and-validation
 */
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, utimesSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { before, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { runTypeScript } from '../typescript-cli.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const generator = join(root, 'tools/Browser.FluentUIWebComponents.CodeGen/bin/Release/net10.0/Browser.FluentUIWebComponents.CodeGen.dll');

before(() => {
    assert.ok(existsSync(generator), `Build Browser.FluentUIWebComponents.CodeGen in Release first: ${generator}`);
});

const metadata = `using System;
namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
public sealed class JSStringUnionAttribute : Attribute
{
    public JSStringUnionAttribute(Type valuesType)
    {
        ValuesType = valuesType;
    }

    public Type ValuesType { get; }
}
`;

function source(extraImports = '', extraExports = '') {
    return `using System;
using Import = System.Runtime.InteropServices.JavaScript.JSImportAttribute;
using Export = System.Runtime.InteropServices.JavaScript.JSExportAttribute;
using System.Runtime.InteropServices.JavaScript;
using Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

namespace BrowserInterop;

public static class Theme
{
    public const string Light = "light";
    public const string Dark = "dark";
}

public static partial class Bridge
{
    private const string Module = "example-" + "bridge";
    private const string TextName = "text";

    [Import(TextName, Module)]
    public static partial string? Text(string? value);

    [Import("measure", Module)]
    public static partial double Measure(bool enabled, int count, double scale);

    [Import("route", Module)]
    public static partial string RouteText(string value, bool enabled);

    [Import("route", Module)]
    public static partial double RouteNumber(int value, string unit);

    [Import("theme", Module)]
    [return: JSStringUnion(typeof(Theme))]
    public static partial string SelectTheme([JSStringUnion(typeof(Theme))] string value);

#if TEST_CONTRACT
    [Import("enabled", Module)]
    public static partial void Enabled();
#else
    [Import("disabled", Module)]
    public static partial void Disabled();
#endif
#if DEBUG
    [Import("leakedSymbol", Module)]
    public static partial long LeakedSymbol();
#endif
    ${extraImports}
}

public static class Events
{
    [Export]
    public static void Changed(string? value, bool enabled, int count, double scale)
    {
    }

    [Export]
    [return: JSStringUnion(typeof(Theme))]
    public static string CurrentTheme()
    {
        return Theme.Light;
    }
    ${extraExports}
}
`;
}

function fixture(t, content = source(), symbol = 'TEST_CONTRACT') {
    // Keep SDK discovery under the product's global.json, without its browser defaults.
    const artifacts = join(root, 'artifacts');
    mkdirSync(artifacts, { recursive: true });
    const directory = mkdtempSync(join(artifacts, 'browser-interop-'));
    t.after(() => {
        rmSync(directory, { recursive: true, force: true });
    });
    const project = join(directory, 'Fixture.csproj');
    const output = join(directory, 'contracts.g.ts');
    writeFileSync(join(directory, 'Directory.Build.props'), '<Project/>\n');
    writeFileSync(project, `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>Example.Backend</AssemblyName>
    <ImplicitUsings>true</ImplicitUsings>
    <Nullable>enable</Nullable>
    <DefineConstants>${symbol}</DefineConstants>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
`);
    writeFileSync(join(directory, 'Metadata.cs'), metadata);
    writeFileSync(join(directory, 'Fixture.cs'), content);
    const restored = spawnSync('dotnet', ['restore', project, '--nologo', '-p:Configuration=Release'], {
        cwd: directory, encoding: 'utf8', timeout: 120_000,
    });
    assert.ifError(restored.error);
    assert.equal(restored.signal, null, `Restore terminated: ${restored.stderr}`);
    assert.equal(restored.status, 0, restored.stdout + restored.stderr);
    return {
        directory,
        project,
        output,
        run(options = ['--configuration', 'Release']) {
            const result = spawnSync('dotnet', [generator, project, output, ...options], {
                cwd: directory, encoding: 'utf8', timeout: 120_000,
            });
            assert.ifError(result.error);
            assert.equal(result.signal, null, `Generator terminated: ${result.stderr}`);
            return result;
        },
        generate() {
            const result = this.run();
            assert.equal(result.status, 0, result.stdout + result.stderr);
            return readFileSync(output, 'utf8');
        },
    };
}

test('CLI help is available without loading a project', () => {
    const result = spawnSync('dotnet', [generator, '--help'], { cwd: root, encoding: 'utf8' });
    assert.ifError(result.error);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    assert.match(result.stdout, /<project> <output>/u);
    assert.match(result.stdout, /--configuration/u);
    assert.match(result.stdout, /Release/u);
    assert.doesNotMatch(result.stderr, /WFCG001/u);
});

test('CLI rejects missing arguments, missing files and unknown options before generation', t => {
    const current = fixture(t);
    for (const args of [
        [],
        [join(current.directory, 'missing.csproj'), current.output],
        [current.project, current.output, '--unknown'],
        [current.project, current.output, '--configuration'],
    ]) {
        const result = spawnSync('dotnet', [generator, ...args], { cwd: root, encoding: 'utf8' });
        assert.ifError(result.error);
        assert.notEqual(result.status, 0, result.stdout + result.stderr);
        assert.doesNotMatch(result.stderr, /WFCG001/u, 'Argument errors must not invoke the generator');
        assert.ok(!existsSync(current.output));
    }
});

test('CLI defaults to Release and accepts the short configuration option', t => {
    const current = fixture(t);
    const defaultResult = current.run([]);
    assert.equal(defaultResult.status, 0, defaultResult.stdout + defaultResult.stderr);
    const expected = readFileSync(current.output, 'utf8');
    const shortResult = current.run(['-c', 'Release']);
    assert.equal(shortResult.status, 0, shortResult.stdout + shortResult.stderr);
    assert.equal(readFileSync(current.output, 'utf8'), expected);
});

test('aliases, constants, primitives, unions and correlated overloads produce function properties', (t) => {
    const output = fixture(t).generate();
    assert.match(output, /export const BridgeModuleName = "example-bridge";/u);
    assert.match(output, /export const EventsAssemblyName = "Example.Backend.dll";/u);
    assert.match(output, /text: \(value: string \| null\) => string \| null;/u);
    assert.match(output, /measure: \(enabled: boolean, count: number, scale: number\) => number;/u);
    assert.match(output, /export type Theme = "dark" \| "light";/u);
    assert.match(output, /theme: \(value: Theme\) => Theme;/u);
    assert.match(output, /CurrentTheme: \(\) => Theme;/u);
    assert.match(output, /Changed: \(value: string \| null, enabled: boolean, count: number, scale: number\) => void;/u);
    assert.match(output, /route: \{\s*(?:\/\/[^\n]*\n\s*)?\(value: number, unit: string\): number;\s*(?:\/\/[^\n]*\n\s*)?\(value: string, enabled: boolean\): string;\s*\};/u);
    assert.doesNotMatch(output, /\bany\b/u);
});

test('reserved parameter names avoid collisions while normal names are retained', (t) => {
    const current = fixture(t, source(`[Import("names", Module)]
    public static partial void Names(string var, string var_, string @default, string @this, int count);`, `[Export]
    public static void Names(string var, string var_, string @default, string @this, int count)
    {
    }`));
    const output = current.generate();
    assert.match(output, /names: \(var__: string, var_: string, default_: string, this_: string, count: number\) => void;/u);
    assert.match(output, /Names: \(var__: string, var_: string, default_: string, this_: string, count: number\) => void;/u);
    assert.doesNotMatch(output, /\barg_/u);
    const result = typeCheck(current, `declare const bridge: BridgeImports;
bridge.names('var', 'var_', 'default', 'this', 1);`);
    assert.equal(result.status, 0, result.stdout + result.stderr);
});

test('uses fixture symbols, not backend symbols, for conditional compilation', (t) => {
    const enabled = fixture(t).generate();
    assert.match(enabled, /enabled: \(\) => void;/u);
    assert.doesNotMatch(enabled, /disabled|leakedSymbol/u);
    const disabled = fixture(t, source(), 'OTHER_CONTRACT').generate();
    assert.match(disabled, /disabled: \(\) => void;/u);
    assert.doesNotMatch(disabled, /\benabled: \(\)|leakedSymbol/u);
});

const rejections = [
    ['long parameter', '[Import("bad", Module)] public static partial void Bad(long value);', '', /Unsupported interop type 'long'/u],
    ['object return', '[Import("bad", Module)] public static partial object Bad();', '', /Unsupported interop type 'object'/u],
    ['Task return', '[Import("bad", Module)] public static partial System.Threading.Tasks.Task Bad();', '', /Unsupported interop type 'System.Threading.Tasks.Task'/u],
    ['parameter JSMarshalAs', '[Import("bad", Module)] public static partial void Bad([JSMarshalAs<JSType.Number>] int value);', '', /JSMarshalAs requires an explicit generator mapping/u],
    ['return JSMarshalAs', '[Import("bad", Module)] [return: JSMarshalAs<JSType.Number>] public static partial int Bad();', '', /JSMarshalAs requires an explicit generator mapping/u],
    ['unknown attribute', '[MissingContract] [Import("bad", Module)] public static partial void Bad();', '', /Unresolved or invalid declaration attribute: MissingContract/u],
    ['missing module', '[Import("bad")] public static partial void Bad();', '', /Imports require constant function and logical module names/u],
    ['multiple modules', '[Import("bad", "other-module")] public static partial void Bad();', '', /one logical import module per output/u],
    ['overloaded exports', '', `[Export]
    public static void Changed(int value)
    {
    }`, /Overloaded JSExports are not supported/u],
];

for (const [name, imports, exports, message] of rejections) {
    test(`rejects ${name} without writing a contract`, (t) => {
        const current = fixture(t, source(imports, exports));
        const result = current.run();
        assert.equal(result.status, 1, result.stdout + result.stderr);
        assert.match(result.stderr, /error WFCG001:/u);
        assert.match(result.stderr, message);
        assert.equal(existsSync(current.output), false);
    });
}

test('repeated generation is byte-identical and preserves output mtime', (t) => {
    const current = fixture(t);
    current.generate();
    const bytes = readFileSync(current.output);
    // Backdate instead of sleeping: rewriting must change even a coarse filesystem timestamp.
    const old = new Date('2001-01-01T00:00:00Z');
    utimesSync(current.output, old, old);
    const mtime = statSync(current.output, { bigint: true }).mtimeNs;
    current.generate();
    assert.deepEqual(readFileSync(current.output), bytes);
    assert.equal(statSync(current.output, { bigint: true }).mtimeNs, mtime);
    assert.deepEqual(Buffer.from(fixture(t).generate()), bytes);
});

test('ESM resolver follows the namespace, returns the original object and validates exports', async (t) => {
    const current = fixture(t);
    current.generate();
    const config = join(current.directory, 'tsconfig.resolver.json');
    writeFileSync(config, JSON.stringify({
        compilerOptions: {
            strict: true, types: [], target: 'ES2022', module: 'ESNext',
            moduleResolution: 'Bundler', outDir: './emitted', noEmitOnError: true,
        },
        files: ['./contracts.g.ts'],
    }, null, 2));
    const result = runTypeScript(['--project', config, '--pretty', 'false'], {
        cwd: current.directory,
    });
    assert.equal(result.status, 0, result.stdout + result.stderr);
    const outputText = readFileSync(join(current.directory, 'emitted/contracts.g.js'), 'utf8');
    const module = await import(`data:text/javascript;base64,${Buffer.from(outputText).toString('base64')}`);
    assert.equal(module.EventsAssemblyName, 'Example.Backend.dll');
    const events = {
        Changed() {
        },
        CurrentTheme() {
            return 'light';
        },
    };
    assert.equal(module.resolveEventsExports({ BrowserInterop: { Events: events } }), events);
    for (const invalid of [null, {}, { BrowserInterop: null }, { BrowserInterop: { Events: null } }]) {
        assert.throws(() => {
            module.resolveEventsExports(invalid);
        }, /Invalid assembly export path|Invalid interop exports object/u);
    }
    for (const name of ['Changed', 'CurrentTheme']) {
        for (const value of [undefined, null, 42, 'not a function']) {
            const invalid = { ...events, [name]: value };
            if (value === undefined) {
                delete invalid[name];
            }
            assert.throws(() => {
                module.resolveEventsExports({ BrowserInterop: { Events: invalid } });
            }, new RegExp(`Missing interop export: ${name}`, 'u'));
        }
    }
});

function typeCheck(current, body) {
    const consumer = join(current.directory, 'consumer.ts');
    writeFileSync(consumer, `import type { BridgeImports, Theme } from './contracts.g.js';\n${body}\n`);
    const config = join(current.directory, 'tsconfig.consumer.json');
    writeFileSync(config, JSON.stringify({
        compilerOptions: {
            noEmit: true, strict: true, strictFunctionTypes: true, types: [],
            target: 'ES2022', module: 'ESNext', moduleResolution: 'Bundler',
        },
        files: ['./contracts.g.ts', './consumer.ts'],
    }, null, 2));
    const result = runTypeScript(['--project', config, '--pretty', 'false'], {
        allowFailure: true, cwd: current.directory,
    });
    assert.ifError(result.error);
    assert.equal(result.signal, null, `TypeScript terminated: ${result.stderr}`);
    return result;
}

test('strict TypeScript accepts the full contract and rejects unsafe implementations and calls', async (t) => {
    const current = fixture(t);
    current.generate();
    const valid = `declare const bridge: BridgeImports;
const text: string | null = bridge.text(null);
const measure: number = bridge.measure(true, 1, 0.5);
const routedText: string = bridge.route('value', true);
const routedNumber: number = bridge.route(1, 'px');
const theme: Theme = bridge.theme('light');
const implementation: Pick<BridgeImports, 'text' | 'theme'> = {
    text(value: string | null): string | null {
        return value;
    },
    theme(value: Theme): Theme {
        return value;
    },
};`;
    const result = typeCheck(current, valid);
    assert.equal(result.status, 0, `Generated contracts and valid calls must compile.\n${result.stdout}${result.stderr}`);
    const invalid = [
        ['string-only implementation', `const implementation: Pick<BridgeImports, 'text'> = {
    text(value: string): string | null {
        return value;
    },
};`, 2322],
        ['narrowed union implementation', `const implementation: Pick<BridgeImports, 'theme'> = {
    theme(value: 'dark'): Theme {
        return value;
    },
};`, 2322],
        ['wrong argument', "declare const bridge: BridgeImports;\nbridge.text(123);", 2345],
        ['unknown union value', "declare const bridge: BridgeImports;\nbridge.theme('sepia');", 2345],
        ['mixed overload arguments', "declare const bridge: BridgeImports;\nbridge.route('value', 'px');", 2769],
        ['overload return correlation', "declare const bridge: BridgeImports;\nconst result: number = bridge.route('value', true);", 2322],
    ];
    for (const [name, body, code] of invalid) {
        await t.test(name, () => {
            const result = typeCheck(current, body);
            const diagnostics = result.stdout + result.stderr;
            assert.equal(result.status, 2, diagnostics);
            assert.deepEqual(diagnostics.match(/\berror TS\d+:/gu), [`error TS${code}:`], diagnostics);
            assert.match(diagnostics, new RegExp(`consumer\\.ts\\(\\d+,\\d+\\): error TS${code}:`, 'u'));
        });
    }
});