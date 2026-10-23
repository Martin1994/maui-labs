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
generator is not run during contract extraction. Ordinary backend `dotnet build`
invokes it automatically: `BuildBrowserAssets` depends on both
`RestoreBrowserDependencies` and `GenerateBrowserInterop` before running the
backend's npm bundle build. Dependency restoration runs product-root `npm ci`
only when `node_modules` is absent, skips design-time builds, and survives clean
operations. A standalone product-root `npm run build` invokes the generation
target in Release; install product npm dependencies and restore the backend
project first. This is separate from the sample's npm-led site pipeline.

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

See [Testing and validation](../../CONTRIBUTING.md#testing-and-validation) for
coverage, prerequisites, command selection and the checks that require a real browser.
The [interop suite](../../scripts/test/browser-interop.behaviour.test.mjs) tests this tool's
contract generation; the [build suite](../../scripts/test/browser.build.test.mjs)
tests its integration with backend builds and package consumers. Their file-level
JSDoc describes the fixtures and execution details.