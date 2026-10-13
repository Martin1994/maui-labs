# MAUI / Fluent UI Web Components

An experimental, minimal .NET MAUI browser backend using NativeAOT LLVM, WebAssembly, and Fluent UI Web Components. This is currently a development starting point, not a complete or supported MAUI platform.

## Two projects

| Project | Responsibility |
| --- | --- |
| `src/Wasm.FluentUIWebComponents` | Reusable platform host, handlers, dispatcher, DOM interop and browser assets |
| `samples/BrowserSample` | A normal MAUI application referencing the backend, with `App.xaml`, `MainPage.xaml`, resources/styles and code-behind events |

The framework is independent of the sample application. The HTML host contains only a mount point and the runtime bootstrap; **all application UI is defined in XAML**. There is no Blazor renderer, Fluent Blazor, or `IJSRuntime`.

The backend is currently consumed through a project reference. Packaged releases are not yet available.

## Application model

The sample uses ordinary MAUI startup registration:

```csharp
var builder = MauiApp.CreateBuilder(useDefaults: false);
builder.UseMauiApp<App>()
    .UseFluentUIWebComponents();
return builder.Build();
```

`BrowserApplication.Run(MauiProgram.CreateMauiApp)` supplies the platform host before the factory is called. Core defaults are disabled because native fonts, images and platform services cannot simply be inherited; Controls registration through `UseMauiApp<App>()` remains in place, followed by the browser handler overrides.

Pages use normal XAML, named controls and events:

```xml
<Label x:Name="CounterLabel" Text="Clicked 0 times" />
<Button Text="Increment" Clicked="OnIncrementClicked" />
```

The sample uses code-behind events. XAML is compiled by MAUI's **SourceGen inflator** into C# constructors, setters and event subscriptions, not interpreted at runtime. New `.xaml` files are included automatically by the sample project. Binding compatibility under NativeAOT is not yet validated.

## Current capabilities

- Application and single Window host, real `MauiContext`, scoped window services and lifecycle.
- ContentPage; shared Layout handler for the demonstrated Grid, VerticalStackLayout and HorizontalStackLayout.
- Label text, wrapping, size/family/weight/italic and horizontal alignment.
- Real Fluent Button with MAUI `Clicked`, text, color, enabled state and padding.
- Solid rectangular/rounded Border and padding.
- Native vertical ScrollView, including offsets and MAUI `Scrolled` notifications.
- Main-thread dispatch and delayed dispatch; pending work is cancelled when the host is disposed.
- Synchronous DOM leaf measurement; MAUI alone calculates child positions and sizes. Root invalidations are coalesced to a browser frame.

Content is remeasured under its effective width, rather than measuring the last arranged rectangle. Browser coordinates are CSS pixels, not physical pixels multiplied by DPR. Border painting does not add a second layout inset.

## Toolchain configuration

Building the backend requires a .NET SDK compatible with the target framework and NativeAOT LLVM packages, a compatible Emscripten toolchain, and Node/npm for the browser assets. The values below describe the repository's current configuration, **not a minimum-version or exhaustive host-support matrix**.

| Component | Current configuration |
| --- | --- |
| Compiler host package | The sample explicitly references `runtime.linux-arm64.Microsoft.DotNet.ILCompiler.LLVM` |
| .NET SDK | Stable .NET 10.0 SDK; [global.json](global.json) selects the latest installed 10.0 feature band (`latestFeature`) |
| MAUI | `10.0.41`, using repository central package management |
| NativeAOT LLVM compiler/runtime | `10.0.0-rc.1.26357.1` |
| Emscripten | `3.1.57`, enforced by the current publish helper |
| Node | 22+, as declared in `package.json` |

NativeAOT LLVM's [compilation guide](https://github.com/dotnet/runtimelab/blob/9954350a58ede8b8eaaeb24112ca4f1e78cc527c/docs/using-nativeaot/compiling.md) includes Windows x64 hosts. The backend is not inherently Linux-only. Building on another host requires selecting the matching compiler host package, providing the corresponding native toolchain, and adapting the helper invocation as needed. Keep compiler and runtime package versions aligned; upstream host support alone is not a guarantee that this sample's unchanged configuration works there.

The application targets `browser-wasm`; browser compatibility is separate from the operating system used to compile it. A complete browser support matrix has not yet been established.

