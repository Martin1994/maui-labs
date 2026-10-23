# BrowserSample

An experimental .NET MAUI XAML application using the [Fluent UI Web Components browser backend](../../README.md). This is a development example, not a production-ready application or a complete MAUI compatibility demonstration.

## What the application demonstrates

- A normal MAUI application using a separate browser backend.
- `App.xaml` resources and styles, a responsive Grid, rounded borders and vertical scrolling.
- Standard `Clicked` code-behind events: Increment and Reset update a named Label.
- Text input with MAUI text/focus/selection/Completed events from a real Fluent input.
- Standard app-head `MauiImage`, `MauiFont` and `MauiAsset` declarations: unchanged
	images, registered Open Sans, and Raw packing without a runtime file API.
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
- **`Resources/` contains app-declared resources.** Images/fonts can also be linked
	from tracked repository fixtures in the csproj. These are standard MAUI items,
	not handwritten web assets; `Resources/Raw/` includes sample data and licenses.
- **`artifacts/maui/` is the .NET publish output.** It contains the MAUI module's Wasm, runtime glue, backend JS and declarations, the mandatory `maui-assets.json` and `maui-assets/` files, plus symbols/metadata when emitted by the toolchain. It is the frontend's only input from .NET, whether the backend is a source or package reference.
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
| Packaged assets | Compare the four image frames; inspect the Label, Increment and text input fonts | Standard resource declarations, file/alias resolution and explicit image sizing |
| Animation | Animate the preview panel | Awaited MAUI animations driven by browser frames |
| Scheduling | Start/restart the timer | UI dispatcher timer callbacks and stopping |
| Dynamic layout | Add / Move / Remove | MAUI child collections updating the rendered tree |

These are user-facing examples, not a benchmark or proof of real-IME/cross-browser compatibility.

## Packaged resource fixtures

[BrowserSample.csproj](BrowserSample.csproj) declares `MauiImage`, `MauiFont` and
`MauiAsset`. [MauiProgram.cs](MauiProgram.cs) uses standard
`ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansSample"))`.
The resource demo needs no special website copies, asset URLs or `@font-face` CSS.

For browser verification, scroll to **Packaged assets. Standard MAUI declarations.**
Every image specifies **both** requested dimensions. The two SVG examples use the
same **160 by 160** gray frame to isolate the effect of `Aspect`; the PNG and JPEG
examples use **160 by 100**. Frames remain subject to MAUI layout constraints;
natural dimensions do not determine their requested size.

The original SVG is 240 by 120 and explicitly uses `preserveAspectRatio="none"`.
This lets `Fill` visibly stretch the artwork instead of the SVG applying its own
default aspect-preserving letterboxing. `AspectFit` still preserves the image's
2:1 intrinsic ratio. This is an authoring choice in the sample SVG, not a build-time
rewrite or a guarantee that every SVG will stretch with `Fill`.

| Visible label | Logical source / registration | AutomationId | Expected rendering |
| --- | --- | --- | --- |
| SVG (.png alias) / AspectFit | `asset_shapes.png` → original `asset_shapes.svg` | `asset-svg-png` | Round circle; 160 by 80 artwork centered between gray bands |
| SVG (.svg name) / Fill | `asset_shapes.svg` → same SVG | `asset-svg-direct` | Artwork fills the square; circle becomes a tall ellipse |
| PNG / Center | `sample_bot.png` | `asset-png` | .NET bot centered without scaling; larger content clipped |
| JPEG / AspectFill | `sample_popcorn.jpg` | `asset-jpeg` | Popcorn photograph covers frame with cropping |
| Open Sans via AddFont: … | Label `FontFamily="OpenSansSample"` | `asset-font-label` | Registered font by alias |
| Increment | Button `FontFamily="OpenSansSample"` | `increment-button` | Same font; original counter interaction still works |
| Type here — including 中文 | Entry `FontFamily="OpenSans-Regular.ttf"` | `demo-entry` | Registered font by filename; original text events still work |
| Raw packaging: data/asset-info.json … | `data/asset-info.json` | `asset-raw-note` | Informational text only; no file read takes place |

Open Sans loads asynchronously, initially displaying fallback text. Inspect the
loaded font rather than assuming that text being visible proves font success;
glyphs not present in this font, including Chinese characters, still use fallback.

### Sources and logical names

Paths in this list are relative to the repository root unless stated otherwise:

- **SVG:** sample-local `Resources/Images/asset_shapes.svg` is original geometric
	artwork under the repository MIT license. Both `.png` and `.svg` logical names
	resolve to that unchanged file; the browser does not receive a converted PNG.
