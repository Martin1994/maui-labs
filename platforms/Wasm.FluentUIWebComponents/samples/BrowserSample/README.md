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
- **`wwwroot/` contains application web assets.** It holds the handwritten HTML entry page and the JS emitted from `web/` by TypeScript. Edit the TS sources, not the generated JS. This directory alone is not the complete runnable application.
- **`artifacts/wwwroot/` is the complete published website.** Publishing combines the application web assets with the backend bundle and .NET/Wasm runtime files. Serve or deploy this directory.

The flow is **TS sources in `web/` → compiled JS in `wwwroot/` → complete site in `artifacts/wwwroot/`**. The sample owns its npm dependencies and TypeScript configuration; MSBuild coordinates backend asset preparation, frontend compilation and publishing.

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

**The checked-in C# project is still a source-checkout development project.** It contains a backend `ProjectReference` and an explicit repo-relative targets `<Import>`, and inherits shared product build properties and root Central Package Management. Copying this directory alone does not make `BrowserSample.csproj` an independent NuGet consumer.

To adapt it for external consumption, remove both the backend `ProjectReference` and explicit `<Import>`, add the backend `PackageReference`, and supply your application's own browser toolchain properties, package versions and feeds formerly inherited from the checkout. That includes the target framework/`browser-wasm` RID, JS interop and NativeAOT/trimming settings, and matching LLVM runtime/compiler packages for an available compiler host. Retain SourceGen XAML and the backend `UnmanagedEntryPointsAssembly` entry. NuGet restore automatically imports the packaged targets; do not substitute a NuGet-cache path for the removed import. See the [toolchain requirements](../../README.md#toolchain-configuration) and [inherited configuration details](../../CONTRIBUTING.md#repository-configuration).

The **frontend is portable**: its own `package.json`, lockfile and TypeScript configuration operate on app-local `web/` and `wwwroot/`, with no backend source or intermediate-output paths. Once the backend assets are supplied, TypeScript compilation uses only these local inputs. This split does not establish that a copied, standalone C# project or its NativeAOT publish has been verified.

## Build and run