The SDK is not locked to `10.0.112`. SDK selection allows newer stable 10.0 feature bands such as `10.0.401`, but not .NET 11 or preview SDKs. This does not change the pinned NativeAOT LLVM compiler/runtime packages or Emscripten version.

NuGet sources are defined in [NuGet.config](NuGet.config), including the experimental compiler feed. NuGet dependencies use repository central package management; JavaScript dependencies and build tools are pinned in `package-lock.json`.

## Build and run

Using the currently configured compiler host and the Bash publish helper, run from this directory:

```bash
export EMSDK=/path/to/emsdk-3.1.57
bash scripts/publish.sh
node scripts/serve.mjs
```

The publish script installs locked npm dependencies, builds the browser bundle and publishes the sample to `artifacts/wwwroot`. It uses locked NuGet restore and enables trimming/AOT diagnostics. `serve.mjs` binds only to `127.0.0.1:8768` with the correct Wasm MIME type.

For a regular build rather than publish, first run `npm ci --ignore-scripts`, then `MSBuildEnableWorkloadResolver=false dotnet build Wasm.FluentUIWebComponents.slnx -c Release`. The backend project builds its own JS asset and declarations, which are copied into the consuming sample through the project reference.

### Package for static deployment

The **published site directory** can be served or uploaded as-is. No trimming is needed: PDBs, declarations and runtime metadata can remain in the directory. Do not deploy the entire `artifacts` directory, which can also contain logs and other build outputs.

To ZIP the complete site after publishing, run this optional command with the project's .NET SDK:

```bash
npm run package:site
```

This invokes the sample's `PackageSite` target using MSBuild's built-in `ZipDirectory` task. It archives **every file** in `artifacts/wwwroot` to `artifacts/BrowserSample-site.zip`, without rebuilding or filtering files. There is no extra language, package dependency or post-publish step. For a collected site or a different output path, specify the properties explicitly:

```bash
npm run package:site -- -p:SiteDirectory=/path/to/published-site -p:SiteArchive=/path/to/site.zip
```

`index.html` is at the ZIP root. Keep the archive destination outside the site directory. You can also ZIP or copy the published directory with your usual deployment tools instead of using this target.

Extract/upload the archive contents without changing their relative paths. Use a static host that serves `.js` as JavaScript and `.wasm` as `application/wasm`; no .NET server is required. Root and subdirectory deployments are supported by the relative asset URLs. HTTP gzip/Brotli compression can be enabled independently of ZIP compression. Avoid mixing cached runtime files from different publishes.

## Browser assets and types

The browser bridge is implemented in TypeScript, with its public entry point in [maui-browser.ts](src/Wasm.FluentUIWebComponents/web/maui-browser.ts). MSBuild invokes the frontend build automatically: TypeScript checks the sources and emits declarations, then esbuild bundles the bridge and Fluent dependencies into a **self-contained ESM module**.

The project reference supplies three assets to the application output and publish directory:

- `maui-browser.js`: the browser bridge and Fluent implementation.
- `maui-browser.d.ts`: `startMaui` and the returned `MauiApplicationHandle`.
- `dotnet.d.ts`: the original declaration from the matching LLVM runtime package.

The application imports its own runtime's `dotnet.js` and passes its builder to `startMaui`. The runtime JS exports live in the backend assembly; `UnmanagedEntryPointsAssembly` in the sample preserves them. Use the runtime assets supplied by the configured LLVM toolchain, not SDK/Blazor replacements.

The sample's `jsconfig.json` resolves source imports against the generated declarations, and its JavaScript is type-checked during a normal build. Declarations become available after the first build. A standalone frontend build (`npm run build`) requires the backend's NuGet restore first; `npm run check:sample` checks the sample against the generated public declarations.

The backend uses explicit internal interop contracts and validates the managed export object. Runtime declarations may describe APIs beyond the subset supported by NativeAOT LLVM; their presence alone does not guarantee runtime support.

### Web Component contract

C# handlers choose the exact HTML tag: Label uses `maui-label`, ScrollView uses `maui-scroll-view`, Button uses the existing `fluent-button`, and ordinary containers use `div`. The bridge no longer translates semantic control kinds into tags or installs control-specific defaults. Custom elements must be registered before creation; the bundle registers the two native components, and Fluent initialization registers its button before managed startup.

