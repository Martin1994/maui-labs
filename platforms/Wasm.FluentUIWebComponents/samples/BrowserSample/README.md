# BrowserSample

An experimental .NET MAUI XAML application using the [Fluent UI Web Components browser backend](../../README.md). This is a development example, not a production-ready application or a complete MAUI compatibility demonstration.

## What the application demonstrates

- A normal MAUI application using a separate browser backend.
- `App.xaml` resources and styles, a responsive Grid, rounded borders and vertical scrolling.
- Standard `Clicked` code-behind events: Increment and Reset update a named Label.
- Text input with MAUI text/focus/selection/Completed events from a real Fluent input.
- Animation through awaited MAUI opacity transitions on a dedicated panel.
- Scheduling through a dispatcher timer that updates the UI and stops after five ticks.
- Dynamic layout through runtime child insertion, movement and removal.
- System / Light / Dark theme selection, Fluent palette resources and app-owned theme colors.
- Navigation-based startup timing, uncompressed Wasm size and a source-repository link.

All application UI is defined in [MainPage.xaml](MainPage.xaml). `wwwroot/index.html` supplies only the mount point and runtime bootstrap, except for bootstrap-error reporting. There is no Blazor UI or sample-side DOM layout logic.

## Application structure

- **C# and XAML at the project root** define the MAUI application, UI, state and event handling. MAUI owns layout; the browser-side code does not implement a second UI renderer. XAML is compiled with MAUI's SourceGen inflator.
- **`web/` is the browser source directory.** Edit the TypeScript here for startup and browser-only diagnostics. Backend asset preparation also places its JS bundle and public `.d.ts` files here so imports resolve locally; those supplied files are gitignored and should not be edited.
- **`wwwroot/` contains handwritten static assets only**, including the HTML entry page. TypeScript does not emit here. This directory alone is not the complete runnable application.
- **`artifacts/maui/` is the .NET publish output.** It contains the MAUI module's Wasm, runtime glue, backend JS and declarations, plus symbols/metadata when emitted by the toolchain. It is the frontend's only input from .NET, whether the backend is a source or package reference.
- **`artifacts/typescript/` contains compiled application JS** emitted from `web/`. Edit the TS sources, not these outputs.
- **`artifacts/wwwroot/` is the complete website.** npm site assembly combines all files from `artifacts/maui/`, `wwwroot/` and `artifacts/typescript/`. Serve or deploy this directory.

The npm-led flow is **publish MAUI → prepare adjacent browser assets and compile TypeScript → assemble the site**. The sample owns its npm dependencies, TypeScript configuration and site assembly. MSBuild does not call the sample frontend or register its files for native deployment; a source backend still builds its own npm bundle as part of the .NET build.

## Theme and color resources

The System/Light/Dark buttons set `Application.UserAppTheme` to `Unspecified`, `Light` or `Dark`. The status label uses MAUI's effective theme and `RequestedThemeChanged`; there is no sample-side theme interop. System mode follows the browser preference. Explicit Light/Dark overrides it, and returning to System uses the latest preference. The choice is not persisted across reloads.

[App.xaml](App.xaml) explicitly merges the backend's `FluentThemeResources`. Page backgrounds, foregrounds, muted text, surfaces, strokes and the main heading reference its six keys with `DynamicResource`. Fluent buttons leave their colors unset so their native theme and interaction-state tokens remain active.

The `.NET` wordmark is application-owned branding: `DotNetBrandColor` and `DotNetBrandDarkColor` stay in `App.xaml`, and the label selects between them with `AppThemeBinding`. The distinction is:

| XAML mechanism | Use in this application |
| --- | --- |
| `StaticResource` | Resolve the application-owned brand colors used as binding inputs |
| `DynamicResource` | Keep a property linked to a backend palette key as its value changes |
| `AppThemeBinding` | Choose the light/dark brand input when the effective application theme changes |

Resource values may be declared in XAML or inserted into a `ResourceDictionary` in C#. These mechanisms are built into MAUI; the sample registers no custom resource provider. The backend dictionary reads Fluent presets and updates its entries on effective-theme changes. See the [backend color-resource contract](../../README.md#fluent-color-resources) for the keys and ownership rules.

## Feature sections

Each section demonstrates one integration boundary using normal MAUI APIs:

| Section | Try it | Integration demonstrated |
| --- | --- | --- |
| Events | Increment / Reset | Native clicks reaching C# event handlers |
| Text input | Type, Set text, Select, Clear | Text, focus, selection and Completed notifications |
| Animation | Animate the preview panel | Awaited MAUI animations driven by browser frames |
| Scheduling | Start/restart the timer | UI dispatcher timer callbacks and stopping |
| Dynamic layout | Add / Move / Remove | MAUI child collections updating the rendered tree |

These are user-facing examples, not a benchmark or proof of real-IME/cross-browser compatibility.