- **PNG:** `samples/EssentialsAISample/Resources/Images/dotnet_bot.png`, linked as
	`Resources/Images/sample_bot.png`. The .NET bot comes from the
	[CC0 .NET brand artwork](https://github.com/dotnet/brand).
- **JPEG:** `src/Comet/sample/CometComposeProbe/Resources/drawable/placeholder.jpg`,
	linked as `Resources/Images/sample_popcorn.jpg`. The tracked bytes match
	[Jetsnack's fixture](https://github.com/android/compose-samples/blob/main/Jetsnack/app/src/main/res/drawable-nodpi/placeholder.jpg);
	Jetsnack attributes its imagery to Unsplash. See the packaged notices for the
	[Unsplash license](https://unsplash.com/license) and source details.
- **Font:** `samples/DevFlow.Sample/Resources/Fonts/OpenSans-Regular.ttf`, linked as
	`Resources/Fonts/OpenSans-Regular.ttf`. This font version's embedded metadata
	specifies Apache 2.0 and Google Corporation's 2010–2011 copyright.
- **Raw:** sample-local `Resources/Raw/data/asset-info.json` becomes
	`data/asset-info.json` through `LogicalName`. License/attribution files become
	`licenses/*`; the repository MIT license is explicitly linked as
	`licenses/repository.txt`.

Image/font logical names use the filename of `Link` (or the original filename).
Raw preserves paths, with `Link` taking precedence over `LogicalName`. All lookups
are case-sensitive. See the [full naming and metadata contract](../../README.md#logical-names-and-metadata).
The app explicitly declares the linked binaries; no referenced app's resource
graph is imported. Keep [asset notices](Resources/Raw/licenses/asset-notices.txt)
and [Apache 2.0](Resources/Raw/licenses/Apache-2.0.txt) with redistributed assets.

### Resource boundaries and failures

Supported images are static PNG/JPG/JPEG/SVG file sources with explicit width and
height and `AspectFit`, `AspectFill`, `Fill` or `Center`. There is no Uri/Stream/
Font image source, image animation, transforms, intrinsic sizing, single-sided
size inference, Resizetizer, SVG rasterization, resizing, tinting or density
generation. Fonts support TTF/OTF through `AddFont` on Label/Button/Entry, not
automatic assembly-embedded font discovery. Raw files are **packed only**:
`FileSystem.OpenAppPackageFileAsync` and other browser FileSystem APIs are not
implemented. The UI's Raw note does not prove a runtime file read.

Build and publish generate the resource catalog/files automatically through the
product's source-build entry point or NuGet's packaged targets. The frontend
copies the complete publish output without interpreting resource declarations.
`maui-assets.json` is mandatory even with no assets and loads before managed Main;
missing/invalid catalogs are startup failures. Individual image/font failures
instead emit nonfatal `maui-resource-error` on the mount (`event.detail` is a
diagnostic string). A failed image is cleared; a failed font retains fallback.
Other controls and diagnostics continue running. See the
[resource regression checks](../../CONTRIBUTING.md#resource-regression-checks)
for Node/managed tests and separate real-browser verification requirements.

## Runtime diagnostics

The footer displays:

- **Startup:** elapsed browser navigation time at the frame following the first MAUI layout. It includes loading and Wasm initialization, unlike a stopwatch started in C# `Main`. This measures a first-render opportunity, not an exact GPU presentation timestamp. Cache, network and background-tab scheduling affect the value; it is recorded once per startup.
- **Wasm size:** uncompressed bytes of `dotnet.native.wasm`, not transfer size or linear memory size. The sample first reads Resource Timing's `decodedBodySize`, avoiding a second request. If unavailable, C# counts the decoded HTTP response through a stream after startup timing finishes; that fallback may make another request.

The source button opens the [backend repository directory](https://github.com/Martin1994/maui-labs/tree/fluent-web/platforms/Wasm.FluentUIWebComponents) in a new tab. Metric orchestration and formatting live in `SampleDiagnostics.cs`; the sample-only JS module supplies browser timing/resource information and opens the URL. No file-size constant is generated at build time. If runtime asset URLs change, update the diagnostics URL accordingly.

## External package consumption

Use the backend's [NuGet PackageReference instructions](../../README.md#consume-the-backend-package) for an external application. There are **no released packages currently**; use an experimental/local package version actually available from your configured feed.

**The checked-in C# project is still a source-checkout development project.** It contains a backend `ProjectReference`, with no manual targets import, and inherits shared product build properties, app asset targets and root Central Package Management. It also links image/font/license fixtures outside this directory. Copying this directory alone does not make `BrowserSample.csproj` an independent NuGet consumer. Copy/redeclare those licensed resources in the external app as well as configuring its toolchain.

To adapt it for external consumption, replace the backend `ProjectReference` with a `PackageReference`, and supply your application's own browser toolchain properties, package versions and feeds formerly inherited from the checkout. That includes the target framework/`browser-wasm` RID, JS interop and NativeAOT/trimming settings, and matching LLVM runtime/compiler packages for an available compiler host. Retain SourceGen XAML and the backend `UnmanagedEntryPointsAssembly` entry. NuGet restore automatically imports the packaged bridge `Content` and app resource rules. A source `ProjectReference` outside this product does not automatically import those rules. Remove any manual backend import left from an older setup; do not substitute a NuGet-cache path. See the [toolchain requirements](../../README.md#toolchain-configuration) and [inherited configuration details](../../CONTRIBUTING.md#repository-configuration).

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
| `npm run prepare:maui-bridge` | `node scripts/prepare-maui-bridge.mjs`: copy three published backend assets into `web/` and clear `artifacts/typescript/` |
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

Extract/upload the contents without changing relative paths. Serve `.js` as JavaScript and `.wasm` as `application/wasm`, and serve the JSON catalog, SVG/PNG/JPEG and TTF/OTF resources with their corresponding content types, not SPA HTML fallbacks. No .NET server is required. Catalog and resource URLs resolve relative to `maui-browser.js`, supporting root or subdirectory hosting independently of the current page route. HTTP gzip/Brotli compression is independent of ZIP compression.

Deploy the app/Wasm, runtime glue, backend JS, `maui-assets.json` and `maui-assets/`
as **one matching publish**, preserving the packaged notices. Replace and invalidate
caches together: resource filenames are not content-hashed, and the catalog's
schema/application checks do not detect every mixture of releases. Never mix
cached assets from different publishes or omit the catalog for an empty app.

## Limitations

The application inherits the [backend's limitations](../../README.md#boundaries-and-known-limitations). It is not a production accessibility, high-contrast, cross-browser, performance or long-running reliability certification. Publishing and browser regression checks must be rerun when the interop protocol or runtime assets change.