The internal interop distinguishes JavaScript **properties** (`textContent`, `disabled`), HTML **attributes** (`data-automation-id`, removed when the value is null), and CSS **styles**. Property writes target existing writable properties rather than creating arbitrary fields. This is a trusted handler interface, not a sandbox for untrusted markup or scripts. Managed code and JS assets must be rebuilt and deployed together after protocol changes.

`maui-label` and `maui-scroll-view` are native Custom Elements, not FAST subclasses. Their shadow roots contain a slot; text and MAUI children remain in light DOM, and the host remains the measure/arrange target. Label owns its wrapping defaults. The ScrollView host itself is the scrolling element, preserving native scroll events and offsets. Detaching and reinserting a component does not destroy its content or managed handle; final release remains the handler/bridge's responsibility.

The label's default foreground consumes Fluent's `--colorNeutralForeground1` token. An explicit MAUI text color overrides that default; clearing it restores the token. Fluent Button continues to use its own token-based styles. This establishes shared token consumption, **not complete app theme support**: startup currently applies the light theme, fixed sample colors do not automatically change, and system-theme tracking, application theme overrides and MAUI `AppThemeBinding` integration are not implemented. A future FAST implementation can preserve the same tag/property/event contract and host behavior without changing interop.

## Sample

[MainPage.xaml](samples/BrowserSample/MainPage.xaml) demonstrates resources, styles, a responsive technology-card grid, rounded borders, scrolling and ordinary MAUI click events. Increment and Reset update a named Label in code-behind. The buttons retain Fluent's neutral interaction-state colors, while product colors provide deliberate heading emphasis.

The sample footer shows two runtime measurements and a button that opens the [repository](https://github.com/Martin1994/maui-labs/tree/fluent-web/platforms/Wasm.FluentUIWebComponents) in a new tab:

- **Startup:** elapsed browser navigation time at the frame following the first MAUI layout. This includes loading and Wasm initialization, unlike a stopwatch started in C# `Main`. It is a first-render opportunity measurement, not an exact GPU presentation timestamp. Cache, network and background-tab scheduling affect the value; it is recorded once per application startup.
- **Wasm size:** the uncompressed bytes of `dotnet.native.wasm`, not transfer size or linear memory size. The sample first uses the existing Resource Timing entry's `decodedBodySize`, avoiding a second request. When unavailable, C# counts the decoded HTTP response through a stream after startup timing has finished; that fallback may require another request.

Metric orchestration, formatting and fallback reading live in `SampleDiagnostics.cs`. A small sample-only JS module supplies browser timing/resource information and opens the repository URL; the framework API is unchanged. No file-size constant is generated at build time. If runtime asset URLs are customized, update the sample's measurement URL accordingly.

## Boundaries and known limitations

- This is a deliberately small control/property surface, not general MAUI compatibility. Other controls and properties may be unsupported.
- No Shell, navigation stack, full Essentials, embedded font/image pipeline, Resizetizer, hot reload, full graphics, native menus, or multi-window behavior.
- No `IDispatcherTimer`, animation ticker, worker-thread UI dispatch, Entry/IME or binding compatibility claim yet.
- ScrollView currently supports vertical user scrolling, not horizontal/both orientations or programmatic `ScrollToAsync`; unsupported requests fail explicitly.
- Border supports solid rectangular/rounded strokes, not arbitrary shape/dash/gradient rendering. Advanced text formatting, selection, truncation and full button input semantics are not complete.
- Standard binding infrastructure belongs to MAUI, but reflection/trimming compatibility of binding scenarios needs separate NativeAOT verification.
- NativeAOT LLVM loses original C# exception messages at the raw JS export boundary. The backend catches callback failures and reports the original diagnostic through `maui-error` on the host element and `console.error`.
- The JS bootstrap returns an object with `dispose()`; duplicate startup on the same page is rejected before replacing runtime imports. Hidden tabs can defer layout frames.
- The sample uses invariant globalization and has not been validated for production accessibility, cross-browser parity, long-running memory behavior or performance budgets.

Full trimming and both AOT/trimming analyzers are enabled. Unused reflection-based implicit conversions and Shell query-property reflection are disabled through MAUI feature switches, not warning suppression. No entire MAUI assembly is preserved and no MAUI/runtime fork is required.

## Dependency changes

Follow the repository's dependency policies. Run `node ../../eng/scripts/audit-npm.mjs` for npm dependency or lockfile changes before opening a PR, and keep the npm project registered in `.github/dependabot.yml`.