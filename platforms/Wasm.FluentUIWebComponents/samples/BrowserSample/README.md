# BrowserSample

An experimental .NET MAUI XAML application using the [Fluent UI Web Components browser backend](../../README.md). This is a development example, not a production-ready application or a complete MAUI compatibility demonstration.

## What the application demonstrates

- A normal MAUI application referencing a separate backend project.
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

| File | Responsibility |
| --- | --- |
| `MauiProgram.cs` / `Program.cs` | MAUI registration and browser-host startup |
| `App.xaml` | Merged backend palette, application branding and implicit styles |
| `MainPage.xaml` / `MainPage.xaml.cs` | Page layout, theme selection and ordinary click events |
| `MainPage.TextInteraction.cs` | Entry events and managed text/selection updates |
| `MainPage.Animation.cs` | Animation preview and cancellation |
| `MainPage.Scheduling.cs` | Dispatcher timer ownership and ticks |
| `MainPage.DynamicLayout.cs` | Runtime child collection changes |
| `SampleDiagnostics.cs` | Startup/size orchestration and repository navigation |
| `wwwroot/main.js` / `wwwroot/sample-diagnostics.js` | Bootstrap and the small browser-only diagnostics surface |

The project compiles XAML with MAUI's **SourceGen inflator**. New `.xaml` files are included automatically. It uses code-behind rather than a ViewModel; theme bindings are not evidence of general data-binding compatibility under NativeAOT.

The feature-named partial files keep each page section's state and event handlers together without introducing separate controls or moving application behavior into JS. The main code-behind retains page lifecycle, responsive layout and theme coordination.

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

## Build and run

See the [backend toolchain configuration](../../README.md#toolchain-configuration) for SDK, MAUI, NativeAOT LLVM and Node versions. The compiler host package is selected automatically as `runtime.$(NETCoreSdkPortableRuntimeIdentifier).Microsoft.DotNet.ILCompiler.LLVM`, following the upstream NativeAOT LLVM guidance. It uses the **SDK host RID**, not the application's `browser-wasm` target RID. Both compiler package versions remain aligned in Central Package Management.

Use a host for which the pinned NativeAOT LLVM compiler package exists, and initialize its native toolchain, including compatible Emscripten **3.1.57**. Automatic package selection does not add support for an unavailable host such as the current macOS ARM64 configuration. On Windows, initialize the Emscripten environment using its Windows activation script; Bash is not required for the npm/MSBuild commands below.

The sample uses `packages.<SDK-host-RID>.lock.json` so different host compilers do not overwrite one shared lockfile. Lockfiles are included for `linux-arm64`, `linux-x64` and `win-x64`. Restore support does not imply a native publish has been tested on every host. Adding another supported compiler host requires generating/reviewing its lockfile. Locked verification is available with `dotnet restore samples/BrowserSample/BrowserSample.csproj -p:RestoreLockedMode=true`.

Run the following from the **backend directory**, `platforms/Wasm.FluentUIWebComponents` (two directories above this README):

```bash
npm ci --ignore-scripts
npm run publish:site
npm run package:site
node scripts/serve.mjs
```

After the one-time toolchain/npm setup, publishing and packaging need no RID, output-path or other custom parameters. `publish:site` invokes standard `dotnet publish` (Release by default for this .NET 10 project); `package:site` invokes the independent `PackageSite` MSBuild target. The project sets `RuntimeIdentifier=browser-wasm` and shares a default site directory between both operations:

- Published website: `artifacts/wwwroot`.
- Complete website archive: `artifacts/BrowserSample-site.zip`.

The equivalent direct commands, also without custom properties, are:

```bash
dotnet publish samples/BrowserSample/BrowserSample.csproj
dotnet msbuild samples/BrowserSample/BrowserSample.csproj -target:PackageSite
```

Both are ordinary MSBuild targets. To invoke `Publish` directly through MSBuild, include the standard `-restore` switch on a clean checkout; unlike `dotnet publish`, `dotnet msbuild -target:Publish` does not implicitly restore. Publishing does not automatically ZIP, and packaging does not rebuild or publish.

The optional `bash scripts/publish.sh` helper remains available without arguments on Bash hosts. It installs locked npm dependencies, performs locked NuGet restore, and captures a publish log/binlog. It uses the same project defaults rather than supplying a separate RID or output directory. It accepts an already initialized Emscripten toolchain, or loads `emsdk_env.sh` from the configured `EMSDK` directory. Trimming/AOT diagnostics are enabled by the project configuration for both publish entry points.

The server binds to `127.0.0.1:8768` with the correct Wasm MIME type. It serves `artifacts/wwwroot`, or the collected site recorded in `artifacts/latest-webroot` when that marker exists. Restart it after selecting new output.

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

`SiteDirectory` also supplies the default `PublishDir`, so the same override can be used for both publishing and packaging. An explicit `PublishDir` is used as the packaging source when no separate `SiteDirectory` is supplied. Custom paths must be supplied again for a later, independent packaging invocation; the project does not persist command-line overrides.

Cross-machine collection is a separate workflow. If a machine only collects output and has no local `artifacts/wwwroot`, pass that collected site as `SiteDirectory`; `PackageSite` deliberately does not auto-select `artifacts/latest-webroot`. No source-path override is needed on the machine that performed a normal publish.

`index.html` is at the ZIP root. Keep the archive destination outside the source site. You can instead copy or ZIP the published directory using your usual deployment tools.

Extract/upload the contents without changing relative paths. Serve `.js` as JavaScript and `.wasm` as `application/wasm`; no .NET server is required. The application uses relative asset URLs for root or subdirectory hosting. HTTP gzip/Brotli compression is independent of ZIP compression. Never mix cached runtime assets from different publishes.

## Limitations

The application inherits the [backend's limitations](../../README.md#boundaries-and-known-limitations). It is not a production accessibility, high-contrast, cross-browser, performance or long-running reliability certification. Publishing and browser regression checks must be rerun when the interop protocol or runtime assets change.