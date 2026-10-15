# BrowserSample

An experimental .NET MAUI XAML application using the [Fluent UI Web Components browser backend](../../README.md). This is a development example, not a production-ready application or a complete MAUI compatibility demonstration.

## What the application demonstrates

- A normal MAUI application referencing a separate backend project.
- `App.xaml` resources and styles, a responsive Grid, rounded borders and vertical scrolling.
- Standard `Clicked` code-behind events: Increment and Reset update a named Label.
- System / Light / Dark theme selection, Fluent palette resources and app-owned theme colors.
- Navigation-based startup timing, uncompressed Wasm size and a source-repository link.

All application UI is defined in [MainPage.xaml](MainPage.xaml). `wwwroot/index.html` supplies only the mount point and runtime bootstrap, except for bootstrap-error reporting. There is no Blazor UI or sample-side DOM layout logic.

## Application structure

| File | Responsibility |
| --- | --- |
| `MauiProgram.cs` / `Program.cs` | MAUI registration and browser-host startup |
| `App.xaml` | Merged backend palette, application branding and implicit styles |
| `MainPage.xaml` / `MainPage.xaml.cs` | Page layout, theme selection and ordinary click events |
| `SampleDiagnostics.cs` | Startup/size orchestration and repository navigation |
| `wwwroot/main.js` / `wwwroot/sample-diagnostics.js` | Bootstrap and the small browser-only diagnostics surface |

The project compiles XAML with MAUI's **SourceGen inflator**. New `.xaml` files are included automatically. It uses code-behind rather than a ViewModel; theme bindings are not evidence of general data-binding compatibility under NativeAOT.

At viewport widths of 800 CSS pixels or more, the header places theme selection to the right of the title and the technology cards use four columns. Narrower viewports put theme selection on its own right-aligned row and use two card columns. MAUI computes these layouts; the sample does not implement them in CSS.

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

## Runtime diagnostics

The footer displays:

- **Startup:** elapsed browser navigation time at the frame following the first MAUI layout. It includes loading and Wasm initialization, unlike a stopwatch started in C# `Main`. This measures a first-render opportunity, not an exact GPU presentation timestamp. Cache, network and background-tab scheduling affect the value; it is recorded once per startup.
- **Wasm size:** uncompressed bytes of `dotnet.native.wasm`, not transfer size or linear memory size. The sample first reads Resource Timing's `decodedBodySize`, avoiding a second request. If unavailable, C# counts the decoded HTTP response through a stream after startup timing finishes; that fallback may make another request.

The source button opens the [backend repository directory](https://github.com/Martin1994/maui-labs/tree/fluent-web/platforms/Wasm.FluentUIWebComponents) in a new tab. Metric orchestration and formatting live in `SampleDiagnostics.cs`; the sample-only JS module supplies browser timing/resource information and opens the URL. No file-size constant is generated at build time. If runtime asset URLs change, update the diagnostics URL accordingly.

## Build and run

See the [backend toolchain configuration](../../README.md#toolchain-configuration) for SDK, MAUI, NativeAOT LLVM and Node versions. This application's project explicitly references `runtime.linux-arm64.Microsoft.DotNet.ILCompiler.LLVM`; its publish helper requires Emscripten **3.1.57**. These describe the configured path, not a claim that all hosts work unchanged. Another compiler host requires the matching package and native toolchain.

Run the following from the **backend directory**, `platforms/Wasm.FluentUIWebComponents` (two directories above this README):

```bash
export EMSDK=/path/to/emsdk-3.1.57
bash scripts/publish.sh
node scripts/serve.mjs
```

The publish script installs locked npm dependencies, builds the browser assets and publishes this application to `artifacts/wwwroot`. It uses locked NuGet restore and enables trimming/AOT diagnostics. The server binds to `127.0.0.1:8768` with the correct Wasm MIME type. It serves `artifacts/wwwroot`, or the collected site recorded in `artifacts/latest-webroot` when that marker exists. Restart it after selecting new output.

For a regular build rather than native publishing, also from the backend directory:

```bash
npm ci --ignore-scripts
MSBuildEnableWorkloadResolver=false dotnet build Wasm.FluentUIWebComponents.slnx -c Release
```

`jsconfig.json` resolves sample bootstrap imports against the backend's generated declarations. JavaScript is checked during the build; after the declarations exist, `npm run check:sample` reruns that check separately. The project uses invariant globalization.

## Static deployment and optional ZIP

The **published site directory** can be served or uploaded as-is. PDBs, declarations and runtime metadata can remain. Do not deploy the entire `artifacts` directory, which can contain logs and unrelated build outputs.

From the backend directory, this optional command archives the complete `artifacts/wwwroot` without rebuilding or filtering files:

```bash
npm run package:site
```

It invokes this project's `PackageSite` target using MSBuild's built-in `ZipDirectory` task and writes `artifacts/BrowserSample-site.zip`. Packaging is not automatic. For another site or archive location:

```bash
npm run package:site -- -p:SiteDirectory=/path/to/published-site -p:SiteArchive=/path/to/site.zip
```

`index.html` is at the ZIP root. Keep the archive destination outside the source site. You can instead copy or ZIP the published directory using your usual deployment tools.

Extract/upload the contents without changing relative paths. Serve `.js` as JavaScript and `.wasm` as `application/wasm`; no .NET server is required. The application uses relative asset URLs for root or subdirectory hosting. HTTP gzip/Brotli compression is independent of ZIP compression. Never mix cached runtime assets from different publishes.

## Limitations

The application inherits the [backend's limitations](../../README.md#boundaries-and-known-limitations). It is not a production accessibility, high-contrast, cross-browser, performance or long-running reliability certification. Publishing and browser regression checks must be rerun when the interop protocol or runtime assets change.