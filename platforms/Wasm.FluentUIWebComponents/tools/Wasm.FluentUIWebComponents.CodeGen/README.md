# Wasm.FluentUIWebComponents.CodeGen

Host-only build tool for the experimental Fluent browser backend. It reads C#
`JSImport` / `JSExport` declarations with Roslyn and emits the private TypeScript
wire contract. It is not a Roslyn source generator, a runtime dependency, or a
shipping package. The existing NativeAOT LLVM generator still owns marshalling.

## Command line

The entry point uses `System.CommandLine`: `Wasm.FluentUIWebComponents.CodeGen
<project> <output> [--configuration <name>]`. The configuration defaults to
`Release` and also accepts `-c`. `--help` and `--version` are supplied by the command
library. Required arguments, existing project files and unknown options are
validated before MSBuild is loaded; generation failures retain diagnostic `WFCG001`.

## Build integration

The backend's `GenerateBrowserInterop` target passes the C# project path and
configuration. `MSBuildLocator` discovers the SDK relative to that project's
`global.json`, and `MSBuildWorkspace` loads its sources, references and compilation
options through a design-time build. There is no custom compiler-input manifest.
The tool is a build-only project reference; its assembly, Roslyn, MSBuild and build
host dependencies do not flow to the app.

The tool runs **before TypeScript checking and before backend compilation**. It
never loads the backend DLL or depends on another generator's output. Analyzer
references are removed before requesting the compilation, so the LLVM marshalling
generator is not run during contract extraction. Ordinary
`dotnet build` invokes it automatically. A standalone `npm run build` invokes the
same target in Release; restore the backend project first.

The generated contract lives in `src/Wasm.FluentUIWebComponents/web/generated/`.
It is shared across configurations and gitignored. Ordinary relative imports
resolve to real files, without a `#browser-interop` alias or backend `rootDirs`.
The matching runtime declaration is copied to `web/dotnet.d.ts`, so its public
`./dotnet.js` import also retains its physical path. Shared files are atomically
replaced only when content changes and are not deleted by configuration-specific
clean operations.

The backend's interop declarations and string-union values must remain identical
across supported configurations; configuration-dependent protocol changes are not
supported by this shared-source layout. The integration test compares Debug and
Release contracts. Final declarations and browser assets remain configuration-local
under `obj/browser/<Configuration>/<TFM>/<RID>/`.

The frontend invokes the installed `tsc` executable using ordinary tsconfig files,
not the JavaScript Compiler API. This avoids coupling the build to the compiler's
implementation language; it does not claim that an untested TS version is supported.

## Generated surface

- `<ImportType>Imports`: strict function properties, including overload call
  signatures when several C# methods import the same JS function. Parameter
  correlations are not flattened into independent unions.
- `<ImportType>ModuleName`: the logical module key from `JSImport`, not a file or
  npm package path.
- `<ExportType>Exports`, `<ExportType>AssemblyName` and
  `resolve<ExportType>Exports`: the assembly identity, namespace traversal and
  runtime check that every declared export is a function.
- Named string unions where parameters/returns use the backend's `JSStringUnion`.
- Original parameter names, except JS reserved names receive an underscore suffix
  that avoids collisions with other parameters.

The parameter-name restrictions follow [ECMA-262 §12.7.2, Keywords and Reserved
Words](https://tc39.es/ecma262/multipage/ecmascript-language-lexical-grammar.html#sec-keywords-and-reserved-words)
and [MDN's lexical grammar reference](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Lexical_grammar#reserved_words).
The set includes strict/module-context restrictions, not just unconditional
keywords: `eval` and `arguments` are restricted bindings rather than keywords.

Function properties deliberately avoid TypeScript's bivariant method parameters:
an implementation accepting only `string` cannot satisfy a `string | null`
parameter. Runtime resolver checks establish method presence, **not** argument
types or managed/JS version compatibility. Always deploy matching managed and JS
artifacts.

`JSStringUnion(typeof(Values))` refers to a type containing distinct non-null
string constants. The type's simple name becomes the TS type name. The actual
boundary must remain `string` / `string?`; the attribute neither performs
marshalling nor validates runtime values. The theme converters use the same
constants, while their runtime rejection of unsupported themes remains handwritten.
The attribute belongs to `Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents`,
not to the CodeGen tool assembly. The backend does not reference the tool at runtime.

## Deliberately limited first version

- One import declaring type and one logical module, plus one export declaring
  type, per output. Multiple partial source files are supported.
- Static, non-generic synchronous methods on non-nested, non-generic types.
- `void`, `string`, `string?`, `bool`, `int`, `double`.
- No async/Task, arbitrary objects, enums, arrays, ref/out/in, optional or params
  arguments, dotted JS function paths, or overloaded exports.
- `JSMarshalAs` is rejected until its actual wire representation has an explicit
  mapping. Unsupported types never fall back to `any`.

Attribute aliases and constant expressions are resolved semantically. Unknown
declaration attributes and invalid syntax fail generation. The tool does not
attempt a complete C# build: missing generated partial implementations and body
diagnostics belong to the subsequent compiler/LLVM generator.

The output is deterministic, contains source-symbol comments and no timestamps or
absolute machine paths, and is only rewritten when its content changes. Do not
edit or commit generated output.

## Tests

From the product directory, after installing its existing npm dependencies:

```sh
npm run test:interop
```

`npm run test:interop` first builds only the CodeGen tool via `pretest:interop`,
then runs independent restored C# fixture
projects through `MSBuildWorkspace`. It does not clean or rebuild the backend.
Tests cover conditional compilation, attribute aliases, parameter escaping,
overloads, unions, rejection diagnostics, deterministic output, strict TS negative
cases and the emitted resolver at runtime. TypeScript checks and JS emit use the
compiler CLI, not its JS API.

Run `npm run test:interop:build` separately for the heavier build regression.
Install dependencies in both the product and `samples/BrowserSample` directories
first. The regression cleans/rebuilds this backend in Debug and Release, compares their shared source
contracts, checks configuration-local assets and unchanged contract mtime, verifies
design-time generation is skipped, and builds a temporary compile-only consumer
to ensure only runtime assets flow through the project reference. It does not
publish or require a native LLVM compiler host. Do not run this clean/build test
concurrently with other builds of the same configurations.

These commands test the interop generator and its build integration, not the
backend's complete runtime or UI behavior; they are not a package-wide test suite.

The build tests also exercise the consumer asset targets through both a source
project reference and an actual local NuGet package with automatic `buildTransitive`
import. They check first-build source materialization, warm-build glob filtering,
missing-file regeneration and a single canonical publish item per browser asset.
The TypeScript sample check also verifies that absent compiled JS outputs are
registered for native deployment before the first build, emitted by its frontend
target, and not duplicated on a warm build. This does not run the native app.
An isolated frontend check copies the sample's npm project and public browser
assets outside the checkout, installs its own dependencies, and runs its local
build/check scripts. It does not claim standalone C# or NativeAOT validation.