See the [backend toolchain configuration](../../README.md#toolchain-configuration) for SDK, MAUI, NativeAOT LLVM and Node requirements. Native publishing requires matching runtime/compiler packages, an available compiler host and an initialized compatible Emscripten toolchain; the backend package does not make arbitrary .NET browser SDK configurations work. For the checked-in source-reference project, first complete the [contributor setup](../../CONTRIBUTING.md#build-from-source).

Run all commands below from **this sample directory**, `platforms/Wasm.FluentUIWebComponents/samples/BrowserSample`, not the product root:

```bash
npm ci --ignore-scripts
```

This installs the sample's own locked frontend dependencies. All sample commands are defined in this directory's `package.json`; the backend package does not provide forwarding aliases.

| Sample-local command | Operation |
| --- | --- |
| `npm run prepare:browser` | `dotnet msbuild BrowserSample.csproj -t:PrepareMauiBrowserAssets -p:Configuration=Release` |
| `npm run build:browser` | `dotnet msbuild BrowserSample.csproj -t:BuildBrowserBootstrap -p:Configuration=Release` |
| `npm run build` | Compile app TypeScript into `wwwroot/` using the sample's local `tsc` |
| `npm run check` | Type-check the same sources with `--noEmit` |
| `npm run publish:site` | `dotnet publish BrowserSample.csproj` |
| `npm run package:site` | `dotnet msbuild BrowserSample.csproj -t:PackageSite` |

### Frontend assets and compilation

The public `MauiBrowserAssetsDirectory` property points to `$(MSBuildProjectDirectory)/web`. Asset preparation places `maui-browser.js`, `maui-browser.d.ts` and `dotnet.d.ts` beside the handwritten TS sources. For a package-based application, restore its NuGet references before preparation; the checked-in source workflow instead requires the backend restore/build described in the contributor guide.

To prepare assets and compile/type-check the frontend without compiling or publishing the native application:

```bash
npm run prepare:browser
npm run build
npm run check
```

Alternatively, `npm run build:browser` combines asset preparation and TypeScript compilation. `BuildBrowserBootstrap` also runs automatically during normal builds; it invokes `npm run build` with the **sample directory** as its working directory. With assets already prepared, `npm run build` and `npm run check` need no backend source build. The MSBuild preparation/bootstrap targets do not implicitly restore the application's native compiler host.

`tsconfig.json` compiles application TS into `wwwroot/`, resolving the adjacent declarations without `allowJs`, `checkJs`, `rootDirs` or a backend-bundle exclusion. The sample's `.gitignore` ignores `web/*.js` and `web/*.d.ts`, **not handwritten `web/*.ts`**; it also ignores emitted `wwwroot/*.js` and its own `bin`, `obj`, `node_modules` and `artifacts` directories. Supplied/generated files should not be edited. `wwwroot/index.html` remains handwritten and supplies the static mount/bootstrap root. Backend targets manage the backend output/publish assets separately; no custom source-path lookup or copy step is needed.

### Publish and host

After toolchain setup and npm installation, run from this sample directory:

```bash
npm run publish:site
npm run package:site
```

Publishing and packaging use the project's defaults without extra RID or output-path parameters. `dotnet publish` defaults to Release for this .NET 10 project. The shared `SiteDirectory` and `SiteArchive` defaults are **sample-local**, not product-root artifacts:

- Published website: `samples/BrowserSample/artifacts/wwwroot` relative to the product root (`artifacts/wwwroot` from here).
- Complete website archive: `samples/BrowserSample/artifacts/BrowserSample-site.zip` relative to the product root (`artifacts/BrowserSample-site.zip` from here).

Publishing does not automatically ZIP, and packaging does not rebuild or publish. If invoking `Publish` directly through MSBuild, include `-restore` on a clean project; unlike `dotnet publish`, `dotnet msbuild -t:Publish` does not implicitly restore. The project uses invariant globalization and enables trimming/AOT diagnostics through its current shared configuration.

Host the published `artifacts/wwwroot` directory with any static server that serves Wasm correctly; no repository hosting script or .NET server is required. See the deployment requirements below.

### Contributor-only checkout details

Backend source builds, the sample's explicit source-reference import, inherited build/CPM settings and compiler-host lockfiles are covered in [CONTRIBUTING.md](../../CONTRIBUTING.md). They describe this checkout, not the recommended reference mechanism for external applications.

## Static deployment and optional ZIP

The **published site directory** can be served or uploaded as-is. PDBs, declarations and runtime metadata can remain. Do not deploy the entire `artifacts` directory, which can contain logs and unrelated build outputs.

From this **sample directory**, this optional command archives the complete sample-local `artifacts/wwwroot` without rebuilding, stripping or filtering files:

```bash
npm run package:site
```

It invokes this project's `PackageSite` target using MSBuild's built-in `ZipDirectory` task and writes `artifacts/BrowserSample-site.zip`. Packaging is not automatic. For another site or archive location:

```bash
npm run package:site -- -p:SiteDirectory=/path/to/published-site -p:SiteArchive=/path/to/site.zip
```

`SiteDirectory` also supplies the default `PublishDir`, so the same override can be used for both publishing and packaging. An explicit `PublishDir` is used as the packaging source when no separate `SiteDirectory` is supplied. Custom paths must be supplied again for a later, independent packaging invocation; the project does not persist command-line overrides.

If packaging an already published site collected from another machine, pass that static folder as `SiteDirectory`. Packaging uses the supplied/default directory, not a repository collection marker, and needs no backend source-path override.

`index.html` is at the ZIP root. Keep the archive destination outside the source site. You can instead copy or ZIP the published directory using your usual deployment tools.

Extract/upload the contents without changing relative paths. Serve `.js` as JavaScript and `.wasm` as `application/wasm`; no .NET server is required. The application uses relative asset URLs for root or subdirectory hosting. HTTP gzip/Brotli compression is independent of ZIP compression. Never mix cached runtime assets from different publishes.

## Limitations

The application inherits the [backend's limitations](../../README.md#boundaries-and-known-limitations). It is not a production accessibility, high-contrast, cross-browser, performance or long-running reliability certification. Publishing and browser regression checks must be rerun when the interop protocol or runtime assets change.