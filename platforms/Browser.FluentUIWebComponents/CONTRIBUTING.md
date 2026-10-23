# Contributing to the Fluent UI Web Components backend

These instructions are for **source-checkout development**, not external application setup. External applications should follow the [NuGet PackageReference guidance](README.md#consume-the-backend-package). There are no released packages for this backend currently. Package consumption is experimental/local only.

## Repository configuration

Run backend commands from `platforms/Browser.FluentUIWebComponents` unless stated otherwise.

- Product [global.json](global.json) selects stable .NET 10.0 with `latestFeature` roll-forward. It is not locked to any patch version, but .NET 11+ and preview SDKs are not tested yet thus not specified in the constraint. This does not change the pinned LLVM packages.
- Product [Directory.Build.props](Directory.Build.props) supplies shared browser build properties, including `net10.0`, `browser-wasm`, unsafe code, JS interop, trimming/AOT analyzers and MAUI feature switches. These settings are not automatically supplied to an external app by referencing the backend package.
- NuGet dependencies use repository [Central Package Management](../../Directory.Packages.props). [NuGet.config](NuGet.config) configures feeds, including the experimental compiler feed. External applications need their own feeds, package versions and browser toolchain configuration rather than relying on these ancestor files.
- The backend and sample packages each have their own `package.json`, `package-lock.json` and npm installation. They shouldn't be shared because a sample package should be considered as an external consumer which does not have access to the source code of the backend.

See the [toolchain requirements](README.md#toolchain-configuration). Native publishing needs a compatible compiler host and initialized Emscripten environment, not just a compatible .NET SDK.

## Build from source

With .NET 10, Node.js 22+ and npm installed, run from the product directory:

```bash
dotnet build src/Browser.FluentUIWebComponents/Browser.FluentUIWebComponents.csproj -c Release
```

This restores/builds the managed backend and its browser bundle/declarations. No Wasm output will be generated in this backend package. Native Wasm and a runnable website will be generated in the consumer packages.

Build instruction:

```
dotnet build [-c Debug|Release]
```

`dotnet build` runs `RestoreBrowserDependencies` (`npm ci`) -> `GenerateBrowserInterop` -> `BuildBrowserAssets` (`npm run build`) in addition to the regular C# build. Dependency restoration runs `npm ci` automatically at the product root when `node_modules` is absent. These targets skip design-time builds, and clean operations retain the shared npm dependencies. After dependency or lockfile changes, run `npm ci` explicitly to refresh an existing installation.

A direct backend-root `npm run build` is a lower-level bundle command. After installing product npm dependencies and restoring the backend project, it invokes `GenerateBrowserInterop` itself and builds the browser bundle/declarations; it does not require a previously compiled backend DLL. Prefer `dotnet build` when checking the managed backend and its JS bridge together.

To compile the full solution (including .NET part of the sample projects) on a compatible compiler host (see toolchain requirements), run from the product directory:

```bash
dotnet build Browser.FluentUIWebComponents.slnx -c Release
```

Please be aware that this does not compile the sample website. For a complete sample build on a supported host with initialized Emscripten, run `npm ci` followed by `npm run build` in the **sample directory**.  See the [sample README](samples/BrowserSample/README.md#build-and-run).

## Sample toolchain setup

Host-specific `packages.<SDK-host-RID>.lock.json` files are included for `linux-arm64`, `linux-x64` and `win-x64` (macOS is not yet supported due to lack of NuGet package). Restore support does not imply native publishing has been tested on every host. Adding another supported host requires generating and reviewing its lockfile. From the sample directory, verify locked restore with:

```bash
dotnet restore BrowserSample.csproj -p:RestoreLockedMode=true
```

## Application resource pipeline

### Design goals

Resource processing belongs to the .NET app build, not to the frontend bundler.
The design has three goals:

1. **Use standard MAUI declarations.** The app declares `MauiImage`, `MauiFont` and
   `MauiAsset`; the backend supplies the rules needed to consume them. No additional
   app-specific resource script is required. The current [resource contract](README.md#packaged-application-resources)
   covers unchanged images/fonts and Raw packing, not Resizetizer or a Raw file API.
2. **Keep one implementation for source and package consumers.** Both entry points
   import [MauiBrowser.AppAssets.targets](src/Browser.FluentUIWebComponents/buildTransitive/MauiBrowser.AppAssets.targets).
   These rules run in browser executable app heads, where the items are declared,
   rather than in referenced libraries or design-time builds.
3. **Make publish output the frontend boundary.** The rules validate resource names,
   generate `maui-assets.json`, and register files with the SDK's build/publish copy
   pipeline. Frontend tooling consumes that output without discovering backend
   intermediates. The task runs on the build host and is not an app runtime dependency;
   ordinary build does not invoke native publish.

Custom resource processing remains possible. When extending the pipeline, preserve
collision detection and ownership-based cleanup so an incremental build neither
retains removed resources nor overwrites unrelated host files.

### Choosing a consumption path

| Reference | When to use it | How the app gets the resource rules |
| --- | --- | --- |
| `ProjectReference` | Developing the backend together with an app; this is how `BrowserSample` exercises local source changes | Apps under this product inherit `Directory.Build.targets`, which imports the shared rules and selects the source host-task project |
| `PackageReference` | Consuming a packaged backend, or checking what an external consumer receives before distributing a change | NuGet imports the package's `buildTransitive` entry point, which loads the same rules and the packaged host-task assembly |

**Source-reference detail:** `ProjectReference` builds/references the backend output,
but does not automatically import `.targets` merely because they reside in the
referenced project's `buildTransitive` directory. The shared `Directory.Build.targets`
provides that connection for our sample. An external source consumer needs an explicit
connection to the app resource rules; receiving the backend's JS Content alone does
not provide it.

The source bootstrap queries the tool project's actual output and stages its DLL
under the app's intermediate directory. This gives `UsingTask` a loading path known
at project evaluation time without assuming the tool's TFM or output layout. The
stage is isolated per app/configuration/TFM/RID, excluded from publish, and removed
by Clean. Browser-specific global properties are filtered before building the host
tool; its own project configuration supplies its framework and host defaults.

**Package-reference remarks:** no backend package is released yet; contributors can
pack locally to test this path. The package supplies resource rules, not the app's
SDK/LLVM configuration or repository-wide package versions. Follow the
[package setup guidance](README.md#consume-the-backend-package) for an independent
consumer. Simply copying `BrowserSample` is insufficient: it inherits repository
configuration and links binary fixtures from other sample directories.

## Testing and validation

Use this section to choose checks by the behavior being changed. The permanent
automated suites use Node's test runner, including suites that launch .NET tools;
they are not run by `dotnet test` on the product solution. Fixture construction,
mock boundaries and script-specific setup are documented in each test file's JSDoc.

### Coverage and when to run

| Validation layer | What it covers | Run when changing |
| --- | --- | --- |
| JS boundary tests: [viewport](scripts/test/browser-viewport.behaviour.test.mjs), [asset runtime](scripts/test/browser-assets-runtime.behaviour.test.mjs), [asset adapters](scripts/test/browser-assets-adapters.behaviour.test.mjs) | Layout scheduling, catalog/URL and startup behavior, image/font request lifecycle against controlled boundary doubles | Browser bridge, bootstrap, catalog resolver, image/font adapters or layout invalidation |
| Managed and contract integration: [interop](scripts/test/browser-interop.behaviour.test.mjs), [managed assets](scripts/test/browser-assets-managed.behaviour.test.mjs) | Real code generation and TypeScript contract checks; MAUI handler/font-registration behavior with the JS boundary replaced | C# interop declarations, generator, handlers, hosting/font services or MAUI dependencies |
| Build and distribution integration: [backend build](scripts/test/browser.build.test.mjs), [app assets](scripts/test/browser-assets.build.test.mjs) | Real build/pack/restore and managed SDK publish through source/package paths; site assembly, resource bytes, diagnostics and incremental cleanup | MSBuild rules, package contents, dependency configuration, build/site scripts or resource naming/ownership |

During development, run the relevant layer. Before submitting backend changes, run
both aggregate commands below. Changes affecting runtime or UI behavior also need
the real-browser validation described afterward; the automated suites do not cover
every supported control or the complete application lifecycle.

### Prerequisites and commands

The automated suites require Node.js 22+ and the product npm dependencies. Managed
and build integration also require the product's .NET 10 SDK and access to the
configured NuGet/npm feeds or the necessary caches. They do **not** require Emscripten
or a running browser. Build integration creates fresh dependency installations, so
an existing backend `node_modules` alone does not make that suite offline-ready.

For a full automated check, run these **sequentially from the product directory**:

```bash
npm ci
dotnet build src/Browser.FluentUIWebComponents/Browser.FluentUIWebComponents.csproj -c Release
npm run test:behaviour
npm run test:build
```

The initial build provides restored MAUI references and Release package inputs.
`test:behaviour` runs the backend JS, contract and managed checks, then delegates to
`npm test` in `samples/BrowserSample`, keeping sample coverage in the full command.
Its pretest hook builds CodeGen but does not restore the backend. `test:build` runs the two build
suites serially and has no setup hook. Keep the initial build for cold checkouts
rather than relying on an earlier developer build or test execution order.

Backend suites belong in product `scripts/test/`: `*.behaviour.test.mjs` for
behavior checks and `*.build.test.mjs` for build/distribution integration.
Sample suites and fixtures belong in `samples/BrowserSample/scripts/test/`, where
the sample's `npm test` discovers `*.behaviour.test.mjs`. Add suites under their
owner with the appropriate suffix; no package-script file list needs updating.
Node expands the quoted globs, excluding helper files and temporary artifacts.

For JS-only work, no .NET setup is needed:

```bash
npm ci
node --test scripts/test/browser-viewport.behaviour.test.mjs scripts/test/browser-assets-runtime.behaviour.test.mjs scripts/test/browser-assets-adapters.behaviour.test.mjs
```

For a focused run, use `node --test <suite-path>` after satisfying the prerequisites
in that file's JSDoc. For example, the interop suite requires a built Release
generator, while managed asset tests require restored backend references. See the
[sample testing guidance](samples/BrowserSample/README.md#sample-regression-coverage)
for its portable-reference prerequisites and browser verification boundaries.

**Build side effects:** build integration cleans/rebuilds backend outputs and
creates local packages. Do not overlap it with another backend build, clean, pack,
test command or sample publish, even in a different configuration: some generated
sources and tool/npm state are shared. The aggregate's serial option does not
coordinate other terminals. Sample builds also replace their assembled website.

### What needs a real publish and browser

Passing these suites establishes protocol and build behavior, not native execution
or rendering. The package tests use managed publish; some site-assembly inputs are
mock native files. A successful copy or TypeScript check does not establish that a
Wasm application can start.

For runtime changes, initialize the matching LLVM/Emscripten toolchain and run
`npm ci`, `npm run build`, then `npm run check` in `samples/BrowserSample`. See the
[sample build/run guide](samples/BrowserSample/README.md#build-and-run). For packaging
changes, also publish an independent app using the locally built NuGet package so
repository imports cannot conceal missing package configuration.

Test the **newly assembled site**, not a previous preview directory. Changes to C#
interop signatures also change the generated JS contract; rebuilding only the DLL
can leave the browser running an incompatible bridge. Similarly, catalog-format
changes affect both the build task that writes it and the runtime that reads it.
Republish and reassemble before checking either change. Even an app without resources
needs an empty catalog; a missing catalog is a broken build/deployment, not an empty app.

Choose the browser checks relevant to the change:

- **Rendering and interaction:** actual Fluent controls, fractional sizes, scrolling,
  DPR/zoom, focus/keyboard/IME and accessibility. Boundary doubles do not reproduce
  the browser's layout, input or accessibility implementation.
- **Resource consumption:** real image decoding, font loading and fixed-viewport
  remeasurement; root/subdirectory hosting; errors and late completions after
  removal/shutdown. Use the sample's [resource fixtures](samples/BrowserSample/README.md#packaged-resource-fixtures).
  Raw checks establish packaging only, not a FileSystem implementation.
- **Native and distribution behavior:** trimming/AOT diagnostics, actual external
  package startup, and HTTP MIME/cache behavior. Test cold and warm starts separately
  on a visible page; a no-store development server is not a warm-cache benchmark.

For sample UI changes, follow the concise
[BrowserSample verification checks](samples/BrowserSample/README.md#sample-regression-coverage).

Record the toolchain/browser versions, serving configuration and checks performed.
Cross-browser behavior, performance and long-running resource retention require
their own measurements; a passing functional suite does not establish them.

## Dependency changes

Follow the repository's dependency policies. From the product directory, run `node ../../eng/scripts/audit-npm.mjs` after npm dependency or lockfile changes before opening a PR. Keep both npm projects registered in `.github/dependabot.yml`; do not treat an unavailable advisory database as a clean audit.