## Runtime diagnostics

The footer displays:

- **Startup:** elapsed browser navigation time at the frame following the first MAUI layout. It includes loading and Wasm initialization, unlike a stopwatch started in C# `Main`. This measures a first-render opportunity, not an exact GPU presentation timestamp. Cache, network and background-tab scheduling affect the value; it is recorded once per startup.
- **Wasm size:** uncompressed bytes of `dotnet.native.wasm`, not transfer size or linear memory size. The sample first reads Resource Timing's `decodedBodySize`, avoiding a second request. If unavailable, C# counts the decoded HTTP response through a stream after startup timing finishes; that fallback may make another request.

The source button opens the [backend repository directory](https://github.com/Martin1994/maui-labs/tree/fluent-web/platforms/Wasm.FluentUIWebComponents) in a new tab. Metric orchestration and formatting live in `SampleDiagnostics.cs`; the sample-only JS module supplies browser timing/resource information and opens the URL. No file-size constant is generated at build time. If runtime asset URLs change, update the diagnostics URL accordingly.

## External package consumption

Use the backend's [NuGet PackageReference instructions](../../README.md#consume-the-backend-package) for an external application. There are **no released packages currently**; use an experimental/local package version actually available from your configured feed.

**The checked-in C# project is still a source-checkout development project.** It contains a backend `ProjectReference`, with no manual targets import, and inherits shared product build properties and root Central Package Management. Copying this directory alone does not make `BrowserSample.csproj` an independent NuGet consumer.

To adapt it for external consumption, replace the backend `ProjectReference` with a `PackageReference`, and supply your application's own browser toolchain properties, package versions and feeds formerly inherited from the checkout. That includes the target framework/`browser-wasm` RID, JS interop and NativeAOT/trimming settings, and matching LLVM runtime/compiler packages for an available compiler host. Retain SourceGen XAML and the backend `UnmanagedEntryPointsAssembly` entry. NuGet restore automatically imports the packaged `Content` items. Remove any manual backend import left from an older setup; do not substitute a NuGet-cache path. See the [toolchain requirements](../../README.md#toolchain-configuration) and [inherited configuration details](../../CONTRIBUTING.md#repository-configuration).

The **frontend pipeline is identical for package and source references**: its own npm project and scripts consume app-local `artifacts/maui/`, `web/` and `wwwroot/`, with no backend source, intermediate-output or NuGet-cache paths. Both reference types publish the same three backend asset filenames. Given a matching complete MAUI publish output, frontend preparation, compilation and site assembly need no C# build. This split does not establish that a copied, standalone C# project or its NativeAOT publish has been verified.

## Build and run

See the [backend toolchain configuration](../../README.md#toolchain-configuration) for SDK, MAUI, NativeAOT LLVM and Node requirements. On a supported compiler host, install .NET 10, Node.js 22+ and npm, and initialize Emscripten 3.1.57 before building. Native publishing requires matching runtime/compiler packages; the backend package does not make arbitrary .NET browser SDK configurations work. The current `runtime.osx-arm64.Microsoft.DotNet.ILCompiler.LLVM` package is unavailable, so macOS ARM64 cannot perform the sample's native publish.

For a clean checkout with those prerequisites ready, run from **this sample directory**, `platforms/Wasm.FluentUIWebComponents/samples/BrowserSample`, not the product root:

```bash
npm ci
npm run build
```

This installs the sample's locked frontend dependencies, publishes the MAUI module, compiles TypeScript and assembles `artifacts/wwwroot`. No separate backend build or product-root `npm ci` is a prerequisite: `dotnet publish` follows the source `ProjectReference`, whose backend build installs its own dependencies when product-root `node_modules` is absent. No extra environment variables are prescribed. All commands below are sample-local scripts; the backend npm project has no forwarding aliases.

| Sample-local command | Operation |
| --- | --- |
| `npm run build` | `npm run build:maui-app && npm run build:ts && npm run build:site` |
| `npm run build:maui-app` | `dotnet publish BrowserSample.csproj` → `artifacts/maui/` |
| `npm run prepare:maui-bridge` | `node scripts/prepare-maui.mjs`: copy three published backend assets into `web/` and clear `artifacts/typescript/` |
| `npm run build:ts` | `npm run prepare:maui-bridge && tsc --project tsconfig.json` → `artifacts/typescript/` |
| `npm run check` | `tsc --project tsconfig.json --noEmit`; requires prepared declarations |
| `npm run build:site` | `node scripts/build-site.mjs` → complete `artifacts/wwwroot/` |
| `npm run package:site` | `dotnet msbuild BrowserSample.csproj -target:PackageSite`; archive an existing site |

### Frontend assets and compilation

`prepare:maui-bridge` reads `maui-browser.js`, `maui-browser.d.ts` and `dotnet.d.ts` from `artifacts/maui/` and copies them beside the handwritten TS sources in `web/`. These adjacent declarations serve both the editor and compiler. The script validates all three inputs before copying, then clears `artifacts/typescript/` so removed sources cannot leave stale compiled JS. It does not restore, build or publish .NET.

With the sample's npm dependencies installed and a matching MAUI publish already in `artifacts/maui/`, rebuild only the frontend:

```bash
npm run build:ts
npm run check
npm run build:site
```

For editor declarations alone, run `npm run prepare:maui-bridge`; remember that it also clears compiled TS output, so run `npm run build:ts` before assembling the site again. `npm run check` neither prepares inputs nor emits files.

`tsconfig.json` resolves adjacent declarations without `allowJs`, `checkJs`, `rootDirs` or backend source paths. The sample's `.gitignore` ignores supplied `web/*.js` and `web/*.d.ts`, **not handwritten `web/*.ts`**, plus its own `bin`, `obj`, `node_modules` and `artifacts` directories. `wwwroot/` is reserved for handwritten static files. When migrating an older checkout, remove previously emitted `wwwroot/main.js` and `wwwroot/sample-diagnostics.js`; they would collide with the new TypeScript output.

### Module publishing and site assembly

`dotnet publish` defaults to Release for this .NET 10 project and writes the MAUI module to `artifacts/maui/`. It does **not** build the sample TypeScript or produce a complete website, and never calls the sample's npm scripts. The source backend still intentionally builds its own JS resources through npm. Ordinary `dotnet build` performs managed compilation and backend resource generation, not the native Wasm publish. The project uses invariant globalization and enables trimming/AOT diagnostics through its shared configuration.

`build:site` preserves **all files** from `artifacts/maui/`, handwritten `wwwroot/` and compiled `artifacts/typescript/`, including declarations, symbols, maps and metadata when present. It rejects missing required runtime/application assets and path collisions rather than overwriting files. Successful assembly replaces the final site directory, so files removed from the inputs do not remain in the site.

Default paths are **sample-local**, not product-root artifacts:

- MAUI module (`PublishDir`): `artifacts/maui/`.
- Assembled website: `artifacts/wwwroot/`.
- Optional ZIP (`SiteArchive`): `artifacts/BrowserSample-site.zip`.

The npm scripts use these fixed app-local input/output directories. Overriding .NET's `PublishDir` does not redirect frontend preparation or site assembly. `SiteDirectory` and `SiteArchive` only configure the optional ZIP utility and are independent of `PublishDir`.

Host the assembled `artifacts/wwwroot` directory with any static server that serves Wasm correctly; no repository hosting script or .NET server is required. The revised pipeline is not a claim of a newly validated native publish or standalone C# consumer. See the deployment requirements below.

### Contributor-only checkout details

Backend source builds, inherited build/CPM settings and compiler-host lockfiles are covered in [CONTRIBUTING.md](../../CONTRIBUTING.md). They describe this checkout, not the recommended reference mechanism for external applications.

## Static deployment and optional ZIP

The **assembled site directory** can be served or uploaded as-is. PDBs, declarations and runtime metadata can remain. Do not deploy the entire `artifacts` directory, which can contain logs and unrelated build outputs.

From this **sample directory**, this optional command archives the complete sample-local `artifacts/wwwroot` without rebuilding, stripping or filtering files:

```bash
npm run package:site
```

It invokes this project's `PackageSite` target using MSBuild's built-in `ZipDirectory` task and writes `artifacts/BrowserSample-site.zip`. Packaging is not automatic and does not restore, rebuild, publish or assemble the site. For another site or archive location:

```bash
npm run package:site -- -p:SiteDirectory=/path/to/published-site -p:SiteArchive=/path/to/site.zip
```

`SiteDirectory` selects only the ZIP source (default `artifacts/wwwroot`); `SiteArchive` selects only the ZIP destination. Neither changes `PublishDir` or the npm scripts' directories, and a `PublishDir` override does not change the ZIP source. Supply custom paths on each packaging invocation; command-line overrides are not persisted.

If packaging an already published site collected from another machine, pass that static folder as `SiteDirectory`. Packaging uses the supplied/default directory, not a repository collection marker, and needs no backend source-path override.

`index.html` is at the ZIP root. Keep the archive destination outside the source site. You can instead copy or ZIP the published directory using your usual deployment tools.

Extract/upload the contents without changing relative paths. Serve `.js` as JavaScript and `.wasm` as `application/wasm`; no .NET server is required. The application uses relative asset URLs for root or subdirectory hosting. HTTP gzip/Brotli compression is independent of ZIP compression. Never mix cached runtime assets from different publishes.

## Limitations

The application inherits the [backend's limitations](../../README.md#boundaries-and-known-limitations). It is not a production accessibility, high-contrast, cross-browser, performance or long-running reliability certification. Publishing and browser regression checks must be rerun when the interop protocol or runtime assets change.